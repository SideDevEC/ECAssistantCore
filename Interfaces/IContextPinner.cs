namespace ECAssistant.Core.Interfaces;

/// <summary>One pinned fact surfaced to the model after compaction.</summary>
public sealed record PinnedFact(string Kind, string Text);

/// <summary>
/// v14.16: deterministic pinning of critical conversation state (original user
/// request, explicit user decisions, touched-file map) so compaction never
/// loses it. No LLM involved.
/// </summary>
public interface IContextPinner
{
    /// <summary>Always-pinned original request; the first non-empty request wins (durable goal).</summary>
    void SetGoal(string userRequest);

    /// <summary>Record an explicit user decision (choice-verb matches only).</summary>
    void ObserveUserMessage(string content);

    /// <summary>Record file paths touched by a tool call/output (most recent kept).</summary>
    void ObserveToolOutput(string toolName, string output);

    /// <summary>Build the tier-aware pinned block; null when nothing to pin.</summary>
    string? BuildPinnedBlock(bool isLargeTier, int maxChars);
}