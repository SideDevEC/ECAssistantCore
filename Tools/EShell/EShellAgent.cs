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
        "Full filesystem and shell command execution on the host OS. " +
        "Can read/write/copy/move/delete files and folders, run any shell command, " +
        "compile code, search files, manage projects. " +
        "Working directory is set automatically — use relative paths.\n" +
        HostShellPrompt();

    public override string UsageExample => OperatingSystem.IsWindows()
        ? "<toolcall>EShellAgent<command>Get-ChildItem -Force</command></toolcall>"
        : "<toolcall>EShellAgent<command>ls -la</command></toolcall>";

    /// <summary>OS-aware shell guidance: exact shell name + worked examples that are valid on THIS host.</summary>
    // Stateless utility — no mutable state; depends only on the host OS.
    private static string HostShellPrompt()
    {
        var rules =
            "Rules:\n" +
            "1. ALWAYS quote paths that contain spaces. An unquoted path is split into multiple arguments and fails.\n" +
            "2. When the user asks about file types, folders vs files, sizes or permissions, use a listing form that SHOWS entry types — a plain name-only listing cannot answer that.\n" +
            "3. If a command fails with an unknown-option or syntax error, the syntax is wrong for this shell — adapt the syntax for the next attempt (do not repeat it).\n" +
            "4. Prefer one well-formed command that answers the whole question over several narrow ones.\n" +
            "5. NEVER run interactive programs (editors like nano/vim, pagers like less/more, top, prompts waiting for input) — they hang this tool forever.\n" +
            "6. Glob patterns (*, ?) do NOT match dotfiles (.env, .gitignore, .*) — a glob listing can look empty while hidden files exist.\n" +
            "7. Read the error message PRECISELY — it names the real problem: 'No such file' = wrong path, 'Permission denied' = access, 'Is a directory' = missing recursive flag. Each has a completely different fix.\n";

        if (OperatingSystem.IsWindows())
        {
            return "This host runs PowerShell (pwsh/Windows PowerShell) — use PowerShell syntax.\n" + rules +
                   "Windows quirks:\n" +
                   "- There is no head/tail: use Get-Content -TotalCount N (first lines) / -Tail N (last lines)\n" +
                   "- Files may have CRLF line endings — end-of-line regex patterns need to account for \\r\n" +
                   "- '>' redirection in Windows PowerShell writes UTF-16 — downstream tools may see garbled bytes\n" +
                   "- Reserved filenames cannot be created: CON, PRN, AUX, NUL, COM1…\n" +
                   "- OneDrive paths may hold placeholder files that are not on disk until opened\n" +
                   "- Known folders are often REDIRECTED into OneDrive: if ~\\Desktop looks empty or missing, check ~\\OneDrive\\Desktop (same for Documents, Pictures)\n" +
                   "Examples (valid on this host):\n" +
                   "Get-ChildItem -Force ~\\Desktop          # list with types, incl. hidden\n" +
                   "Get-ChildItem \"~\\Desktop\\My Folder\"    # quoted path with spaces\n" +
                   "Get-ChildItem ~\\Desktop | Where-Object { $_.PSIsContainer }   # only folders\n" +
                   "Get-Content \"~\\Desktop\\notes.txt\" -TotalCount 20             # read a file\n" +
                   "Copy-Item \"~\\Desktop\\a.txt\" ~\\Documents\\b.txt               # copy\n";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "This host runs macOS — the shell is zsh (BSD userland: some GNU options differ, e.g. date has no -d).\n" + rules +
                   "macOS quirks:\n" +
                   "- ~ does NOT expand inside quotes: use ~/\"My Folder\" (tilde outside, quote only the rest)\n" +
                   "- Listings may contain .DS_Store / ._* metadata files — filter them out before counting\n" +
                   "- APFS stores some filenames Unicode-decomposed (NFD): a composed-text search can match nothing\n" +
                   "- sed in-place editing needs an empty backup arg: sed -i '' \"s/a/b/\" file\n" +
                   "- BSD find has NO -printf; BSD grep has NO -P (use -E); there is NO timeout and NO readlink -f on this host\n" +
                   "Examples (valid on this host):\n" +
                   "ls -la ~/Desktop                         # list with types, sizes, permissions\n" +
                   "ls -la ~/Desktop/\"My Folder\"            # tilde outside the quotes!\n" +
                   "find ~/Desktop -maxdepth 1 -type d       # only folders\n" +
                   "head -n 20 ~/Desktop/notes.txt           # read a file\n" +
                   "cp ~/Desktop/a.txt ~/Documents/b.txt     # copy\n" +
                   "date '+%A %B %e, %Y'                     # formatted date (BSD syntax)\n";
        }
        return "This host runs Linux — the shell is bash (GNU userland).\n" + rules +
               "Linux quirks:\n" +
               "- ~ does NOT expand inside quotes: use ~/\"My Folder\" (tilde outside, quote only the rest)\n" +
               "- GNU tools: find -printf, timeout, readlink -f and grep -P all exist here (unlike macOS)\n" +
               "Examples (valid on this host):\n" +
               "ls -la ~/Desktop                         # list with types, sizes, permissions\n" +
               "ls -la ~/\"My Folder\"                   # quoted path with spaces\n" +
               "find ~/Desktop -maxdepth 1 -type d       # only folders\n" +
               "head -n 20 ~/Desktop/notes.txt           # read a file\n" +
               "cp ~/Desktop/a.txt ~/Documents/b.txt     # copy\n" +
               "date -d tomorrow '+%A %B %e, %Y'         # GNU date\n";
    }

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
                // Failures must include stdout too — many tools print the real cause
                // to stdout and only exit non-zero.
                var failOutput = string.IsNullOrWhiteSpace(result.StandardOutput)
                    ? ""
                    : result.StandardOutput.TrimEnd();
                var failMsg = $"Shell Error (Exit {result.ExitCode})\nSTDERR: {result.StandardError}\n" +
                              (failOutput.Length > 0 ? $"STDOUT: {failOutput}\n" : "") +
                              $"Command: {command}";
                if (failMsg.Length > _maxOutputChars)
                    failMsg = failMsg.Substring(0, _maxOutputChars) + "\n... [truncated]";
                return EToolResult.Failure(Name, failMsg);
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
}

internal record ShellProcessResult(string StandardOutput, string StandardError, int ExitCode);