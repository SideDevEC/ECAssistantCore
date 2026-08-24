using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// Configuration for background tasks (decomposition, summarization) that use
/// HTTP streaming inference (stateless mode, no session KV cache). No separate model load.
/// </summary>
public class BackgroundTasksConfig
{
    [JsonPropertyName("decompose")]
    public DecomposeConfig Decompose { get; init; } = new();

    [JsonPropertyName("summarize")]
    public SummarizeConfig Summarize { get; init; } = new();
}