using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// v15: tier-aware inference parameter tuning — values live in CONFIG
/// (model_tier.inference), applied here. No hardcoded tier defaults; global
/// sampling config is the fallback for fields a tier doesn't set.
/// </summary>
// Stateless utility — no mutable state; pure function over its inputs
public static class TierInferenceTuner
{
    /// <summary>
    /// v15: apply tier-owned inference values from config (ModelTierConfig.Inference).
    /// When a tier defines a value, it WINS — config is the single source of tier
    /// shaping (Emre, 2026-09-23). Fields the tier doesn't set inherit global sampling.
    /// </summary>
    public static InferenceRequestParams Apply(
        InferenceRequestParams parameters, bool isLargeTier,
        SamplingConfig? sampling = null, TierInferenceOverride? tierOverride = null)
    {
        if (parameters == null || tierOverride == null) return parameters;

        if (tierOverride.Temperature is { } t) parameters.Temperature = t;
        if (tierOverride.TopP is { } tp) parameters.TopP = tp;
        if (tierOverride.TopK is { } tk) parameters.TopK = tk;
        if (tierOverride.RepeatPenalty is { } rp) parameters.RepeatPenalty = rp;
        if (tierOverride.MaxTokens is { } mt) parameters.MaxTokens = mt;
        return parameters;
    }
}
