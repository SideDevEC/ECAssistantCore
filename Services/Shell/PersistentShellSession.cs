using System.Diagnostics;
using System.Text;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services.Shell;

/// <summary>
/// Persistent POSIX shell session via one long-lived zsh/bash process.
/// Protocol per command: BEGIN marker → command runs (stderr captured to a temp
/// file, replayed with a prefix) → CWD line → END marker with exit code.
/// Working directory and exported environment persist across calls — the whole
/// point of the session. Non-Windows only.
/// </summary>
public sealed class PersistentShellSession : IShellSession
{
    private const string BeginMarker = "__ECA_BEGIN__";
    private const string EndMarker = "__ECA_END__";
    private const string CwdMarker = "__ECA_CWD__:";
    private const string ErrPrefix = "[stderr] ";

    private readonly Process _process;
    private readonly ILogger _logger;
    private string _cwd;
    private int _seq;

    public string CurrentWorkingDirectory => _cwd;
    public bool IsDead
    {
        get
        {
            try { return _process.HasExited; }
            catch (InvalidOperationException) { return true; } // disposed
        }
    }

    private PersistentShellSession(Process process, string initialCwd, ILogger logger)
    {
        _process = process;
        _cwd = initialCwd;
        _logger = logger;
    }

    public static bool IsSupported => !OperatingSystem.IsWindows();

    public static async Task<PersistentShellSession> StartAsync(string initialWorkingDirectory, ILogger? logger = null)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Persistent shell sessions are POSIX-only.");
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = initialWorkingDirectory,
        };
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start shell process.");
        var session = new PersistentShellSession(process, initialWorkingDirectory, logger ?? new NoOpLogger());
        var warmup = await session.RunAsync("true", CancellationToken.None);
        if (warmup.ExitCode != 0)
            throw new InvalidOperationException($"Shell session warm-up failed (rc={warmup.ExitCode}).");
        return session;
    }

    public async Task<ShellCommandResult> RunAsync(string command, CancellationToken ct = default)
    {
        if (IsDead) throw new InvalidOperationException("Shell session has exited.");
        var id = Interlocked.Increment(ref _seq);
        var endMarker = $"{EndMarker}{id:x8}";
        var errFile = $"/tmp/eca_shell_err_{Environment.ProcessId}_{id}";
        // stderr → temp file (no pipe quirks, exact ordering of stdout preserved);
        // replayed with a prefix before END; CWD line feeds directory tracking.
        var script =
            $"echo {BeginMarker}\n" +
            $"{{ {command}\n" +
            $"}} 2>\"{errFile}\"\n" +
            $"__eca_rc=$?\n" +
            $"if [ -s \"{errFile}\" ]; then sed 's/^/{ErrPrefix}/' \"{errFile}\"; fi\n" +
            $"rm -f \"{errFile}\"\n" +
            $"echo \"{CwdMarker}$PWD\"\n" +
            $"echo {EndMarker}$__eca_rc\n";
        await _process.StandardInput.WriteAsync(script);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exitCode = 0;
        var sawBegin = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await _process.StandardOutput.ReadLineAsync(ct) ??
                throw new InvalidOperationException("Shell session stdout closed (process died).");

            if (!sawBegin)
            {
                if (line.StartsWith(BeginMarker, StringComparison.Ordinal)) sawBegin = true;
                continue;
            }
            if (line.StartsWith(EndMarker, StringComparison.Ordinal))
            {
                _ = int.TryParse(line[EndMarker.Length..], out exitCode);
                break;
            }
            if (line.StartsWith(CwdMarker, StringComparison.Ordinal))
            {
                var cwd = line[CwdMarker.Length..].Trim();
                if (Directory.Exists(cwd)) _cwd = cwd;
                continue;
            }
            if (line.StartsWith(ErrPrefix, StringComparison.Ordinal))
                stderr.AppendLine(line[ErrPrefix.Length..]);
            else
                stdout.AppendLine(line);
        }

        return new ShellCommandResult(exitCode, stdout.ToString().TrimEnd('\n'), stderr.ToString().TrimEnd('\n'));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("exit");
                if (!_process.WaitForExit(2000)) _process.Kill(entireProcessTree: true);
            }
            _process.Dispose();
        }
        catch { /* best-effort teardown */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>Headless logger — the session protocol must never require logging setup.</summary>
    private sealed class NoOpLogger : ILogger
    {
        public void Initialize(string logFilePath, LogLevel minLevel) { }
        public void SetLevel(LogLevel level) { }
        public bool IsDebugEnabled => false;
        public void Debug(string tag, string message) { }
        public void Info(string tag, string message) { }
        public void Warn(string tag, string message) { }
        public void Error(string tag, string message) { }
        public void Error(string tag, string message, Exception ex) { }
        public string GetRecentLines(int count) => string.Empty;
        public string LogFilePath => "";
        public long LogFileSize => 0;
    }
}
