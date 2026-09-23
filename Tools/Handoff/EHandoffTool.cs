using ECAssistant.Core.Engine;
using ECAssistant.Core.Orchestration;

namespace ECAssistant.Core.Tools.Handoff;

/// <summary>
/// Handoff tool — allows the main agent to delegate the entire remaining task
/// to an ephemeral specialist agent with a custom system prompt and tool subset.
///
/// Unlike <see cref="SubAgent.ESubAgentTool"/> (which runs a sub-task and returns
/// the result to the parent for further processing), a handoff means the specialist
/// takes over. Its answer becomes the final answer — the parent does not continue.
///
/// The specialist is ephemeral: it exists only for the duration of the handoff,
/// then is disposed. Nothing is persisted, no config is needed, and no specialist
/// list is injected into the system prompt (zero per-turn token cost).
///
/// Usage:
///   EHandoff(prompt:"You are a SQL optimization specialist. Schema: Users, Orders…", tools:"EShellAgent,EFileReader", reason:"Deep SQL focus needed", context:"The user wants to optimize a 5-table join query")
/// </summary>
public sealed class EHandoffTool : EToolBase
{
    /// <summary>
    /// Callback the orchestrator hooks into. When the tool executes, it builds
    /// the <see cref="HandoffRequest"/> and invokes this callback. The orchestrator
    /// runs the specialist and returns the final result — this tool's ExecuteAsync
    /// is a thin wrapper that never actually "succeeds" in the normal tool sense;
    /// the orchestrator intercepts before the result is fed back.
    /// </summary>
    private readonly Func<HandoffRequest, CancellationToken, Task<OrchestratorResult>> _executeHandoff;

    public EHandoffTool(Func<HandoffRequest, CancellationToken, Task<OrchestratorResult>> executeHandoff)
    {
        _executeHandoff = executeHandoff;
    }

    public override string Name => "EHandoff";

    public override string Description =>
        "Delegate the entire remaining task to an ephemeral specialist agent with a custom system prompt. " +
        "The specialist takes over and its answer becomes the final answer — you do not continue after. " +
        "Use when a task needs deep focus, a different persona, or restricted tools. " +
        "The specialist is temporary and disposed after completion.";

    public override string UsageExample => "EHandoff(task=\"Delegate to specialist\")";

    public override string GetToolRules() =>
        "prompt = full system prompt for the specialist (required). " +
        "tools = comma-separated tool names the specialist may use (optional, empty = all built-in). " +
        "reason = why you are handing off (optional, for logging). " +
        "context = summary of what the user wants and what you know so far (optional but recommended). " +
        "name = short name for the specialist (optional, for logging). " +
        "model_override = different model ID on the same endpoint (optional). " +
        "max_turns = max turns for the specialist (optional, default 8). " +
        "timeout = timeout in seconds (optional, default 180). " +
        "Do NOT use for simple questions or single tool calls — handle those yourself. " +
        "Do NOT use EHandoff for sub-tasks where you need the result to continue — use ESubAgent for that.";

    public override string GetToolExample() =>
        "EHandoff(prompt:\"You are a SQL optimization specialist. The schema has tables: Users(id, name), Orders(id, user_id, total), Products(id, name, price). Only write SQL.\", tools:\"EShellAgent,EFileReader\", reason:\"Query optimization needs deep SQL focus\", context:\"User wants to optimize a 5-table join that runs slowly\")";

    public override string GetParameterSchema() =>
        """
        {
          "type": "object", "required": ["prompt"],
          "properties": {
            "prompt": { "type": "string", "description": "Full system prompt for the specialist agent" },
            "tools": { "type": "string", "description": "Comma-separated tool names the specialist may use (default: all)" },
            "reason": { "type": "string", "description": "Why the handoff is happening (logging only)" },
            "context": { "type": "string", "description": "Summary of task context to pass to the specialist" },
            "name": { "type": "string", "description": "Short name for the specialist (logging only)" },
            "model_override": { "type": "string", "description": "Different model ID on the same endpoint (optional)" },
            "max_turns": { "type": "string", "description": "Max turns for the specialist (default: 8)" },
            "timeout": { "type": "string", "description": "Timeout in seconds (default: 180)" }
          }
        }
        """;

    public override async Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        var prompt = arguments.GetValueOrDefault("prompt")?.Trim();
        if (string.IsNullOrEmpty(prompt))
            return EToolResult.Failure(Name, "Missing required 'prompt' argument.");

        var request = new HandoffRequest
        {
            SystemPrompt = prompt,
            AllowedTools = arguments.GetValueOrDefault("tools") ?? "",
            Reason = arguments.GetValueOrDefault("reason") ?? "",
            ContextSummary = arguments.GetValueOrDefault("context") ?? "",
            Name = arguments.GetValueOrDefault("name") ?? "specialist",
            ModelOverride = arguments.GetValueOrDefault("model_override"),
        };

        if (int.TryParse(arguments.GetValueOrDefault("max_turns"), out var mt))
            request = request with { MaxTurns = mt };
        if (int.TryParse(arguments.GetValueOrDefault("timeout"), out var ts))
            request = request with { TimeoutSeconds = ts };

        try
        {
            var result = await _executeHandoff(request, cancellationToken);

            // The orchestrator intercepts EHandoff results — but if we get here
            // (e.g. the orchestrator didn't intercept), return the specialist's
            // output as the tool result so it flows back normally.
            return result.Status == OrchestratorStatus.GoalAchieved
                ? EToolResult.Success(Name, result.FinalOutput, new Dictionary<string, string>
                {
                    ["handoff"] = "true",
                    ["status"] = "GoalAchieved",
                    ["tool_calls"] = result.ToolCallsMade.ToString(),
                })
                : EToolResult.Failure(Name, result.FinalOutput, new Dictionary<string, string>
                {
                    ["handoff"] = "true",
                    ["status"] = result.Status.ToString(),
                });
        }
        catch (Exception ex)
        {
            return EToolResult.Failure(Name, $"Handoff execution failed: {ex.Message}");
        }
    }
        /// <summary>v15: small tier gets when-to-handoff do-nots; large tier gets judgment guidance.</summary>
        public override string GetToolRulesForTier(bool isLargeTier)
        {
            if (isLargeTier)
            {
                return "Rules:\n" +
                       "- Hand off genuinely isolated sub-problems; keep coherent threads yourself.\n" +
                       "- Write the specialist prompt as a complete standalone task brief (context, constraints, expected output).\n";
            }
            // v15 small-tier rules: anti-narration lines. Small models drift with
            // recency bias — rules stated long ago lose attention, so they announce
            // ("I will call EHandoff...") instead of acting, or stop before relaying
            // the specialist's result. State both failure modes as hard do-nots.
            return "Rules:\n" +
                   "- Use EHandoff ONLY when the user asks for a specialist handoff or the task needs a fully separate agent.\n" +
                   "- Do NOT use EHandoff for sub-tasks you can do yourself with one tool call.\n" +
                   "- The specialist prompt MUST be a full standalone instruction (what to do + what to return). Never 'see above'.\n" +
                   "- NEVER describe or announce a handoff (e.g. 'I will call EHandoff...'). If a handoff is needed, emit the EHandoff tool call in THIS turn.\n" +
                   "- Answering the delegated task yourself instead of calling EHandoff is a failure.\n" +
                   "- After the specialist returns, report ITS result to the user — do not re-do the task yourself.\n";
        }

}