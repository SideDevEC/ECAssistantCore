using System.Text.Json;
using ECAssistant.Core.Config;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.12: model-tier profile tests — IsLargeRuntime resolution (small/large/default),
/// config wiring (model_tier section), and the Preplanning auto-resolution contract.
/// Config-driven only — no transport heuristic (isLocal removed 2026-09-24).
/// </summary>
public sealed class ModelTierConfigTests
{
    // ── IsLargeRuntime: explicit modes ──

    [Fact]
    public void IsLargeRuntime_ModeSmall_ReturnsFalse_RegardlessOfModel()
    {
        var tier = new ModelTierConfig { Mode = "small" };
        Assert.False(tier.IsLargeRuntime("llama-70b", isRemoteProvider: false));
        Assert.False(tier.IsLargeRuntime("qwen3-4b", isRemoteProvider: false));
        Assert.False(tier.IsLargeRuntime(null, isRemoteProvider: false));
    }

    [Fact]
    public void IsLargeRuntime_ModeLarge_ReturnsTrue_RegardlessOfModel()
    {
        var tier = new ModelTierConfig { Mode = "large" };
        Assert.True(tier.IsLargeRuntime("qwen3-4b", isRemoteProvider: false));
        Assert.True(tier.IsLargeRuntime(null, isRemoteProvider: false));
    }

    [Theory]
    [InlineData("LARGE")]
    [InlineData("Large")]
    [InlineData(" large ")]
    public void IsLargeRuntime_ModeCaseInsensitiveAndTrimmed_ReturnsTrue(string mode)
    {
        var tier = new ModelTierConfig { Mode = mode };
        Assert.True(tier.IsLargeRuntime("qwen3-4b", isRemoteProvider: false));
    }

    [Theory]
    [InlineData("SMALL")]
    [InlineData(" small ")]
    public void IsLargeRuntime_ModeSmallCaseInsensitiveAndTrimmed_ReturnsFalse(string mode)
    {
        var tier = new ModelTierConfig { Mode = mode };
        Assert.False(tier.IsLargeRuntime("llama-70b", isRemoteProvider: false));
    }

    // ── IsLargeRuntime: auto derives from the MODEL (Emre, 2026-09-24) ──
    // ≤14B or instruct-type → small; >14B → large; unparsable → small (safe).

    [Theory]
    [InlineData("qwen3-4b")]
    [InlineData("qwen3-4b-instruct")]
    [InlineData("Llama-3.3-14B")]
    [InlineData("ministral-8b-instruct-2410")]
    [InlineData("glm-4.5-instruct")]
    public void IsLargeRuntime_Auto_SmallModels_ReturnsFalse(string modelId)
    {
        var tier = new ModelTierConfig { Mode = "auto" };
        Assert.False(tier.IsLargeRuntime(modelId, isRemoteProvider: false));
    }

    [Theory]
    [InlineData("llama-3.3-70b")]
    [InlineData("qwen2.5-32b-instruct")]
    [InlineData("Llama-3.3-70B-Instruct")]
    public void IsLargeRuntime_Auto_LargeModels_ReturnsTrue(string modelId)
    {
        var tier = new ModelTierConfig { Mode = "auto" };
        Assert.True(tier.IsLargeRuntime(modelId, isRemoteProvider: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("gpt-4o")]
    [InlineData("claude-sonnet-4.6")]
    public void IsLargeRuntime_Auto_UnparsableLocalModelId_DefaultsSmall(string? modelId)
    {
        var tier = new ModelTierConfig { Mode = null };
        Assert.False(tier.IsLargeRuntime(modelId, isRemoteProvider: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("gpt-4o")]
    [InlineData("claude-sonnet-4.6")]
    public void IsLargeRuntime_Auto_UnparsableRemoteModelId_IsLarge(string? modelId)
    {
        var tier = new ModelTierConfig { Mode = null };
        Assert.True(tier.IsLargeRuntime(modelId, isRemoteProvider: true));
    }

    // ── Config wiring ──

    [Fact]
    public void ModelTier_ParsesFromConfig()
    {
        var config = JsonSerializer.Deserialize<AppConfig>(
            """{"model_tier": {"mode": "small"}}""");
        Assert.NotNull(config!.ModelTier);
        Assert.Equal("small", config.ModelTier!.Mode);
        Assert.False(config.ModelTier.IsLargeRuntime("llama-70b", isRemoteProvider: false)); // explicit small beats model size
    }

    [Fact]
    public void ModelTier_Absent_IsNull()
    {
        var config = JsonSerializer.Deserialize<AppConfig>("{}");
        Assert.Null(config!.ModelTier);
    }

    // v15: preplanning config removed — tier decides. Contract test:
    [Fact]
    public void Preplanning_RemovedFromConfig_SurfaceIsClean()
    {
        var config = JsonSerializer.Deserialize<AppConfig>("{}");
        Assert.DoesNotContain("Preplanning", typeof(InterfaceConfig).GetProperties().Select(p => p.Name));
        // Tier timeout contract: null = turn-limited (small), value = time-limited (large).
        Assert.Null(config!.ModelTier?.TimeoutSeconds);
    }

    [Fact]
    public void TierInferenceOverride_ParsesFromConfig()
    {
        var config = JsonSerializer.Deserialize<AppConfig>(
            """{"model_tier": {"mode": "small", "inference": {"temperature": 0.1, "repeat_penalty": 1.2}, "timeout_seconds": 600}}""");
        Assert.Equal(0.1f, config!.ModelTier!.Inference!.Temperature);
        Assert.Equal(1.2f, config.ModelTier.Inference.RepeatPenalty);
        Assert.Equal(600, config.ModelTier.TimeoutSeconds);
    }
}