using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.TestSupport;

namespace ECAssistant.Core.Tests.E2E;

/// <summary>
/// v14.19: extended user journeys — every remaining harness feature experienced
/// from the user's chair. All asserts are transcript-based (what appears on
/// screen). Gated on ECA_E2E_SERVER; silently skips without it.
/// </summary>
public sealed class UserJourneyExtendedE2E
{
    private string? ServerUrl => Environment.GetEnvironmentVariable("ECA_E2E_SERVER");
    private string ModelId =>
        Environment.GetEnvironmentVariable("ECA_E2E_MODEL") ?? "qwen35-4b";

    private static EAgentConfig BuildConfig(string modelId, string tier = "small") =>
        JsonSerializer.Deserialize<EAgentConfig>(
            $$"""
            {
              "llm_provider": { "mode": "local", "model_id": "{{modelId}}" },
              "model_tier": { "mode": "{{tier}}" },
              "interface": { "max_turns": 6 },
              "context_management": { "max_context_tokens": 16384 }
            }
            """)!;

    private static void CleanCwdArtifacts()
    {
        // ECodeEditor resolves relative paths against process CWD (known quirk) —
        // keep the testhost dir clean between attempts/runs.
        foreach (var f in new[] { "note.txt", "hello.txt", "bogus.txt" })
            try { File.Delete(Path.Combine(Directory.GetCurrentDirectory(), f)); } catch { }
    }

    private const string CreateNoteCall =
        "Make EXACTLY this tool call: ECodeEditor(action=create, file=note.txt, content=hello). " +
        "Do not send anything else — just make that call now.";

    private static void DumpTranscript(string tag, string transcript)
    {
        try { File.WriteAllText($"/tmp/eca-uj-dump-{tag}.txt", transcript); } catch { }
    }

    // ── J4: the user DENIES an approval prompt ──
    [Fact]
    public async Task PolicyDenial_UserSeesPromptAndGracefulOutcome()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;
        CleanCwdArtifacts();

        var harness = await UserExperienceHarness.CreateAsync(
            ServerUrl!, BuildConfig(ModelId),
            approvalResponder: _ => ECAssistant.Core.Session.ApprovalScope.Deny);
        try
        {
            var turn = await harness.SendAndAwaitAsync(
                "Create a file named note.txt containing hello. " + CreateNoteCall);

            var visible = string.Join("\n", turn.Select(t => t.Text));
            DumpTranscript("policy-deny", harness.TranscriptText);
            // The user must SEE the approval gate: policy line + explicit denial.
            Assert.Contains("Policy", visible, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DENIED", visible, StringComparison.OrdinalIgnoreCase);
            // Turn must END gracefully — no hang, no exception dump.
            Assert.DoesNotContain("Unhandled exception", visible, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    // ── J5: verification gate as the user experiences it (REAL product wiring) ──
    [Fact]
    public async Task VerificationGate_RealWiring_VisibleToUser()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;

        // 4B models fumble tool args ~50% of runs — retry across fresh sessions.
        var sawVerify = false;
        string? lastTranscript = null;
        for (var attempt = 1; attempt <= 3 && !sawVerify; attempt++)
        {
            CleanCwdArtifacts();
            var harness = await UserExperienceHarness.CreateAsync(ServerUrl!, BuildConfig(ModelId));
            try
            {
                await harness.SendAndAwaitAsync(
                    "Create a file named note.txt containing hello. " + CreateNoteCall);
                lastTranscript = harness.TranscriptText;
                if (lastTranscript.Contains("[Verify]", StringComparison.OrdinalIgnoreCase))
                    sawVerify = true;
            }
            finally
            {
                await harness.DisposeAsync();
            }
        }

        if (!sawVerify) DumpTranscript("verify-real2", lastTranscript ?? "");
        Assert.True(sawVerify,
            $"verification gate never surfaced to the user across attempts (dump: /tmp/eca-uj-dump-verify-real2.txt)");
    }

    // ── J6: tool failure resilience — the user asks for an invalid action ──
    [Fact]
    public async Task ToolError_AgentRecoversGracefully()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;
        CleanCwdArtifacts();

        var harness = await UserExperienceHarness.CreateAsync(ServerUrl!, BuildConfig(ModelId));
        try
        {
            var turn = await harness.SendAndAwaitAsync(
                "Make EXACTLY this tool call: ECodeEditor(action=definitely_not_an_action, file=x.txt, content=hi). " +
                "Do not send anything else — just make that call now.");

            var visible = string.Join("\n", turn.Select(t => t.Text));
            // The error surfaces; the agent finishes the turn instead of dying.
            Assert.Contains("ECodeEditor", visible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Unhandled exception", visible, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    // ── J7: large-tier session — slim profile still completes the journey ──
    [Fact]
    public async Task LargeTier_SlimProfile_CompletesFileTask()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;
        CleanCwdArtifacts();

        var harness = await UserExperienceHarness.CreateAsync(ServerUrl!, BuildConfig(ModelId, tier: "large"));
        try
        {
            var turn = await harness.SendAndAwaitAsync(
                "Create a file named note.txt containing hello. " + CreateNoteCall);

            var visible = string.Join("\n", turn.Select(t => t.Text));
            Assert.Contains("ECodeEditor", visible, StringComparison.OrdinalIgnoreCase);
            // Large-tier scaffolding must NOT leak guidance-style nags.
            Assert.DoesNotContain("[VERIFY FAIL]", visible);
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    // ── J8: playbook capture is announced and replay works ──
    [Fact]
    public async Task Playbook_CapturedAndAnnounced_SecondRunWorks()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;
        CleanCwdArtifacts();

        var harness = await UserExperienceHarness.CreateAsync(ServerUrl!, BuildConfig(ModelId));
        try
        {
            var probe = "Call the ProbeTool with action probe, then report what it returned.";
            await harness.SendAndAwaitAsync(probe);

            // First successful tool goal → capture announced to the user.
            DumpTranscript("playbook", harness.TranscriptText);
            var storeDump = string.Join("|", harness.Session.Engine.PlaybookStore?.All.Select(p => p.Title) ?? []);
            DumpTranscript("playbook-store", storeDump);
            Assert.Contains("[Playbook]", harness.TranscriptText, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(Path.Combine(harness.WorkingDir, "playbooks")));

            // Replay: same task again in the same session — completes cleanly.
            var second = await harness.SendAndAwaitAsync(probe);
            var visible = string.Join("\n", second.Select(t => t.Text));
            Assert.False(string.IsNullOrWhiteSpace(visible), "second run went silent");
            Assert.DoesNotContain("Unhandled exception", visible, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }
}
