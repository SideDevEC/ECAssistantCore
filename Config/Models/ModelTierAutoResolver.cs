using System.Text.RegularExpressions;

namespace ECAssistant.Core.Config;

/// <summary>
/// Derives the model tier from the model id/name (Emre's rule, 2026-09-24):
/// - Parameter markers (e.g. "4b", "7.5b", "14b") ≤ 14 → small
/// - Parameter markers > 14 → large (size wins over "instruct" markers)
/// - "instruct" in the id (no size marker) → small (instruct-tuned chat models)
/// - No parsable info → remote: large (frontier API models), local: small (safe default)
///
/// Stateless utility — no mutable state. Pure string analysis only.
/// </summary>
public static class ModelTierAutoResolver
{
    /// <summary>Models at or below this parameter count resolve to the small tier.</summary>
    public const double SmallModelMaxB = 14.0;

    // Matches parameter markers: "4b", "14B", "7.5b", "70b" in ids like
    // "qwen3-4b-instruct", "Llama-3.3-70B", "ministral-8b-instruct-2410".
    private static readonly Regex ParamMarker = new(
        @"(?<![a-z0-9])(?<params>\d+(?:\.\d+)?)\s*b(?![a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// True when the model id/name indicates a small model (≤14B or instruct-type).
    /// Unparsable ids: remote providers → large (frontier APIs), local → small (safe).
    /// </summary>
    public static bool IsSmallModel(string? modelId, bool isRemoteProvider = false)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return !isRemoteProvider; // no info: remote → large, local → small

        var id = modelId.Trim().ToLowerInvariant();

        // Parameter marker wins when present (a "70B-Instruct" is large)
        var match = ParamMarker.Match(id);
        if (match.Success)
        {
            if (!double.TryParse(match.Groups["params"].Value,
                    System.Globalization.CultureInfo.InvariantCulture, out var paramsB))
                return !isRemoteProvider;
            return paramsB <= SmallModelMaxB;
        }

        // Instruct-tuned chat models without a size marker → small
        if (id.Contains("instruct"))
            return true;

        // No parsable info: remote → large (frontier APIs), local → small (safe)
        return !isRemoteProvider;
    }
}