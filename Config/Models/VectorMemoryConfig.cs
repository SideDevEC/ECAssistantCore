using System.Text.Json.Serialization;

namespace ECAssistant.Config;

public class VectorMemoryConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("directory")]
    public string Directory { get; set; } = "vecmem";
    [JsonPropertyName("max_results")]
    public int MaxResults { get; set; } = 5;
    [JsonPropertyName("auto_index")]
    public bool AutoIndex { get; set; } = true;
}