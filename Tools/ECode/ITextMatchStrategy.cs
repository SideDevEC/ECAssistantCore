namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// A single text-matching strategy for patch search/replace. Implementations
/// locate candidate matches of a search text inside content and report either a
/// unique match (with the exact matched text so indentation can be restored),
/// an ambiguous set of candidate lines, or no match.
/// </summary>
public interface ITextMatchStrategy
{
    /// <summary>Strategy name for result reporting (e.g. "exact", "whitespace-tolerant").</summary>
    string Name { get; }

    /// <summary>
    /// Locate the search text in content. Multiple candidates are reported as
    /// Ambiguous — strategies never guess between them.
    /// </summary>
    TextMatchResult Find(string content, string searchText);
}