using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Interface for decomposing user requests into sub-tasks.
/// </summary>
public interface ITaskPlanner
{
    List<SubTask> Decompose(string request);
    SubTask? Current { get; }
    void CompleteCurrent();
    void FailCurrent(string reason);
    string GetProgressContext();
    bool HasRemaining { get; }
    string GetSummary();
}