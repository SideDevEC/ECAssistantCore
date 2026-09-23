namespace ECAssistant.Core.Playbooks;

/// <summary>
/// v14.14: Pure text helpers for playbook keyword extraction and matching.
/// STATIC METHODS ARE PURE FUNCTIONS ONLY — no state, no I/O, deterministic.
/// </summary>
public static class PlaybookMatcher
{
    /// <summary>Common English filler words excluded from trigger keywords. Pure data.</summary>
    private static readonly string[] StopWords =
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "then", "when",
        "please", "must", "should", "have", "has", "are", "was", "were", "will",
        "would", "could", "them", "they", "their", "there", "than", "then",
        "make", "made", "using", "used", "also", "some", "each", "your", "you",
        "all", "any", "can", "not", "but", "out", "how", "why", "what", "who",
        "get", "set", "new", "own", "one", "two", "its", "it's", "via", "per"
    };

    /// <summary>Normalize a title for dedup comparison: lowercase, strip non-alphanumeric, collapse whitespace. Pure.</summary>
    public static string NormalizeTitle(string title)
    {
        var chars = (title ?? "").ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Extract lowercase trigger keywords from a user request: words &gt; 2 chars, no stopwords, deduped, capped. Pure.</summary>
    public static List<string> ExtractKeywords(string text, int maxKeywords = 8)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '"', '\'', '(', ')', '/', '\\' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .Distinct(StringComparer.Ordinal)
            .Take(maxKeywords)
            .ToList();
    }

    /// <summary>True when the request contains the given keyword as a substring (case-insensitive). Pure.</summary>
    public static bool RequestContains(string request, string keyword) =>
        !string.IsNullOrWhiteSpace(request) &&
        !string.IsNullOrWhiteSpace(keyword) &&
        request.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a playbook's trigger set matches the request (any keyword hit). Pure.</summary>
    public static bool MatchesRequest(Playbook playbook, string request) =>
        playbook.TriggerKeywords.Any(k => RequestContains(request, k));

    /// <summary>True when two playbooks are dedup-equivalent: equal normalized titles OR equal trigger sets. Pure.</summary>
    public static bool IsDuplicate(Playbook existing, Playbook candidate)
    {
        if (string.Equals(NormalizeTitle(existing.Title), NormalizeTitle(candidate.Title), StringComparison.Ordinal))
            return true;
        var a = existing.TriggerKeywords.ToHashSet(StringComparer.Ordinal);
        var b = candidate.TriggerKeywords.ToHashSet(StringComparer.Ordinal);
        return a.Count > 0 && a.SetEquals(b);
    }
}