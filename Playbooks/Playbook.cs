namespace ECAssistant.Core.Playbooks;

/// <summary>
/// v14.14: A persistent success playbook — a lightweight, deterministic recipe
/// distilled from a completed goal's successful tool-call sequence. Persisted as
/// JSON in {workingDir}/playbooks/ (mirroring the .snapshots pattern used by
/// SelfCorrectionManager). Immutable after creation except the usage counters
/// (UseCount / LastUsedAt), which the store updates on dedup hits.
/// </summary>
public class Playbook
{
    /// <summary>Stable id (pb_ + timestamp + short random suffix).</summary>
    public string Id { get; init; } = "";

    /// <summary>Human-readable title, derived from the user goal.</summary>
    public string Title { get; init; } = "";

    /// <summary>Lowercased trigger keywords matched against future user requests.</summary>
    public List<string> TriggerKeywords { get; init; } = new();

    /// <summary>Ordered steps — one line per successful tool call with summarized args.</summary>
    public List<string> Steps { get; init; } = new();

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Updated by the store on dedup hits; otherwise creation time.</summary>
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>How many times this playbook (or a dedup-equivalent) has been captured/used.</summary>
    public int UseCount { get; set; } = 1;

    /// <summary>Origin — e.g. "goal" for orchestrator-captured playbooks.</summary>
    public string Source { get; init; } = "goal";
}