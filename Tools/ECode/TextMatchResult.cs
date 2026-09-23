namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// Outcome of one match strategy scanning content for a search text:
/// Found (single unambiguous span + the matched text), Ambiguous (several
/// candidate lines — reported, never guessed among), or NotFound. Immutable.
/// </summary>
public sealed class TextMatchResult
{
    public TextMatchStatus Status { get; }
    public string StrategyName { get; }

    /// <summary>0-based char offset of the match (Status=Found).</summary>
    public int StartIndex { get; }

    /// <summary>The exact original text the strategy matched (Status=Found).</summary>
    public string MatchedText { get; }

    /// <summary>1-based line numbers of all candidates (Status=Ambiguous).</summary>
    public IReadOnlyList<int> CandidateLines { get; }

    private TextMatchResult(TextMatchStatus status, string strategyName, int startIndex, string matchedText, IReadOnlyList<int> candidateLines)
    {
        Status = status;
        StrategyName = strategyName;
        StartIndex = startIndex;
        MatchedText = matchedText;
        CandidateLines = candidateLines;
    }

    // Factory methods — the only permitted statics on this type.
    public static TextMatchResult Matched(string strategyName, int startIndex, string matchedText) => new(
        TextMatchStatus.Found, strategyName, startIndex, matchedText, Array.Empty<int>());

    public static TextMatchResult Ambiguous(string strategyName, IReadOnlyList<int> candidateLines) => new(
        TextMatchStatus.Ambiguous, strategyName, -1, "", candidateLines);

    public static TextMatchResult NoMatch(string strategyName) => new(
        TextMatchStatus.NotFound, strategyName, -1, "", Array.Empty<int>());
}