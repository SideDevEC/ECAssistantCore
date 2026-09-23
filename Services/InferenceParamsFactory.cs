using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// Factory for creating InferenceRequestParams from AppConfig.
/// Instance class (not static) for OOP compliance.
/// </summary>
public class InferenceParamsFactory
{
    /// <summary>Default shared instance for convenience.</summary>
    public static readonly InferenceParamsFactory Default = new();

    /// <summary>
    /// Create InferenceRequestParams from an AppConfig's Inference and Sampling settings.
    /// </summary>
    public InferenceRequestParams Create(AppConfig config)
    {
        return new InferenceRequestParams
        {
            MaxTokens = config.Inference.MaxTokens,
            Temperature = config.Sampling.Temperature,
            TopP = config.Sampling.TopP,
            TopK = config.Sampling.TopK,
            RepeatPenalty = config.Sampling.RepeatPenalty,
            Stop = config.Inference.AntiPrompts,
            ModelId = config.LlmProvider.ModelId,
            Stream = true
        };
    }

    /// <summary>
    /// v14.17: create params from config, then apply the tier profile (small tier
    /// gets tighter sampling; large tier and customized sampling unchanged).
    /// </summary>
    public InferenceRequestParams CreateTiered(AppConfig config, bool isLargeTier)
    {
        return TierInferenceTuner.Apply(Create(config), isLargeTier, config?.Sampling);
    }

    /// <summary>
    /// Create InferenceRequestParams with explicit values.
    /// Used by sub-agents and secondary tasks that may override config defaults.
    /// </summary>
    public InferenceRequestParams Create(
        int maxTokens,
        string[]? stop = null,
        float temperature = 0.8f,
        float topP = 0.9f,
        int topK = 40,
        float repeatPenalty = 1.1f)
    {
        return new InferenceRequestParams
        {
            MaxTokens = maxTokens,
            Temperature = temperature,
            TopP = topP,
            TopK = topK,
            RepeatPenalty = repeatPenalty,
            Stop = stop ?? new[] { "</s>" },
            Stream = true
        };
    }
}