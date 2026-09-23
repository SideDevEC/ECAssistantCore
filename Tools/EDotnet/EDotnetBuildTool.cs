using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using System.Text.Json;

namespace ECAssistant.Core.Tools.Build;

/// <summary>
/// .NET build/test tool.
/// </summary>
public class EDotnetBuildTool : EToolBase
{
    private readonly IProcessRunner _processRunner;
    private readonly JsonElement? _toolConfig;
    private readonly string? _workingDir;
    private readonly BuildErrorParser _errorParser = new();

    public override string Name => "EDotnetBuild";
    public override string Description => "Run dotnet build, test, or restore commands. " +
        "Use ONLY when the user asks to build, test, or restore the project. Do NOT use for " +
        "coding questions or explanations — answer those directly.";
    public override string GetParameterSchema() =>
        """
        {
          "type": "object",
          "properties": {
            "action": { "type": "string", "enum": ["build", "test", "format"], "description": "build (default), test, or format" },
            "projectPath": { "type": "string", "description": "Project or solution path; empty = current project" }
          }
        }
        """;
    public override string UsageExample => "DotnetBuild(action:build)";

    public override bool IsEnabled { get; protected set; } = true;

    // Actions the tool may run — anything else is rejected (the command is built
    // from user/LLM input, so free-form 'action' would be arbitrary code execution).
    private static readonly HashSet<string> AllowedActions = new(StringComparer.Ordinal)
        { "build", "test", "restore", "publish", "clean", "pack" };

    public EDotnetBuildTool(IProcessRunner processRunner, EAgentConfig config)
    {
        _processRunner = processRunner;
        config.Tools.TryGetValue(Name, out var tc);
        _toolConfig = tc.ValueKind == JsonValueKind.Undefined ? null : tc;
        IsEnabled = ReadCfg(_toolConfig, "enabled", true);

        // Use the agent's working directory (AgentSettings.WorkingDirectory), not
        // Environment.CurrentDirectory — builds must run where the project lives.
        var dir = ReadCfg(_toolConfig, "workingDir", (string?)null);
        if (string.IsNullOrWhiteSpace(dir)) dir = config.AgentSettings?.WorkingDirectory;
        _workingDir = string.IsNullOrWhiteSpace(dir) ? null : Path.GetFullPath(dir);
    }

    public override object GetConfigSection() => new { enabled = true };

    public override async Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        arguments ??= new Dictionary<string, string?>();
        var action = arguments.GetValueOrDefault("action")?.Trim().ToLower() ?? "build";
        var projectPath = arguments.GetValueOrDefault("projectPath")?.Trim()
                       ?? arguments.GetValueOrDefault("project")?.Trim()
                       ?? "";

        if (!AllowedActions.Contains(action))
            return EToolResult.Failure(Name,
                $"Invalid action '{action}'. Allowed: {string.Join(", ", AllowedActions.OrderBy(a => a))}");

        if (projectPath.Length > 0)
        {
            // Reject shell metacharacters — projectPath is interpolated into the command.
            if (projectPath.IndexOfAny(new[] { ';', '&', '|', '$', '`', '(', ')', '<', '>', '\n', '\r', '\"', '\'' }) >= 0)
                return EToolResult.Failure(Name, $"Invalid projectPath '{projectPath}': shell metacharacters are not allowed.");
        }

        var command = $"dotnet {action}{(string.IsNullOrEmpty(projectPath) ? "" : $" {projectPath}")}";
        var result = await _processRunner.ExecuteAsync(command, _workingDir, cancellationToken);

        if (result.TimedOut)
            return EToolResult.Failure(Name, "Build exceeded time limit.");

        var allOutput = result.StdOut + "\n" + result.StdErr;
        var errors = _errorParser.ParseErrors(allOutput);
        var warnings = _errorParser.ParseWarnings(allOutput);

        // Exit code 0 with parsed errors is NOT a success — some actions (e.g. test)
        // can exit 0 while the log still reports compile errors.
        if (result.ExitCode == 0 && errors.Count > 0)
            return EToolResult.Failure(Name,
                $"[Build Failed (Exit 0, {errors.Count} error(s) parsed)] {warnings.Count} warning(s).\n{allOutput}");
        else if (result.ExitCode == 0)
            return EToolResult.Success(Name, $"[Build Success] {warnings.Count} warning(s).\n{result.StdOut}");
        else
            return EToolResult.Failure(Name, $"[Build Failed (Exit {result.ExitCode})] {errors.Count} error(s), {warnings.Count} warning(s).\n{allOutput}");
    }

}