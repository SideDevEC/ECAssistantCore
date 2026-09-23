using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Tools;
using ECAssistant.Core.Verification;
using ECAssistant.TestSupport;
using System.Text.Json;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.13: tier-aware post-edit verification loop. Pure-logic tests — the build/test
/// execution lives behind IVerificationRunner (stubbed here); no real dotnet build,
/// no DB, no server.
/// </summary>
public sealed class PostEditVerifierTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-postedit").FullName;

    // ── config surface ──

    [Fact]
    public void VerificationConfig_Defaults_MatchBrief()
    {
        var config = JsonSerializer.Deserialize<EAgentConfig>("{}");
        Assert.NotNull(config!.Verification);
        Assert.True(config.Verification.Enabled);
        Assert.Equal(2, config.Verification.MaxRounds);
        Assert.StartsWith("dotnet build", config.Verification.BuildCommand);
        Assert.Null(config.Verification.TestCommand);
    }

    [Fact]
    public void VerificationConfig_ParsesFromConfig()
    {
        var json = """
        { "verification": { "enabled": false, "max_rounds": 3, "trivial_edit_max_chars": 50,
                            "build_command": "dotnet build -c Release", "test_command": "dotnet test --filter Fast" } }
        """;
        var config = JsonSerializer.Deserialize<EAgentConfig>(json);
        Assert.False(config!.Verification.Enabled);
        Assert.Equal(3, config.Verification.MaxRounds);
        Assert.Equal(50, config.Verification.TrivialEditMaxChars);
        Assert.Equal("dotnet build -c Release", config.Verification.BuildCommand);
        Assert.Equal("dotnet test --filter Fast", config.Verification.TestCommand);
    }

    // ── IsFileModifyingCall: tool name + args only, no concrete tool coupling ──

    [Fact]
    public void CodeEditor_MutatingActions_AreFileModifying()
    {
        var v = NewVerifier();
        foreach (var action in new[] { "create", "patch", "replace-all", "insert", "delete-lines", "delete" })
            Assert.True(v.IsFileModifyingCall("ECodeEditor", new Dictionary<string, string?> { ["action"] = action }),
                action);
    }

    [Fact]
    public void CodeEditor_ReadOnlyActions_AreNotFileModifying()
    {
        var v = NewVerifier();
        foreach (var action in new[] { "diff", "search" })
            Assert.False(v.IsFileModifyingCall("ECodeEditor", new Dictionary<string, string?> { ["action"] = action }));
        Assert.False(v.IsFileModifyingCall("EFileReader", new Dictionary<string, string?>()));
    }

    [Fact]
    public void Shell_WriteMoveDeleteCommands_AreFileModifying()
    {
        var v = NewVerifier();
        string[] writes =
        {
            "echo hi > out.txt", "echo hi >> log.txt", "rm -rf build/", "mv a b", "cp a b",
            "mkdir newdir", "touch f.txt", "Set-Content f.txt 'x'", "Remove-Item f.txt",
            "Move-Item a b", "git add . && git commit -m x > /dev/null"
        };
        foreach (var cmd in writes)
            Assert.True(v.IsFileModifyingCall("EShellAgent", new Dictionary<string, string?> { ["command"] = cmd }), cmd);
    }

    [Fact]
    public void Shell_ReadOnlyCommands_AreNotFileModifying()
    {
        var v = NewVerifier();
        string[] reads = { "ls -la", "git status", "cat file.txt", "dotnet build", "grep -r TODO ." };
        foreach (var cmd in reads)
            Assert.False(v.IsFileModifyingCall("EShellAgent", new Dictionary<string, string?> { ["command"] = cmd }), cmd);
    }

    // ── tier gating ──

    [Fact]
    public void SmallTier_VerifiesEveryEdit_NoTrivialSkip()
    {
        var v = NewVerifier();
        var args = new Dictionary<string, string?> { ["action"] = "create", ["content"] = "x" };
        Assert.True(v.ShouldVerify("ECodeEditor", args, isLargeTier: false));
        Assert.Equal(2, v.MaxRounds(isLargeTier: false));
    }

    [Fact]
    public void LargeTier_SkipsTrivialSingleLineEdits()
    {
        var v = NewVerifier(new VerificationConfig { TrivialEditMaxChars = 200 });
        var trivial = new Dictionary<string, string?> { ["action"] = "create", ["content"] = "short one-line fix" };
        Assert.False(v.ShouldVerify("ECodeEditor", trivial, isLargeTier: true));
        // multi-line or long payloads still verify
        var multiline = new Dictionary<string, string?> { ["action"] = "create", ["content"] = "line1\nline2" };
        var longSingle = new Dictionary<string, string?> { ["action"] = "create", ["content"] = new string('x', 201) };
        Assert.True(v.ShouldVerify("ECodeEditor", multiline, isLargeTier: true));
        Assert.True(v.ShouldVerify("ECodeEditor", longSingle, isLargeTier: true));
    }

    [Fact]
    public void LargeTier_MaxRoundsIsOne()
    {
        var v = NewVerifier();
        Assert.Equal(1, v.MaxRounds(isLargeTier: true));
    }

    // ── VerifyAsync via stub runner (no real dotnet build) ──

    [Fact]
    public async Task VerifyAsync_BuildFails_ReturnsBuildFailure()
    {
        var runner = new StubRunner(failCommands: ["dotnet build"]);
        var v = NewVerifier(runner: runner);
        var result = await v.VerifyAsync(_dir);
        Assert.False(result.Succeeded);
        Assert.Contains("dotnet build", result.Command);
    }

    [Fact]
    public async Task VerifyAsync_BuildOk_NoTestCommand_ReturnsSuccess()
    {
        var v = NewVerifier(runner: new StubRunner());
        var result = await v.VerifyAsync(_dir);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_BuildOk_TestsFail_ReportsTestFailure()
    {
        var v = NewVerifier(new VerificationConfig { TestCommand = "dotnet test --filter Fast" },
            new StubRunner(failCommands: ["dotnet test --filter Fast"]));
        var result = await v.VerifyAsync(_dir);
        Assert.False(result.Succeeded);
        Assert.Contains("--filter Fast", result.Command);
    }

    [Fact]
    public async Task VerifyAsync_BuildOk_TestsOk_Succeeds()
    {
        var v = NewVerifier(new VerificationConfig { TestCommand = "dotnet test --filter Fast" }, new StubRunner());
        Assert.True((await v.VerifyAsync(_dir)).Succeeded);
    }

    // ── feedback formatting ──

    [Fact]
    public void FailureFeedback_ContainsRoundAndOutput_AndIsTailTruncated()
    {
        var v = NewVerifier();
        var big = new string('x', 3000) + "error CS1002: ; expected";
        var feedback = v.BuildFailureFeedback(1, 2, new VerificationResult(false, big, "dotnet build", 1));
        Assert.Contains("[VERIFY FAIL]", feedback);
        Assert.Contains("round 1/2", feedback);
        Assert.Contains("error CS1002", feedback);          // tail preserved
        Assert.True(feedback.Length < big.Length);          // truncated
    }

    [Fact]
    public void FailureFeedback_FinalRound_TellsModelToReport()
    {
        var v = NewVerifier();
        var feedback = v.BuildFailureFeedback(2, 2, new VerificationResult(false, "err", "cmd", 1));
        Assert.DoesNotContain("run again", feedback);
        var mid = v.BuildFailureFeedback(1, 2, new VerificationResult(false, "err", "cmd", 1));
        Assert.Contains("run again", mid);
        Assert.Contains("[VERIFY]", v.BuildSuccessNote());
    }

    // ── orchestrator wiring: failure injection + round cap (MockEngine, no real build) ──

    [Fact]
    public async Task Orchestrator_SmallTier_FailedVerificationIsFedBack_AndCappedAtMaxRounds()
    {
        var engine = new MockEngine(workingDir: _dir);
        engine.RegisterTool(new FakeCodeEditorTool());
        var runner = new StubRunner(failCommands: ["dotnet build --nologo -v q"]);
        var config = TierConfig("small", isLocal: true);
        var orchestrator = new AgentOrchestrator(engine, null, maxTurns: 6, maxFailures: 3,
            toolPolicy: AllowEditorPolicy(), config: config,
            postEditVerifier: new PostEditVerifier(runner, config.Verification));

        // edit → fail(1/2) → edit again (different args) → fail(2/2, capped) → answer
        engine.EnqueueToolCall("ECodeEditor", new Dictionary<string, string?> { ["action"] = "create", ["content"] = "aaa" });
        engine.EnqueueToolCall("ECodeEditor", new Dictionary<string, string?> { ["action"] = "create", ["content"] = "bbb" });
        engine.EnqueueDirectAnswer("done");

        var result = await orchestrator.ExecuteMultiStep("add a feature");
        Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);

        var fails = engine.ToolResults.Where(r => r.output.StartsWith("[VERIFY FAIL]")).ToList();
        Assert.Equal(2, fails.Count);
        Assert.Contains("round 1/2", fails[0].output);
        Assert.Contains("round 2/2", fails[1].output);
        Assert.All(fails, f => Assert.Equal("ECodeEditor", f.toolName));
    }

    [Fact]
    public async Task Orchestrator_LargeTier_TrivialEditSkipped_NoVerification()
    {
        var engine = new MockEngine(workingDir: _dir);
        engine.RegisterTool(new FakeCodeEditorTool());
        var runner = new StubRunner();
        var config = TierConfig("large", isLocal: true);
        var orchestrator = new AgentOrchestrator(engine, null, maxTurns: 5, maxFailures: 3,
            toolPolicy: AllowEditorPolicy(), config: config,
            postEditVerifier: new PostEditVerifier(runner, config.Verification));

        engine.EnqueueToolCall("ECodeEditor", new Dictionary<string, string?> { ["action"] = "create", ["content"] = "tiny" });
        engine.EnqueueDirectAnswer("done");

        await orchestrator.ExecuteMultiStep("small tweak");
        Assert.Empty(engine.ToolResults.Where(r => r.output.StartsWith("[VERIFY")));
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task Orchestrator_LargeTier_NonTrivialEdit_VerifiesOnlyOnce()
    {
        var engine = new MockEngine(workingDir: _dir);
        engine.RegisterTool(new FakeCodeEditorTool());
        var runner = new StubRunner(failCommands: ["dotnet build --nologo -v q"]);
        var config = TierConfig("large", isLocal: true);
        var orchestrator = new AgentOrchestrator(engine, null, maxTurns: 5, maxFailures: 3,
            toolPolicy: AllowEditorPolicy(), config: config,
            postEditVerifier: new PostEditVerifier(runner, config.Verification));

        var bigContent = new string('x', 250);
        engine.EnqueueToolCall("ECodeEditor", new Dictionary<string, string?> { ["action"] = "create", ["content"] = bigContent });
        engine.EnqueueToolCall("ECodeEditor", new Dictionary<string, string?> { ["action"] = "create", ["content"] = bigContent + "y" });
        engine.EnqueueDirectAnswer("done");

        await orchestrator.ExecuteMultiStep("add a big file");
        // Large tier: one verification round per run even after two edits.
        Assert.Equal(1, runner.CallCount);
        Assert.Single(engine.ToolResults.Where(r => r.output.StartsWith("[VERIFY FAIL]")));
    }

    [Fact]
    public async Task Orchestrator_SuccessfulVerification_SmallTier_GetsSuccessNote()
    {
        var engine = new MockEngine(workingDir: _dir);
        engine.RegisterTool(new FakeCodeEditorTool());
        var runner = new StubRunner();
        var config = TierConfig("small", isLocal: true);
        var orchestrator = new AgentOrchestrator(engine, null, maxTurns: 5, maxFailures: 3,
            toolPolicy: AllowEditorPolicy(), config: config,
            postEditVerifier: new PostEditVerifier(runner, config.Verification));

        engine.EnqueueToolCall("ECodeEditor", new Dictionary<string, string?> { ["action"] = "create", ["content"] = "ok content" });
        engine.EnqueueDirectAnswer("done");

        await orchestrator.ExecuteMultiStep("add a thing");
        Assert.Contains(engine.ToolResults, r => r.output.StartsWith("[VERIFY]") && r.output.Contains("passed"));
        Assert.Equal(1, runner.CallCount);
    }

    // ── helpers ──

    /// <summary>ECodeEditor is ApprovalRequired by default — tests run headless, so allow it.</summary>
    private static global::ECAssistant.Core.Tools.ToolPolicy AllowEditorPolicy()
    {
        var policy = new global::ECAssistant.Core.Tools.ToolPolicy();
        policy.SetPermission("ECodeEditor", approvalRequired: false, "test harness");
        return policy;
    }

    private static PostEditVerifier NewVerifier(
        VerificationConfig? config = null, IVerificationRunner? runner = null) =>
        new(runner ?? new StubRunner(), config ?? new VerificationConfig());

    private static EAgentConfig TierConfig(string tierMode, bool isLocal) =>
        JsonSerializer.Deserialize<EAgentConfig>($$"""
        {
          "llm_provider": { "mode": "{{(isLocal ? "local" : "remote")}}" },
          "model_tier": { "mode": "{{tierMode}}" }
        }
        """)!;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

/// <summary>Scripted verification runner — no real dotnet build ever spawns.</summary>
file sealed class StubRunner : IVerificationRunner
{
    private readonly HashSet<string> _failCommands;
    public int CallCount { get; private set; }

    public StubRunner(params string[] failCommands) => _failCommands = new HashSet<string>(failCommands);

    public Task<VerificationResult> RunAsync(string command, string? workingDir = null, CancellationToken ct = default)
    {
        CallCount++;
        var failed = _failCommands.Any(fc => command.Contains(fc, StringComparison.Ordinal));
        return Task.FromResult(failed
            ? new VerificationResult(false, "build FAILED\nerror CS1002: ; expected", command, 1)
            : new VerificationResult(true, "Build succeeded.", command, 0));
    }
}

/// <summary>Stand-in for ECodeEditorTool — verifies by NAME + args, so no concrete tool dependency is needed.</summary>
file sealed class FakeCodeEditorTool : EToolBase
{
    public override string Name => "ECodeEditor";
    public override string Description => "Test double for the code editor (write path only).";
    public override string UsageExample => "ECodeEditor(action=\"create\")";

    public override Task<EToolResult> ExecuteAsync(
        Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        // Unique temp name: multiple verifier tests run headless and parallel —
        // a fixed path would collide across tests and leak a shared mutable file.
        var path = arguments.GetValueOrDefault("file_path")
            ?? Path.Combine(Path.GetTempPath(), "fake-edit-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        File.WriteAllText(path, arguments.GetValueOrDefault("content") ?? "");
        return Task.FromResult(EToolResult.Success(Name, "created " + path));
    }
}