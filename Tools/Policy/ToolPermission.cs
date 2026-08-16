using System.Text.Json.Serialization;

namespace ECAssistant.Core.Tools;

/// <summary>
/// Permission rule for a single tool.
/// </summary>
public class ToolPermission
{
    public string ToolName { get; init; } = "";
    public ToolPermissionLevel Level { get; init; } = ToolPermissionLevel.Allowed;
    public List<string>? ApprovalPatterns { get; init; }
    public string? Reason { get; init; }
}