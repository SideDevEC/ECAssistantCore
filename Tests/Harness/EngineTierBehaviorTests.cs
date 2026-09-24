using System.Reflection;
using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Services;
using ECAssistant.Core.Tools;
using ECAssistant.TestSupport;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.12: model-tier-adaptive behavior tests at the ENGINE level — verifies the
/// tier resolution + scaffolding gates where they live (AgentEngine). Uses a thin
/// engine subclass with test doubles (config flows through the real ctor, like
/// MockEngine does) plus a registered typed-schema tool.
/// Config-level resolution is covered by ModelTierConfigTests; these cover the
/// wiring inside the engine.
/// </summary>
public sealed class EngineTierBehaviorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-engine-tier").FullName;
    private readonly List<AgentEngine> _engines = new();

    private MockEngine CreateEngine(AppConfig config)
    {
        var engine = new MockEngine(workingDir: _dir, config: config);
        _engines.Add(engine);
        engine.RegisterTool(new ProbeTestTool());
        return engine;
    }

    private static AppConfig BuildConfig(string? tierMode, bool isLocal)
    {
        var tierJson = tierMode == null ? "" : $", \"model_tier\": {{ \"mode\": \"{tierMode}\" }}";
        var json = $$"""
        {
          "llm_provider": { "mode": "{{(isLocal ? "local" : "remote")}}" }{{tierJson}}
        }
        """;
        return JsonSerializer.Deserialize<AppConfig>(json)!;
    }

    // ── IsLargeModelTier resolution (private → reflection, established pattern) ──

    private static bool InvokeIsLargeModelTier(AgentEngine engine)
    {
        var m = typeof(AgentEngine).GetMethod("IsLargeModelTier",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        return (bool)m.Invoke(engine, null)!;
    }

    // ── tier resolution ──

    [Fact]
    public void Tier_DefaultLocal_ResolvesSmall()
    {
        var engine = CreateEngine(BuildConfig(null, isLocal: true));
        Assert.False(InvokeIsLargeModelTier(engine));
    }

    [Fact]
    public void Tier_ExplicitLargeOnLocal_ResolvesLarge()
    {
        var engine = CreateEngine(BuildConfig("large", isLocal: true));
        Assert.True(InvokeIsLargeModelTier(engine));
    }

    [Fact]
    public void Tier_AutoRemote_ResolvesLarge()
    {
        var engine = CreateEngine(BuildConfig(null, isLocal: false));
        Assert.True(InvokeIsLargeModelTier(engine));
    }

    [Fact]
    public void Tier_ExplicitSmallOnRemote_ResolvesSmall()
    {
        var engine = CreateEngine(BuildConfig("small", isLocal: false));
        Assert.False(InvokeIsLargeModelTier(engine));
    }

    // ── turn-2+ incremental input: small keeps nag lines, large gets one-liner ──

    [Fact]
    public void IncrementalInput_SmallTier_KeepsRepeatGuardNag()
    {
        var engine = CreateEngine(BuildConfig("small", isLocal: true));
        var input = InvokeBuildIncrementalInput(engine);
        Assert.Contains("Do NOT repeat", input);
        Assert.Contains("<tooloutput>", input);
    }

    [Fact]
    public void IncrementalInput_LargeTier_OneLinerNoNag()
    {
        var engine = CreateEngine(BuildConfig("large", isLocal: true));
        var input = InvokeBuildIncrementalInput(engine);
        Assert.Contains("final answer now", input);
        Assert.DoesNotContain("Do NOT repeat", input);
        Assert.Contains("<tooloutput>", input);
    }

    // ── v15 anti-parrot directive (Emre, 2026-09-24) — small tier only ──

    [Fact]
    public void IncrementalInput_SmallTier_InjectsAntiParrotDirective()
    {
        var engine = CreateEngine(BuildConfig("small", isLocal: true));
        var input = InvokeBuildIncrementalInput(engine);
        Assert.Contains("Never reuse the wording of your earlier replies", input);
        Assert.Contains("write something NEW", input);
    }

    [Fact]
    public void IncrementalInput_LargeTier_NoAntiParrotDirective()
    {
        var engine = CreateEngine(BuildConfig("large", isLocal: true));
        var input = InvokeBuildIncrementalInput(engine);
        Assert.DoesNotContain("reuse the wording of your earlier replies", input);
    }

    [Fact]
    public void Turn1Input_SmallTier_InjectsAntiParrotDirective_OnlyWhenHistoryExists()
    {
        var engine = CreateEngine(BuildConfig("small", isLocal: true));

        // Turn-1 branch fires when TurnCount == 1; a fresh engine has 0.
        SetTurnCount(engine, 1);

        // No assistant history yet → no directive (fresh conversation).
        var fresh = InvokeBuildIncrementalInput(engine, turn1: true);
        Assert.DoesNotContain("reuse the wording of your earlier replies", fresh);

        // Assistant history present (multi-request session) → directive injected.
        engine.ContextWindow.AddAssistantMessage("The waves crashed against the shore, ancient and unyielding.");
        var withHistory = InvokeBuildIncrementalInput(engine, turn1: true);
        Assert.Contains("reuse the wording of your earlier replies", withHistory);
    }

    [Fact]
    public void Turn1Input_LargeTier_NeverInjectsAntiParrotDirective()
    {
        var engine = CreateEngine(BuildConfig("large", isLocal: true));
        SetTurnCount(engine, 1);
        engine.ContextWindow.AddAssistantMessage("Earlier assistant content that could be repeated.");
        var input = InvokeBuildIncrementalInput(engine, turn1: true);
        Assert.DoesNotContain("reuse the wording of your earlier replies", input);
    }

    private static void SetTurnCount(AgentEngine engine, int target)
    {
        var lifecycle = typeof(AgentEngine).GetField("_lifecycle",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lifecycle);
        var state = lifecycle!.GetValue(engine)!;
        var current = (int)state.GetType().GetProperty("TurnCount")!.GetValue(state)!;
        var increment = state.GetType().GetMethod("IncrementTurn")!;
        for (var i = current; i < target; i++) increment.Invoke(state, null);
    }

    private static string InvokeBuildIncrementalInput(AgentEngine engine, bool turn1 = false)
    {
        if (turn1)
        {
            // Turn-1 branch: no tool output seeded — pure conversational input.
            var m1 = typeof(AgentEngine).GetMethod("BuildIncrementalInput",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(m1);
            return (string)m1.Invoke(engine, new object[] { "Tell me a story about the sea." })!;
        }

        // Turn-2+ branch fires whenever TurnCount != 1; a fresh engine has 0, and a
        // tool output in the window supplies the <tooloutput> block the branch feeds.
        engine.AddToolResult("ProbeTool", "some tool output");
        var m = typeof(AgentEngine).GetMethod("BuildIncrementalInput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        return (string)m.Invoke(engine, new object[] { "continue" })!;
    }


    // ── registered tools: what the grammar union allows (tool_names plumbing) ──

    [Fact]
    public void RegisteredToolNames_AreWhatTheEngineWouldSend()
    {
        var engine = CreateEngine(BuildConfig(null, isLocal: true));
        var names = engine.Tools.Select(t => t.Name).ToList();
        Assert.Contains("ProbeTool", names);
    }

    // ── BuildToolSpecs: ParameterSchema copy (the v14.12 fix) ──

    [Fact]
    public void BuildToolSpecs_CopiesParameterSchema()
    {
        var engine = CreateEngine(BuildConfig(null, isLocal: true));
        var m = typeof(AgentEngine).GetMethod("BuildToolSpecs",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        var specs = (List<ToolSpec>)m.Invoke(engine, null)!;
        var spec = specs.Single(s => s.Name == "ProbeTool");
        Assert.Contains("\"action\"", spec.ParameterSchema);
    }

    // ── v15: tier operating profiles (system prompt overlays) ──

    private static string InvokeBuildSystemToolsPrompt(AgentEngine engine)
    {
        var m = typeof(AgentEngine).GetMethod("BuildSystemToolsPrompt",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        return (string)m.Invoke(engine, null)!;
    }

    [Fact]
    public void SystemPrompt_SmallTier_NoOverlayInEngine()
    {
        // Small tier's rules live in the dedicated SystemPrompt.*.Small.md file —
        // the engine must NOT append a duplicate overlay.
        var engine = CreateEngine(BuildConfig("small", isLocal: true));
        var prompt = InvokeBuildSystemToolsPrompt(engine);
        Assert.DoesNotContain("OPERATING RULES (small model", prompt);
        Assert.DoesNotContain("senior autonomous engineer-agent", prompt);
    }

    [Fact]
    public void SystemPrompt_SmallFiles_EmbeddedAndConflictFree()
    {
        foreach (var name in new[] { "SystemPrompt.Mac.Small.md", "SystemPrompt.Windows.Small.md", "SystemPrompt.Linux.Small.md" })
        {
            var text = ECAssistant.Core.Services.ResourceLoader.Default.LoadTextWithFallback(name, null);
            Assert.False(string.IsNullOrEmpty(text), $"{name} not embedded");
            // Multi-call allowance must NOT appear in small prompts (zero conflict).
            Assert.DoesNotContain("MULTIPLE tool calls", text);
            // The override-precedence hack is gone — no conflicts to resolve.
            Assert.DoesNotContain("overrides any earlier", text);
            // Format contract + precision rules present.
            Assert.Contains("ONE tool call per turn", text);
            Assert.Contains("NEVER put JSON inside `answer`", text);
        }
    }

    [Fact]
    public void SystemPrompt_LargeTier_BaseTone_AutonomousNoOverlay()
    {
        // Base prompt is retuned FOR large models; no overlay appended anymore.
        var engine = CreateEngine(BuildConfig("large", isLocal: true));
        var prompt = InvokeBuildSystemToolsPrompt(engine);
        Assert.Contains("autonomous local engineer-agent", prompt);
        Assert.Contains("MULTIPLE tool calls", prompt);
        Assert.DoesNotContain("OPERATING PROFILE (autonomous)", prompt); // overlay gone
    }

    // ── v15: reasoning effort config → request params ──

    [Fact]
    public void ReasoningEffort_Config_FlowsToParams()
    {
        var json = """
        {
          "llm_provider": { "mode": "remote", "reasoning_effort": "medium" }
        }
        """;
        var config = JsonSerializer.Deserialize<AppConfig>(json)!;
        var p = InferenceParamsFactory.Default.Create(config);
        Assert.Equal("medium", p.ReasoningEffort);
    }

    [Fact]
    public void ReasoningEffort_Default_Null()
    {
        var p = InferenceParamsFactory.Default.Create(new AppConfig());
        Assert.Null(p.ReasoningEffort);
    }

    [Fact]
    public void SystemPrompt_SmallFiles_HaveConcreteEnvelopeExamples()
    {
        // Envelope examples now live in the dedicated small prompt files.
        var text = ECAssistant.Core.Services.ResourceLoader.Default.LoadTextWithFallback("SystemPrompt.Mac.Small.md", null);
        Assert.False(string.IsNullOrEmpty(text));
        Assert.Contains("NEVER put JSON inside `answer`", text);
        Assert.Contains("\"toolcalls\": [{\"name\": \"EShellAgent\", \"args\"", text);
    }

    [Fact]
    public void SystemPrompt_MultiCall_InBaseBothTiers_SmallOverrides()
    {
        var small = InvokeBuildSystemToolsPrompt(CreateEngine(BuildConfig("small", isLocal: true)));
        var large = InvokeBuildSystemToolsPrompt(CreateEngine(BuildConfig("large", isLocal: true)));
        // Base prompt (large-toned) carries the multi-call line;
        // the small prompt file forbids multi-call — zero shared text.
        Assert.Contains("MULTIPLE tool calls", large);
    }

    public void Dispose()
    {
        foreach (var engine in _engines)
            try { engine.Dispose(); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }
}
