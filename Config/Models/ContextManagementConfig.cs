using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class ContextManagementConfig
{
    [JsonPropertyName("strategy")]
    public string Strategy { get; init; } = "SummaryAndShift";
    [JsonPropertyName("shift_at_messages")]
    public int ShiftAtMessages { get; init; } = 40;
    [JsonPropertyName("keep_last")]
    public int KeepLast { get; init; } = 20;
    [JsonPropertyName("summarize_prompt")]
    public string SummarizePrompt { get; init; } = "Summarize the key decisions, actions taken, and current state of our conversation. Keep it concise but include any important findings or tool results that will help me continue this task.";
    [JsonPropertyName("max_summary_length")]
    public int MaxSummaryLength { get; init; } = 2000;
    [JsonPropertyName("auto_shift_on_generate")]
    public bool AutoShiftOnGenerate { get; init; } = true;
    [JsonPropertyName("shift_guardrail_threshold")]
    public int ShiftGuardrailThreshold { get; init; } = 3;

    /// <summary>
    /// Context-window fill percentage (0-100) that triggers KV-cache rebuild +
    /// conversation compaction. Default: 80.
    /// </summary>
    [JsonPropertyName("compact_threshold_percent")]
    public int CompactThresholdPercent { get; init; } = 80;
}