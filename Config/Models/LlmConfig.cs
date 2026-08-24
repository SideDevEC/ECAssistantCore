using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// LLM configuration. Model loading params (gpu_layers, batch_size, threads) live in
/// ECAssistantLLM's llm-server.json, NOT here. Core only needs model_path (for display)
/// and context_size (for validation and sub-agent context windows).
/// </summary>
public class LlmConfig
{
    [JsonPropertyName("model_path")]
    public string ModelPath { get; init; } = "";
    [JsonPropertyName("context_size")]
    public uint ContextSize { get; init; } = 16384;
}