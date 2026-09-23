using ECAssistant.Core.Config;
using ECAssistant.Core;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Services;
using ECAssistant.TestSupport;
using ECAssistant.Core.Tools;
using ECAssistant.Core.Tools.Shell;
using ECAssistant.Core.Tools.Build;
using ECAssistant.Core.UI;
using Moq;

namespace ECAssistant.Core.Tests.Integration;

/// <summary>
/// v14.20 integration: typed per-tool outputs (RenderForModel) and dataflow
/// {{N}} chains through the REAL Orchestrator → Engine → ParallelToolExecutor
/// → Tools pipeline. MockEngine scripts the LLM; process/filesystem mocked.
/// Journey: a build failure must reach the model as a compact render, and a
/// {{0}} chain must substitute the prior call's output into the later call's
/// real tool arguments.
/// </summary>
[Collection("ProgramGuiCollection")]
public class TypedOutputAndChainIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GuiTestHarness _gui;
    private readonly List<MockEngine> _engines = new();
    private readonly List<AgentOrchestrator> _orchestrators = new();

    private const string FailedBuildOutput = """
        /repo/src/App/Program.cs(12,34): error CS1061: 'Order' does not contain a definition for 'Total'
        /repo/src/App/Program.cs(20,5): error CS0103: The name 'customer' does not exist
        Build FAILED.
        """;

    public TypedOutputAndChainIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ECAInteg_Typed_" + Guid.NewGuid().ToString("N")[..8]);
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

    private MockEngine NewEngine()
    {
        var engine = new MockEngine(_tempDir);
        _engines.Add(engine);
        return engine;
    }

    /// <summary>What the MODEL actually sees in context (post-render, post-truncate).</summary>
    private string ModelVisibleToolOutput(MockEngine engine, string toolName)
        => string.Join("\n", engine.ContextWindow.GetWindowMessages()
            .Where(m => m.Role == "tool_output" && m.Source.Equals(toolName, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Content));

    private AgentOrchestrator NewOrchestrator(MockEngine engine, Mock<ILogger> logger)
    {
        var orchestrator = new AgentOrchestrator(
            engine,
            sessionOutput: new TestSessionOutput(_gui),
            maxTurns: 10, maxFailures: 3,
            toolPolicy: new ToolPolicy(),
            logger: logger.Object);
        _orchestrators.Add(orchestrator);
        return orchestrator;
    }

    // ── Journey 1: build failure reaches the model as a compact render ──

    [Fact]
    public async Task BuildFailure_ModelSeesCompactRender_NotRawLog()
    {
        var logger = new Mock<ILogger>();
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(1, FailedBuildOutput, "", false));

        var config = new AppConfig();
        config.AgentSettings.WorkingDirectory = _tempDir;
        var engine = NewEngine();
        engine.RegisterTool(new EDotnetBuildTool(processRunner.Object, config));

        engine.EnqueueToolCall("EDotnetBuild", new() { ["action"] = "build" });
        engine.EnqueueDirectAnswer("Build failed with CS1061 in Program.cs.");

        var result = await NewOrchestrator(engine, logger).ExecuteMultiStep("fix the build");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        var visible = ModelVisibleToolOutput(engine, "EDotnetBuild");
        // Model got the semantic render (failure path renders too)...
        Assert.Contains("[BUILD FAILED]", visible);
        Assert.Contains("CS1061", visible);
        Assert.Contains("2 error(s)", visible);
        // ...not the raw log
        Assert.DoesNotContain("/repo/src/App/Program.cs", visible);
    }

    [Fact]
    public async Task BuildSuccess_ModelSeesOkVerdict()
    {
        var logger = new Mock<ILogger>();
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)", "", false));

        var config = new AppConfig();
        config.AgentSettings.WorkingDirectory = _tempDir;
        var engine = NewEngine();
        engine.RegisterTool(new EDotnetBuildTool(processRunner.Object, config));

        engine.EnqueueToolCall("EDotnetBuild", new() { ["action"] = "build" });
        engine.EnqueueDirectAnswer("Build is green.");

        await NewOrchestrator(engine, logger).ExecuteMultiStep("build the project");

        var visible = ModelVisibleToolOutput(engine, "EDotnetBuild");
        Assert.Contains("[BUILD OK]", visible);
        Assert.DoesNotContain("Build succeeded.", visible); // raw MSBuild tail replaced
    }

    // ── Journey 2: {{0}} dataflow chain through the orchestrator batch path ──

    [Fact]
    public async Task DataflowChain_PriorOutputSubstitutedIntoLaterRealToolArgs()
    {
        var logger = new Mock<ILogger>();
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "echoed", "", false));

        var config = new AppConfig();
        config.AgentSettings.WorkingDirectory = _tempDir;
        var engine = NewEngine();
        engine.RegisterTool(new ProbeTestTool());
        engine.RegisterTool(new EShellAgent(processRunner.Object, config, _tempDir));

        // Call 0 probes; call 1 shells the probe output through {{0}}.
        engine.EnqueueMultiToolCall(
            ("ProbeTool", new Dictionary<string, string?> { ["action"] = "probe" }),
            ("EShellAgent", new Dictionary<string, string?> { ["command"] = "echo {{0}}" }));
        engine.EnqueueDirectAnswer("Chained.");

        await NewOrchestrator(engine, logger).ExecuteMultiStep("probe and echo");

        // The REAL tool received the substituted argument, in call order.
        processRunner.Verify(
            p => p.ExecuteAsync("echo PROBE OK: all systems nominal", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DataflowChain_FailedFirstCall_LaterToolSeesUnavailableMarker()
    {
        var logger = new Mock<ILogger>();
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(1, "", "", false)); // every shell call fails

        var config = new AppConfig();
        config.AgentSettings.WorkingDirectory = _tempDir;
        var engine = NewEngine();
        engine.RegisterTool(new EShellAgent(processRunner.Object, config, _tempDir));

        engine.EnqueueMultiToolCall(
            ("EShellAgent", new Dictionary<string, string?> { ["command"] = "failing-step" }),
            ("EShellAgent", new Dictionary<string, string?> { ["command"] = "echo {{0}}" }));
        engine.EnqueueDirectAnswer("First step failed.");

        await NewOrchestrator(engine, logger).ExecuteMultiStep("run the chain");

        processRunner.Verify(
            p => p.ExecuteAsync(
                It.Is<string>(cmd => cmd.StartsWith("echo [reference {{0}} unavailable")),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BatchWithBuildCall_CombinedOutputCarriesPerToolRender()
    {
        var logger = new Mock<ILogger>();
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(1, FailedBuildOutput, "", false));

        var config = new AppConfig();
        config.AgentSettings.WorkingDirectory = _tempDir;
        var engine = NewEngine();
        engine.RegisterTool(new EDotnetBuildTool(processRunner.Object, config));
        engine.RegisterTool(new ProbeTestTool());

        // Independent calls → parallel batch; combined 'Batch' output must carry
        // the per-tool render (previously raw under a name matching no tool).
        engine.EnqueueMultiToolCall(
            ("EDotnetBuild", new Dictionary<string, string?> { ["action"] = "build" }),
            ("ProbeTool", new Dictionary<string, string?> { ["action"] = "probe" }));
        engine.EnqueueDirectAnswer("Done.");

        await NewOrchestrator(engine, logger).ExecuteMultiStep("build and probe");

        var batch = ModelVisibleToolOutput(engine, "Batch");
        Assert.Contains("[BUILD FAILED]", batch); // failed build call renders compact, not raw
        Assert.Contains("PROBE OK", batch);
        Assert.DoesNotContain("/repo/src/App/Program.cs", batch);
    }
}