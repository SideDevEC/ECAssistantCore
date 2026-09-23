namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// Outcome of a single text-match strategy attempt against file content.
/// </summary>
public enum TextMatchStatus
{
    /// <summary>Exactly one unambiguous match was found.</summary>
    Found,

    /// <summary>Multiple candidate matches — never guess, caller must disambiguate.</summary>
    Ambiguous,

    /// <summary>No match found by this strategy.</summary>
    NotFound
}
