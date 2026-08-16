using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class SamplingConfig
{
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.3f;
    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.9f;
    [JsonPropertyName("top_k")]
    public int TopK { get; init; } = 40;
    [JsonPropertyName("repeat_penalty")]
    public float RepeatPenalty { get; init; } = 1.1f;
    [JsonPropertyName("penalty_last_n")]
    public int PenaltyLastN { get; init; } = 64;
    [JsonPropertyName("mirostat")]
    public bool Mirostat { get; init; } = false;
    [JsonPropertyName("mirostat_tau")]
    public float MirostatTau { get; init; } = 5.0f;
    [JsonPropertyName("mirostat_eta")]
    public float MirostatEta { get; init; } = 0.1f;
}