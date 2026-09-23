using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Tests.Tools;

/// <summary>
/// v15: tier-aware tool prompt enforcement (Emre, 2026-09-23) — the abstract base
/// routes tier hooks; the engine renders every registered tool's block for the
/// active tier. Covers the contract + a real tool (EShellAgent) override.
/// </summary>
public sealed class EToolBaseTierPromptsTests
{
    private sealed class TierProbeTool : EToolBase
    {
        public override string Name => "EProbe";
        public override string Description => "probe";
        public override string UsageExample => "example";
        public override Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken ct = default)
            => Task.FromResult(EToolResult.Success(Name, "ok"));
        public override string GetToolRules() => "base-rules";
        public override string GetToolRulesForTier(bool isLargeTier)
            => isLargeTier ? "large-rules" : "small-rules";
        public override string GetToolExampleForTier(bool isLargeTier)
            => isLargeTier ? "large-example" : "small-example";
    }

    [Fact]
    public void ToSystemPromptBlock_SmallTier_UsesSmallHooks()
    {
        var block = new TierProbeTool().ToSystemPromptBlock(isLargeTier: false);
        Assert.Contains("small-rules", block);
        Assert.Contains("small-example", block);
        Assert.DoesNotContain("large-rules", block);
    }

    [Fact]
    public void ToSystemPromptBlock_LargeTier_UsesLargeHooks()
    {
        var block = new TierProbeTool().ToSystemPromptBlock(isLargeTier: true);
        Assert.Contains("large-rules", block);
        Assert.Contains("large-example", block);
        Assert.DoesNotContain("small-rules", block);
    }

    [Fact]
    public void ToSystemPromptBlock_NoArg_DefaultsSmall()
    {
        var block = new TierProbeTool().ToSystemPromptBlock();
        Assert.Contains("small-rules", block);
    }

    private sealed class DefaultProbeTool : EToolBase
    {
        public override string Name => "EDefaultProbe";
        public override string Description => "probe";
        public override string UsageExample => "example";
        public override Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken ct = default)
            => Task.FromResult(EToolResult.Success(Name, "ok"));
        public override string GetToolRules() => "agnostic-rules";
    }

    [Fact]
    public void DefaultHooks_FallBackToTierAgnostic()
    {
        var tool = new DefaultProbeTool();
        Assert.Equal("agnostic-rules", tool.GetToolRulesForTier(isLargeTier: true));
        Assert.Equal("agnostic-rules", tool.GetToolRulesForTier(isLargeTier: false));
    }

    [Fact]
    public void ShellAgent_SmallTier_LiteralDoNotRules()
    {
        var block = new ShellProbe().ToSystemPromptBlock(isLargeTier: false);
        Assert.Contains("ONE command per call", block);
        Assert.DoesNotContain("&& or ; when safe", block);
    }

    [Fact]
    public void ShellAgent_LargeTier_CompositionGuidance()
    {
        var block = new ShellProbe().ToSystemPromptBlock(isLargeTier: true);
        Assert.Contains("&& or ; when safe", block);
    }

    private sealed class ShellProbe : ECAssistant.Core.Tools.Shell.EShellAgent
    {
        public ShellProbe() : base(new ECAssistant.Core.Services.ProcessRunner(),
            new ECAssistant.Core.Config.AppConfig(), "/tmp") { }
    }
}

/// <summary>v15: every shipped tool must have a tier-differentiated rules hook.</summary>
public sealed class AllToolsTierPromptsTests
{
    public static System.Collections.Generic.IEnumerable<object[]> ToolFactories()
    {
        yield return new object[] { "EShellAgent", (Func<EToolBase>)(() => new ECAssistant.Core.Tools.Shell.EShellAgent(new ECAssistant.Core.Services.ProcessRunner(), new ECAssistant.Core.Config.AppConfig(), "/tmp")) };
        yield return new object[] { "ECodeEditor", (Func<EToolBase>)(() => new ECAssistant.Core.Tools.Code.ECodeEditorTool(new ECAssistant.Core.Services.FileSystemAdapter(), new ECAssistant.Core.Config.AppConfig())) };
        yield return new object[] { "EDotnetBuild", (Func<EToolBase>)(() => new ECAssistant.Core.Tools.Build.EDotnetBuildTool(new ECAssistant.Core.Services.ProcessRunner(), new ECAssistant.Core.Config.AppConfig())) };
        yield return new object[] { "EFileReader", (Func<EToolBase>)(() => new ECAssistant.Core.Tools.Reader.EFileReaderTool(new ECAssistant.Core.Services.FileSystemAdapter(), new ECAssistant.Core.Config.AppConfig())) };
        yield return new object[] { "EFileResearchTool", (Func<EToolBase>)(() => new ECAssistant.Core.Tools.Research.EFileResearchTool(new ECAssistant.Core.Services.FileSystemAdapter(), new ECAssistant.Core.Config.AppConfig())) };
        yield return new object[] { "EGitTool", (Func<EToolBase>)(() => new ECAssistant.Core.Tools.Git.EGitTool(new ECAssistant.Core.Services.ProcessRunner(), new ECAssistant.Core.Services.FileSystemAdapter(), new ECAssistant.Core.Config.AppConfig())) };
        yield return new object[] { "EHandoff", (Func<EToolBase>)(() => new ECAssistant.Core.Tools.Handoff.EHandoffTool((req, ct) => System.Threading.Tasks.Task.FromResult(new ECAssistant.Core.Orchestration.OrchestratorResult()))) };
        yield return new object[] { "EAskUser", (Func<EToolBase>)(() => new ECAssistant.Core.Tools.User.EUserAskTool(null)) };
    }

    [Theory]
    [MemberData(nameof(ToolFactories))]
    public void EveryTool_SmallVsLarge_RulesDiffer(string name, Func<EToolBase> make)
    {
        var tool = make();
        Assert.Equal(name, tool.Name);
        var small = tool.GetToolRulesForTier(isLargeTier: false);
        var large = tool.GetToolRulesForTier(isLargeTier: true);
        Assert.False(string.IsNullOrWhiteSpace(small), $"{name}: small-tier rules empty");
        Assert.False(string.IsNullOrWhiteSpace(large), $"{name}: large-tier rules empty");
        Assert.NotEqual(small, large);
    }
}
