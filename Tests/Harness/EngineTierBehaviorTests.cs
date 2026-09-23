using System.Reflection;
using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Interfaces;
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

    private static string InvokeBuildIncrementalInput(AgentEngine engine)
    {
        // Turn-2+ branch fires whenever TurnCount != 1; a fresh engine has 0, and a
        // tool output in the window supplies the <tooloutput> block the branch feeds.
        engine.AddToolResult("ProbeTool", "some tool output");
        var m = typeof(AgentEngine).GetMethod("BuildIncrementalInput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        return (string)m.Invoke(engine, new object[] { "continue" })!;
    }

    private static int InvokeApplyEnvelopeBudget(int configured, bool isLarge)
    {
        var m = typeof(AgentEngine).GetMethod("ApplyEnvelopeBudget",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(m);
        return (int)m.Invoke(null, new object[] { configured, isLarge })!;
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

    // ── structured envelope budget (production helper, reflection-invoked) ──

    [Fact]
    public void EnvelopeBudget_SmallTier_CapsAt1024()
    {
        Assert.Equal(1024, InvokeApplyEnvelopeBudget(8192, isLarge: false));
        Assert.Equal(768, InvokeApplyEnvelopeBudget(0, isLarge: false));
        Assert.Equal(900, InvokeApplyEnvelopeBudget(900, isLarge: false));
    }

    [Fact]
    public void EnvelopeBudget_LargeTier_CapsAt4096()
    {
        Assert.Equal(4096, InvokeApplyEnvelopeBudget(8192, isLarge: true));
        Assert.Equal(1024, InvokeApplyEnvelopeBudget(0, isLarge: true));
        Assert.Equal(2048, InvokeApplyEnvelopeBudget(2048, isLarge: true));
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

    public void Dispose()
    {
        foreach (var engine in _engines)
            try { engine.Dispose(); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }
}
