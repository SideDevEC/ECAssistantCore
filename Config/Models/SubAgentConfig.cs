using System.Text.Json.Serialization;

namespace ECAssistant.Config;

public class SubAgentConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("context_size")]
    public uint ContextSize { get; set; } = 16384;
    [JsonPropertyName("gpu_layers")]
    public int GpuLayers { get; set; } = 15;
    [JsonPropertyName("threads")]
    public int Threads { get; set; } = -1;
    [JsonPropertyName("max_concurrent")]
    public int MaxConcurrent { get; set; } = 3;
    [JsonPropertyName("max_turns")]
    public int MaxTurns { get; set; } = 5;
    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 120;
    [JsonPropertyName("max_tool_calls")]
    public int MaxToolCalls { get; set; } = 20;
    [JsonPropertyName("max_retries")]
    public int MaxRetries { get; set; } = 1;
}