using System.Text.Json.Serialization;

namespace ECAssistant.Core.Tools;

/// <summary>
/// Config entry for system-critical tools.
/// These tools always register — cannot be disabled.
/// Only the permission level can be configured: Allowed or ApprovalRequired.
/// Default: ApprovalRequired.
/// </summary>
public sealed class SystemToolConfigEntry
{
    [JsonPropertyName("tool")]
    public string ToolName { get; init; } = "";

    /// <summary>Allowed or ApprovalRequired. Never Blocked. Default: ApprovalRequired.</summary>
    [JsonPropertyName("level")]
    public string Level { get; init; } = "ApprovalRequired";

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}