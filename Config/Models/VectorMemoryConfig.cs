using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class VectorMemoryConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
    [JsonPropertyName("directory")]
    public string Directory { get; init; } = "vecmem";
    [JsonPropertyName("max_results")]
    public int MaxResults { get; init; } = 5;
    [JsonPropertyName("auto_index")]
    public bool AutoIndex { get; init; } = true;
}