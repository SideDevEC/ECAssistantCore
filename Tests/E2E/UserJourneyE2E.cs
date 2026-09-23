using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Session;
using ECAssistant.TestSupport;

namespace ECAssistant.Core.Tests.E2E;

/// <summary>
/// v14.19: USER-EXPERIENCE E2E — drives the session through AgentSession.Prompt
/// (the real user entry point) and asserts on the visible transcript: what the
/// user would read on screen. Gated on ECA_E2E_SERVER like the rest of the E2E
/// suite; silently skips without it.
/// </summary>
public sealed class UserJourneyE2E
{
    private string? ServerUrl => Environment.GetEnvironmentVariable("ECA_E2E_SERVER");
    private string ModelId =>
        Environment.GetEnvironmentVariable("ECA_E2E_MODEL") ?? "qwen35-4b";

    private static EAgentConfig BuildConfig(string modelId) => JsonSerializer.Deserialize<EAgentConfig>(
        $$"""
        {
          "llm_provider": { "mode": "local", "model_id": "{{modelId}}" },
          "model_tier": { "mode": "small" },
          "interface": { "max_turns": 6 },
          "context_management": { "max_context_tokens": 16384 }
        }
        """)!;

    [Fact]
    public async Task Greeting_GetsDirectAnswer_WithoutToolNoise()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;

        var harness = await UserExperienceHarness.CreateAsync(ServerUrl!, BuildConfig(ModelId));
        try
        {
            var turn = await harness.SendAndAwaitAsync("Hi! Quick question: can you code in VBA? Just answer.");

            var visible = string.Join("\n", turn.Select(t => t.Text));
            Assert.False(string.IsNullOrWhiteSpace(visible), "user saw nothing at all");

            // The complaint that started v14.11: a greeting/knowledge question must
            // be answered directly, with NO file-research tool run in between.
            Assert.DoesNotContain("EFileResearch", visible, StringComparison.OrdinalIgnoreCase);
            // The user must see an actual answer mentioning VBA or coding —
            // either term counts, but at least one must appear.
            var low = visible.ToLowerInvariant();
            Assert.True(low.Contains("vba") || low.Contains("code"),
                $"answer mentions neither VBA nor coding: {visible}");
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    [Fact]
    public async Task FileCreation_UserSeesToolWorkAndResult()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;

        var harness = await UserExperienceHarness.CreateAsync(ServerUrl!, BuildConfig(ModelId));
        try
        {
            var turn = await harness.SendAndAwaitAsync(
                "Create a file named note.txt containing the word hello. " +
                "Make EXACTLY this tool call: ECodeEditor(action=create, file=note.txt, content=hello). " +
                "Do not send anything else — just make that call now.");

            var visible = string.Join("\n", turn.Select(t => t.Text));

            // The user watches the tool actually run (status/line mentioning the editor)
            // and gets a completion — not silence, not raw internals.
            Assert.Contains("ECodeEditor", visible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"thinking\"", visible); // envelope internals must never be user-visible
            Assert.DoesNotContain("<plan>", visible);       // legacy tag format must never leak
        }
        finally
        {
            await harness.DisposeAsync();
            // ECodeEditor resolves relative paths against process CWD (known quirk)
            // — remove the created file so the testhost dir is left clean.
            try { File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "note.txt")); } catch { }
        }
    }

    [Fact]
    public async Task MultiTurn_ContextCarriesOver()
    {
        if (string.IsNullOrEmpty(ServerUrl)) return;

        var harness = await UserExperienceHarness.CreateAsync(ServerUrl!, BuildConfig(ModelId));
        try
        {
            await harness.SendAndAwaitAsync("Remember this: our project codename is TURQUOISE.");
            var second = await harness.SendAndAwaitAsync("What codename did I give you? Answer in one short sentence.");

            var visible = string.Join("\n", second.Select(t => t.Text)).ToLowerInvariant();
            Assert.Contains("turquoise", visible);
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }
}
