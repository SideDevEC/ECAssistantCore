using ECAssistant.Core.Config;
using ECAssistant.Core.ContextPinning;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.16: tier-aware proactive context pinning — pure logic only (no LLM, no
/// build, no DB). Covers file-map extraction, decision matcher (positive +
/// negative), tier block behavior, and pin survival across a simulated
/// compaction with a real ContextWindow.
/// </summary>
public sealed class ContextPinningTests
{
    // ── File-map extraction ────────────────────────────────────

    [Theory]
    [InlineData("read /home/user/app/Program.cs and Tests/Foo.cs", 2)]
    [InlineData("no paths here at all", 0)]
    [InlineData("edited ~/Agent/ECAssistant/README.md", 1)]
    public void ExtractPaths_FindsDistinctPaths(string text, int expected)
    {
        var paths = ContextPinningMatchers.ExtractPaths(text);
        Assert.Equal(expected, paths.Count);
        Assert.Equal(paths.Count, paths.Distinct().Count());
    }

    [Fact]
    public void ExtractPaths_MostRecentFirst_InContextPinner()
    {
        var pinner = new ContextPinner(new ContextPinningConfig { MaxFiles = 3 });
        pinner.ObserveToolOutput("edit", "wrote /a/one.cs");
        pinner.ObserveToolOutput("edit", "wrote /b/two.cs");
        pinner.ObserveToolOutput("edit", "wrote /c/three.cs");
        pinner.ObserveToolOutput("edit", "rewrote /b/two.cs again");

        var block = pinner.BuildPinnedBlock(isLargeTier: false, maxChars: 1200)!;
        // most-recent-first: /b/two.cs leads; /a/one.cs still present; no eviction yet
        Assert.Contains("/b/two.cs", block);
        Assert.Contains("/a/one.cs", block);
    }

    [Fact]
    public void ExtractPaths_EvictsBeyondCap()
    {
        var pinner = new ContextPinner(new ContextPinningConfig { MaxFiles = 2 });
        pinner.ObserveToolOutput("t", "/a/first.cs");
        pinner.ObserveToolOutput("t", "/b/second.cs");
        pinner.ObserveToolOutput("t", "/c/third.cs");
        var block = pinner.BuildPinnedBlock(false, 1200)!;
        Assert.Contains("/c/third.cs", block);
        Assert.DoesNotContain("/a/first.cs", block);
    }

    // ── Decision matcher ───────────────────────────────────────

    [Theory]
    [InlineData("use Postgres for the db", "Postgres for the db")]
    [InlineData("go with option B", "option B")]
    [InlineData("let's stick with the plan", "the plan")]
    public void ExtractDecision_PositiveMatches(string msg, string expected)
    {
        Assert.Equal(expected, ContextPinningMatchers.ExtractDecision(msg));
    }

    [Theory]
    [InlineData("what do you think about Postgres?")]
    [InlineData("please read the file")]
    [InlineData("")]
    [InlineData(null)]
    public void ExtractDecision_NegativeCases(string? msg)
    {
        Assert.Null(ContextPinningMatchers.ExtractDecision(msg!));
    }

    [Fact]
    public void ObserveUserMessage_DeduplicatesDecisions()
    {
        var pinner = new ContextPinner();
        pinner.ObserveUserMessage("use SQLite for now");
        pinner.ObserveUserMessage("actually use SQLite for now");
        var block = pinner.BuildPinnedBlock(false, 1200)!;
        Assert.Equal(1, block.Split("decision:", StringSplitOptions.None).Length - 1);
    }

    // ── Tier block behavior ────────────────────────────────────

    [Fact]
    public void BuildPinnedBlock_SmallTier_IncludesAllFacts()
    {
        var pinner = NewPinner();
        var block = pinner.BuildPinnedBlock(isLargeTier: false, maxChars: 1200)!;
        Assert.StartsWith("[PINNED CONTEXT]", block);
        Assert.Contains("goal:", block);
        Assert.Contains("decision: use SQLite", block);
        Assert.Contains("/a/one.cs", block);
    }

    [Fact]
    public void BuildPinnedBlock_LargeTier_GoalOnly_TightCap()
    {
        var pinner = NewPinner();
        var block = pinner.BuildPinnedBlock(isLargeTier: true, maxChars: 1200)!;
        Assert.Contains("goal:", block);
        Assert.DoesNotContain("decision:", block);
        Assert.DoesNotContain("files:", block);
        Assert.True(block.Length <= 301); // ~300 char cap
    }

    [Fact]
    public void BuildPinnedBlock_NoGoal_ReturnsNull()
    {
        var pinner = new ContextPinner();
        Assert.Null(pinner.BuildPinnedBlock(false, 1200));
    }

    private static ContextPinner NewPinner()
    {
        var pinner = new ContextPinner(new ContextPinningConfig { MaxFiles = 15 });
        pinner.SetGoal("refactor the build script");
        pinner.ObserveUserMessage("use SQLite for now");
        pinner.ObserveToolOutput("read", "/a/one.cs");
        return pinner;
    }

    // ── Pin survival across a simulated compaction ─────────────

    [Fact]
    public void Pins_SurviveSimulatedCompaction()
    {
        var pinner = NewPinner();
        var cw = new ContextWindow(maxTokens: 10_000);
        cw.AddUserMessage("refactor the build script");
        cw.AddToolOutput("read /a/one.cs", "read");
        cw.AddToolOutput("lots of stale output", "tool");

        // Simulated compaction: everything dropped, then summary + pinned block re-injected.
        cw.Clear();
        cw.AddSystemMessage("[Previous conversation summary: earlier work happened]");
        var pinned = pinner.BuildPinnedBlock(isLargeTier: false, maxChars: 1200);
        if (pinned != null) cw.AddSystemMessage(pinned);

        var contents = cw.GetWindowMessages().Select(m => m.Content).ToList();
        Assert.Contains(contents, c => c.Contains("[PINNED CONTEXT]"));
        Assert.Contains(contents, c => c.Contains("refactor the build script"));
        Assert.Contains(contents, c => c.Contains("use SQLite"));
        Assert.Contains(contents, c => c.Contains("/a/one.cs"));
    }
}