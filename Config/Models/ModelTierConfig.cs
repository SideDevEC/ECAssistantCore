using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// v14.12: Model-tier profile — gates how much harness scaffolding (hand-holding
/// directives, pre-planning, thinking budgets) the agent loop applies. Small models
/// need the full scaffolding; large models get a slim profile so the harness does
/// not hinder them.
/// 
/// Tier is derived from the MODEL, not transport (Emre, 2026-09-24):
/// - "small" / "large" → explicit config wins
/// - "auto"/null → parsed from the model id: ≤14B parameter markers or
///   instruct-type models are small; larger → large; unparsable → remote: large,
///   local: small (safe default).
/// </summary>
public class ModelTierConfig
{
    /// <summary>Tier mode: "small", "large", "auto", or null (auto = derive from model id).</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    /// <summary>
    /// v15: tier-owned inference values (Emre, 2026-09-23). When set, these OVERRIDE
    /// the global sampling defaults for this tier — small tiers get deterministic
    /// values here, large tiers keep freedom. Keys mirror sampling config.
    /// </summary>
    [JsonPropertyName("inference")]
    public TierInferenceOverride? Inference { get; init; }

    /// <summary>
    /// v15: time budget in seconds for THIS tier's agent runs. Small tier: null =
    /// turn-limited (the turn system). Large tier: set this (e.g. 900) and the
    /// orchestrator enforces TIME, not turns — work runs until the budget expires.
    /// </summary>
    [JsonPropertyName("timeout_seconds")]
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Resolve whether the active model should get the large-model (slim) profile.
    /// Pure resolution from immutable config + model id — explicit mode wins;
    /// auto/null derives from the model id via <see cref="ModelTierAutoResolver"/>
    /// (≤14B or instruct → small; >14B → large; unparsable → remote: large, local: small).
    /// </summary>
    // Stateless utility — no mutable state
    public bool IsLargeRuntime(string? modelId, bool isRemoteProvider)
    {
        var mode = Mode?.Trim().ToLowerInvariant();
        return mode switch
        {
            "small" => false,
            "large" => true,
            _ => !ModelTierAutoResolver.IsSmallModel(modelId, isRemoteProvider) // auto: derive from the model
        };
    }
}

/// <summary>
/// v15: per-tier inference overrides. Null fields inherit global sampling config.
/// </summary>
public class TierInferenceOverride
{
    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }
    [JsonPropertyName("top_p")]
    public float? TopP { get; init; }
    [JsonPropertyName("top_k")]
    public int? TopK { get; init; }
    [JsonPropertyName("repeat_penalty")]
    public float? RepeatPenalty { get; init; }
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }
}
