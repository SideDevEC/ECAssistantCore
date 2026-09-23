namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// Match pipeline seam: runs match strategies in order (exact →
/// whitespace-tolerant → line-anchored) and returns the first unambiguous
/// match, reporting which strategy won. Ambiguity is resolved conservatively:
/// the FIRST ambiguous result is returned immediately (never guessed through),
/// so callers produce a structured error.
/// </summary>
public interface ITextMatchPipeline
{
    /// <summary>
    /// Find <paramref name="searchText"/> in <paramref name="content"/> using the
    /// ordered strategy chain.
    /// </summary>
    TextMatchResult Find(string content, string searchText);
}