using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// Tool-result truncation limits (2026-09-21 — previously hardcoded constants).
/// Tool results are truncated BEFORE injection into the context window; the full
/// output is stored and re-readable via tool_outputs/. Per-tool overrides win.
/// Config section: "tool_output_limits".
/// </summary>
public class ToolOutputLimitsConfig
{
    /// <summary>Default max chars injected for any tool result. Default: 4000.</summary>
    [JsonPropertyName("max_result_chars")]
    public int MaxResultChars { get; init; } = 4000;

    /// <summary>Per-tool overrides, e.g. {"ECodeEditor": 8000, "EShellAgent": 6000}.</summary>
    [JsonPropertyName("max_result_chars_per_tool")]
    public Dictionary<string, int>? MaxResultCharsPerTool { get; init; }

    /// <summary>How many full outputs to keep in the in-memory store for retrieval. Default: 20.</summary>
    [JsonPropertyName("max_stored_outputs")]
    public int MaxStoredOutputs { get; init; } = 20;
}
