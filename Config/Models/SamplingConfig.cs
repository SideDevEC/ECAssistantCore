using System.Text.Json.Serialization;

namespace ECAssistant.Config;

public class SamplingConfig
{
    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.3f;
    [JsonPropertyName("top_p")]
    public float TopP { get; set; } = 0.9f;
    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 40;
    [JsonPropertyName("repeat_penalty")]
    public float RepeatPenalty { get; set; } = 1.1f;
    [JsonPropertyName("penalty_last_n")]
    public int PenaltyLastN { get; set; } = 64;
    [JsonPropertyName("mirostat")]
    public bool Mirostat { get; set; } = false;
    [JsonPropertyName("mirostat_tau")]
    public float MirostatTau { get; set; } = 5.0f;
    [JsonPropertyName("mirostat_eta")]
    public float MirostatEta { get; set; } = 0.1f;
}