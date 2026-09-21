using ECAssistant.Core.Session;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Interactive Decision Loop — lets the agent ask clarifying questions,
/// present options, and wait for user input before proceeding.
///
/// All output goes through ISessionOutput — no direct UI calls.
/// DEAD CODE: not wired into any production execution path (only tests reference it).
/// Kept as an experimental interactive-loop prototype.
/// </summary>
public class EDecisionLoop : IDisposable
{
    private readonly EAgentEngine _engine;
    private readonly ISessionOutput? _out;
    private bool _running = false;

    public EDecisionLoop(EAgentEngine engine, ISessionOutput? sessionOutput = null)
    {
        _engine = engine;
        _out = sessionOutput;
    }

    public async Task<DecisionResult> ExecuteInteractiveLoop(string taskDescription)
    {
        _out?.WriteTag("Decision", $"Interactive loop for: {taskDescription}", OutputState.Info);
        _out?.BlankLine();

        _running = true;
        var maxRounds = 5;
        // Preserve the original instruction — the approval notice must not overwrite it.
        var originalTask = taskDescription;

        for (int round = 1; round <= maxRounds && _running; round++)
        {
            _out?.WriteTag("Round", $"{round}/{maxRounds}", OutputState.Info);

            var decision = await _engine.GenerateAsync(taskDescription);

            _out?.BlankLine();

            if (decision.WantsDirectAnswer)
            {
                var answer = decision.AnswerText ?? string.Empty;
                _out?.WriteTag("Decision", "Final answer received.", OutputState.Success);
                _out?.WriteLine(answer, OutputState.Bold);

                return new DecisionResult
                {
                    Success = true,
                    OptionChosen = "final",
                    Outcome = answer
                };
            }

            _out?.WriteLine($"[Decision] LLM says: {decision.AnswerText ?? "(tool call requested)"}", OutputState.Info);
            _out?.BlankLine();

            if (round < maxRounds)
            {
                var approved = _out?.RequestApproval("Continue? (approve to proceed, deny to cancel)") ?? false;

                if (!approved)
                {
                    _out?.WriteTag("Decision", "Cancelled by user.", OutputState.Warning);
                    _running = false;
                    return new DecisionResult
                    {
                        Success = false,
                        OptionChosen = "cancelled",
                        Outcome = "User cancelled the decision loop."
                    };
                }

                taskDescription = $"User approved continuing. Original task:\n\n{originalTask}";
            }
        }

        _out?.WriteTag("Decision", "Max rounds reached.", OutputState.Warning);
        return new DecisionResult
        {
            Success = false,
            OptionChosen = "max_rounds",
            Outcome = "Decision loop reached maximum rounds without a final answer."
        };
    }

    public void Dispose() => _running = false;
}

