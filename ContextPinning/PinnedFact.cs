using System.Text.Json.Serialization;

namespace ECAssistant.Core.ContextPinning;

/// <summary>
/// v14.16: One pinned fact — a piece of critical conversation state held outside
/// the context window so compaction can never lose it. Immutable; pinning is
/// append-only (newer facts win on eviction order, except Goal which never evicts).
/// </summary>
public class PinnedFact
{
    [JsonPropertyName("kind")]
    public PinnedFactKind Kind { get; init; }

    /// <summary>Compact one-line text of the fact (no newlines).</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>1-based conversation turn when the fact was captured.</summary>
    [JsonPropertyName("created_turn")]
    public int CreatedTurn { get; init; }
}
