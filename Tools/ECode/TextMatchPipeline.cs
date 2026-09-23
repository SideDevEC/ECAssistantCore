namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// Runs match strategies in order (exact → whitespace-tolerant → line-anchored)
/// and returns the first unambiguous match, reporting which strategy won.
/// Ambiguity is resolved conservatively: the FIRST ambiguous result is returned
/// immediately (never guessed through), so callers produce a structured error.
/// </summary>
public sealed class TextMatchPipeline : ITextMatchPipeline
{
    public const string DefaultNotFoundStrategy = "pipeline";

    private readonly IReadOnlyList<ITextMatchStrategy> _strategies;

    public TextMatchPipeline()
        : this(new ITextMatchStrategy[]
        {
            new ExactMatchStrategy(),
            new WhitespaceTolerantMatchStrategy(),
            new LineAnchoredMatchStrategy()
        })
    {
    }

    public TextMatchPipeline(IReadOnlyList<ITextMatchStrategy> strategies)
    {
        _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
    }

    public TextMatchResult Find(string content, string searchText)
    {
        TextMatchResult? lastNotFound = null;
        foreach (var strategy in _strategies)
        {
            var result = strategy.Find(content, searchText);
            switch (result.Status)
            {
                case TextMatchStatus.Found:
                case TextMatchStatus.Ambiguous:
                    return result;
                case TextMatchStatus.NotFound:
                default:
                    lastNotFound = result;
                    break;
            }
        }

        return lastNotFound ?? TextMatchResult.NoMatch(DefaultNotFoundStrategy);
    }
}
