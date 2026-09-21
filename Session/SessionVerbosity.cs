namespace ECAssistant.Core.Session;

/// <summary>
/// UI output verbosity for a session.
/// </summary>
public enum SessionVerbosity
{
    /// <summary>
    /// Default. Show only what matters: user input, streamed answers, final results,
    /// warnings and errors, tool denials. Diagnostic chatter ([Orchestrator] decisions,
    /// [Engine] dumps, [KVCache]/[Memory] status, plan steps) is hidden from the UI but
    /// still written to the session transcript file.
    /// </summary>
    Silent,

    /// <summary>Show everything, including diagnostic and debug lines.</summary>
    Verbose
}
