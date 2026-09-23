using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// v14.12: Model-tier profile — gates how much harness scaffolding (hand-holding
/// directives, pre-planning, thinking budgets) the agent loop applies. Small models
/// need the full scaffolding; large models get a slim profile so the harness does
/// not hinder them. Absent config = auto: local → small (current behavior preserved),
/// remote → large.
/// </summary>
public class ModelTierConfig
{
    /// <summary>Tier mode: "small", "large", or "auto"/null (auto = decide from local/remote).</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    /// <summary>
    /// Resolve whether the active model should get the large-model (slim) profile.
    /// Pure resolution from immutable config — no mutable state.
    /// </summary>
    // Stateless utility — no mutable state
    public bool IsLargeRuntime(bool isLocal)
    {
        var mode = Mode?.Trim().ToLowerInvariant();
        return mode switch
        {
            "small" => false,
            "large" => true,
            _ => !isLocal // auto/null: local runs small models, remote runs frontier models
        };
    }
}
