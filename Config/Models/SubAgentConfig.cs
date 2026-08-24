using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// Sub-agent configuration. GPU/thread params are server-side concerns
/// (live in ECAssistantLLM's llm-server.json). Core only controls session behavior.
/// </summary>
public class SubAgentConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
    [JsonPropertyName("context_size")]
    public uint ContextSize { get; init; } = 16384;
    [JsonPropertyName("max_concurrent")]
    public int MaxConcurrent { get; init; } = 3;
    [JsonPropertyName("max_turns")]
    public int MaxTurns { get; init; } = 5;
    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; init; } = 120;
    [JsonPropertyName("max_tool_calls")]
    public int MaxToolCalls { get; init; } = 20;
    [JsonPropertyName("max_retries")]
    public int MaxRetries { get; init; } = 1;
}