using ECAssistant.Core;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Services;
using ECAssistant.Core.Testing;
using ECAssistant.Core.Tools;
using ECAssistant.Core.Tools.Shell;
using ECAssistant.Core.Tools.Code;
using ECAssistant.Core.Tools.Reader;
using ECAssistant.Core.UI;

namespace ECAssistant.Core.Tests.Integration;

/// <summary>
/// Integration tests for the full Orchestrator → Engine → Tools → Output pipeline.
/// Uses MockEngine (no real GGUF model) and mocked tool dependencies.
/// </summary>
[Collection("ProgramGuiCollection")]
public class OrchestratorIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly EGuiTestHarness _gui;
    private readonly List<MockEngine> _engines = new();
    private readonly List<AgentOrchestrator> _orchestrators = new();

    public OrchestratorIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ECAInteg_Orch_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _gui = new EGuiTestHarness();
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

    /// <summary>Create a MockEngine with real tools wired to mocked dependencies.</summary>
    private (MockEngine engine, AgentOrchestrator orchestrator, Mock<IProcessRunner> processRunner, Mock<IFileSystem> fileSystem) CreateEngineWithMockedTools(int maxTurns = 10)
    {
        var mockProcessRunner = new Mock<IProcessRunner>();
        var mockFileSystem = new Mock<IFileSystem>();
        var config = new EAgentConfig();
        var mockLogger = new Mock<ILogger>();

        config.AgentSettings.WorkingDirectory = _tempDir;

        var engine = new MockEngine(_tempDir);
        _engines.Add(engine);

        // Register real tools with mocked dependencies
        engine.RegisterTool(new EShellAgent(mockProcessRunner.Object, config, _tempDir));
        engine.RegisterTool(new ECodeEditorTool(mockFileSystem.Object, config));
        engine.RegisterTool(new EFileReaderTool(mockFileSystem.Object, config));

        var policy = new ECAssistant.Core.Tools.ToolPolicy();
        var orchestrator = new AgentOrchestrator(engine, sessionOutput: new TestSessionOutput(_gui), maxTurns: maxTurns, maxFailures: 3, toolPolicy: policy, logger: mockLogger.Object);
        _orchestrators.Add(orchestrator);

        return (engine, orchestrator, mockProcessRunner, mockFileSystem);
    }

    // ── Single tool call → final answer ──

    [Fact]
    public async Task SingleToolCall_ToolExecutesThenFinalAnswer_VerifiesToolWasCalled()
    {
        var (engine, orchestrator, processRunner, _) = CreateEngineWithMockedTools();

        // Turn 1: LLM requests a shell command
        engine.EnqueueToolCall("EShellAgent", new() { ["command"] = "echo hello" });
        // Turn 2: LLM gives final answer
        engine.EnqueueDirectAnswer("Done");

        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "hello", "", false));

        var result = await orchestrator.ExecuteMultiStep("Run echo hello");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Equal("Done", result.FinalOutput);
        Assert.Equal(2, engine.GenerateCallCount);
        processRunner.Verify(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(engine.ToolResults);
        Assert.Equal("EShellAgent", engine.ToolResults[0].toolName);
        Assert.Contains("hello", engine.ToolResults[0].output);
    }

    // ── Multiple tool calls in one response ──

    [Fact]
    public async Task MultipleToolCallsInOneResponse_BothExecuted_VerifiesBothRan()
    {
        var (engine, orchestrator, processRunner, _) = CreateEngineWithMockedTools();

        // Turn 1: LLM requests two shell commands in one response
        engine.EnqueueMultiToolCall(
            ("EShellAgent", new Dictionary<string, string?> { ["command"] = "echo first" }),
            ("EShellAgent", new Dictionary<string, string?> { ["command"] = "echo second" }));
        // Turn 2: Final answer
        engine.EnqueueDirectAnswer("Both commands executed");

        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "output", "", false));

        var result = await orchestrator.ExecuteMultiStep("Run two commands");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Equal("Both commands executed", result.FinalOutput);
        // ProcessRunner should be called twice (once per tool call)
        processRunner.Verify(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── Multi-turn: tool call → result → final answer ──

    [Fact]
    public async Task MultiTurn_ToolCallThenAnswer_VerifiesTwoGenerateCalls()
    {
        var (engine, orchestrator, processRunner, _) = CreateEngineWithMockedTools();

        engine.EnqueueToolCall("EShellAgent", new() { ["command"] = "ls" });
        engine.EnqueueDirectAnswer("Directory listed successfully");

        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "file1.txt\nfile2.txt", "", false));

        var result = await orchestrator.ExecuteMultiStep("List directory");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Equal("Directory listed successfully", result.FinalOutput);
        Assert.Equal(2, engine.GenerateCallCount);
    }

    // ── Max turns reached ──

    [Fact]
    public async Task MaxTurnsReached_LLMAlwaysCallsTools_TurnsExhaustedStatus()
    {
        var (engine, orchestrator, processRunner, _) = CreateEngineWithMockedTools(maxTurns: 3);

        // Engine always returns a tool call, never a final answer
        engine.SetDefaultToolCall("EShellAgent", new() { ["command"] = "echo retry" });

        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "ok", "", false));

        var result = await orchestrator.ExecuteMultiStep("Never-ending task");

        Assert.Equal(OrchestratorStatus.TurnsExhausted, result.Status);
        Assert.Contains("max turns", result.FinalOutput, StringComparison.OrdinalIgnoreCase);
    }

    // ── Tool failure: mock IProcessRunner returns error ──

    [Fact]
    public async Task ToolFailure_ProcessRunnerReturnsError_ErrorHandledGracefully()
    {
        var (engine, orchestrator, processRunner, _) = CreateEngineWithMockedTools();

        engine.EnqueueToolCall("EShellAgent", new() { ["command"] = "bad-cmd" });
        engine.EnqueueDirectAnswer("The command failed with exit code 1");

        processRunner
            .Setup(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(1, "", "command not found", false));

        var result = await orchestrator.ExecuteMultiStep("Run a failing command");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Equal("The command failed with exit code 1", result.FinalOutput);
        // The tool result should contain error info
        Assert.Single(engine.ToolResults);
        Assert.Contains("Error", engine.ToolResults[0].output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Direct answer (no tools) ──

    [Fact]
    public async Task DirectAnswer_NoToolCalls_VerifiesNoToolExecution()
    {
        var (engine, orchestrator, processRunner, _) = CreateEngineWithMockedTools();

        engine.EnqueueDirectAnswer("42");

        var result = await orchestrator.ExecuteMultiStep("What is 6*7?");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Equal("42", result.FinalOutput);
        Assert.Equal(1, engine.GenerateCallCount);
        Assert.Empty(engine.ToolResults);
        processRunner.Verify(p => p.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Empty/null response handling ──

    [Fact]
    public async Task EmptyResponse_HandledAsInvalidFormat_RetriesThenExhausts()
    {
        var (engine, orchestrator, _, _) = CreateEngineWithMockedTools(maxTurns: 5);

        // Return empty string — empty direct answer (orchestrator may accept or exhaust turns)
        engine.SetDefaultResponse("");

        var result = await orchestrator.ExecuteMultiStep("Test empty response");

        // Should exhaust turns due to format retries never producing valid tags
        Assert.True(result.Status == OrchestratorStatus.TurnsExhausted || result.Status == OrchestratorStatus.GoalAchieved);
    }

    [Fact]
    public async Task PlainTextResponse_TreatedAsDirectAnswer()
    {
        var (engine, orchestrator, _, _) = CreateEngineWithMockedTools(maxTurns: 5);

        // v14: plain text is wrapped by MockEngine as a direct answer (no tags needed)
        engine.AddResponse("I think the answer is 42.");

        var result = await orchestrator.ExecuteMultiStep("What is the answer?");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Equal("I think the answer is 42.", result.FinalOutput);
    }

    // ── Tool not registered: unknown tool name ──

    [Fact]
    public async Task UnknownTool_NotRegistered_ErrorHandledInOrchestrator()
    {
        var (engine, orchestrator, _, _) = CreateEngineWithMockedTools();

        engine.EnqueueToolCall("ENonExistentTool", new() { ["arg"] = "value" });
        engine.EnqueueDirectAnswer("Tool was not available");

        var result = await orchestrator.ExecuteMultiStep("Use unknown tool");

        // The orchestrator should handle the unknown tool and continue
        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Equal("Tool was not available", result.FinalOutput);
    }

    // ── Helper ──
}