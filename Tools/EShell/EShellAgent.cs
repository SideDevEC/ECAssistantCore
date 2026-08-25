using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Tools.Shell;

/// <summary>
/// Shell Agent Tool — the primary tool for all file and system operations.
/// </summary>
public class EShellAgent : EToolBase
{
    private readonly IProcessRunner _processRunner;
    private readonly string _workingDirectory;
    private readonly JsonElement? _toolConfig;
    private readonly int _maxOutputChars;

    public override string Name => "EShellAgent";

    public override string Description =>
        "Full filesystem and shell command execution. " +
        "Can read/write/copy/move/delete files and folders, run any shell command, " +
        "compile code, search files, manage projects. " +
        "Working directory is set automatically — use relative paths.";

    public override string UsageExample =>
        "<toolcall>EShellAgent<command>Get-ChildItem</command></toolcall>";

    public override bool IsEnabled { get; protected set; } = true;
    public override bool IsSystemCritical => true;

    public EShellAgent(IProcessRunner processRunner, EAgentConfig config, string workingDirectory)
    {
        _processRunner = processRunner;
        _workingDirectory = Path.GetFullPath(workingDirectory);
        config.Tools.TryGetValue(Name, out var tc);
        _toolConfig = tc.ValueKind == JsonValueKind.Undefined ? null : tc;
        IsEnabled = ReadCfg(_toolConfig, "enabled", true);
        _maxOutputChars = ReadCfg(_toolConfig, "max_output_chars", 50000);
    }

    public override object GetConfigSection() => new
    {
        enabled = true,
        max_output_chars = 50000
    };

    public override async Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        arguments ??= new Dictionary<string, string?>();
        var command = arguments.GetValueOrDefault("command")?.Trim();
        if (string.IsNullOrWhiteSpace(command))
            return EToolResult.Failure(Name, "Missing command argument.");

        try
        {
            var result = await RunShellAsync(command, _workingDirectory, cancellationToken);

            var hasStderrOutput = !string.IsNullOrWhiteSpace(result.StandardError);

            if (result.ExitCode == 0 && !hasStderrOutput)
            {
                var output = string.IsNullOrEmpty(result.StandardOutput)
                    ? "Command completed (no output)."
                    : result.StandardOutput;
                if (output.Length > _maxOutputChars)
                    output = output.Substring(0, _maxOutputChars) + "\n... [truncated]";
                return EToolResult.Success(Name, output);
            }
            else if (result.ExitCode == 0 && hasStderrOutput)
            {
                var output = string.IsNullOrEmpty(result.StandardOutput)
                    ? $"Command completed but produced error output:\nSTDERR: {result.StandardError}"
                    : $"{result.StandardOutput}\n\nSTDERR: {result.StandardError}";
                if (output.Length > _maxOutputChars)
                    output = output.Substring(0, _maxOutputChars) + "\n... [truncated]";
                return EToolResult.Success(Name, output);
            }
            else
            {
                return EToolResult.Failure(Name, $"Shell Error (Exit {result.ExitCode})\nSTDERR: {result.StandardError}\nCommand: {command}");
            }
        }
        catch (Exception ex)
        {
            return EToolResult.Failure(Name, $"Execution failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ProcessRunner now owns OS-aware shell selection (pwsh on Windows, zsh on macOS, bash on Linux).
    // EShellAgent passes the raw command — no double-wrapping.
    private async Task<ShellProcessResult> RunShellAsync(string command, string workingDir, CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.ExecuteAsync(command, workingDir, cancellationToken);
        return new ShellProcessResult(result.StdOut, result.StdErr, result.ExitCode);
    }

    private string EscapeXml(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Replace("<", "&lt;").Replace(">", "&gt;");
    }
}

internal record ShellProcessResult(string StandardOutput, string StandardError, int ExitCode);