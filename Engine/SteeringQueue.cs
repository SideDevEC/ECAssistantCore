namespace ECAssistant.Core.Engine;

/// <summary>
/// v14.12.2: mid-run steering seam. The host queues user input while the
/// orchestrator loop is running; the loop drains it at the top of each turn
/// (before the next LLM decision) and injects it as fresh instructions.
/// Single pending slot — the newest steering wins (it supersedes, not queues).
/// Thread-safe; owned by the orchestrator, exposed to hosts via AgentSession.
/// </summary>
public sealed class SteeringQueue
{
    private readonly object _gate = new();
    private string? _pending;

    /// <summary>Queue steering input; overwrites any pending steering.</summary>
    public void Steer(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_gate) _pending = message.Trim();
    }

    /// <summary>Take and clear the pending steering (null when none).</summary>
    public string? Drain()
    {
        lock (_gate)
        {
            var m = _pending;
            _pending = null;
            return m;
        }
    }

    /// <summary>True when steering is pending.</summary>
    public bool HasPending { get { lock (_gate) return _pending != null; } }
}