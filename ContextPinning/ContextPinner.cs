using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.ContextPinning;

/// <summary>
/// v14.16: deterministic context pinner. Small tier → file map + decisions + goal
/// (≤ max_chars, default ~1200); large tier → goal only (≤ ~300 chars). All state
/// is in-memory per session; compaction re-injects the block via EAgentEngine.
/// </summary>
public sealed class ContextPinner : IContextPinner
{
    private readonly int _maxFiles;
    private readonly object _lock = new();
    private readonly List<string> _fileMap = new();       // most-recent-first
    private readonly List<string> _decisions = new();     // oldest-first
    private string? _goal;

    public ContextPinner(ContextPinningConfig? config = null)
    {
        _maxFiles = Math.Max(1, config?.MaxFiles ?? 15);
    }

    public void SetGoal(string userRequest)
    {
        if (string.IsNullOrWhiteSpace(userRequest)) return;
        lock (_lock)
        {
            // First non-empty request is the durable "original user request" fact.
            _goal ??= userRequest.Trim();
        }
    }

    public void ObserveUserMessage(string content)
    {
        var decision = ContextPinningMatchers.ExtractDecision(content);
        if (decision == null) return;
        lock (_lock)
        {
            if (!_decisions.Contains(decision, StringComparer.OrdinalIgnoreCase))
                _decisions.Add(decision);
        }
    }

    public void ObserveToolOutput(string toolName, string output)
    {
        foreach (var path in ContextPinningMatchers.ExtractPaths(output))
        {
            lock (_lock)
            {
                _fileMap.Remove(path);          // move-to-front = most-recent-first
                _fileMap.Insert(0, path);
                while (_fileMap.Count > _maxFiles) _fileMap.RemoveAt(_fileMap.Count - 1);
            }
        }
    }

    public string? BuildPinnedBlock(bool isLargeTier, int maxChars)
    {
        string? goal; List<string> decisions; List<string> files;
        lock (_lock)
        {
            goal = _goal;
            decisions = _decisions.ToList();
            files = _fileMap.ToList();
        }
        if (goal == null && decisions.Count == 0 && files.Count == 0) return null;

        var sb = new System.Text.StringBuilder("[PINNED CONTEXT]");
        if (goal != null)
            sb.AppendLine(" goal: " + goal);
        if (!isLargeTier)
        {
            foreach (var d in decisions)
                sb.AppendLine("[PINNED CONTEXT] decision: use " + d);
            if (files.Count > 0)
            {
                sb.Append("[PINNED CONTEXT] files: ");
                sb.AppendLine(string.Join(", ", files));
            }
        }

        var cap = isLargeTier ? Math.Min(maxChars, 300) : maxChars;
        var text = sb.ToString().TrimEnd();
        if (text.Length > cap) text = text[..cap].TrimEnd() + "…";
        return text;
    }
}