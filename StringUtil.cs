namespace ECAssistant.Core;

/// <summary>
/// String utility — truncation and text helpers.
/// Stateless, no dependencies. Instance class (not static) for OOP compliance.
/// Use the Default instance for convenience, or inject your own.
/// </summary>
public class StringUtil
{
    /// <summary>Default shared instance for convenience.</summary>
    public static readonly StringUtil Default = new();

    /// <summary>Truncate text to maxChars and append [...] if truncated.</summary>
    public string Truncate(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? "";
        return text.Substring(0, maxChars) + " [...]";
    }
}