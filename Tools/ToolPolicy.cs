namespace ECAssistant.Core.Tools;

/// <summary>
/// Tool policy manager — checks if a tool requires user approval before execution.
/// Two states only: approved (runs immediately) or requires approval (user prompted y/n).
/// To completely disable a tool, set enabled: false in the tools config section.
/// </summary>
public class ToolPolicy
{
    private readonly HashSet<string> _requiresApproval = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolPermission> _permissions = new(StringComparer.OrdinalIgnoreCase);

    public ToolPolicy()
    {
        SetDefaultPermissions();
    }

    private void SetDefaultPermissions()
    {
        // ── Read-only tools: no approval needed ──
        // IMPORTANT: names must match the tool's `Name` property exactly.
        SetPermission("EFileResearchTool", approvalRequired: false, "Read-only research");
        SetPermission("EFileAnalyzer", approvalRequired: false, "Read-only analysis");
        SetPermission("EFileReader", approvalRequired: false, "Read-only file access");
        SetPermission("EDotnetBuild", approvalRequired: false, "Build only — no side effects");
        SetPermission("ESubAgent", approvalRequired: false, "Sub-agent orchestration");

        // ── Dangerous tools: require approval ──
        SetPermission("EShellAgent", approvalRequired: true, "Shell command execution");
        SetPermission("EGitTool", approvalRequired: true, "Git operations can push/commit");
        SetPermission("ECodeEditor", approvalRequired: true, "File modification");
        SetPermission("EBackgroundExec", approvalRequired: true, "Background process execution");
    }

    public void SetPermission(string toolName, bool approvalRequired, string? reason = null)
    {
        _permissions[toolName] = new ToolPermission { ToolName = toolName, Level = approvalRequired ? "ApprovalRequired" : "Allowed", Reason = reason };
        if (approvalRequired)
            _requiresApproval.Add(toolName);
        else
            _requiresApproval.Remove(toolName);
    }

    // v14.10.2: session-scoped pattern approvals ("remember this decision").
    // Key format: "ToolName:pattern" — e.g. "ECodeEditor:create" or "EShellAgent:git".
    private readonly HashSet<string> _sessionApprovals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// v14.10.2: remember an approval for the rest of the session (Emre's
    /// Claude-Code-style remember-decision). Cleared when the process exits —
    /// nothing is persisted, so a fresh start is always fully gated again.
    /// </summary>
    public void ApproveSessionPattern(string toolName, string pattern)
    {
        if (!string.IsNullOrWhiteSpace(toolName) && !string.IsNullOrWhiteSpace(pattern))
            _sessionApprovals.Add($"{toolName.Trim()}:{pattern.Trim()}");
    }

    /// <summary>True when the tool+pattern was approved for this session.</summary>
    public bool IsSessionApproved(string toolName, string pattern)
        => _sessionApprovals.Contains($"{toolName.Trim()}:{pattern.Trim()}");

    public bool RequiresApproval(string toolName) => _requiresApproval.Contains(toolName);
    public bool IsAllowed(string toolName) => !RequiresApproval(toolName);

    public List<ToolPermission> GetAllPermissions() => _permissions.Values.ToList();

    public ToolPolicyDecision Check(string toolName, Dictionary<string, string?> args)
    {
        if (RequiresApproval(toolName))
        {
            // v14.10.2: session-scoped remember-decision check (before any prompting).
            var sessionPattern = BuildSessionPattern(toolName, args);
            if (!string.IsNullOrEmpty(sessionPattern) && IsSessionApproved(toolName, sessionPattern))
                return new ToolPolicyDecision { CanExecute = true, NeedsApproval = false, Message = $"Allowed by session approval ({sessionPattern})." };
            // EShellAgent: inspect the command to decide if approval is truly needed.
            // Read-only commands (ls, cat, date, echo, grep, etc.) are auto-approved.
            // Only commands that modify/delete/create files or change system state need approval.
            if (toolName == "EShellAgent" && args.TryGetValue("command", out var cmd) && !string.IsNullOrWhiteSpace(cmd))
            {
                if (IsReadOnlyCommand(cmd))
                    return new ToolPolicyDecision { CanExecute = true, NeedsApproval = false, Message = "Read-only command — auto-approved." };
                return new ToolPolicyDecision { CanExecute = false, NeedsApproval = true, Message = $"Shell command requires approval: {TruncateForDisplay(cmd, 80)}" };
            }

            return new ToolPolicyDecision { CanExecute = false, NeedsApproval = true, Message = $"Tool '{toolName}' requires approval." };
        }

        return new ToolPolicyDecision { CanExecute = true, NeedsApproval = false, Message = "Allowed" };
    }

