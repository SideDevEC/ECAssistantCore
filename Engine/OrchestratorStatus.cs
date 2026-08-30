namespace ECAssistant.Core.Orchestration;

/// <summary>Status code for orchestrator completion.</summary>
public enum OrchestratorStatus
{
    GoalAchieved,
    TurnsExhausted,
    /// <summary>Engine/transport failure surfaced (not a model-format problem).</summary>
    Failed
}
