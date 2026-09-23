namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// Priority 1: exact substring match — the pre-v14.15 behavior, unchanged.
/// </summary>
public sealed class ExactMatchStrategy : ITextMatchStrategy
{
    public string Name => "exact";

    public TextMatchResult Find(string content, string searchText)
    {
        if (string.IsNullOrEmpty(searchText)) return TextMatchResult.NoMatch(Name);

        var positions = new List<int>();
        var pos = 0;
        while ((pos = content.IndexOf(searchText, pos, StringComparison.Ordinal)) >= 0)
        {
            positions.Add(pos);
            pos += searchText.Length;
        }

        if (positions.Count == 0) return TextMatchResult.NoMatch(Name);
        if (positions.Count > 1)
            return TextMatchResult.Ambiguous(Name, positions.Select(p => LineOf(content, p)).ToList());
        return TextMatchResult.Matched(Name, positions[0], searchText);
    }

    // Pure function: 1-based line number containing the char offset.
    private static int LineOf(string content, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }
}