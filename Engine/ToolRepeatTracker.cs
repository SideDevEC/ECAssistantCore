using System.Collections.Concurrent;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Detects pathological tool-call repetition in the orchestrator loop — beyond the
/// v12.4/v12.5 last-call guards (failed repeat / just-successful repeat), this tracks
/// a bounded signature history so multi-step loops (A→B→A→B) and long-range repeats
/// (same call 3+ times) are caught too. Batch-path calls can be recorded as well.
///
/// Levels:
/// - count 2 (or A→B→A→B alternation): nudge — orchestrator injects a redirect and
///   does not execute the repeat.
/// - count 3+: stop — the orchestrator ends the run with a loop-detected report.
///
/// Stateless per instance; one tracker per orchestrator run. Thread-safe for the
/// parallel batch path.
/// </summary>
public sealed class ToolRepeatTracker
{
    /// <summary>Identical repeats before the orchestrator injects a redirect.</summary>
    public const int NudgeThreshold = 2;

    /// <summary>Identical repeats before the run is stopped as a detected loop.</summary>
    public const int StopThreshold = 3;

    /// <summary>Alternation window length (a,b,a,b).</summary>
    private const int AlternationWindow = 4;

    private readonly ConcurrentDictionary<string, int> _counts = new();
    private readonly ConcurrentQueue<string> _history = new();
    private int _historyCount;

    /// <summary>
    /// v14.10.2: clears all recorded signatures and history. Called by the
    /// orchestrator on Reset() so repeat guards never leak across goals.
    /// </summary>
    public void Reset()
    {
        _counts.Clear();
        while (_history.TryDequeue(out _)) { }
        _historyCount = 0;
    }

    /// <summary>
    /// v14.10.2: rolls back a prior Record for a call that never executed
    /// (denied/blocked). A user denial must not count toward loop detection —
    /// otherwise the tool gets un-executable until the run dies.
    /// </summary>
    public void Unrecord(string signature)
    {
        if (_counts.TryGetValue(signature, out var c))
        {
            if (c <= 1) _counts.TryRemove(signature, out _);
            else _counts[signature] = c - 1;
        }
        // History is a fixed-size window; leaving a stale entry only weakens
        // alternation detection by one slot, never causes false stops.
    }

    /// <summary>Records a call signature and returns the current repeat count (1 = first).</summary>
    public int Record(string signature)
    {
        var count = _counts.AddOrUpdate(signature, 1, (_, c) => c + 1);
        _history.Enqueue(signature);
        if (Interlocked.Increment(ref _historyCount) > AlternationWindow)
        {
            _history.TryDequeue(out _);
            Interlocked.Decrement(ref _historyCount);
        }
        return count;
    }

    /// <summary>True when this signature has reached the nudge level (seen exactly NudgeThreshold times).</summary>
    public bool IsNudgeLevel(int count) => count == NudgeThreshold;

    /// <summary>True when this signature has reached the stop level (StopThreshold or more).</summary>
    public bool IsStopLevel(int count) => count >= StopThreshold;

    /// <summary>
    /// True when the last AlternationWindow signatures form an a→b→a→b alternation
    /// (a ≠ b). Catches ping-pong loops where each individual call is "fresh".
    /// </summary>
    public bool IsAlternationLoop()
    {
        if (_historyCount < AlternationWindow) return false;
        var sigs = _history.ToArray();
        var a = sigs[0]; var b = sigs[1];
        if (string.Equals(a, b, StringComparison.Ordinal)) return false;
        return string.Equals(sigs[2], a, StringComparison.Ordinal)
            && string.Equals(sigs[3], b, StringComparison.Ordinal);
    }

    /// <summary>How many times the given signature has been recorded (0 if never).</summary>
    public int CountOf(string signature) => _counts.GetValueOrDefault(signature, 0);
}
