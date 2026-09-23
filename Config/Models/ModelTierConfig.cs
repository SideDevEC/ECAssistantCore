using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// v14.12: Model-tier profile — gates how much harness scaffolding (hand-holding
/// directives, pre-planning, thinking budgets) the agent loop applies. Small models
/// need the full scaffolding; large models get a slim profile so the harness does
/// not hinder them.
/// 
/// Config-driven only — no transport heuristic. The user sets mode explicitly:
/// "small", "large", or absent (defaults to "small" — safe default, extra
/// scaffolding doesn't hurt large models but missing scaffolding hurts small ones).
/// </summary>
public class ModelTierConfig
{
    /// <summary>Tier mode: "small", "large", or null/absent (defaults to small — safe).</summary>
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
    /// Pure resolution from immutable config — no transport heuristic, no isLocal.
    /// Explicit mode wins; absent config defaults to small (safe).
    /// </summary>
    // Stateless utility — no mutable state
    public bool IsLargeRuntime()
    {
        var mode = Mode?.Trim().ToLowerInvariant();
        return string.Equals(mode, "large", StringComparison.OrdinalIgnoreCase);
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
