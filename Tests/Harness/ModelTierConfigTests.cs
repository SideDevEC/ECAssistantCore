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
    public void IsLargeRuntime_ModeSmall_ReturnsFalse()
    {
        var tier = new ModelTierConfig { Mode = "small" };
        Assert.False(tier.IsLargeRuntime());
    }

    [Fact]
    public void IsLargeRuntime_ModeLarge_ReturnsTrue()
    {
        var tier = new ModelTierConfig { Mode = "large" };
        Assert.True(tier.IsLargeRuntime());
    }

    [Theory]
    [InlineData("LARGE")]
    [InlineData("Large")]
    [InlineData(" large ")]
    public void IsLargeRuntime_ModeCaseInsensitiveAndTrimmed_ReturnsTrue(string mode)
    {
        var tier = new ModelTierConfig { Mode = mode };
        Assert.True(tier.IsLargeRuntime());
    }

    [Theory]
    [InlineData("SMALL")]
    [InlineData(" small ")]
    public void IsLargeRuntime_ModeSmallCaseInsensitiveAndTrimmed_ReturnsFalse(string mode)
    {
        var tier = new ModelTierConfig { Mode = mode };
        Assert.False(tier.IsLargeRuntime());
    }

    // ── IsLargeRuntime: absent/default config defaults to small (safe) ──

    [Fact]
    public void IsLargeRuntime_NullMode_DefaultsToFalse()
    {
        var tier = new ModelTierConfig { Mode = null };
        Assert.False(tier.IsLargeRuntime());
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("bogus-mode")]
    public void IsLargeRuntime_UnknownMode_DefaultsToFalse(string mode)
    {
        var tier = new ModelTierConfig { Mode = mode };
        Assert.False(tier.IsLargeRuntime());
    }

    // ── Config wiring ──

    [Fact]
    public void ModelTier_ParsesFromConfig()
    {
        var config = JsonSerializer.Deserialize<AppConfig>(
            """{"model_tier": {"mode": "small"}}""");
        Assert.NotNull(config!.ModelTier);
        Assert.Equal("small", config.ModelTier!.Mode);
        Assert.False(config.ModelTier.IsLargeRuntime());
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