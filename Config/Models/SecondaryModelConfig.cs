using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class SecondaryModelConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = false;
    [JsonPropertyName("model_path")]
    public string ModelPath { get; init; } = "";
    [JsonPropertyName("context_size")]
    public uint ContextSize { get; init; } = 4096;
    [JsonPropertyName("gpu_layers")]
    public int GpuLayers { get; init; } = 0;
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.1f;
    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.8f;
    [JsonPropertyName("top_k")]
    public int TopK { get; init; } = 40;
    [JsonPropertyName("repeat_penalty")]
    public float RepeatPenalty { get; init; } = 1.1f;
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = 512;
    [JsonPropertyName("anti_prompts")]
    public string[] AntiPrompts { get; init; } = new[] { "User:", "Question:", "\n```,\n" };
}