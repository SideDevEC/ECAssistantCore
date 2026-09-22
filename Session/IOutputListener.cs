namespace ECAssistant.Core.Session;

/// <summary>
/// Listener interface for session output.
/// Any UI (console, web, test harness) implements this.
/// Session never knows what's behind it.
/// </summary>
public interface IOutputListener
{
    /// <summary>Called when a line is written (non-stream output or flushed stream content).</summary>
    void OnOutput(string text, OutputState state);

    /// <summary>Called when streaming starts. Listener can poll GetStreamBuffer() for live content.</summary>
    void OnStreamStart();

    /// <summary>Called when streaming stops. Buffer is about to be flushed via WriteLine.</summary>
    void OnStreamStop();

    /// <summary>
    /// Called when the session needs user approval. The listener must display the
    /// message to the user and collect a yes/no response. Blocks until the user responds.
    /// Returns true if approved, false if denied.
    /// </summary>
    bool OnRequestApproval(string message);

    /// <summary>
    /// v14.9: display the prompt and options, collect a choice (1-based index).
    /// Default: null (no choice) — existing listeners keep compiling and the
    /// orchestrator proceeds autonomously when null is returned.
    /// </summary>
    int? OnRequestChoice(string prompt, IReadOnlyList<string> options) => null;

    /// <summary>
    /// v14.10.1: processing-status hint (spinner label): "Thinking…",
    /// "Running EShellAgent…". Null/empty = clear (output is arriving).
    /// Default: no-op — existing listeners keep compiling.
    /// </summary>
    void OnStatus(string? status) { }
}