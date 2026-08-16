using System.Text.Json.Serialization;

namespace ECAssistant.Config;

public class ContextManagementConfig
{
    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "SummaryAndShift";
    [JsonPropertyName("shift_at_messages")]
    public int ShiftAtMessages { get; set; } = 40;
    [JsonPropertyName("keep_last")]
    public int KeepLast { get; set; } = 20;
    [JsonPropertyName("summarize_prompt")]
    public string SummarizePrompt { get; set; } = "Summarize the key decisions, actions taken, and current state of our conversation. Keep it concise but include any important findings or tool results that will help me continue this task.";
    [JsonPropertyName("max_summary_length")]
    public int MaxSummaryLength { get; set; } = 2000;
    [JsonPropertyName("auto_shift_on_generate")]
    public bool AutoShiftOnGenerate { get; set; } = true;
    [JsonPropertyName("shift_guardrail_threshold")]
    public int ShiftGuardrailThreshold { get; set; } = 3;
}