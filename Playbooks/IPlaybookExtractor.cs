namespace ECAssistant.Core.Playbooks;

/// <summary>
/// v14.14: Deterministic playbook extraction from a completed run's successful
/// tool-call log. NO LLM involvement — Core stays LLM-agnostic.
/// </summary>
public interface IPlaybookExtractor
{
    /// <summary>
    /// Build a candidate playbook from the user goal and the run's successful
    /// tool calls. Returns null when there is nothing worth persisting
    /// (no successful tool calls).
    /// </summary>
    Playbook? Extract(string goal, IReadOnlyList<CapturedToolCall> successfulCalls, string source = "goal");
}