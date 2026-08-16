using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class InterfaceConfig
{
    [JsonPropertyName("history_max_messages")]
    public int HistoryMaxMessages { get; init; } = 50;
    [JsonPropertyName("show_elapsed_time")]
    public bool ShowElapsedTime { get; init; } = true;
    [JsonPropertyName("prompt_prefix")]
    public string PromptPrefix { get; init; } = "[You]: ";
    [JsonPropertyName("response_prefix")]
    public string ResponsePrefix { get; init; } = "[Agent]: ";
    [JsonPropertyName("auto_clear_history_after")]
    public object? AutoClearHistoryAfter { get; init; } = null;
}