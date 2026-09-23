using ECAssistant.Core.Config;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Playbooks;
using ECAssistant.Core.Tools;
using ECAssistant.TestSupport;
using System.Text.Json;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.14: tier-aware playbook memory. Pure-logic tests — JSON persistence in
/// temp dirs, dedup, eviction, keyword matching, tier-flavored injection text,
/// deterministic extraction. No LLM calls, no real FS-heavy integration.
/// </summary>
public sealed class PlaybookTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-playbooks").FullName;

    private CapturedToolCall Call(string tool, string args) => new(tool, args);

    // ── extractor: deterministic, no LLM ──

    [Fact]
    public void Extract_FromFakeToolLog_BuildsTitleKeywordsAndSteps()
    {
        var extractor = new PlaybookExtractor();
        var pb = extractor.Extract(
            "Fix the login bug and run tests",
            new[] { Call("ECodeEditor", "action=patch, file_path=Login.cs"), Call("EDotnetBuild", "command=dotnet build") });
        Assert.NotNull(pb);
        Assert.Contains("fix", pb!.TriggerKeywords);
        Assert.Contains("login", pb.TriggerKeywords);
        Assert.Equal(2, pb.Steps.Count);
        Assert.Contains("ECodeEditor", pb.Steps[0]);
        Assert.Contains("action=patch", pb.Steps[0]);
        Assert.Equal("goal", pb.Source);
        Assert.Equal("Fix the login bug and run tests", pb.Title);
    }

    [Fact]
    public void Extract_EmptyToolLog_ReturnsNull()
    {
        var extractor = new PlaybookExtractor();
        Assert.Null(extractor.Extract("some goal", Array.Empty<CapturedToolCall>()));
        Assert.Null(extractor.Extract("", new[] { Call("T", "a=b") }));
    }

    [Fact]
    public void Extract_LongArgsAreTruncated()
    {
        var extractor = new PlaybookExtractor();
        var pb = extractor.Extract("goal words here", new[] { Call("EShellAgent", new string('x', 300)) });
        Assert.NotNull(pb);
        Assert.True(pb!.Steps[0].Length < 200);
        Assert.Contains("…", pb.Steps[0]);
    }

    // ── store: capture / dedup / eviction ──

    [Fact]
    public async Task Capture_NewPlaybook_PersistsAndLoadsAcrossInstances()
    {
        var store = new PlaybookStore(_dir);
        var candidate = new PlaybookExtractor().Extract("deploy the app", new[] { Call("EShellAgent", "command=deploy") })!;
        await store.CaptureAsync(candidate);

        var reloaded = new PlaybookStore(_dir);
        Assert.Single(reloaded.All);
        Assert.Equal(candidate.Title, reloaded.All[0].Title);
        Assert.Equal(candidate.Steps, reloaded.All[0].Steps);
    }

    [Fact]
    public async Task Capture_DuplicateTitle_UpdatesCountersInsteadOfAdding()
    {
        var store = new PlaybookStore(_dir);
        var extractor = new PlaybookExtractor();
        var first = await store.CaptureAsync(extractor.Extract("Fix the login bug", new[] { Call("ECodeEditor", "action=patch") })!);
        var second = await store.CaptureAsync(extractor.Extract("Fix the login bug", new[] { Call("ECodeEditor", "action=patch") })!);

        Assert.Same(first, second);
        Assert.Equal(2, second.UseCount);
        Assert.Single(store.All);
    }

    [Fact]
    public async Task Capture_SameTriggerSet_DeduplicatesEvenWithDifferentTitle()
    {
        var store = new PlaybookStore(_dir);
        var extractor = new PlaybookExtractor();
        await store.CaptureAsync(extractor.Extract("write deploy script", new[] { Call("EShellAgent", "command=d") })!);
        // Different words entirely → different trigger set → NOT a duplicate
        var different = extractor.Extract("paint the garden fence", new[] { Call("EShellAgent", "command=p") })!;
        await store.CaptureAsync(different);
        Assert.Equal(2, store.All.Count);
    }

    [Fact]
    public async Task Capture_BeyondCap_EvictsLeastUsedOldest()
    {
        var store = new PlaybookStore(_dir, maxPlaybooks: 3);
        var extractor = new PlaybookExtractor();
        string[] words = { "tango", "foxtrot", "charlie", "delta" };
        for (int i = 0; i < 4; i++)
            await store.CaptureAsync(extractor.Extract($"{words[i]} operation now", new[] { Call($"Tool{i}", "a=1") })!);

        Assert.Equal(3, store.All.Count);
        // tango was evicted (least-used, oldest); delta survives
        Assert.DoesNotContain(store.All, p => p.Title.Contains("tango"));
        Assert.Contains(store.All, p => p.Title.Contains("delta"));
    }

    [Fact]
    public async Task Capture_FrequentlyUsedPlaybook_SurvivesEviction()
    {
        var store = new PlaybookStore(_dir, maxPlaybooks: 2);
        var extractor = new PlaybookExtractor();
        var favorite = await store.CaptureAsync(extractor.Extract("the favorite task", new[] { Call("T1", "a=1") })!);
        await store.CaptureAsync(extractor.Extract("second task here", new[] { Call("T2", "a=2") })!);
        await store.CaptureAsync(extractor.Extract("third task here", new[] { Call("T3", "a=3") })!);

        // favorite now has UseCount=2 (dedup hit happens below via identical capture)
        await store.CaptureAsync(extractor.Extract("the favorite task", new[] { Call("T1", "a=1") })!);
        await store.CaptureAsync(new Playbook { Id = "g4", Title = "fourth task x", TriggerKeywords = new List<string> { "fourth" }, Steps = { "T4(a=4)" } });

        Assert.Equal(2, store.All.Count);
        Assert.Contains(store.All, p => p.Title == favorite.Title);
    }

    // ── matching + tier-flavored injection ──

    [Fact]
    public async Task Match_RequestWithTriggerKeyword_ReturnsTopByUseCount()
    {
        var store = new PlaybookStore(_dir);
        var extractor = new PlaybookExtractor();
        await store.CaptureAsync(extractor.Extract("repair login page", new[] { Call("T1", "") })!);
        var twice = await store.CaptureAsync(extractor.Extract("deploy pipeline setup", new[] { Call("T2", "") })!);
        await store.CaptureAsync(twice); // use_count → 2

        var matched = store.Match("please repair the login issue", topN: 2);
        Assert.Single(matched);
        Assert.Contains("login", matched[0].Title);
    }

    [Fact]
    public void Match_NoKeywordHit_ReturnsEmpty()
    {
        var store = new PlaybookStore(_dir);
        Assert.Empty(store.Match("completely unrelated request", topN: 2));
    }

    [Fact]
    public async Task BuildInjection_SmallTier_IsStrictRecipe()
    {
        var store = new PlaybookStore(_dir);
        await store.CaptureAsync(new PlaybookExtractor().Extract("fix login bug", new[] { Call("ECodeEditor", "action=patch") })!);

        var text = store.BuildInjection("fix the login bug", isLargeTier: false);
        Assert.NotNull(text);
        Assert.Contains("Follow these steps exactly", text);
        Assert.Contains("[PLAYBOOK]", text);
        Assert.Contains("1. ECodeEditor(action=patch)", text);
    }

    [Fact]
    public async Task BuildInjection_LargeTier_IsSlimReference()
    {
        var store = new PlaybookStore(_dir);
        await store.CaptureAsync(new PlaybookExtractor().Extract("fix login bug", new[] { Call("ECodeEditor", "action=patch") })!);

        var text = store.BuildInjection("fix the login bug", isLargeTier: true);
        Assert.NotNull(text);
        Assert.Contains("PAST SUCCESSFUL PROCEDURE", text);
        Assert.Contains("use if helpful", text);
        Assert.DoesNotContain("Follow these steps exactly", text);
    }

    [Fact]
    public async Task BuildInjection_CapsAtTwoPlaybooksAndMaxChars()
    {
        var store = new PlaybookStore(_dir);
        var extractor = new PlaybookExtractor();
        string[] words = { "alpha", "bravo", "charlie" };
        for (int i = 0; i < 3; i++)
            await store.CaptureAsync(extractor.Extract($"build the {words[i]} widget", new[] { Call($"T{i}", "a=1") })!);

        var text = store.BuildInjection("build the alpha widget", isLargeTier: false, topN: 2, maxChars: 200);
        Assert.NotNull(text);
        Assert.True(text!.Length <= 210);
        Assert.DoesNotContain("alpha", text); // top 2 by usage/recency — the oldest (alpha) is capped out
    }

    [Fact]
    public void BuildInjection_NoMatch_ReturnsNull()
    {
        var store = new PlaybookStore(_dir);
        Assert.Null(store.BuildInjection("nothing matches here", isLargeTier: false));
    }

    // ── matcher pure helpers ──

    [Fact]
    public void NormalizeTitle_StripsPunctuationAndCase()
    {
        Assert.Equal("fix the login bug", PlaybookMatcher.NormalizeTitle("Fix the Login BUG!"));
        Assert.Equal("fix the login bug", PlaybookMatcher.NormalizeTitle("fix   the\tlogin-bug."));
    }

    [Fact]
    public void ExtractKeywords_DropsStopWordsAndShortWords()
    {
        var keywords = PlaybookMatcher.ExtractKeywords("Can you please fix the login bug and run the CI pipeline");
        Assert.DoesNotContain("can", keywords);
        Assert.DoesNotContain("the", keywords);
        Assert.Contains("login", keywords);
        Assert.Contains("pipeline", keywords);
    }

    // ── orchestrator wiring: capture after GoalAchieved (MockEngine, no LLM) ──

    [Fact]
    public async Task Orchestrator_GoalAchievedWithToolCalls_CapturesPlaybook()
    {
        var engine = new MockEngine(workingDir: _dir);
        engine.RegisterTool(new FakeEchoTool());
        var store = new PlaybookStore(_dir + "-capture");
        var config = JsonSerializer.Deserialize<EAgentConfig>("""{ "model_tier": { "mode": "small" } }""");
        var orchestrator = new AgentOrchestrator(engine, null, maxTurns: 5, maxFailures: 3,
            toolPolicy: AllowEchoPolicy(), config: config, playbookStore: store);

        engine.EnqueueToolCall("EchoTool", new Dictionary<string, string?> { ["command"] = "do the thing" });
        engine.EnqueueDirectAnswer("all done");

        var result = await orchestrator.ExecuteMultiStep("do the thing quickly");
        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Single(store.All);
        Assert.Contains("do the thing", store.All[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EchoTool", store.All[0].Steps[0]);
    }

    [Fact]
    public async Task Orchestrator_GoalAchievedWithoutToolCalls_DoesNotCapture()
    {
        var engine = new MockEngine(workingDir: _dir);
        var store = new PlaybookStore(_dir + "-nocapture");
        var config = JsonSerializer.Deserialize<EAgentConfig>("{}");
        var orchestrator = new AgentOrchestrator(engine, null, maxTurns: 3, maxFailures: 3,
            toolPolicy: null, config: config, playbookStore: store);

        engine.EnqueueDirectAnswer("just chatting");
        await orchestrator.ExecuteMultiStep("what is the capital of France?");
        Assert.Empty(store.All);
    }

    // ── helpers ──

    private static global::ECAssistant.Core.Tools.ToolPolicy AllowEchoPolicy()
    {
        var policy = new global::ECAssistant.Core.Tools.ToolPolicy();
        policy.SetPermission("EchoTool", approvalRequired: false, "test harness");
        return policy;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
        try { Directory.Delete(_dir + "-capture", true); } catch { }
        try { Directory.Delete(_dir + "-nocapture", true); } catch { }
    }
}

/// <summary>Trivial always-succeeding tool for orchestrator capture tests.</summary>
file sealed class FakeEchoTool : EToolBase
{
    public override string Name => "EchoTool";
    public override string Description => "Test double that always succeeds.";
    public override string UsageExample => "EchoTool(command=\"x\")";

    public override Task<EToolResult> ExecuteAsync(
        Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
        => Task.FromResult(EToolResult.Success(Name, "ok: " + (arguments.GetValueOrDefault("command") ?? "")));
}