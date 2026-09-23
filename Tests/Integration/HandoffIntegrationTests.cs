using ECAssistant.Core.Config;
using ECAssistant.Core;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Services;
using ECAssistant.TestSupport;
using ECAssistant.Core.Tools;
using ECAssistant.Core.UI;
using ToolPolicy = ECAssistant.Core.Tools.ToolPolicy;

namespace ECAssistant.Core.Tests.Integration;

/// <summary>
/// Integration tests for the v15 ephemeral handoff: EHandoff tool registration,
/// orchestrator interception (specialist result becomes the final answer, parent
/// loop stops), and the no-executor failure path.
///
/// Executor internals (real child engine + HTTP) are exercised in E2E
/// (HandoffE2E, env-gated live server) — these tests pin the WIRING only.
/// </summary>
[Collection("ProgramGuiCollection")]
public class HandoffIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GuiTestHarness _gui;
    private readonly List<MockEngine> _engines = new();
    private readonly List<AgentOrchestrator> _orchestrators = new();

    public HandoffIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ECAInteg_Handoff_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _gui = new GuiTestHarness();
        TestRunner.TestGui = _gui;
    }

    public void Dispose()
    {
        foreach (var orch in _orchestrators)
            try { orch.DisposeAsync().AsTask().Wait(1000); } catch { }
        foreach (var eng in _engines)
            try { eng.DisposeAsync().AsTask().Wait(1000); } catch { }
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, true); } catch { }
    }

    private (MockEngine engine, AgentOrchestrator orchestrator) CreateEngineWithOrchestrator(int maxTurns = 5)
    {
        var logger = new Mock<ILogger>();
        var engine = new MockEngine(_tempDir);
        _engines.Add(engine);
        var policy = new ECAssistant.Core.Tools.ToolPolicy();
        var orchestrator = new AgentOrchestrator(engine, sessionOutput: new TestSessionOutput(_gui), maxTurns: maxTurns, maxFailures: 3, toolPolicy: policy, logger: logger.Object);
        _orchestrators.Add(orchestrator);
        return (engine, orchestrator);
    }

    // ── Registration wiring ──

    [Fact]
    public void InitializeHandoff_RegistersEHandoffTool()
    {
        var (engine, orchestrator) = CreateEngineWithOrchestrator();

        Assert.DoesNotContain(engine.Tools, t => t.Name == "EHandoff");

        orchestrator.InitializeHandoff(_tempDir);

        Assert.Contains(engine.Tools, t => t.Name == "EHandoff");
    }

    [Fact]
    public void InitializeHandoff_PolicyAllowsEHandoff()
    {
        var (engine, orchestrator) = CreateEngineWithOrchestrator();

        orchestrator.InitializeHandoff(_tempDir);

        Assert.True(orchestrator.Policy.IsAllowed("EHandoff"));
    }

    [Fact]
    public void InitializeHandoff_ToolHasPromptSchema_ForGrammarUnion()
    {
        var (engine, orchestrator) = CreateEngineWithOrchestrator();
        orchestrator.InitializeHandoff(_tempDir);

        var tool = engine.Tools.Single(t => t.Name == "EHandoff");
        var schema = tool.GetParameterSchema();

        // The structured/grammar path needs a REAL parameter schema, not a stub.
        Assert.Contains("\"required\"", schema);
        Assert.Contains("prompt", schema);
        Assert.NotEmpty(tool.GetToolRules());
    }

    [Fact]
    public async Task InitializeHandoffAsync_MockEngine_CompletesCacheRebuildNoOp()
    {
        var (engine, orchestrator) = CreateEngineWithOrchestrator();

        // MockEngine overrides ResetAndRebuildCacheAsync as a no-op — the
        // async init path must complete without touching real HTTP.
        await orchestrator.InitializeHandoffAsync(_tempDir);

        Assert.Contains(engine.Tools, t => t.Name == "EHandoff");
    }

    // ── Interception: no executor initialized ──

    [Fact]
    public async Task EHandoffCall_WithoutInitialize_FailsGracefully_ParentStops()
    {
        var (engine, orchestrator) = CreateEngineWithOrchestrator(maxTurns: 5);
        orchestrator.Policy.SetPermission("EHandoff", approvalRequired: false, "Test");

        engine.EnqueueToolCall("EHandoff", new Dictionary<string, string?> { ["prompt"] = "You are a specialist." });

        var result = await orchestrator.ExecuteMultiStep("Hand off to a specialist");

        // Interception runs, finds no executor, and returns a FAILED final
        // result — the parent loop stops rather than feeding an error back
        // for more turns.
        Assert.Equal(OrchestratorStatus.Failed, result.Status);
        Assert.Contains("Executor not initialized", result.FinalOutput);
    }

    // ── Interception bypasses the normal tool execution path ──

    [Fact]
    public async Task EHandoffCall_IsInterceptedBeforeNormalToolExecution()
    {
        // With InitializeHandoff against MockEngine, the executor builds a REAL
        // child engine whose endpoint is MockEngine's InferenceEngineNoop
        // ("mock") — the child orchestrator fails on the first inference call.
        // Key behavioral pin: the parent does NOT fall through to normal tool
        // execution — the EHandoff tool's ExecuteAsync is never reached
        // (no "[Handoff] execution failed" double-handling), and the parent
        // returns the child's result as its own.
        var (engine, orchestrator) = CreateEngineWithOrchestrator(maxTurns: 5);
        orchestrator.InitializeHandoff(_tempDir);

        engine.EnqueueToolCall("EHandoff", new Dictionary<string, string?>
        {
            ["prompt"] = "You are a specialist.",
            ["name"] = "test-specialist",
        });

        var result = await orchestrator.ExecuteMultiStep("Hand off");

        // Either the child failed on the mock endpoint (Failed) — but the
        // parent MUST NOT continue looping afterwards. A Failed result that
        // came from the child (not "Executor not initialized") proves the
        // executor ran and its result was adopted.
        Assert.NotEqual(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.DoesNotContain("Executor not initialized", result.FinalOutput);
    }

    // ── Non-handoff path unaffected (regression) ──

    [Fact]
    public async Task RegularToolCalls_UnaffectedByHandoffInitialization()
    {
        var (engine, orchestrator) = CreateEngineWithOrchestrator(maxTurns: 5);
        orchestrator.InitializeHandoff(_tempDir);

        // EHandoff registered but NOT called — normal flow still works.
        // Use a registered built-in-style mock tool name that exists on MockEngine?
        // MockEngine registers no built-ins; drive via a direct answer instead.
        engine.EnqueueDirectAnswer("plain answer, no delegation needed");

        var result = await orchestrator.ExecuteMultiStep("What is 2+2?");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Equal("plain answer, no delegation needed", result.FinalOutput);
    }

    [Fact]
    public async Task OtherToolCalls_StillExecuteNormally_AlongsideHandoffRegistration()
    {
        var (engine, orchestrator) = CreateEngineWithOrchestrator(maxTurns: 5);
        orchestrator.InitializeHandoff(_tempDir);
        engine.RegisterTool(new MockProbeTool());
        orchestrator.Policy.SetPermission("EProbe", approvalRequired: false, "Test");

        engine.EnqueueToolCall("EProbe", new Dictionary<string, string?> { ["action"] = "probe" });
        engine.EnqueueDirectAnswer("probe done");

        var result = await orchestrator.ExecuteMultiStep("Run the probe");

        // Normal tools go through the normal path even though EHandoff is
        // registered and the interception code exists in the loop.
        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Contains(engine.ToolResults, t => t.toolName == "EProbe");
        Assert.Equal("probe done", result.FinalOutput);
    }

    // ── Case-insensitivity of the interception key ──

    [Fact]
    public async Task EHandoffCall_CaseInsensitiveToolName_IsIntercepted()
    {
        var (engine, orchestrator) = CreateEngineWithOrchestrator(maxTurns: 5);

        engine.EnqueueToolCall("ehandoff", new Dictionary<string, string?> { ["prompt"] = "p" });

        var result = await orchestrator.ExecuteMultiStep("hand off");

        // Interception matches OrdinalIgnoreCase — hits the no-executor guard.
        Assert.Equal(OrchestratorStatus.Failed, result.Status);
        Assert.Contains("Executor not initialized", result.FinalOutput);
    }

    // ── Dispose safety ──

    [Fact]
    public async Task DisposeAsync_AfterInitializeHandoff_DoesNotThrow()
    {
        var (engine, orchestrator) = CreateEngineWithOrchestrator();
        orchestrator.InitializeHandoff(_tempDir);

        await orchestrator.DisposeAsync(); // must dispose executor cleanly
    }
}

/// <summary>Minimal mock tool used to verify the non-handoff path still works.</summary>
public class MockProbeTool : ECAssistant.Core.Tools.ToolBase
{
    public override string Name => "EProbe";
    public override string Description => "Mock probe tool";
    public override string UsageExample => "EProbe(action=\"probe\")";

    public override Task<ECAssistant.Core.Tools.ToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
        => Task.FromResult(new ECAssistant.Core.Tools.ToolResult
        {
            ToolName = Name,
            Succeeded = true,
            Output = $"Probe ok: {arguments.GetValueOrDefault("action")}"
        });
}