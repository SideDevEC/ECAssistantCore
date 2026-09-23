using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Session;
using ECAssistant.Core.Verification;
using ECAssistant.TestSupport;

namespace ECAssistant.Core.Tests.E2E;

/// <summary>
/// v14.13–v14.16 feature E2E against a REAL server + real local model
/// (same gate as HarnessE2E: ECA_E2E_SERVER required). Covers, with a live
/// small-tier model:
///   1. v14.14 playbook capture — a successful tool goal persists a playbook.
///   2. v14.16 context pinning — goal + decisions pinned; block buildable.
///   3. v14.13 verification gate — a file-editing tool call routes through the
///      verifier (fake runner records the invocation; no real dotnet build).
/// Tier tuning (v14.17.1) is covered at unit level and via the factory's
/// CreateTiered wiring; sub-agent spawn is intentionally not driven live (a 4B
/// model's nested orchestration is too flaky for a gate — brief text is
/// unit-tested).
/// </summary>
public sealed class HarnessE2EFeatures
{
    private string? ServerUrl => Environment.GetEnvironmentVariable("ECA_E2E_SERVER");
    private string ModelId =>
        Environment.GetEnvironmentVariable("ECA_E2E_MODEL") ?? "qwen35-4b";

    private static readonly string ToolTask =
        "Call the ProbeTool with action probe, then report what it returned.";

    private EAgentConfig BuildConfig() => JsonSerializer.Deserialize<EAgentConfig>(
        $$"""
        {
          "llm_provider": { "mode": "local", "model_id": "{{ModelId}}" },
          "model_tier": { "mode": "small" },
          "interface": { "max_turns": 6 },
          "context_management": { "max_context_tokens": 16384 }
        }
        """)!;

    [Fact]
    public async Task Playbook_Persisted_AfterSuccessfulToolGoal()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;

        var (session, dir) = await CreateSessionAsync();
        try
        {
            var result = await session.Orchestrator.ExecuteMultiStep(ToolTask);
            Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);

            var playbookDir = Path.Combine(dir, "playbooks");
            Assert.True(Directory.Exists(playbookDir), $"no playbook store at {playbookDir}");
            var files = Directory.GetFiles(playbookDir, "*.json");
            Assert.True(files.Length >= 1, "successful tool goal produced no playbook");
        }
        finally
        {
            await session.DisposeAsync();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Pinner_HoldsGoal_AfterToolTurn()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;

        var (session, dir) = await CreateSessionAsync();
        try
        {
            await session.Orchestrator.ExecuteMultiStep(ToolTask);

            var pinner = session.Engine.ContextPinner;
            Assert.NotNull(pinner);
            var block = pinner!.BuildPinnedBlock(isLargeTier: false, maxChars: 1200);
            Assert.NotNull(block);
            Assert.Contains("[PINNED CONTEXT]", block!);
            Assert.Contains("ProbeTool", block!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await session.DisposeAsync();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task VerificationGate_Invoked_AfterFileEdit()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;

        // 4B models sometimes narrate instead of calling — retry across fresh
        // sessions; the gate fires whenever a file-modifying call actually lands.
        string? lastOutput = null;
        var allAttempts = new List<string>();
        var gateRan = false;
        for (var attempt = 1; attempt <= 5 && !gateRan; attempt++)
        {
            // ECodeEditor resolves relative paths against process CWD (known product
            // quirk — session working dir not plumbed into the tool), so leftovers
            // from previous attempts/runs leak across sessions. Clean before each.
            try { File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "hello.txt")); } catch { }
            var (s2, d2) = await CreateSessionAsync(registerEditor: true);
            try
            {
                var v = new RecordingVerifier();
                var policy = new global::ECAssistant.Core.Tools.ToolPolicy();
                policy.SetPermission("ECodeEditor", approvalRequired: false, "E2E: auto-approve file editor");
                var orch = new AgentOrchestrator(
                    s2.Engine, sessionOutput: null, maxTurns: 6, maxFailures: 3,
                    toolPolicy: policy, logger: null, config: null, postEditVerifier: v);
                var r = await orch.ExecuteMultiStep(
                    "Create a file named hello.txt containing the word hello.\n" +
                    "Make EXACTLY this tool call: ECodeEditor(action=create, file=hello.txt, content=hello).\n" +
                    "Do not send anything else — just make that call now.");
                lastOutput = r.FinalOutput + " | TOOL_OUT: " +
                    string.Join(" ; ", s2.Engine.ContextWindow.GetWindowMessages()
                        .Where(m => m.Role == "tool_output").Select(m => m.Source + ": " +
                        (m.Content.Length > 300 ? m.Content[..200] + "…" : m.Content)));
                allAttempts.Add($"[attempt {attempt}] status={r.Status} calls={r.ToolCallsMade} out={lastOutput}");
                if (v.VerifyCalls >= 1) gateRan = true;
            }
            finally
            {
                await s2.DisposeAsync();
                try { Directory.Delete(d2, true); } catch { }
            }
        }

        Assert.True(gateRan,
            $"no attempt drove a file edit through the verification gate. Attempts:\n" +
            string.Join("\n", allAttempts));
    }

    private async Task<(AgentSession session, string dir)> CreateSessionAsync(bool registerEditor = false)
    {
        var factory = new HarnessE2ESessionFactory();
        var (session, dir) = await factory.CreateAsync(
            ServerUrl!,
            BuildConfig(),
            configure: engine =>
            {
                if (registerEditor)
                {
                    engine.RegisterTool(new global::ECAssistant.Core.Tools.Code.ECodeEditorTool(
                        new global::ECAssistant.Core.Services.FileSystemAdapter(),
                        new global::ECAssistant.Core.Config.EAgentConfig()));
                    return;
                }
                engine.RegisterTool(new ProbeTestTool());
            });
        return (session, dir);
    }

    /// <summary>Recording fake — never runs a real build; counts gate invocations.</summary>
    private sealed class RecordingVerifier : IPostEditVerifier
    {
        public int VerifyCalls;

        public bool IsFileModifyingCall(string toolName, IReadOnlyDictionary<string, string?> args) =>
            toolName is "ECodeEditor" or "EShellAgent";

        public bool ShouldVerify(string toolName, IReadOnlyDictionary<string, string?> args, bool isLargeTier) =>
            IsFileModifyingCall(toolName, args);

        public int MaxRounds(bool isLargeTier) => isLargeTier ? 1 : 2;

        public string BuildFailureFeedback(int round, int maxRounds, VerificationResult result) =>
            $"[VERIFY FAIL] round {round}/{maxRounds}: {result.Output}";

        public string BuildSuccessNote() => "[VERIFY OK]";

        public Task<VerificationResult> VerifyAsync(string? workingDir, CancellationToken ct = default)
        {
            VerifyCalls++;
            return Task.FromResult(new VerificationResult(true, "(fake — no build run)", "fake", 0));
        }
    }
}
