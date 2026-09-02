using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// Task decomposition settings. When use_llm is true, uses HTTP streaming
/// inference (stateless mode). When false, falls back to keyword-based TaskPlanner.
/// </summary>
public class DecomposeConfig
{
    [JsonPropertyName("use_llm")]
    public bool UseLlm { get; init; } = true;
    [JsonPropertyName("context_size")]
    public uint ContextSize { get; init; } = 4096;
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = 256;
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.1f;
    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.8f;
    [JsonPropertyName("top_k")]
    public int TopK { get; init; } = 40;
    [JsonPropertyName("repeat_penalty")]
    public float RepeatPenalty { get; init; } = 1.1f;
    [JsonPropertyName("anti_prompts")]
    public string[] AntiPrompts { get; init; } = new[] { "User:", "Question:" };
}