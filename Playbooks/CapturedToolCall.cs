namespace ECAssistant.Core.Playbooks;

/// <summary>
/// v14.14: One successful tool call from a completed run, in summarized form.
/// The orchestrator records these as the run progresses; the extractor turns the
/// sequence into playbook steps. Args are pre-summarized (truncated key=value)
/// so no concrete tool types are needed — keeps Core LLM-agnostic AND tool-agnostic.
/// </summary>
public sealed record CapturedToolCall(string ToolName, string ArgsSummary);