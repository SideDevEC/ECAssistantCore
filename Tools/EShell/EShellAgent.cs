using System.Text.Json;
using ECAssistant.Core.Tools.Build;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using ECAssistant.Core.Config;
using ECAssistant.Core.Services.Shell;
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

    // v15: persistent shell session (large tier) — cwd/env survive across calls.
    private readonly bool _usePersistentSession;
    private IShellSession? _session;
    private readonly IShellSessionFactory? _sessionFactory;
    private readonly bool _isLargeTier;
    private readonly IShellSandbox _sandbox;

    public override string Name => "EShellAgent";

    public override string Description =>
        "Full filesystem and shell command execution on the host OS. " +
        "Can read/write/copy/move/delete files and folders, run any shell command, " +
        "compile code, search files, manage projects. " +
        "NEVER use heredocs ('<<') to write files — use ECodeEditor with action=create and the 'content' argument instead. " +
        "Use ONLY when the request requires actually running commands on this machine (file " +
        "operations, installs, system state). Do NOT use for knowledge or coding questions you " +
        "can answer directly. " +
        "Working directory is set automatically — use relative paths.\n" +
        HostShellPrompt();

    public override string GetParameterSchema() =>
        """
        {
          "type": "object", "required": ["command"],
          "properties": { "command": { "type": "string", "description": "The shell command to execute. Quote paths with spaces." } }
        }
        """;
    public override string UsageExample => OperatingSystem.IsWindows()
        ? "EShellAgent(command:Get-ChildItem -Force)"
        : "EShellAgent(command:ls -la)";

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

    public EShellAgent(IProcessRunner processRunner, AppConfig config, string workingDirectory,
        IShellSessionFactory? sessionFactory = null, bool isLargeTier = false, IShellSandbox? sandbox = null)
    {
        _processRunner = processRunner;
        _workingDirectory = Path.GetFullPath(workingDirectory);
        config.Tools.TryGetValue(Name, out var tc);
        _toolConfig = tc.ValueKind == JsonValueKind.Undefined ? null : tc;
        IsEnabled = ReadCfg(_toolConfig, "enabled", true);
        _maxOutputChars = ReadCfg(_toolConfig, "max_output_chars", 50000);
        _sessionFactory = sessionFactory;
        _isLargeTier = isLargeTier;
        // v15: persistent session on every tier/platform that can host one
        // (POSIX: zsh/bash; Windows: pwsh). Requires a session factory (hosts that
        // wire it opt in; direct ctor use without factory = isolated calls, keeping
        // mock-based tests and minimal embeds working). Config kill-switch still wins.
        _usePersistentSession = sessionFactory != null
            && ReadCfg(_toolConfig, "persistent_session", true)
            && (PersistentShellSession.IsSupported || PersistentPowerShellSession.IsSupported);
        // v15: sandboxing enabled for the large tier by default (config can disable).
        var sandboxEnabled = isLargeTier && ReadCfg(_toolConfig, "sandbox", true);
        _sandbox = sandbox ?? new SeatbeltShellSandbox(
            new ShellSandboxOptions(sandboxEnabled, _workingDirectory), null);
    }

    public override object GetConfigSection() => new
    {
        enabled = true,
        max_output_chars = 50000
    };

    /// <summary>
    /// v14.20: semantic render for known noisy command families. dotnet
    /// build/test output collapses via BuildOutputRenderer; everything else is
    /// passthrough (shell output is command-specific — no blanket guessing).
    /// </summary>
    public override string RenderForModel(string rawOutput)
        => BuildOutputRenderer.IsDotnetOutput(rawOutput)
            ? BuildOutputRenderer.Render(rawOutput)
            : rawOutput;

    public override async Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        arguments ??= new Dictionary<string, string?>();
        var command = arguments.GetValueOrDefault("command")?.Trim();
        if (string.IsNullOrWhiteSpace(command))
            return EToolResult.Failure(Name, "Missing command argument.");

        try
        {
            // v14.10.2: heredoc-collapse guard. Small/grammar-constrained models emit
            // tool-call args with newlines collapsed to spaces, turning a multi-line
            // heredoc into ONE line — which zsh -c treats as "delimiter never found":
            // exit 0, empty output, and NO side effect (verified repro). Report it
            // instead of reporting SUCCESS on a silent no-op.
            if (command.Contains("<<") && !command.Contains('\n'))
            {
                return EToolResult.Failure(Name,
                    "Command contains a heredoc ('<<') but has no newlines — the heredoc body was lost when the arguments were produced, so this command would do nothing. " +
                    "Do NOT use shell heredocs to write files: use the ECodeEditor tool with action=create and pass the file content in the 'content' argument instead.");
            }

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
        // v15: large tier + POSIX → persistent session (cwd/env persist across calls).
        if (_usePersistentSession)
        {
            try
            {
                _session ??= await (_sessionFactory ?? new ShellSessionFactory()).CreateAsync(_workingDirectory, cancellationToken);
                if (_session.IsDead) { _session = null; } // recreate once on crash
                _session ??= await (_sessionFactory ?? new ShellSessionFactory()).CreateAsync(_workingDirectory, cancellationToken);
                var r = await _session.RunAsync(command, cancellationToken);
                return new ShellProcessResult(r.StdOut, r.StdErr, r.ExitCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _session = null; // next call falls back to isolated execution
                return new ShellProcessResult("", $"[persistent shell unavailable: {ex.Message}; ran isolated]", -1);
            }
        }
        // v15: large tier executes inside the Seatbelt sandbox (workspace-writes-only).
        var result = await _processRunner.ExecuteAsync(_sandbox.Wrap(command), workingDir, cancellationToken);
        return new ShellProcessResult(result.StdOut, result.StdErr, result.ExitCode);
    }
        /// <summary>v15: dispose the persistent shell session + sandbox profile (run teardown).</summary>
    public async Task DisposeSessionAsync()
    {
        if (_session != null)
        {
            try { await _session.DisposeAsync(); } catch { /* best-effort */ }
            _session = null;
        }
        if (_sandbox is SeatbeltShellSandbox seatbelt)
        {
            try { seatbelt.Cleanup(); } catch { /* best-effort */ }
        }
    }

    /// <summary>v15: small tier gets literal do-not rules; large tier gets judgment-based guidance.</summary>
        public override string GetToolRulesForTier(bool isLargeTier)
        {
            if (isLargeTier)
            {
                return "Rules:\n" +
                       "- Your commands run in a PERSISTENT shell session: your working directory (cd) and exported environment variables survive between calls. Do NOT re-cd or re-export on every call — run pwd only if unsure where you are.\n" +
                       "- Commands execute inside a Seatbelt sandbox: file WRITES are allowed only inside the agent workspace and /tmp. Writes elsewhere (e.g. your home directory) fail with 'Operation not permitted' — that is the sandbox working as intended, not a bug. Keep all writes inside the workspace.\n" +
                       "- Network access is denied in the sandbox. If a command fails with connection or permission errors, report it — do not retry.\n" +
                       "- Combine related shell steps with && or ; when safe — fewer, larger calls beat many round-trips.\n" +
                       "- Prefer targeted output (head/tail/grep) over dumping unbounded streams.\n";
            }
            return "Rules:\n" +
                   "- Your commands run in a PERSISTENT shell session: your working directory (cd) and exported environment variables survive between calls. Do NOT re-cd or re-export on every call.\n" +
                   "- ONE command per call. NEVER chain with && or ; — separate calls only.\n" +
                   "- NEVER use interactive commands (vim, top, sudo). They hang the agent.\n" +
                   "- NEVER redirect output to files the user did not ask for.\n" +
                   "- If a command returns an error, do NOT repeat it unchanged. Fix the path/argument or report the error.\n" +
                   "- Keep output small: use head, tail, or grep instead of printing everything.\n";
        }

}

internal record ShellProcessResult(string StandardOutput, string StandardError, int ExitCode);