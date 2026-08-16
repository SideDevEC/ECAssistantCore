using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class InterfaceConfig
{
    [JsonPropertyName("history_max_messages")]
    public int HistoryMaxMessages { get; set; } = 50;
    [JsonPropertyName("show_elapsed_time")]
    public bool ShowElapsedTime { get; set; } = true;
    [JsonPropertyName("prompt_prefix")]
    public string PromptPrefix { get; set; } = "[You]: ";
    [JsonPropertyName("response_prefix")]
    public string ResponsePrefix { get; set; } = "[Agent]: ";
    [JsonPropertyName("auto_clear_history_after")]
    public object? AutoClearHistoryAfter { get; set; } = null;
}