    /// <summary>
    /// Determine if a shell command is read-only (no file modification, no system changes).
    /// Supports PowerShell (pwsh) and POSIX (bash/zsh) command patterns.
    /// </summary>
    private static bool IsReadOnlyCommand(string command)
    {
        var lower = command.Trim().ToLowerInvariant();

        // ── Multi-command safety: if the command contains ; || && | followed by a write command, require approval ──
        // Split on command separators and check each sub-command
        var subCommands = lower.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var sub in subCommands)
        {
            var trimmed = sub.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // ── Shell redirection writes files: > >> < << heredocs ──
            // Detect outside quotes to tolerate echo "a > b" style literals.
            if (HasRedirection(trimmed)) return false;

            // Handle pipe: check the leftmost command (pipe receivers are read-only consumers)
            // but if any part of the pipe writes files, require approval
            var pipeParts = trimmed.Split('|');
            foreach (var part in pipeParts)
            {
                var p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;

                // If any part is a write command, the whole thing needs approval
                if (IsWriteCommand(p))
                    return false;
            }

            // Check && and || chains
            var chainParts = trimmed.Split(new[] { "&&", "||" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in chainParts)
            {
                var p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;
                var pipeParts2 = p.Split('|');
                foreach (var pp in pipeParts2)
                {
                    if (IsWriteCommand(pp.Trim()))
                        return false;
                }
            }
        }

        // All sub-commands are read-only
        return true;
    }

