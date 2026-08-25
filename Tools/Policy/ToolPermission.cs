namespace ECAssistant.Core.Tools;

/// <summary>
/// Permission rule for a single tool.
/// Level is a string ("Allowed" or "ApprovalRequired") for display purposes.
/// </summary>
public class ToolPermission
{
    public string ToolName { get; init; } = "";
    public string Level { get; init; } = "Allowed";
    public string? Reason { get; init; }
}