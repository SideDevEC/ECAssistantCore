using System.Text.Json.Serialization;

namespace ECAssistant.Core.Tools;

/// <summary>
/// Permission level for a tool.
/// </summary>
public enum ToolPermissionLevel
{
    Allowed,
    ApprovalRequired,
    Blocked
}