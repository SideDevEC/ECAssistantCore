namespace ECAssistant.Core.Engine;

/// <summary>
/// v14.12.2: curates an oversized tool output for the context window — key lines
/// (errors, warnings, failures, diffs, summaries, results) plus head and tail,
/// instead of a blind head-truncate that hides the lines that matter. Pure and
/// deterministic; wired in AgentEngine.TruncateToolOutput, gated by
/// context_management.curate_tool_outputs.
/// Stateless utility — no mutable state.
/// </summary>
public static class ToolOutputProjector
{
    /// <summary>Verbatim head kept from the original text.</summary>
    public const int KeepHeadChars = 1200;

    /// <summary>Verbatim tail kept from the original text.</summary>
    public const int KeepTailChars = 1200;

    /// <summary>Maximum number of key lines kept from the elided middle.</summary>
    public const int MaxKeyLines = 30;

    // Case-insensitive substring markers for lines worth keeping from the middle.
    private static readonly string[] KeyMarkers =
    {
        "error", "exception", "fatal", "warn", "fail", "denied", "blocked",
        "diff --git", "summary", "result", "passed", "skipped", "✗", "✘"
    };

    /// <summary>
    /// Project an oversized output. storeNote is the retrieval note the caller
    /// appends (mentions where the full output lives). Returns curated text.
    /// Pure function — deterministic, no side effects.
    /// </summary>
    // Stateless utility — no mutable state
    public static string Project(string text, string storeNote)
    {
        var head = text[..Math.Min(KeepHeadChars, text.Length)];
        var tailStart = Math.Max(0, text.Length - KeepTailChars);
        // v14.12.2-audit: tiny outputs (max_result_chars below head+tail size) would
        // overlap head and tail and duplicate the overlap region — clamp tail start
        // past the head boundary so the elided marker stays truthful.
        if (tailStart < KeepHeadChars) tailStart = KeepHeadChars;
        var tail = tailStart >= text.Length ? string.Empty : text[tailStart..];

        var sb = new System.Text.StringBuilder();
        sb.Append(head);
        sb.Append("\n[... middle elided — key lines below ...]\n");

        var kept = 0;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (kept >= MaxKeyLines) break;
            if (line.Length == 0) continue;
            var lower = line.ToLowerInvariant();
            if (!KeyMarkers.Any(m => lower.Contains(m))) continue;

            // Skip key lines already visible in the verbatim head/tail.
            if (head.Contains(line, StringComparison.Ordinal) || tail.Contains(line, StringComparison.Ordinal))
                continue;

            sb.Append(line.TrimEnd());
            sb.Append('\n');
            kept++;
        }

        sb.Append("[... tail ...]\n");
        sb.Append(tail);
        sb.Append('\n');
        sb.Append(storeNote);
        return sb.ToString();
    }
}