using System.Diagnostics;
using System.Text;

using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services.Shell;

/// <summary>
/// Persistent PowerShell session via one long-lived pwsh process (Windows).
/// Same sentinel protocol as the POSIX variant: BEGIN marker → command (stderr
/// merged, prefixed) → CWD line → END marker carrying exit code. Working
/// directory and exported environment persist across calls.
/// Windows-only counterpart to <see cref="PersistentShellSession"/>.
/// </summary>
public sealed class PersistentPowerShellSession : IShellSession
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

    private PersistentPowerShellSession(Process process, string initialCwd, ILogger logger)
    {
        _process = process;
        _cwd = initialCwd;
        _logger = logger;
    }

    public static bool IsSupported { get; } = OperatingSystem.IsWindows(); // immutable OS fact

    public static async Task<PersistentPowerShellSession> StartAsync(string initialWorkingDirectory, ILogger? logger = null)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Persistent PowerShell sessions are Windows-only.");
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = initialWorkingDirectory,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("-"); // read commands from stdin
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start pwsh process.");
        var session = new PersistentPowerShellSession(process, initialWorkingDirectory, logger ?? new NoOpLogger());
        var warmup = await session.RunAsync("Write-Output ok", CancellationToken.None);
        if (warmup.ExitCode != 0)
            throw new InvalidOperationException($"PowerShell session warm-up failed (rc={warmup.ExitCode}).");
        return session;
    }

    public async Task<ShellCommandResult> RunAsync(string command, CancellationToken ct = default)
    {
        if (IsDead) throw new InvalidOperationException("Shell session has exited.");
        var id = Interlocked.Increment(ref _seq);
        var endMarker = $"{EndMarker}{id:x8}";
        var errFile = Path.Combine(Path.GetTempPath(), $"eca_shell_err_{Environment.ProcessId}_{id}");
        // PS-native: capture stderr to a temp file via redirection, replay prefixed;
        // $LASTEXITCODE after native commands, 0 fallback for cmdlets (which don't set it).
        var script =
            $"Write-Output '{BeginMarker}'\n" +
            $"try {{ {command} }} finally {{ }}\n" +
            $"$__eca_rc = if ($global:LASTEXITCODE -ne $null) {{ $global:LASTEXITCODE }} else {{ 0 }}\n" +
            $"if (Test-Path '{errFile}') {{ Get-Content '{errFile}' | ForEach-Object {{ Write-Output ('{ErrPrefix}' + $_) }}; Remove-Item '{errFile}' -Force }}\n" +
            $"Write-Output '{CwdMarker}' + (Get-Location).Path\n" +
            $"Write-Output '{endMarker}' + $__eca_rc\n";
        // Note: string concat in the CWD line would break the marker; emit with format:
        var fixedScript = script.Replace(
            $"Write-Output '{CwdMarker}' + (Get-Location).Path",
            "Write-Output ('" + CwdMarker + "' + (Get-Location).Path)");
        await _process.StandardInput.WriteAsync(fixedScript);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exitCode = 0;
        var sawBegin = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await _process.StandardOutput.ReadLineAsync(ct) ??
                throw new InvalidOperationException("PowerShell session stdout closed (process died).");

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
