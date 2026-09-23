using ECAssistant.Core.Config;
using AppConfig = ECAssistant.Core.Config.AppConfig;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v15: tier inference values are CONFIG-DRIVEN (model_tier.inference overrides
/// global sampling for the active tier). No tier override = config as-is.
/// </summary>
public sealed class TierInferenceTunerTests
{
    private static InferenceRequestParams Params(float? temp = 0.3f, float? penalty = 1.1f) =>
        new() { Temperature = temp, RepeatPenalty = penalty, ModelId = "m", Stream = true };

    private static TierInferenceOverride Override(float? temp = null, float? penalty = null, float? topP = null, int? topK = null, int? maxTokens = null) =>
        new() { Temperature = temp, RepeatPenalty = penalty, TopP = topP, TopK = topK, MaxTokens = maxTokens };

    [Fact]
    public void Apply_TierOverride_Wins()
    {
        var p = TierInferenceTuner.Apply(Params(), isLargeTier: false, tierOverride: Override(temp: 0.15f, penalty: 1.25f));
        Assert.Equal(0.15f, p.Temperature);
        Assert.Equal(1.25f, p.RepeatPenalty);
    }

    [Fact]
    public void Apply_NoTierOverride_ParamsUnchanged()
    {
        var original = Params(temp: 0.55f);
        var p = TierInferenceTuner.Apply(original, isLargeTier: false, tierOverride: null);
        Assert.Equal(0.55f, p.Temperature);
        Assert.Same(original, p);
    }

    [Fact]
    public void Apply_LargeTier_WithOverride_StillWins()
    {
        // Tier override is tier-shaped config — applies to whichever tier declares it.
        var p = TierInferenceTuner.Apply(Params(), isLargeTier: true, tierOverride: Override(temp: 0.7f));
        Assert.Equal(0.7f, p.Temperature);
    }

    [Fact]
    public void Apply_PartialOverride_OnlySetFieldsMove()
    {
        var p = TierInferenceTuner.Apply(Params(), isLargeTier: false, tierOverride: Override(temp: 0.2f));
        Assert.Equal(0.2f, p.Temperature);
        Assert.Equal(1.1f, p.RepeatPenalty); // untouched
    }

    [Fact]
    public void Apply_MaxTokensOverride_Wins()
    {
        var p = TierInferenceTuner.Apply(Params(), isLargeTier: false, tierOverride: Override(maxTokens: 512));
        Assert.Equal(512, p.MaxTokens);
    }

    [Fact]
    public void Apply_NullParams_ReturnsNull()
    {
        Assert.Null(TierInferenceTuner.Apply(null!, isLargeTier: false, tierOverride: Override()));
    }

    [Fact]
    public void CreateTiered_Factory_WiresTierOverride()
    {
        var json = """{"model_tier": {"mode": "small", "inference": {"temperature": 0.1}}}""";
        var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json)!;
        var p = InferenceParamsFactory.Default.CreateTiered(config, isLargeTier: false);
        Assert.Equal(0.1f, p.Temperature);
    }
}
