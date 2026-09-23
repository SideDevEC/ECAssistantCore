using System.Text;
using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Tools.Background;

/// <summary>
/// Background Exec Tool — lets the LLM start long-running processes
/// without blocking the agent loop.
/// </summary>
public class EBackgroundExecTool : EToolBase
{
    private readonly BackgroundProcessManager _mgr;
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly JsonElement? _toolConfig;
    private readonly string _workingDir;

    public override string Name => "EBackgroundExec";

    public override string Description =>
        "Start, check, or kill background processes. Non-blocking — lets you run long commands " +
        "like builds while continuing to work. Use action=start to begin, action=status to list, " +
        "action=output to get results, action=kill to terminate. Use ONLY for long-running commands " +
        "that must not block the conversation. Do NOT use for quick commands or knowledge questions.";

    public override string UsageExample =>
        "EBackgroundExec(action:start, command:dotnet build)";

    public override bool IsEnabled { get; protected set; } = true;

    public EBackgroundExecTool(
        BackgroundProcessManager mgr,
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        AppConfig config)
    {
        _mgr = mgr;
        _processRunner = processRunner;
        _fileSystem = fileSystem;
        config.Tools.TryGetValue(Name, out var tc);
        _toolConfig = tc.ValueKind == JsonValueKind.Undefined ? null : tc;
        IsEnabled = ReadCfg(_toolConfig, "enabled", true);
        _workingDir = ReadCfg(_toolConfig, "workingDir", Environment.CurrentDirectory);
    }

    public override object GetConfigSection() => new { enabled = true };

    public override string GetParameterSchema() =>
        """
        {
          "type": "object", "required": ["action"],
          "properties": {
            "action": { "type": "string", "enum": ["start", "status", "output", "kill"], "description": "Background process operation" },
            "command": { "type": "string", "description": "Command to run (required for action=start)" },
            "id": { "type": "string", "description": "Process id (required for action=output and action=kill)" }
          }
        }
        """;

    public override async Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        var action = arguments.GetValueOrDefault("action")?.ToLower().Trim();

        switch (action)
        {
            case "start":
            {
                var command = arguments.GetValueOrDefault("command");
                if (string.IsNullOrWhiteSpace(command))
                    return EToolResult.Failure(Name, "Missing 'command' argument for action=start.");

                if (cancellationToken.IsCancellationRequested)
                    return EToolResult.Failure(Name, "Background process start was cancelled by user.");

                var id = await _mgr.StartAsync(command, _workingDir);
                return EToolResult.Success(Name, $"Background process started: {id}\nCommand: {command}\nUse EBackgroundExec with action=output and id={id} to check results.");
            }

            case "status":
            {
                var list = _mgr.List();
                if (list.Count == 0)
                    return EToolResult.Success(Name, "No background processes running.");

                var sb = new StringBuilder();
                sb.AppendLine($"Background processes ({list.Count}):");
                foreach (var p in list)
                    sb.AppendLine($"  {p}");
                return EToolResult.Success(Name, sb.ToString());
            }

            case "output":
            {
                var id = arguments.GetValueOrDefault("id");
                if (string.IsNullOrWhiteSpace(id))
                    return EToolResult.Failure(Name, "Missing 'id' argument for action=output.");

                var status = _mgr.GetStatus(id);
                var output = _mgr.GetOutput(id);
                return EToolResult.Success(Name, $"Process {id} — Status: {status}\n\n{output}");
            }

            case "kill":
            {
                var id = arguments.GetValueOrDefault("id");
                if (string.IsNullOrWhiteSpace(id))
                    return EToolResult.Failure(Name, "Missing 'id' argument for action=kill.");

                var killed = _mgr.Kill(id);
                return killed
                    ? EToolResult.Success(Name, $"Killed process: {id}")
                    : EToolResult.Failure(Name, $"Failed to kill process: {id} (not running or not found)");
            }

            default:
                return EToolResult.Failure(Name, $"Unknown action: '{action}'. Use start, status, output, or kill.");
        }
    }
        /// <summary>v15: small tier gets lifecycle discipline; large tier gets orchestration guidance.</summary>
        public override string GetToolRulesForTier(bool isLargeTier)
        {
            if (isLargeTier)
            {
                return "Rules:\n" +
                       "- Use for long-running work (servers, watchers, big builds); foreground the rest.\n" +
                       "- Track what you started; stop processes you no longer need.\n";
            }
            return "Rules:\n" +
                   "- Start a background job ONCE. NEVER start the same command again while it may still be running.\n" +
                   "- After starting, report ONLY: started/not-started and the job id. Do not guess whether it finished.\n" +
                   "- To check a job, use the status/check action — do not re-run the command.\n";
        }

    /// <summary>v15: kill all tracked background processes (run teardown).</summary>
    public void DisposeProcesses()
    {
        foreach (var id in _mgr.List().Select(p => p.Id))
        {
            try { _mgr.Kill(id); } catch { /* best-effort */ }
        }
    }
}