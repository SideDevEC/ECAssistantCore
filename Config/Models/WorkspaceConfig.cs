using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class WorkspaceConfig
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "Workspace";
    [JsonPropertyName("allow_delete")]
    public bool AllowDelete { get; init; } = true;
    [JsonPropertyName("max_size_mb")]
    public int MaxSizeMB { get; init; } = 500;
    [JsonPropertyName("auto_cleanup")]
    public bool AutoCleanup { get; init; } = true;
}