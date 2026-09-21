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

/// <summary>
/// Path expansion utility — expands ~ to the user home directory.
/// Stateless, no dependencies. Instance class (not static) for OOP compliance.
/// Use the Default instance for convenience, or inject your own.
/// </summary>
public class PathExpander
{
    /// <summary>Default shared instance for convenience.</summary>
    public static readonly PathExpander Default = new();

    /// <summary>
    /// Expand a leading ~ to the user's home directory.
    /// Cross-platform: uses Environment.SpecialFolder.UserProfile.
    /// Returns the path unchanged if it doesn't start with ~.
    /// </summary>
    public string Expand(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path == "~") return GetUserHome();
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            return Path.Combine(GetUserHome(), path.Substring(2));
        return path;
    }

    private static string GetUserHome()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}