    /// <summary>True when an unquoted shell redirection operator is present.</summary>
    private static bool HasRedirection(string command)
    {
        var inDouble = false; var inSingle = false;
        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (ch == '"' && !inSingle) inDouble = !inDouble;
            else if (ch == '\'' && !inDouble) inSingle = !inSingle;
            else if (!inDouble && !inSingle)
            {
                // 2>&1 / 1>&2 only re-route an existing stream — they do not write files.
                if (ch == '2' && i + 3 < command.Length && command[i + 1] == '>' && command[i + 2] == '&' && command[i + 3] == '1') { i += 3; continue; }
                if (ch == '1' && i + 3 < command.Length && command[i + 1] == '>' && command[i + 2] == '&' && command[i + 3] == '2') { i += 3; continue; }
                if (ch == '>') return true;   // > and >>
                if (ch == '<') return true;   // < and <<
            }
        }
        return false;
    }

    /// <summary>
    /// Check if a single command verb is a write/modification command.
    /// </summary>
    private static bool IsWriteCommand(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return false;
        var c = cmd.Trim().ToLowerInvariant();

        // ── PowerShell write commands ──
        string[] pwshWrite = {
            "set-content", "set-item", "set-itemproperty", "set-location",
            "add-content", "add-member", "clear-content", "clear-item",
            "copy-item", "move-item", "remove-item", "rename-item",
            "new-item", "new-file", "new-directory", "mkdir", "md",
            "out-file",
            "set-variable", "remove-variable",
            "start-process", "stop-process", "stop-job",
            "invoke-webrequest", "iwr", "curl", "wget",
            "expand-archive", "compress-archive",
            "install-module", "install-package",
            "del ", "rm ", "rmdir ", "rd ", "erase ",
            "cp ", "copy ", "mv ", "move ",
            "touch ", "tee ",
            "format-volume", "clear-disk",
        };

        // ── POSIX write commands ──
        string[] posixWrite = {
            "rm ", "rmdir ", "rm -", "mkdir ", "mkdir -",
            "mv ", "cp ", "touch ", "tee ",
            "chmod ", "chown ", "chgrp ",
            "dd ", "mkfs", "mount ", "umount",
            "kill ", "killall ", "pkill ",
            "apt ", "apt-get ", "brew ", "pip install", "npm install", "dotnet ",
            "git add", "git commit", "git push", "git pull", "git merge", "git rebase", "git reset", "git checkout", "git stash",
            "sed -i", "sed --in-place",
            "truncate ",
            "ln -s", "ln ",
            "curl -o", "curl -O", "wget ",
            "tar ", "zip ", "unzip ",
            "scp ", "rsync ",
            "crontab ",
        };

        // Check file-writing redirects (>, >>) anywhere in the command.
        // HasRedirection ignores 2>&1/1>&2 — those alone must NOT force approval.
        if (HasRedirection(c))
            return true;

        // ── Command/process substitution can execute arbitrary code ──
        if (c.Contains("$(") || c.Contains("`"))   // $(…) and backticks
            return true;
        if (c.Contains("<("))                       // process substitution (bash/zsh)
            return true;

        // ── Environment-variable prefixes: FOO=bar cmd — the real command hides after the assignment ──
        var firstToken = c.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (firstToken.Length > 1 && firstToken.IndexOf('=') > 0 &&
            char.IsLetter(firstToken[0]) && firstToken.Take(firstToken.IndexOf('=')).All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
            return true;

        // ── Nested interpreters / shells that can execute arbitrary code ──
        // These must require approval because the inner command is not inspected.
        string[] nestedExec = {
            "bash ", "bash -", "sh ", "sh -", "zsh ", "zsh -",
            "sudo ", "su ",
            "python ", "python3 ", "python -", "python3 -",
            "perl ", "perl -", "ruby ", "ruby -",
            "node ", "node -",
            "eval ", "exec ",
            "xargs ",
            "invoke-expression", "iex ",
        };

        foreach (var w in nestedExec)
        {
            if (c.StartsWith(w) || c == w.Trim())
                return true;
        }

        // ── find with -delete or -exec is destructive ──
        if (c.StartsWith("find ") && (c.Contains("-delete") || c.Contains("-exec") || c.Contains("-ok")))
            return true;

        // Check against write command prefixes
        foreach (var w in pwshWrite)
        {
            if (c.StartsWith(w) || c == w.Trim())
                return true;
        }
        foreach (var w in posixWrite)
        {
            if (c.StartsWith(w) || c == w.Trim())
                return true;
        }

        // PowerShell alias checks
        if (c.StartsWith("ni ") || c.StartsWith("cp ") || c.StartsWith("mv ") ||
            c.StartsWith("rm ") || c.StartsWith("del ") || c.StartsWith("rd ") ||
            c.StartsWith("rni ") || c.StartsWith("rnp "))
            return true;

        return false;
    }

    private static string TruncateForDisplay(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
    }

    public void LoadFromConfig(List<ToolPermissionConfigEntry>? entries)
    {
        if (entries == null) return;
        foreach (var entry in entries)
            SetPermission(entry.ToolName, entry.ApprovalRequired, entry.Reason);
    }

    /// <summary>
    /// Load system-critical tool permissions. Default is approvalRequired = true.
    /// </summary>
    public void LoadSystemTools(List<SystemToolConfigEntry>? entries)
    {
        if (entries == null) return;
        foreach (var entry in entries)
            SetPermission(entry.ToolName, entry.ApprovalRequired, entry.Reason ?? "System-critical tool");
    }


    /// <summary>
    /// v14.10.2: coarse per-call pattern for remember-decision approvals.
    /// EShellAgent → first token of the command (e.g. "git", "dotnet", "ls");
    /// ECodeEditor → the action argument; otherwise the first present arg value
    /// (truncated). Deliberately coarser than full signatures: the point is to
    /// stop re-asking for the same KIND of action, not to whitelist exact calls.
    /// Stateless utility — no mutable state.
    /// </summary>
    public static string BuildSessionPattern(string toolName, Dictionary<string, string?> args)
    {
        if (toolName == "EShellAgent" && args.TryGetValue("command", out var cmd) && !string.IsNullOrWhiteSpace(cmd))
        {
            var token = cmd.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return token.Length > 0 ? token[0].ToLowerInvariant() : "";
        }
        if (args.TryGetValue("action", out var action) && !string.IsNullOrWhiteSpace(action))
            return action.ToLowerInvariant().Trim();
        foreach (var kv in args)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value))
                return kv.Value.Length > 40 ? kv.Value[..40] : kv.Value;
        }
        return "";
    }

}
