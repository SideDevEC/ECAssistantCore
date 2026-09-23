using System.Text.Json;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Playbooks;

/// <summary>
/// v14.14: JSON-file-backed playbook store. One JSON file per playbook under
/// {workingDir}/playbooks/ (same durable-per-entry pattern as SelfCorrectionManager's
/// .snapshots). Load failures and disk errors are logged and never thrown —
/// playbook memory is an enhancement, never a critical dependency.
/// </summary>
public sealed class PlaybookStore : IPlaybookStore
{
    /// <summary>Total-playbook cap. Beyond this, the least-used/oldest entries are evicted.</summary>
    private readonly int _maxPlaybooks;
    private readonly string _storeDir;
    private readonly ILogger? _logger;
    private readonly List<Playbook> _playbooks = new();
    private readonly object _lock = new();

    public PlaybookStore(string workingDir, ILogger? logger = null, int maxPlaybooks = 50)
    {
        _storeDir = Path.Combine(workingDir, "playbooks");
        _logger = logger;
        _maxPlaybooks = Math.Max(1, maxPlaybooks);
        try
        {
            if (!Directory.Exists(_storeDir)) Directory.CreateDirectory(_storeDir);
            LoadAll();
        }
        catch (Exception ex)
        {
            _logger?.Warn("Playbooks", $"Store init failed (starting empty): {ex.Message}");
        }
    }

    public IReadOnlyList<Playbook> All
    {
        get { lock (_lock) return _playbooks.ToList(); }
    }

    public async Task<Playbook> CaptureAsync(Playbook candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Playbook result;
        lock (_lock)
        {
            var dupe = _playbooks.FirstOrDefault(p => PlaybookMatcher.IsDuplicate(p, candidate));
            if (dupe != null)
            {
                dupe.UseCount++;
                dupe.LastUsedAt = DateTime.UtcNow;
                result = dupe;
            }
            else
            {
                result = candidate;
                _playbooks.Add(result);
                EvictBeyondCap();
            }
        }
        await PersistAllAsync();
        return result;
    }

    public IReadOnlyList<Playbook> Match(string userRequest, int topN)
    {
        lock (_lock)
        {
            return _playbooks
                .Where(p => PlaybookMatcher.MatchesRequest(p, userRequest))
                .OrderByDescending(p => p.UseCount)
                .ThenByDescending(p => p.LastUsedAt)
                .Take(Math.Max(0, topN))
                .ToList();
        }
    }

    public string? BuildInjection(string userRequest, bool isLargeTier, int topN = 2, int maxChars = 1500)
    {
        var matched = Match(userRequest, topN);
        if (matched.Count == 0) return null;

        // Tier flavor: small tier gets a STRICT recipe (max scaffolding helps small
        // models); large tier gets slim reference material.
        var header = isLargeTier
            ? "[PAST SUCCESSFUL PROCEDURE] Reference material from a previous run — use if helpful:"
            : "[PLAYBOOK] A previous run succeeded with these steps. Follow these steps exactly; adapt only if a step fails:";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(header);
        foreach (var pb in matched)
        {
            sb.AppendLine($"- {pb.Title} (used x{pb.UseCount}):");
            for (int i = 0; i < pb.Steps.Count; i++)
                sb.AppendLine($"  {i + 1}. {pb.Steps[i]}");
        }
        var text = sb.ToString().TrimEnd();
        if (text.Length > maxChars)
            text = text[..maxChars] + "\n…";
        return text;
    }

    /// <summary>Evict the oldest least-used playbooks until the cap holds. Called under lock.</summary>
    private void EvictBeyondCap()
    {
        while (_playbooks.Count > _maxPlaybooks)
        {
            var victim = _playbooks
                .OrderBy(p => p.UseCount)
                .ThenBy(p => p.LastUsedAt)
                .ThenBy(p => p.CreatedAt)
                .First();
            _playbooks.Remove(victim);
            try { File.Delete(Path.Combine(_storeDir, FileName(victim))); }
            catch (Exception ex) { _logger?.Debug("Playbooks", $"Evict delete skipped: {ex.Message}"); }
        }
    }

    /// <summary>Load all persisted playbooks; corrupt entries are skipped (logged).</summary>
    private void LoadAll()
    {
        foreach (var file in Directory.GetFiles(_storeDir, "pb_*.json"))
        {
            try
            {
                var pb = JsonSerializer.Deserialize<Playbook>(File.ReadAllText(file));
                if (pb == null || string.IsNullOrEmpty(pb.Id)) continue;
                _playbooks.Add(pb);
            }
            catch (Exception ex) { _logger?.Debug("Playbooks", $"Skipping corrupt playbook {file}: {ex.Message}"); }
        }
        _playbooks.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
    }

    /// <summary>Rewrite the store directory to exactly mirror memory (add/update/delete).</summary>
    private async Task PersistAllAsync()
    {
        List<Playbook> snapshot;
        lock (_lock) snapshot = _playbooks.ToList();
        try
        {
            var onDisk = Directory.Exists(_storeDir)
                ? Directory.GetFiles(_storeDir, "pb_*.json").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>();
            var wanted = snapshot.Select(FileName).ToHashSet();
            foreach (var stale in onDisk.Where(f => !wanted.Contains(f)))
            {
                try { File.Delete(Path.Combine(_storeDir, stale)); }
                catch (Exception ex) { _logger?.Debug("Playbooks", $"Stale delete skipped: {ex.Message}"); }
            }
            foreach (var pb in snapshot)
            {
                var json = JsonSerializer.Serialize(pb);
                await File.WriteAllTextAsync(Path.Combine(_storeDir, FileName(pb)), json);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn("Playbooks", $"Persist failed (in-memory state kept): {ex.Message}");
        }
    }

    /// <summary>Deterministic per-playbook file name. Pure.</summary>
    private static string FileName(Playbook pb) => $"pb_{pb.Id}.json";
}