namespace ECAssistant.Core.Engine;

/// <summary>
/// Execution lifecycle state for the main agent loop (CTS, ESC flag, turn counter).
/// Thread-safe: the ESC flag and turn counter are touched from streaming callbacks
/// and parallel tool execution, so they use volatile/interlocked access and the
/// CTS is guarded by a lock.
/// Extracted from AgentEngine to group related mutable state (v12 refactor).
/// </summary>
public sealed class ExecutionLifecycleState
{
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private int _escFlag;
    private int _turnCount;

    /// <summary>Current execution token (None when not executing).</summary>
    public CancellationToken Token
    {
        get { lock (_lock) return _cts?.Token ?? CancellationToken.None; }
    }

    /// <summary>True when the user pressed ESC to stop the current generation.</summary>
    public bool EscPressed
    {
        get => Volatile.Read(ref _escFlag) == 1;
        set => Volatile.Write(ref _escFlag, value ? 1 : 0);
    }

    /// <summary>Current turn number (1-based after first IncrementTurn).</summary>
    public int TurnCount
    {
        get => Volatile.Read(ref _turnCount);
        set => Volatile.Write(ref _turnCount, value);
    }

    /// <summary>Atomically increment the turn counter and return the new value.</summary>
    public int IncrementTurn() => Interlocked.Increment(ref _turnCount);

    /// <summary>Begin an execution: create a fresh CTS and clear the ESC flag.</summary>
    public void Start()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }
        EscPressed = false;
    }

    /// <summary>Signal cancellation of the current execution.</summary>
    public void Stop()
    {
        lock (_lock) _cts?.Cancel();
    }

    /// <summary>End an execution: dispose and clear the CTS.</summary>
    public void End()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
}
