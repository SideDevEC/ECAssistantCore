using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// v14.9: interactive checkpoint policy — decides WHEN the orchestrator may pause
/// and ask the user a question with options (via ISessionOutput.RequestChoice).
/// Checkpoints, not a mode: autonomous by default, ask only at explicit hook points.
/// </summary>
public class InteractionConfig
{
    /// <summary>
    /// When true, the orchestrator presents the preplanning/decomposition result as
    /// choices (approve / answer directly / proceed without plan) before executing.
    /// Default: false — non-breaking, autonomous plan execution unchanged.
    /// </summary>
    [JsonPropertyName("confirm_plan")]
    public bool ConfirmPlan { get; init; } = false;
}
