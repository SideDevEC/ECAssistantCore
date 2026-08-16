using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class LlmConfig
{
    [JsonPropertyName("model_path")]
    public string ModelPath { get; set; } = "Qwen3-8B-Q4_K_M.gguf";
    [JsonPropertyName("context_size")]
    public uint ContextSize { get; set; } = 16384;
    [JsonPropertyName("gpu_layers")]
    public int GpuLayers { get; set; } = 15;
    [JsonPropertyName("threads")]
    public int Threads { get; set; } = -1;
    [JsonPropertyName("batch_size")]
    public uint BatchSize { get; set; } = 256;
    [JsonPropertyName("ubatch_size")]
    public uint UBatchSize { get; set; } = 128;
}