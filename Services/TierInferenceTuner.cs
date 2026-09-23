using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// v14.17: tier-aware inference parameter tuning. Small models need tighter
/// sampling (lower temperature, stronger repeat penalty) to counter rambling
/// and loops; frontier models are best left at configured values. Explicitly
/// customized sampling always wins — the tuner only shifts DEFAULTS.
/// </summary>
// Stateless utility — no mutable state; pure function over its inputs
public static class TierInferenceTuner
{
    /// <summary>Small-tier temperature when sampling is at defaults.</summary>
    public const float SmallTierTemperature = 0.2f;

    /// <summary>Small-tier repeat penalty when sampling is at defaults.</summary>
    public const float SmallTierRepeatPenalty = 1.15f;

    /// <summary>
    /// Apply the tier profile to inference params. Pure — returns the adjusted
    /// instance (small tier) or the original (large tier / custom sampling).
    /// Default-ness is detected against a fresh SamplingConfig so user
    /// customizations are never overridden.
    /// </summary>
    public static InferenceRequestParams Apply(
        InferenceRequestParams parameters, bool isLargeTier, SamplingConfig? sampling = null)
    {
        if (parameters == null || isLargeTier) return parameters;
        var defaults = sampling ?? new SamplingConfig();

        // Respect explicit user customization — only shift shipped defaults.
        if ((parameters.Temperature ?? defaults.Temperature) == defaults.Temperature)
            parameters.Temperature = SmallTierTemperature;
        if ((parameters.RepeatPenalty ?? defaults.RepeatPenalty) == defaults.RepeatPenalty)
            parameters.RepeatPenalty = SmallTierRepeatPenalty;
        return parameters;
    }
}
