using System.Text.Json.Serialization;

namespace ECAssistant.Config;

public class SecondaryModelConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;
    [JsonPropertyName("model_path")]
    public string ModelPath { get; set; } = "";
    [JsonPropertyName("context_size")]
    public uint ContextSize { get; set; } = 4096;
    [JsonPropertyName("gpu_layers")]
    public int GpuLayers { get; set; } = 0;
    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.1f;
    [JsonPropertyName("top_p")]
    public float TopP { get; set; } = 0.8f;
    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 40;
    [JsonPropertyName("repeat_penalty")]
    public float RepeatPenalty { get; set; } = 1.1f;
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 512;
    [JsonPropertyName("anti_prompts")]
    public string[] AntiPrompts { get; set; } = new[] { "User:", "Question:", "\n```,\n" };
}