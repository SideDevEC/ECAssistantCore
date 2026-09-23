using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.17: tier-aware sub-agent brief — pure logic only (no LLM, no build, no DB).
/// Verifies output contract presence, tier flavoring, and objective passthrough.
/// </summary>
public sealed class SubAgentBriefBuilderTests
{
    [Fact]
    public void Build_IncludesObjective()
    {
        var task = new SubAgentTask { Description = "Analyze project structure" };
        var brief = SubAgentBriefBuilder.Build(task, isLargeTier: false);
        Assert.Contains("Analyze project structure", brief);
    }

    [Fact]
    public void Build_AlwaysIncludesOutputContract()
    {
        var small = SubAgentBriefBuilder.Build(NewTask(), isLargeTier: false);
        var large = SubAgentBriefBuilder.Build(NewTask(), isLargeTier: true);
        Assert.Contains("OUTPUT CONTRACT", small);
        Assert.Contains("OUTPUT CONTRACT", large);
        Assert.Contains("Caveats or blockers", small);
        Assert.Contains("Caveats or blockers", large);
    }

    [Fact]
    public void Build_SmallTier_IncludesGuidance()
    {
        var brief = SubAgentBriefBuilder.Build(NewTask(), isLargeTier: false);
        Assert.Contains("GUIDANCE", brief);
        Assert.Contains("step by step", brief);
    }

    [Fact]
    public void Build_LargeTier_OmitsGuidance()
    {
        var brief = SubAgentBriefBuilder.Build(NewTask(), isLargeTier: true);
        Assert.DoesNotContain("GUIDANCE", brief);
    }

    [Fact]
    public void Build_EmptyDescription_FallsBackToPrompt()
    {
        var task = new SubAgentTask { Description = "", Prompt = "fallback objective" };
        var brief = SubAgentBriefBuilder.Build(task, isLargeTier: true);
        Assert.Contains("fallback objective", brief);
    }

    [Fact]
    public void Build_EmptyEverything_UsesPlaceholder()
    {
        var task = new SubAgentTask();
        var brief = SubAgentBriefBuilder.Build(task, isLargeTier: true);
        Assert.Contains("(no objective given)", brief);
    }

    private static SubAgentTask NewTask() => new() { Description = "Analyze project structure" };
}
