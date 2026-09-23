using ECAssistant.Core;
using ECAssistant.Core.Config;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Session;
using ECAssistant.Core.Tools;
using ECAssistant.Core.Tools.Shell;
using ECAssistant.Core.Interfaces;
using ECAssistant.TestSupport;

using Moq;

namespace ECAssistant.Core.Tests.Integration;

/// <summary>
/// v14.19.1 unit coverage for the gaps found in the feature audit:
///   1. Batch-path verification gate (decomposed plans previously bypassed it).
///   2. Max-turns exit now captures partial-progress playbooks.
/// Follows the OrchestratorIntegrationTests pattern: MockEngine + real tools with
/// mocked dependencies. No LLM, no real builds, no DB.
/// </summary>
public sealed class OrchestratorV1419Tests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "ECAInteg_V1419_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly List<AgentOrchestrator> _orchestrators = new();
    private readonly List<MockEngine> _engines = new();

    public OrchestratorV1419Tests()
    {
        Directory.CreateDirectory(_tempDir);
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

    /// <summary>Verifier that treats every tool as file-modifying and records invocations.</summary>
    private sealed class RecordingVerifier : IPostEditVerifier
    {
        public int VerifyCalls;
        public bool IsFileModifyingCall(string toolName, IReadOnlyDictionary<string, string?> args) => true;
        public bool ShouldVerify(string toolName, IReadOnlyDictionary<string, string?> args, bool isLargeTier) => true;
        public int MaxRounds(bool isLargeTier) => isLargeTier ? 1 : 2;
        public string BuildFailureFeedback(int round, int maxRounds, VerificationResult result) => "[VERIFY FAIL]";
        public string BuildSuccessNote() => "[VERIFY OK]";
        public Task<VerificationResult> VerifyAsync(string? workingDir, CancellationToken ct = default)
        {
            VerifyCalls++;
            return Task.FromResult(new VerificationResult(true, "(fake)", "fake", 0));
        }
    }

    private (MockEngine engine, AgentOrchestrator orchestrator, RecordingVerifier verifier) Create(
        int maxTurns = 10, IPostEditVerifier? verifier = null)
    {
        var engine = new MockEngine(_tempDir);
        _engines.Add(engine);
        engine.InitializePlaybooks(_tempDir); // mirrors AgentSession ctor wiring

        var mockRunner = new Mock<IProcessRunner>();
        mockRunner
            .Setup(r => r.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "out", "", false));
        engine.RegisterTool(new EShellAgent(mockRunner.Object, new AppConfig(), _tempDir));

        var v = verifier as RecordingVerifier ?? new RecordingVerifier();
        var orchestrator = new AgentOrchestrator(
            engine, sessionOutput: null, maxTurns: maxTurns, maxFailures: 3,
            toolPolicy: new ToolPolicy(), logger: null,
            config: null, postEditVerifier: v);
        _orchestrators.Add(orchestrator);
        return (engine, orchestrator, v);
    }

    // ── Gap 1: batch-path verification gate ──

    [Fact]
    public async Task BatchToolCalls_VerificationGateRuns_PerSuccessfulCall()
    {
        var (engine, orchestrator, verifier) = Create();

        engine.EnqueueMultiToolCall(
            ("EShellAgent", new Dictionary<string, string?> { ["command"] = "echo first" }),
            ("EShellAgent", new Dictionary<string, string?> { ["command"] = "echo second" }));
        engine.EnqueueDirectAnswer("Done");

        var result = await orchestrator.ExecuteMultiStep("Run two commands");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        // v14.19.1: EVERY successful file-modifying batch call is gated — both.
        Assert.Equal(2, verifier.VerifyCalls);
    }

    [Fact]
    public async Task SingleToolCall_VerificationGateRuns_Once()
    {
        var (engine, orchestrator, verifier) = Create();

        engine.EnqueueToolCall("EShellAgent", new() { ["command"] = "echo hi" });
        engine.EnqueueDirectAnswer("Done");

        var result = await orchestrator.ExecuteMultiStep("Run one command");

        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
        Assert.Equal(1, verifier.VerifyCalls);
    }

    // ── Gap 2: max-turns exit captures partial-progress playbooks ──

    [Fact]
    public async Task MaxTurnsExit_StillCapturesPlaybook()
    {
        var (engine, orchestrator, _) = Create(maxTurns: 1);

        engine.EnqueueToolCall("EShellAgent", new() { ["command"] = "ls" });
        // No further decisions — the run must end via the max-turns path.

        var result = await orchestrator.ExecuteMultiStep("List the directory");

        Assert.Equal(OrchestratorStatus.TurnsExhausted, result.Status);
        // v14.19: the successful tool call must NOT be lost — a playbook persists
        // even when the run exhausts its turns.
        var pbDir = Path.Combine(_tempDir, "playbooks");
        Assert.True(Directory.Exists(pbDir), $"no playbook store at {pbDir}");
        Assert.NotEmpty(Directory.GetFiles(pbDir, "pb_*.json"));
        Assert.NotEmpty(engine.PlaybookStore!.All);
    }
}
