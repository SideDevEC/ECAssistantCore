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
        SetPermission("EFileResearchTool", approvalRequired: false, "Read-only research");
        SetPermission("EFileAnalyzer", approvalRequired: false, "Read-only analysis");
        SetPermission("EFileReaderTool", approvalRequired: false, "Read-only file access");
        SetPermission("EWebSearchTool", approvalRequired: false, "Read-only web search");
        SetPermission("EWebFetchTool", approvalRequired: false, "Read-only web fetch");
        SetPermission("EDotnetBuildTool", approvalRequired: false, "Build only — no side effects");
        SetPermission("ESubAgentTool", approvalRequired: false, "Sub-agent orchestration");

        // ── Dangerous tools: require approval ──
        SetPermission("EShellAgent", approvalRequired: true, "Shell command execution");
        SetPermission("EGitTool", approvalRequired: true, "Git operations can push/commit");
        SetPermission("ECodeEditorTool", approvalRequired: true, "File modification");
        SetPermission("EBackgroundExecTool", approvalRequired: true, "Background process execution");
    }

    public void SetPermission(string toolName, bool approvalRequired, string? reason = null)
    {
        _permissions[toolName] = new ToolPermission { ToolName = toolName, Level = approvalRequired ? "ApprovalRequired" : "Allowed", Reason = reason };
        if (approvalRequired)
            _requiresApproval.Add(toolName);
        else
            _requiresApproval.Remove(toolName);
    }

    public bool RequiresApproval(string toolName) => _requiresApproval.Contains(toolName);
    public bool IsAllowed(string toolName) => !RequiresApproval(toolName);

    public List<ToolPermission> GetAllPermissions() => _permissions.Values.ToList();

    public ToolPolicyDecision Check(string toolName, Dictionary<string, string?> args)
    {
        if (RequiresApproval(toolName))
        {
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
            else if (!inDouble && !inSingle && ch == '>' ) return true;   // > and >>
            else if (!inDouble && !inSingle && ch == '<' ) return true;   // < and <<
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

        // Check redirects (>, >>) anywhere in the command
        if (c.Contains(">") || c.Contains(">>"))
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
}