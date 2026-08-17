using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// Summarization settings. When use_llm is true, uses StatelessExecutor
/// with shared main weights. When false, falls back to extractive truncation.
/// </summary>
public class SummarizeConfig
{
    [JsonPropertyName("use_llm")]
    public bool UseLlm { get; init; } = true;
    [JsonPropertyName("context_size")]
    public uint ContextSize { get; init; } = 4096;
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = 200;
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.1f;
    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.8f;
    [JsonPropertyName("top_k")]
    public int TopK { get; init; } = 40;
    [JsonPropertyName("repeat_penalty")]
    public float RepeatPenalty { get; init; } = 1.1f;
    [JsonPropertyName("anti_prompts")]
    public string[] AntiPrompts { get; init; } = new[] { "User:", "Question:", "</lm>" };
}