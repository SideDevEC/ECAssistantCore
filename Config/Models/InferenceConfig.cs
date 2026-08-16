using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class InferenceConfig
{
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 8192;
    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.3f;
    [JsonPropertyName("top_p")]
    public float TopP { get; set; } = 0.9f;
    [JsonPropertyName("repeat_penalty")]
    public float RepeatPenalty { get; set; } = 1.1f;
    [JsonPropertyName("anti_prompts")]
    public string[] AntiPrompts { get; set; } = new[] {
        "</s>",
        "\n```\n",
        "User:",
        "Question:"
    };
    [JsonPropertyName("tokens_keep")]
    public int TokensKeep { get; set; } = 0;
    [JsonPropertyName("overflow_strategy")]
    public string OverflowStrategy { get; set; } = "ThrowException";
    [JsonPropertyName("decode_special_tokens")]
    public bool DecodeSpecialTokens { get; set; } = false;
}