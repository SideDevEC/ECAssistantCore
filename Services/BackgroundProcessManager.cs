using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ECAssistant.Core.Services;

/// <summary>
/// Background Process Manager — starts, tracks, and manages long-running processes
/// without blocking the main thread.
/// 
/// Usage:
///   var id = await _bgMgr.StartAsync("dotnet build", workingDir);
///   var status = _bgMgr.GetStatus(id);  // Running, Completed, Failed
///   var output = _bgMgr.GetOutput(id);  // stdout/stderr so far
///   _bgMgr.Kill(id);                    // terminate
///   _bgMgr.List();                       // all active processes
/// </summary>
public class BackgroundProcessManager : IDisposable
{
    private readonly ConcurrentDictionary<string, BgProcess> _processes = new();
    private int _counter = 0;

    /// <summary>Start a background process (non-blocking). The cancellation token is honored for script writing and process start.</summary>
    public async Task<string> StartAsync(string command, string workingDirectory, int timeoutSeconds = 300, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = $"bg-{Interlocked.Increment(ref _counter)}";
        // v10.19.2: Temp scripts inside working dir, not OS temp. OS-aware.
        var tempDir = Path.Combine(workingDirectory, ".tmp");
        Directory.CreateDirectory(tempDir);
        var isWindows = OperatingSystem.IsWindows();
        var isMacOS = OperatingSystem.IsMacOS();
        var ext = isWindows ? ".ps1" : ".sh";
        var tempScript = Path.Combine(tempDir, $"ecagent_bg_{id}{ext}");
        // UTF-8 with BOM for .ps1 — PowerShell 5.1 misreads UTF-8 without BOM as ANSI.
        await File.WriteAllTextAsync(tempScript, command,
            isWindows ? new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true) : new System.Text.UTF8Encoding(false),
            cancellationToken);

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : isMacOS ? "/bin/zsh" : "/bin/bash",
            // Execute the temp script file directly — never interpolate the raw command
            // into -c (breaks on embedded quotes).
            Arguments = isWindows
                ? $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScript}\""
                : $"\"{tempScript}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start background process.");

        var bgProc = new BgProcess
        {
            Id = id,
            Process = proc,
            Command = command,
            StartedAt = DateTime.UtcNow,
            TimeoutSeconds = timeoutSeconds,
            TempScriptPath = tempScript,
        };

        // Capture output PROGRESSIVELY — ReadToEndAsync only surfaces output after
        // the process exits, so GetOutput on a running process returned nothing.
        // The buffers live ON the BgProcess (GetOutput reads bg.StdoutBuffer) — the
        // pump tasks below must write THERE, not into detached locals (wiring bug).
        var stdoutBuffer = bgProc.StdoutBuffer;
        var stderrBuffer = bgProc.StderrBuffer;
        var outputLock = bgProc.OutputLock;
        bgProc.OutputTask = Task.Run(async () =>
        {
            var line = await proc.StandardOutput.ReadLineAsync();
            while (line != null)
            {
                lock (outputLock) stdoutBuffer.AppendLine(line);
                line = await proc.StandardOutput.ReadLineAsync();
            }
        });
        bgProc.ErrorTask = Task.Run(async () =>
        {
            var line = await proc.StandardError.ReadLineAsync();
            while (line != null)
            {
                lock (outputLock) stderrBuffer.AppendLine(line);
                line = await proc.StandardError.ReadLineAsync();
            }
        });

        // Set up completion + timeout
        _ = Task.Run(async () =>
        {
            try
            {
                var completed = await Task.Run(() => proc.WaitForExit(timeoutSeconds * 1000));
                if (!completed && !proc.HasExited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BackgroundProcessManager] Non-critical error ignored: {ex.Message}"); }
                    bgProc.TimedOut = true;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BackgroundProcessManager] Non-critical error ignored: {ex.Message}"); }
            finally
            {
                bgProc.CompletedAt = DateTime.UtcNow;
                bgProc.ExitCode = proc.HasExited ? proc.ExitCode : -1;
                bgProc.IsFinished = true;
                try { await Task.WhenAll(bgProc.OutputTask ?? Task.CompletedTask, bgProc.ErrorTask ?? Task.CompletedTask); } catch { /* readers end with the process */ }
                try { File.Delete(tempScript); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BackgroundProcessManager] Non-critical error ignored: {ex.Message}"); }
                // Release the OS handles — long-running managers leak otherwise.
                try { proc.Dispose(); } catch { } // buffers already captured the output
            }
        });

        _processes[id] = bgProc;
        return id;
    }

    /// <summary>Get the status of a background process.</summary>
    public BgStatus GetStatus(string id)
    {
        if (!_processes.TryGetValue(id, out var bg))
            return BgStatus.NotFound;

        if (bg.IsFinished)
            return bg.ExitCode == 0 ? BgStatus.Completed : (bg.TimedOut ? BgStatus.TimedOut : BgStatus.Failed);

        return BgStatus.Running;
    }

    /// <summary>Get output (stdout + stderr) from a background process — including output produced while still running.</summary>
    public string GetOutput(string id)
    {
        if (!_processes.TryGetValue(id, out var bg))
            return $"Process '{id}' not found.";

        var sb = new StringBuilder();
        lock (bg.OutputLock)
        {
            if (bg.StdoutBuffer.Length > 0) sb.AppendLine(bg.StdoutBuffer.ToString());
            if (bg.StderrBuffer.Length > 0) sb.AppendLine($"[STDERR] {bg.StderrBuffer}");
        }
        return sb.ToString();
    }

    /// <summary>Get full status info for a background process.</summary>
    public BgProcessInfo GetInfo(string id)
    {
        if (!_processes.TryGetValue(id, out var bg))
            return new BgProcessInfo { Id = id, Status = BgStatus.NotFound };

        var elapsed = bg.CompletedAt.HasValue
            ? (bg.CompletedAt.Value - bg.StartedAt).TotalSeconds
            : (DateTime.UtcNow - bg.StartedAt).TotalSeconds;

        return new BgProcessInfo
        {
            Id = id,
            Command = bg.Command,
            Status = GetStatus(id),
            ExitCode = bg.ExitCode,
            ElapsedSeconds = elapsed,
            StartedAt = bg.StartedAt,
            CompletedAt = bg.CompletedAt
        };
    }

    /// <summary>Kill a running background process.</summary>
    public bool Kill(string id)
    {
        if (!_processes.TryGetValue(id, out var bg)) return false;
        if (bg.IsFinished) return false;

        try
        {
            bg.Process.Kill(entireProcessTree: true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>List all background processes.</summary>
    public List<BgProcessInfo> List()
    {
        var result = new List<BgProcessInfo>();
        foreach (var kvp in _processes)
        {
            result.Add(GetInfo(kvp.Key));
        }
        return result;
    }

    /// <summary>Clean up finished processes (remove from tracking).</summary>
    public void CleanupFinished()
    {
        var toRemove = _processes
            .Where(kvp => kvp.Value.IsFinished)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
            _processes.TryRemove(key, out _);
    }

    public void Dispose()
    {
        foreach (var bg in _processes.Values)
        {
            try { if (!bg.Process.HasExited) bg.Process.Kill(entireProcessTree: true); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BackgroundProcessManager] Non-critical error ignored: {ex.Message}"); }
            try { bg.Process.Dispose(); } catch { }
        }
        _processes.Clear();
    }
}
