using System.Reflection;

namespace ECAssistant.Services;

/// <summary>
/// Loads embedded resources from the Core DLL.
/// Resources (SystemPrompt.md, appsettings.json, etc.) are embedded at build time,
/// making Core.dll fully self-contained — no external files needed.
/// </summary>
public static class ResourceLoader
{
    private static readonly Assembly _assembly = typeof(ResourceLoader).Assembly;
    private const string _baseNamespace = "ECAssistant.";

    /// <summary>
    /// Load a text resource embedded in the Core DLL.
    /// Returns null if the resource is not found.
    /// </summary>
    public static string? LoadText(string resourceName)
    {
        var fullId = _baseNamespace + resourceName;
        using var stream = _assembly.GetManifestResourceStream(fullId);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Load a text resource, with fallback to an alternative name.
    /// Returns null if neither resource is found.
    /// </summary>
    public static string? LoadTextWithFallback(string primary, string fallback)
    {
        return LoadText(primary) ?? LoadText(fallback);
    }

    /// <summary>
    /// Check if an embedded resource exists.
    /// </summary>
    public static bool Exists(string resourceName)
    {
        return _assembly.GetManifestResourceInfo(_baseNamespace + resourceName) != null;
    }
}