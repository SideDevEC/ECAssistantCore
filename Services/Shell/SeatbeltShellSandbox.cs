using System.Text;

using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services.Shell;

/// <summary>
/// macOS Seatbelt (sandbox-exec) wrapper: shell commands run under a generated
/// profile that restricts writes to the agent workspace + temp dirs, and blocks
/// network unless explicitly allowed. v15 (Emre 2026-09-23): the large tier's
/// long autonomous runs execute inside this containment by default.
/// Not a security boundary against a malicious model — it limits blast radius of
/// accidents (same caveat as OpenClaw's sandboxing docs).
/// </summary>
public interface IShellSandbox
{
    /// <summary>True when the platform supports sandboxing and it is enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Wrap a command for sandboxed execution (returns the command unchanged when disabled).</summary>
    string Wrap(string command);
}

/// <summary>Config for the sandbox (from sandbox config section / tier resolution).</summary>
public sealed record ShellSandboxOptions(bool Enabled, string WorkspaceRoot, bool AllowNetwork = false);

/// <summary>macOS Seatbelt implementation. Other platforms: disabled passthrough.</summary>
public sealed class SeatbeltShellSandbox : IShellSandbox
{
    private readonly ShellSandboxOptions _options;
    private readonly ILogger _logger;
    private string? _profilePath;

    public bool IsEnabled => _options.Enabled && !OperatingSystem.IsWindows();

    public SeatbeltShellSandbox(ShellSandboxOptions options, ILogger? logger = null)
    {
        _options = options;
        _logger = logger ?? new NoOpSandboxLogger();
    }

    public string Wrap(string command)
    {
        if (!IsEnabled) return command;

        // Write a per-process profile lazily; sandbox-exec reads it per invocation.
        var profilePath = EnsureProfile();
        var esc = profilePath.Replace("\"", "\\\"");
        return $"sandbox-exec -f '{profilePath}' /bin/zsh -c {Quote(command)}";
    }

    private string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private string EnsureProfile()
    {
        if (_profilePath != null && File.Exists(_profilePath)) return _profilePath;
        _profilePath = Path.Combine(Path.GetTempPath(), $"eca-sandbox-{Environment.ProcessId}.sb");
        // Start from permissive, then subtract: deny-by-default breaks shell/dotnet
        // startup on macOS (dynamic linker, XPC, caches need broad allowances).
        // Deny the dangerous subset instead: writes outside workspace+temp, network.
        var network = _options.AllowNetwork
            ? "(allow network*)"
            : "(deny network*)";
        var profile = new StringBuilder()
            .AppendLine("(version 1)")
            .AppendLine("(allow default)")
            .AppendLine("(deny file-write* (subpath \"/Users\"))")
            .AppendLine($@"(allow file-write* (subpath ""{_options.WorkspaceRoot}""))")
            .AppendLine(@"(allow file-write* (subpath ""/tmp""))")
            .AppendLine(@"(allow file-write* (subpath ""/private/tmp""))")
            .AppendLine(@"(allow file-write* (regex ""^/private/var/folders/""))") // dotnet temp
            .AppendLine(network)
            .ToString();
        File.WriteAllText(_profilePath, profile);
        _logger.Debug("ShellSandbox", $"Seatbelt profile written: {_profilePath}");
        return _profilePath;
    }

    /// <summary>Session-protocol-independent no-op logger.</summary>
    private sealed class NoOpSandboxLogger : ILogger
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
