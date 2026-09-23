namespace ECAssistant.Core.Playbooks;

/// <summary>
/// v14.14: Deterministic playbook extractor — builds a playbook purely from the
/// goal text and the sequence of successful tool calls. No LLM, no I/O.
/// </summary>
public sealed class PlaybookExtractor : IPlaybookExtractor
{
    /// <summary>Max args-summary characters kept per step line.</summary>
    private const int MaxArgsChars = 80;

    public Playbook? Extract(string goal, IReadOnlyList<CapturedToolCall> successfulCalls, string source = "goal")
    {
        if (string.IsNullOrWhiteSpace(goal) || successfulCalls == null || successfulCalls.Count == 0)
            return null;

        var steps = successfulCalls
            .Where(c => !string.IsNullOrWhiteSpace(c.ToolName))
            .Select(c => $"{c.ToolName}({TruncateArgs(c.ArgsSummary)})")
            .Where(s => s.Length > 0)
            .ToList();
        if (steps.Count == 0) return null;

        return new Playbook
        {
            Id = NewId(),
            Title = BuildTitle(goal),
            TriggerKeywords = PlaybookMatcher.ExtractKeywords(goal),
            Steps = steps,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            UseCount = 1,
            Source = string.IsNullOrWhiteSpace(source) ? "goal" : source
        };
    }

    /// <summary>Title from the user goal: first sentence, single-line, char-capped. Pure.</summary>
    private static string BuildTitle(string goal)
    {
        var oneLine = goal.Replace("\r", " ").Replace("\n", " ").Trim();
        // v14.19: first sentence only — prompts often append tool-call instructions
        // ("Make EXACTLY this tool call: ...") that would otherwise become the title.
        // A period inside a token ("note.txt", version numbers) is NOT a sentence
        // boundary: the terminator must be followed by whitespace or end-of-string.
        for (var i = 10; i < oneLine.Length; i++)
        {
            var c = oneLine[i];
            if (c != '.' && c != '!' && c != '?') continue;
            if (i == oneLine.Length - 1 || char.IsWhiteSpace(oneLine[i + 1]))
            {
                oneLine = oneLine[..(i + 1)];
                break;
            }
        }
        return oneLine.Length <= 80 ? oneLine : oneLine[..80] + "…";
    }

    /// <summary>Args summary trimmed to a compact line. Pure.</summary>
    private static string TruncateArgs(string? argsSummary)
    {
        var compact = string.IsNullOrWhiteSpace(argsSummary) ? "" : argsSummary.Trim();
        return compact.Length <= MaxArgsChars ? compact : compact[..MaxArgsChars] + "…";
    }

    /// <summary>Time+random id — uniqueness comes from the random suffix, not the clock. Pure.</summary>
    private static string NewId() =>
        $"g{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
}