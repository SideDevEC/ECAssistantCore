using System.Text.Json;
using ECAssistant.Core.Config;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.12: model-tier profile tests — IsLargeRuntime resolution (small/large/auto),
/// config wiring (model_tier section), and the Preplanning auto-resolution contract.
/// </summary>
public sealed class ModelTierConfigTests
{
    // ── IsLargeRuntime: explicit modes ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsLargeRuntime_ModeSmall_ReturnsFalse(bool isLocal)
    {
        var tier = new ModelTierConfig { Mode = "small" };
        Assert.False(tier.IsLargeRuntime(isLocal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsLargeRuntime_ModeLarge_ReturnsTrue(bool isLocal)
    {
        var tier = new ModelTierConfig { Mode = "large" };
        Assert.True(tier.IsLargeRuntime(isLocal));
    }

    [Theory]
    [InlineData("LARGE")]
    [InlineData("Large")]
    [InlineData(" large ")]
    public void IsLargeRuntime_ModeCaseInsensitiveAndTrimmed_ReturnsTrue(string mode)
    {
        var tier = new ModelTierConfig { Mode = mode };
        Assert.True(tier.IsLargeRuntime(isLocal: true));
        Assert.True(tier.IsLargeRuntime(isLocal: false));
    }

    [Theory]
    [InlineData("SMALL")]
    [InlineData(" small ")]
    public void IsLargeRuntime_ModeSmallCaseInsensitiveAndTrimmed_ReturnsFalse(string mode)
    {
        var tier = new ModelTierConfig { Mode = mode };
        Assert.False(tier.IsLargeRuntime(isLocal: false));
    }

    // ── IsLargeRuntime: auto (null/empty/unknown mode) resolves from local/remote ──

    [Fact]
    public void IsLargeRuntime_NullMode_Local_ReturnsFalse()
    {
        var tier = new ModelTierConfig { Mode = null };
        Assert.False(tier.IsLargeRuntime(isLocal: true));
    }

    [Fact]
    public void IsLargeRuntime_NullMode_Remote_ReturnsTrue()
    {
        var tier = new ModelTierConfig { Mode = null };
        Assert.True(tier.IsLargeRuntime(isLocal: false));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("bogus-mode")]
    public void IsLargeRuntime_UnknownMode_AutoResolvesFromLocality(string mode)
    {
        var tier = new ModelTierConfig { Mode = mode };
        Assert.False(tier.IsLargeRuntime(isLocal: true));   // auto: local → small
        Assert.True(tier.IsLargeRuntime(isLocal: false));   // auto: remote → large
    }

    // ── Config wiring ──

    [Fact]
    public void ModelTier_ParsesFromConfig()
    {
        var config = JsonSerializer.Deserialize<AppConfig>(
            """{"model_tier": {"mode": "small"}}""");
        Assert.NotNull(config!.ModelTier);
        Assert.Equal("small", config.ModelTier!.Mode);
        Assert.False(config.ModelTier.IsLargeRuntime(isLocal: false)); // explicit small beats remote
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
