using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class InferenceConfig
{
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = 8192;
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.3f;
    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.9f;
    [JsonPropertyName("repeat_penalty")]
    public float RepeatPenalty { get; init; } = 1.1f;
    [JsonPropertyName("anti_prompts")]
    public string[] AntiPrompts { get; init; } = new[] {
        "</s>",
        "\n```\n",
        "User:",
        "Question:"
    };
    [JsonPropertyName("tokens_keep")]
    public int TokensKeep { get; init; } = 0;
    [JsonPropertyName("overflow_strategy")]
    public string OverflowStrategy { get; init; } = "ThrowException";
    [JsonPropertyName("decode_special_tokens")]
    public bool DecodeSpecialTokens { get; init; } = false;
}