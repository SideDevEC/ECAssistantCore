using System.Text.Json.Serialization;

namespace ECAssistant.Core.Tools;

/// <summary>
/// Config entry for tool permissions in appsettings.json.
/// Simple: approvalRequired = true means user is prompted before execution.
/// approvalRequired = false (default) means tool runs immediately.
/// To completely disable a tool, set enabled: false in the tools section.
/// </summary>
public class ToolPermissionConfigEntry
{
    [JsonPropertyName("tool")]
    public string ToolName { get; init; } = "";

    [JsonPropertyName("approvalRequired")]
    public bool ApprovalRequired { get; init; } = false;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}