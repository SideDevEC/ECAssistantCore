using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// Logging configuration for ECAssistantCore. Encapsulated — Core uses its own
/// ILogger/Logger, no cross-repo logging dependencies.
/// When enabled is false, the logger is created with LogLevel.None and every
/// call returns immediately (single integer compare) — zero overhead.
/// </summary>
public sealed class LoggingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";

    [JsonPropertyName("file")]
    public string File { get; set; } = "ECAssistant.log";

    /// <summary>
    /// Component filter — when non-empty, only debug/info messages from these
    /// tags are logged. Error/Warn always pass. Empty array = all components.
    /// Known components: compaction, summarize, engine, tools, session, transport, composition.
    /// </summary>
    [JsonPropertyName("components")]
    public string[] Components { get; set; } = Array.Empty<string>();

    /// <summary>Resolve the string level to enum. Unknown → Info.</summary>
    public Services.LogLevel ResolvedLevel =>
        Enabled
            ? Level.ToLowerInvariant() switch
            {
                "debug" => Services.LogLevel.Debug,
                "warn" => Services.LogLevel.Warn,
                "error" => Services.LogLevel.Error,
                _ => Services.LogLevel.Info
            }
            : Services.LogLevel.None;

    /// <summary>True when the component tag is allowed by the filter.</summary>
    public bool IsComponentEnabled(string tag)
    {
        if (Components is null or { Length: 0 }) return true;
        foreach (var c in Components)
            if (tag.StartsWith(c, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}