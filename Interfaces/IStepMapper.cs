using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Interface for mapping sub-tasks to concrete tool calls.
/// </summary>
public interface IStepMapper
{
    Task<ExecutionPlan> MapAsync(List<SubTask> subTasks, string originalGoal);
}