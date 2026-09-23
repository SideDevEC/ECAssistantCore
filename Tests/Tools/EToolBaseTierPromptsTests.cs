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
