namespace ECAssistant.Core.ContextPinning;

/// <summary>
/// v14.16: Kind of a pinned fact — the three categories of critical conversation
/// state that must survive compaction.
/// </summary>
public enum PinnedFactKind
{
    /// <summary>The original user request (turn 1). Always pinned, never evicted.</summary>
    Goal,

    /// <summary>A user-stated choice ("use X", "go with X", "stick with X").</summary>
    Decision,

    /// <summary>A file path touched by a tool call (most recent first).</summary>
    FileMap,
}
