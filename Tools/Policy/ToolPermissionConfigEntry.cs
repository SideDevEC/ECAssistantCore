using System.Text.Json.Serialization;

namespace ECAssistant.Core.Tools;

/// <summary>
/// Config entry for tool permissions in appsettings.json.
/// </summary>
public class ToolPermissionConfigEntry
{
    [JsonPropertyName("tool")]
    public string ToolName { get; init; } = "";

    [JsonPropertyName("level")]
    public string Level { get; init; } = "Allowed";

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}