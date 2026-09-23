using ECAssistant.Core.Config;
using ECAssistant.Core.Services;
using ECAssistant.Core.Tools.Background;
using ECAssistant.Core.Tools.Code;
using ECAssistant.Core.Tools.Build;
using ECAssistant.Core.Tools.Git;
using ECAssistant.Core.Tools.Research;
using ECAssistant.Core.Tools.Shell;
using ECAssistant.Core.Tools.Reader;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.19.1: every user-facing tool description carries an explicit scope —
/// "Use ONLY" positive scope and "Do NOT" negative scope. This is what keeps
/// small models (qwen3.5-4b) from calling EFileResearch for a VBA question.
/// Covers the 7 constructible built-ins (EVision/EUserAsk/ESubAgent need
/// engine/session/manager dependencies and are exercised via E2E).
/// </summary>
public sealed class ToolDescriptionScopeTests
{
    private static readonly EAgentConfig Config = new();

    private static IEnumerable<(string Name, string Description)> Descriptions()
    {
        var fs = new FileSystemAdapter();
        var runner = new ProcessRunner();
        yield return ("EShellAgent", new EShellAgent(runner, Config, ".").Description);
        yield return ("EBackgroundExec", new EBackgroundExecTool(null!, runner, fs, Config).Description);
        yield return ("EDotnetBuild", new EDotnetBuildTool(runner, Config).Description);
        yield return ("EGitTool", new EGitTool(runner, fs, Config).Description);
        yield return ("ECodeEditor", new ECodeEditorTool(fs, Config).Description);
        yield return ("EFileReader", new EFileReaderTool(fs, Config).Description);
        yield return ("EFileResearchTool", new EFileResearchTool(fs, Config).Description);
    }

    public static IEnumerable<object[]> Tools() => Descriptions().Select(d => new object[] { d.Name, d.Description });

    [Theory]
    [MemberData(nameof(Tools))]
    public void Description_HasPositiveScope(string name, string description)
    {
        Assert.True(
            description.Contains("Use ONLY", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Use only", StringComparison.Ordinal) ||
            description.Contains("Use ONLY", StringComparison.Ordinal),
            $"{name}: description missing a positive 'Use ONLY' scope clause:\n{description}");
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void Description_HasNegativeScope(string name, string description)
    {
        Assert.True(
            description.Contains("Do NOT", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Do not", StringComparison.Ordinal),
            $"{name}: description missing a negative 'Do NOT' scope clause:\n{description}");
    }
}
