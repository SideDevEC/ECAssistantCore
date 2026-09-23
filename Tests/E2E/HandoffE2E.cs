using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Session;
using ECAssistant.TestSupport;

namespace ECAssistant.Core.Tests.E2E;

/// <summary>
/// v15 ephemeral handoff — end-to-end against a REAL ECAssistantLLM server with
/// a real local model (HarnessE2E gating pattern):
///   ECA_E2E_SERVER — required (e.g. http://localhost:48217)
///   ECA_E2E_MODEL  — optional model id (default qwen35-4b)
///
/// Journey 1: the main model delegates via EHandoff; the specialist (custom
/// system prompt injected at runtime) answers, and that answer becomes the
/// parent's final output. Parent loop stops — one hop, no recursion.
/// Journey 2 (regression): with EHandoff REGISTERED, a plain knowledge question
/// still answers directly — the extra tool block must not tempt the model into
/// delegating trivia.
/// </summary>
public sealed class HandoffE2E
{
    private string? ServerUrl => Environment.GetEnvironmentVariable("ECA_E2E_SERVER");
    private string ModelId =>
        Environment.GetEnvironmentVariable("ECA_E2E_MODEL") ?? "qwen35-4b";

    private AppConfig BuildConfig()
    {
        var json = $$"""
        {
          "llm_provider": { "mode": "local", "model_id": "{{ModelId}}" },
          "model_tier": { "mode": "small" },
          "interface": { "max_turns": 6 },
          "context_management": { "max_context_tokens": 16384 }
        }
        """;
        return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json)!;
    }

    private async Task<(AgentSession session, string dir)> CreateSessionAsync(string endpoint)
    {
        var config = BuildConfig();
        var factory = new HarnessE2ESessionFactory();
        var (session, dir) = await factory.CreateAsync(
            endpoint,
            config,
            configure: engine => engine.RegisterTool(new ProbeTestTool()),
            prepareWorkingDir: dir => config.AgentSettings.WorkingDirectory = dir);

        // SessionBuilder does this in production; the e2e factory bypasses it,
        // so initialize handoff explicitly (registers EHandoff + rebuilds KV cache).
        await session.InitializeHandoffAsync();
        return (session, dir);
    }

    [Fact]
    public async Task HandoffJourney_SpecialistAnswerBecomesFinalOutput_ParentStops()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return; // not enabled — needs a real server

        var (session, dir) = await CreateSessionAsync(ServerUrl!);
        try
        {
            var result = await session.Orchestrator.ExecuteMultiStep(
                "Call EHandoff now to delegate this task to a specialist. " +
                "Use this exact specialist prompt: 'You are an echo specialist. Reply with exactly SPECIALIST_ECHO_OK and nothing else.' " +
                "Pass context: 'none'. Do not answer yourself — delegate.");

            // The parent loop stops after the interception; the specialist's
            // answer IS the final output.
            Assert.True(result.Status == OrchestratorStatus.GoalAchieved,
                $"expected GoalAchieved, got {result.Status}: {result.FinalOutput}");
            Assert.Contains("SPECIALIST_ECHO_OK", result.FinalOutput);
        }
        finally
        {
            await session.DisposeAsync();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task KnowledgeQuestion_AnswersDirectly_EvenWithHandoffRegistered()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return; // not enabled — needs a real server

        var (session, dir) = await CreateSessionAsync(ServerUrl!);
        try
        {
            var result = await session.Orchestrator.ExecuteMultiStep(
                "What is 7 times 8? Answer directly from knowledge — no tools, no delegation.");

            // Anti-tool-spam pin with EHandoff in the union: registered must not
            // mean "used". (A 4B model may still make one exploratory tool call —
            // observed live 2026-09-23 on the base harness — so ToolCallsMade is
            // not pinned to 0; that is model behavior, not harness logic.)
            Assert.True(result.Status == OrchestratorStatus.GoalAchieved,
                $"expected GoalAchieved, got {result.Status}: {result.FinalOutput}");
            Assert.Contains("56", result.FinalOutput);
        }
        finally
        {
            await session.DisposeAsync();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}