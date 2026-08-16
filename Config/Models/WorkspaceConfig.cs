using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class WorkspaceConfig
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "Workspace";
    [JsonPropertyName("allow_delete")]
    public bool AllowDelete { get; set; } = true;
    [JsonPropertyName("max_size_mb")]
    public int MaxSizeMB { get; set; } = 500;
    [JsonPropertyName("auto_cleanup")]
    public bool AutoCleanup { get; set; } = true;
}