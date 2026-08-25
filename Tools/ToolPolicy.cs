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
            return new ToolPolicyDecision { CanExecute = false, NeedsApproval = true, Message = $"Tool '{toolName}' requires approval." };

        return new ToolPolicyDecision { CanExecute = true, NeedsApproval = false, Message = "Allowed" };
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