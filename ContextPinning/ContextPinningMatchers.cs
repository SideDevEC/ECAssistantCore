using System.Text.RegularExpressions;

namespace ECAssistant.Core.ContextPinning;

/// <summary>
/// v14.16: pure-function matchers for proactive context pinning. Static methods
/// are intentional — they are deterministic, side-effect-free helpers (allowed
/// pure-function statics per package rules).
/// </summary>
public static class ContextPinningMatchers
{
    /// <summary>Path-like tokens (unix paths, tilde paths, drive-letter paths).</summary>
    private static readonly Regex PathRegex = new(
        @"(?:~|\.{1,2})?/(?:[\w.\-+]+/)*[\w.\-+]+|[A-Za-z]:\\(?:[\w.\-+]+\\)*[\w.\-+]+",
        RegexOptions.Compiled);

    /// <summary>
    /// Explicit-choice verbs only: "use X", "go with X", "stick with X" (also
    /// "switch to X", "keep X"). Deliberately narrow — no imperative crawl.
    /// </summary>
    private static readonly Regex DecisionRegex = new(
        @"\b(?:use|go with|stick with|switch to|keep)\s+(?<choice>[A-Za-z][\w./+#\- ]{0,60})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Distinct file paths in a tool output, in order of appearance.</summary>
    public static IReadOnlyList<string> ExtractPaths(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (Match m in PathRegex.Matches(text))
        {
            var p = m.Value.TrimEnd('.', ',', ')');
            if (p.Length < 2 || !seen.Add(p)) continue;
            // Skip obvious non-file noise (urls like /foo in http, lone roots).
            if (p is "/" or "~" or "." or "..") continue;
            result.Add(p);
        }
        return result;
    }

    /// <summary>Explicit user choices in a user message; null when none. Pure.</summary>
    public static string? ExtractDecision(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return null;
        var m = DecisionRegex.Match(userMessage);
        if (!m.Success) return null;
        var choice = m.Groups["choice"].Value.Trim().TrimEnd('.', ',', '!', '?');
        return choice.Length == 0 ? null : choice;
    }

    /// <summary>True when the message looks like a real user request (not tool/system noise). Pure.</summary>
    public static bool IsUserRequest(string message) =>
        !string.IsNullOrWhiteSpace(message) && message.Length >= 4;
}