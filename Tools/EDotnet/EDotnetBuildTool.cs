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
    private readonly BuildErrorParser _errorParser = new();

    public override string Name => "DotnetBuild";
    public override string Description => "Run dotnet build, test, or restore commands.";
    public override string UsageExample => "<toolcall>DotnetBuild<action>build</action></toolcall>";

    public override bool IsEnabled { get; protected set; } = true;

    public EDotnetBuildTool(IProcessRunner processRunner, EAgentConfig config)
    {
        _processRunner = processRunner;
        config.Tools.TryGetValue(Name, out var tc);
        _toolConfig = tc.ValueKind == JsonValueKind.Undefined ? null : tc;
        IsEnabled = ReadCfg(_toolConfig, "enabled", true);
    }

    public override object GetConfigSection() => new { enabled = true };

    public override async Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        arguments ??= new Dictionary<string, string?>();
        var action = arguments.GetValueOrDefault("action")?.Trim().ToLower() ?? "build";
        var projectPath = arguments.GetValueOrDefault("projectPath")?.Trim()
                       ?? arguments.GetValueOrDefault("project")?.Trim()
                       ?? "";

        var command = $"dotnet {action}{(string.IsNullOrEmpty(projectPath) ? "" : $" {projectPath}")}";
        var result = await _processRunner.ExecuteAsync(command, null, cancellationToken);

        if (result.TimedOut)
            return EToolResult.Failure(Name, "Build exceeded time limit.");

        var allOutput = result.StdOut + "\n" + result.StdErr;
        var errors = _errorParser.ParseErrors(allOutput);
        var warnings = _errorParser.ParseWarnings(allOutput);

        if (result.ExitCode == 0 && errors.Count == 0)
            return EToolResult.Success(Name, $"[Build Success] {warnings.Count} warning(s).\n{result.StdOut}");
        else if (result.ExitCode != 0)
            return EToolResult.Failure(Name, $"[Build Failed (Exit {result.ExitCode})] {errors.Count} error(s), {warnings.Count} warning(s).\n{allOutput}");
        else
            return EToolResult.Success(Name, $"[Build Success with warnings] {warnings.Count} warning(s).\n{result.StdOut}");
    }

}