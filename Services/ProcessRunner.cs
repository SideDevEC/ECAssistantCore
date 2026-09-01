using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// Concrete process execution implementation.
/// OS-aware: uses pwsh on Windows, zsh on macOS, bash on Linux.
/// </summary>
public class ProcessRunner : IProcessRunner
{
    private readonly bool _isWindows = OperatingSystem.IsWindows();
    private readonly bool _isMacOS = OperatingSystem.IsMacOS();

    public async Task<ProcessResult> ExecuteAsync(string command, string? workDir = null, CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workDir ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_isWindows)
        {
            // PowerShell Core (pwsh) if available, otherwise Windows PowerShell
            // Use ArgumentList to avoid quoting corruption when the command contains embedded quotes.
            startInfo.FileName = "pwsh";
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(command);

            // Fallback to powershell.exe if pwsh not found
            if (!CommandExists("pwsh"))
            {
                startInfo.FileName = "powershell.exe";
            }
        }
        else if (_isMacOS)
        {
            // Pass command as a separate argument to avoid quoting corruption.
            startInfo.FileName = "/bin/zsh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            // Linux
            startInfo.FileName = "/bin/bash";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Default timeout: 60 seconds if no cancellation token provided
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return new ProcessResult(process.ExitCode, stdout, stderr, false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            var timedOut = !ct.IsCancellationRequested; // If our timeout fired (not external ct)
            return new ProcessResult(-1, "", timedOut ? "Process timed out (60s)" : "Process cancelled", true);
        }
    }

    /// <summary>Check if a command exists on the system (for pwsh detection on Windows).</summary>
    private bool CommandExists(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}