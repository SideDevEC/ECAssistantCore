namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// Shared machinery for line-based strategies: split content into lines while
/// tracking char offsets, slide a window of the search-text's line count over the
/// document comparing NORMALIZED lines, and report the unique match with its exact
/// original text (so the caller can restore indentation). Derived strategies
/// supply only their line normalization.
/// </summary>
public abstract class LineMatchStrategyBase : ITextMatchStrategy
{
    private readonly string _name;

    protected LineMatchStrategyBase(string name)
    {
        _name = name;
    }

    public string Name => _name;

    public TextMatchResult Find(string content, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return TextMatchResult.NoMatch(Name);

        var lines = SplitLines(content);
        var searchNorm = SplitLines(searchText)
            .Select(l => Normalize(l.Text))
            .Where(t => t.Length > 0)
            .ToList();
        if (searchNorm.Count == 0) return TextMatchResult.NoMatch(Name);

        // Compact the document to lines with significant (normalized) content; blank
        // lines inside the matched block stay part of the matched range.
        var significant = new List<(int OrigIndex, string Norm)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var norm = Normalize(lines[i].Text);
            if (norm.Length > 0) significant.Add((i, norm));
        }

        var candidateLines = new List<int>();
        var matchStart = -1;
        var matchEnd = -1;
        for (var start = 0; start + searchNorm.Count <= significant.Count; start++)
        {
            var all = true;
            for (var i = 0; i < searchNorm.Count && all; i++)
                all = significant[start + i].Norm == searchNorm[i];
            if (!all) continue;

            var first = significant[start].OrigIndex;
            var last = significant[start + searchNorm.Count - 1].OrigIndex;
            candidateLines.Add(first + 1);
            matchStart = lines[first].Start;
            matchEnd = lines[last].Start + lines[last].Text.Length;
        }

        if (candidateLines.Count == 0) return TextMatchResult.NoMatch(Name);
        if (candidateLines.Count > 1) return TextMatchResult.Ambiguous(Name, candidateLines);
        return TextMatchResult.Matched(Name, matchStart, content[matchStart..matchEnd]);
    }

    /// <summary>Line normalization for matching — implemented per strategy.</summary>
    protected abstract string Normalize(string line);

    // Pure function: split into (startOffset, text) lines, excluding line terminators
    // and any trailing empty line after a final newline.
    private static List<(int Start, string Text)> SplitLines(string content)
    {
        var result = new List<(int, string)>();
        var start = 0;
        for (var i = 0; i <= content.Length; i++)
        {
            if (i < content.Length && content[i] != '\n') continue;
            // Every '\n' terminates a line (even empty ones); the loop end only
            // produces a final entry when content does not end with a newline.
            if (i < content.Length || start < i)
            {
                var end = i;
                if (end > start && content[end - 1] == '\r') end--;
                result.Add((start, content[start..end]));
            }
            start = i + 1;
        }
        return result;
    }
}