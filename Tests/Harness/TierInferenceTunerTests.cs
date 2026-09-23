using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.17: tier-aware inference tuning — pure logic. Small tier tightens DEFAULT
/// sampling only; explicit user customization always wins; large tier untouched.
/// </summary>
public sealed class TierInferenceTunerTests
{
    private static InferenceRequestParams Params(float? temp = 0.3f, float? penalty = 1.1f) =>
        new() { Temperature = temp, RepeatPenalty = penalty, ModelId = "m", Stream = true };

    [Fact]
    public void Apply_SmallTierDefaultSampling_Tightens()
    {
        var p = TierInferenceTuner.Apply(Params(), isLargeTier: false);
        Assert.Equal(TierInferenceTuner.SmallTierTemperature, p.Temperature);
        Assert.Equal(TierInferenceTuner.SmallTierRepeatPenalty, p.RepeatPenalty);
    }

    [Fact]
    public void Apply_SmallTierCustomSampling_Wins()
    {
        var p = TierInferenceTuner.Apply(Params(temp: 0.7f, penalty: 1.3f), isLargeTier: false);
        Assert.Equal(0.7f, p.Temperature);
        Assert.Equal(1.3f, p.RepeatPenalty);
    }

    [Fact]
    public void Apply_LargeTier_Unchanged()
    {
        var original = Params();
        var p = TierInferenceTuner.Apply(original, isLargeTier: true);
        Assert.Equal(0.3f, p.Temperature);
        Assert.Equal(1.1f, p.RepeatPenalty);
        Assert.Same(original, p);
    }

    [Fact]
    public void Apply_NullParams_ReturnsNull()
    {
        Assert.Null(TierInferenceTuner.Apply(null!, isLargeTier: false));
    }

    [Fact]
    public void Apply_SmallTierMixed_DefaultTempCustomPenalty_OnlyTempMoves()
    {
        var p = TierInferenceTuner.Apply(Params(temp: 0.3f, penalty: 1.25f), isLargeTier: false);
        Assert.Equal(TierInferenceTuner.SmallTierTemperature, p.Temperature);
        Assert.Equal(1.25f, p.RepeatPenalty);
    }

    [Fact]
    public void CreateTiered_Factory_SmallTierWireUp()
    {
        var config = new EAgentConfig();
        var p = InferenceParamsFactory.Default.CreateTiered(config, isLargeTier: false);
        Assert.Equal(TierInferenceTuner.SmallTierTemperature, p.Temperature);
    }

    [Fact]
    public void CreateTiered_Factory_LargeTierWireUp()
    {
        var config = new EAgentConfig();
        var p = InferenceParamsFactory.Default.CreateTiered(config, isLargeTier: true);
        Assert.Equal(config.Sampling.Temperature, p.Temperature);
    }
}
