using ECAssistant.Core.Engine;
using ECAssistant.Core.Orchestration;

namespace ECAssistant.Core.Tools.SubAgent;

/// <summary>
/// Sub-Agent Spawn Tool — allows the main agent to spawn isolated sub-agents
/// for complex tasks. Sub-agents share the loaded model weights but have
/// their own context window, KV cache, and tool set.
///
/// v10.18.1: Structured error handling, retry, resource limits, cancellation.
///
/// Usage:
///   ESubAgent(task:Research the codebase structure and report file count)
///   ESubAgent(task:Fix the bug in line 42, working_dir:/path/to/project, tools:EShellAgent,ECodeEditor,EDotnetBuild)
///   ESubAgent(task:Write unit tests for the auth module, context_size:8192, max_turns:8)
/// </summary>
public class ESubAgentTool : EToolBase
{
    private readonly SubAgentManager _manager;
    private readonly string _defaultWorkingDir;

    public ESubAgentTool(SubAgentManager manager, string defaultWorkingDir)
    {
        _manager = manager;
        _defaultWorkingDir = defaultWorkingDir;
    }

    public override string Name => "ESubAgent";

    public override string Description =>
        "context window and tool set, then returns a result. Use for tasks that need deep focus " +
        "or might fill up the main context. Supports parallel sub-agents via multiple tool calls. " +
        "Includes automatic retry, resource limits, and structured error reporting. " +
        "Use ONLY for genuinely complex, self-contained subtasks. Do NOT use for simple questions " +
        "or a single tool call you can make yourself.";

    public override string UsageExample =>
        "ESubAgent(task=\"Research the codebase\")";

    public override string GetToolRules() =>
        "task = description of what the sub-agent should do (required). " +
        "working_dir = override working directory (optional). " +
        "tools = comma-separated tool names to allow (optional, empty=all). " +
        "context_size = context window size (optional, defaults to subagent config). " +
        "max_turns = max turns for sub-agent (optional, defaults to subagent config). " +
        "timeout = timeout in seconds (optional, defaults to subagent config). " +
        "max_retries = auto-retry attempts on failure (optional, defaults to subagent config). " +
        "Multiple ESubAgent calls in one response run in PARALLEL.";

    public override string GetToolExample() =>
        "ESubAgent(task:Analyze the project structure and list all .cs files)\n" +
        "ESubAgent(task:Fix the bug, tools:EShellAgent,ECodeEditor, context_size:8192)";

    public override string GetParameterSchema() =>
        """
        {
          "type": "object", "required": ["task"],
          "properties": {
            "task": { "type": "string", "description": "Objective for the sub-agent" },
            "working_dir": { "type": "string", "description": "Working directory (default: session's)" },
            "tools": { "type": "string", "description": "Comma-separated tool names the sub-agent may use (default: none)" },
            "context_size": { "type": "string", "description": "Context window size (default: manager default)" },
            "max_turns": { "type": "string", "description": "Max agent turns (default: manager default)" },
            "timeout": { "type": "string", "description": "Timeout in seconds (default: manager default)" },
            "max_retries": { "type": "string", "description": "Max retries (default: manager default)" }
          }
        }
        """;

    public override async Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        var taskDesc = arguments.GetValueOrDefault("task")?.Trim();
        if (string.IsNullOrEmpty(taskDesc))
            return new EToolResult { ToolName = Name, Succeeded = false, Error = "Missing task argument." };

        // Parse optional arguments
        var workingDir = arguments.GetValueOrDefault("working_dir") ?? _defaultWorkingDir;
        var toolsStr = arguments.GetValueOrDefault("tools") ?? "";
        var allowedTools = string.IsNullOrEmpty(toolsStr)
            ? new List<string>()
            : toolsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        uint contextSize = _manager.DefaultContextSize;
        if (uint.TryParse(arguments.GetValueOrDefault("context_size"), out var cs))
            contextSize = cs;

        int maxTurns = _manager.DefaultMaxTurns;
        if (int.TryParse(arguments.GetValueOrDefault("max_turns"), out var mt))
            maxTurns = mt;

        int timeoutSeconds = _manager.DefaultTimeoutSeconds;
        if (int.TryParse(arguments.GetValueOrDefault("timeout"), out var ts))
            timeoutSeconds = ts;

        int maxRetries = _manager.DefaultMaxRetries;
        if (int.TryParse(arguments.GetValueOrDefault("max_retries"), out var mr))
            maxRetries = mr;

        var task = new SubAgentTask
        {
            Description = taskDesc,
            Prompt = taskDesc,
            WorkingDir = workingDir,
            AllowedTools = allowedTools,
            ContextSize = contextSize,
            MaxTurns = maxTurns,
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = maxRetries,
        };

        try
        {
            var result = await _manager.RunAsync(task);

            // v10.18.1: Use structured result for output
            var output = result.ToContextString();

            var metadata = new Dictionary<string, string>
            {
                ["succeeded"] = result.Succeeded.ToString(),
                ["tool_calls"] = result.ToolCallsMade.ToString(),
                ["duration_s"] = result.Duration.TotalSeconds.ToString("F1"),
                ["files_created"] = string.Join(",", result.FilesCreated),
            };

            if (result.Error != null)
            {
                metadata["error_kind"] = result.Error.Kind.ToString();
                metadata["retry_attempt"] = result.Error.RetryAttempt.ToString();
            }

            return result.Succeeded
                ? new EToolResult { ToolName = Name, Succeeded = true, Output = output, Metadata = metadata }
                : new EToolResult { ToolName = Name, Succeeded = false, Error = output, Metadata = metadata };
        }
        catch (Exception ex)
        {
            return new EToolResult { ToolName = Name, Succeeded = false, Error = $"Sub-agent execution failed: {ex.Message}" };
        }
    }
        /// <summary>v15: small tier gets delegation do-nots; large tier gets parallelization guidance.</summary>
        public override string GetToolRulesForTier(bool isLargeTier)
        {
            if (isLargeTier)
            {
                return "Rules:\n" +
                       "- Delegate self-contained sub-tasks that would take multiple tool calls; keep single-call work yourself.\n" +
                       "- Give each sub-agent a self-sufficient task description and its own working area when file writes are involved.\n";
            }
            return "Rules:\n" +
                   "- Use ESubAgent ONLY for tasks that need MULTIPLE tool calls. One tool call = do it yourself.\n" +
                   "- The task description MUST be complete and standalone: what to do, where, what to return. Never 'as discussed above'.\n" +
                   "- NEVER delegate the same task twice.\n" +
                   "- When the sub-agent returns a result, pass it on — do not redo the work.\n";
        }

}