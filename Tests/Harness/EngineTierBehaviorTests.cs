using System.Reflection;
using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.12: model-tier-adaptive behavior tests at the ENGINE level — verifies the
/// tier resolution + scaffolding gates where they live (EAgentEngine). Uses a thin
/// engine subclass with test doubles (config flows through the real ctor, like
/// MockEngine does) plus a registered typed-schema tool.
/// Config-level resolution is covered by ModelTierConfigTests; these cover the
/// wiring inside the engine.
/// </summary>
public sealed class EngineTierBehaviorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-engine-tier").FullName;
    private readonly List<EAgentEngine> _engines = new();

    private TierTestEngine CreateEngine(EAgentConfig config)
    {
        var engine = new TierTestEngine(config, _dir);
        _engines.Add(engine);
        engine.RegisterTool(new TierTestTool());
        return engine;
    }

    private static EAgentConfig BuildConfig(string? tierMode, bool isLocal)
    {
        var tierJson = tierMode == null ? "" : $", \"model_tier\": {{ \"mode\": \"{tierMode}\" }}";
        var json = $$"""
        {
          "llm_provider": { "mode": "{{(isLocal ? "local" : "remote")}}" }{{tierJson}}
        }
        """;
        return JsonSerializer.Deserialize<EAgentConfig>(json)!;
    }

    // ── IsLargeModelTier resolution (private → reflection, established pattern) ──

    private static bool InvokeIsLargeModelTier(EAgentEngine engine)
    {
        var m = typeof(EAgentEngine).GetMethod("IsLargeModelTier",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        return (bool)m.Invoke(engine, null)!;
    }

    private static string InvokeBuildIncrementalInput(EAgentEngine engine)
    {
        // Turn-2+ branch fires whenever TurnCount != 1; a fresh engine has 0, and a
        // tool output in the window supplies the <tooloutput> block the branch feeds.
        engine.AddToolResult("TierTestTool", "some tool output");
        var m = typeof(EAgentEngine).GetMethod("BuildIncrementalInput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        return (string)m.Invoke(engine, new object[] { "continue" })!;
    }

    private static int InvokeApplyEnvelopeBudget(int configured, bool isLarge)
    {
        var m = typeof(EAgentEngine).GetMethod("ApplyEnvelopeBudget",
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
        Assert.Contains("TierTestTool", names);
    }

    // ── BuildToolSpecs: ParameterSchema copy (the v14.12 fix) ──

    [Fact]
    public void BuildToolSpecs_CopiesParameterSchema()
    {
        var engine = CreateEngine(BuildConfig(null, isLocal: true));
        var m = typeof(EAgentEngine).GetMethod("BuildToolSpecs",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        var specs = (List<ToolSpec>)m.Invoke(engine, null)!;
        var spec = specs.Single(s => s.Name == "TierTestTool");
        Assert.Contains("\"action\"", spec.ParameterSchema);
    }

    public void Dispose()
    {
        foreach (var engine in _engines)
            try { engine.Dispose(); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }
}

/// <summary>
/// Thin EAgentEngine subclass that flows a specific config through the real ctor —
/// same pattern as TestSupport's MockEngine (which hard-codes config: null).
/// </summary>
internal sealed class TierTestEngine : EAgentEngine
{
    public TierTestEngine(EAgentConfig config, string workingDir)
        : base("tier-" + Guid.NewGuid().ToString("N")[..8],
            new NoopInferenceEngine(),
            new NoopKvCacheController(),
            tokenizer: null,
            inferenceParams: new InferenceRequestParams(),
            contextSize: 8192,
            modelPath: "mock",
            config: config,
            workingDir: workingDir)
    {
    }
}

/// <summary>Inference test double — no HTTP, structured path unsupported (null).</summary>
internal sealed class NoopInferenceEngine : IInferenceEngine
{
    public string Endpoint => "http://noop.test";

    public IAsyncEnumerable<string> StreamAsync(
        string prompt, InferenceRequestParams parameters, CancellationToken ct = default)
        => EmptyStream();

    public Task<string> GenerateAsync(
        string prompt, InferenceRequestParams parameters, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    private static async IAsyncEnumerable<string> EmptyStream()
    {
        await Task.CompletedTask;
        yield break;
    }
}

/// <summary>KV cache test double — all operations succeed as no-ops.</summary>
internal sealed class NoopKvCacheController : IKvCacheController
{
    public Task<bool> CreateSessionAsync(string sessionId, string? modelId = null, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<bool> DestroySessionAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<bool> PrefillAsync(string sessionId, string text, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<bool> SaveStateAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<bool> RewindAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<bool> ResetAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<KvCacheStatus?> GetStatusAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<KvCacheStatus?>(null);
}

/// <summary>
/// Minimal registered tool for tier tests — real registration path, typed schema.
/// </summary>
public sealed class TierTestTool : EToolBase
{
    public override string Name => "TierTestTool";
    public override string Description => "Tier test tool.";
    public override string UsageExample => "<toolcall><tool>TierTestTool</tool><arg_name>action</arg_name></toolcall>";

    public override string GetParameterSchema() =>
        """
        {"type":"object","required":["action"],"properties":{"action":{"type":"string","enum":["probe"]}}}
        """;

    public override Task<EToolResult> ExecuteAsync(
        Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
        => Task.FromResult(EToolResult.Success("TierTestTool", "probed"));
}