using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// v14.16: tier-aware proactive context pinning. Critical state (original user
/// request, explicit user decisions, file map) survives compaction by being
/// re-injected after the summarize rebuild. Deterministic — no LLM in Core.
/// </summary>
public class ContextPinningConfig
{
    /// <summary>Master switch. Default true.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>Max distinct file paths kept in the file map (most-recent wins). Default 15.</summary>
    [JsonPropertyName("max_files")]
    public int MaxFiles { get; init; } = 15;

    /// <summary>Char cap for the small-tier pinned block (large tier uses a tighter implicit cap).</summary>
    [JsonPropertyName("max_chars")]
    public int MaxChars { get; init; } = 1200;
}