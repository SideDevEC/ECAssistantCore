namespace ECAssistant.Core.Playbooks;

/// <summary>
/// v14.14: Storage for persistent success playbooks. Owns dedup, usage counters,
/// eviction and JSON persistence under {workingDir}/playbooks/ — mirroring the
/// SelfCorrectionManager .snapshots storage pattern (one JSON file per entry,
/// corrupt files skipped with a debug log).
/// </summary>
public interface IPlaybookStore
{
    /// <summary>All loaded playbooks (in-memory mirror of disk).</summary>
    IReadOnlyList<Playbook> All { get; }

    /// <summary>
    /// Capture a candidate playbook. Dedup-equivalent existing playbooks get
    /// UseCount++/LastUsedAt updated instead of a new entry; otherwise the
    /// candidate is added and the store is re-persisted (evicting beyond cap).
    /// Returns the playbook that now represents this capture.
    /// </summary>
    Task<Playbook> CaptureAsync(Playbook candidate);

    /// <summary>Match a user request against playbook triggers; top N by usage.</summary>
    IReadOnlyList<Playbook> Match(string userRequest, int topN);

    /// <summary>Tier-flavored injection text for matched playbooks; null when nothing matches.</summary>
    string? BuildInjection(string userRequest, bool isLargeTier, int topN = 2, int maxChars = 1500);
}