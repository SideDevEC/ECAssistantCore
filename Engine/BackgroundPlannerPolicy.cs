namespace ECAssistant.Core.Engine;

/// <summary>Decision produced by <see cref="BackgroundPlannerPolicy"/>.</summary>
public enum BackgroundPlanAction
{
    /// <summary>Plan arrived while the loop is running — fold it in.</summary>
    FoldIn,

    /// <summary>Loop finished (or plan useless) — discard without touching the loop.</summary>
    Discard
}

/// <summary>
/// v14.10.3: merge policy for the non-blocking background planner (Emre's option 3).
/// The decision loop starts immediately on the raw goal; the decomposition pass runs
/// concurrently. When the plan lands, this policy decides whether it may still
/// influence the run: fold in ONLY while the loop is active AND the plan has more
/// than one step (a 1-step "plan" adds no information). Discard otherwise.
/// Stateless — the orchestrator owns all mutable orchestration state.
/// </summary>
public class BackgroundPlannerPolicy
{
    /// <summary>Minimum useful plan size. A single-step plan never folds in.</summary>
    public const int MinFoldPlanSize = 2;

    /// <summary>
    /// Decide what to do with a completed background plan.
    /// </summary>
    /// <param name="planSize">Number of decomposed steps (0 if decomposition failed).</param>
    /// <param name="loopFinished">True when the decision loop already produced its final answer.</param>
    public BackgroundPlanAction Decide(int planSize, bool loopFinished)
    {
        if (loopFinished || planSize < MinFoldPlanSize)
            return BackgroundPlanAction.Discard;
        return BackgroundPlanAction.FoldIn;
    }

    /// <summary>
    /// Compute the raised turn budget for a folded plan. Never lowers an existing
    /// budget (2 turns per sub-task + buffer, mirroring the blocking planner path).
    /// </summary>
    public int AdjustTurnBudget(int currentMaxTurns, int planSize, int turnsPerSubtask = 2, int turnBuffer = 2)
    {
        if (planSize < MinFoldPlanSize)
            return currentMaxTurns;
        return Math.Max(currentMaxTurns, planSize * turnsPerSubtask + turnBuffer);
    }
}
