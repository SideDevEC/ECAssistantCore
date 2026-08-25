using System.Text.Json.Serialization;

namespace ECAssistant.Core.Tools;

/// <summary>
/// Config entry for system-critical tools.
/// These tools always register — cannot be disabled.
/// approvalRequired = true (default) means user is prompted before execution.
/// approvalRequired = false means tool runs immediately.
/// </summary>
public sealed class SystemToolConfigEntry
{
    [JsonPropertyName("tool")]
    public string ToolName { get; init; } = "";

    [JsonPropertyName("approvalRequired")]
    public bool ApprovalRequired { get; init; } = true;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}