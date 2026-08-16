using LLama.Common;
using LLama.Sampling;
using ECAssistant.Config;

namespace ECAssistant.Services;

/// <summary>
/// Factory for creating InferenceParams from EAgentConfig.
/// Centralizes all LLamaSharp-specific construction so callers don't need LLamaSharp references.
/// </summary>
public static class InferenceParamsFactory
{
    /// <summary>
    /// Create InferenceParams from an EAgentConfig's Inference and Sampling settings.
    /// Uses TruncateAndReprefill overflow strategy (handles context window gracefully).
    /// </summary>
    public static InferenceParams Create(EAgentConfig config)
    {
        return new InferenceParams
        {
            MaxTokens = config.Inference.MaxTokens,
            AntiPrompts = config.Inference.AntiPrompts.Length > 0
                ? config.Inference.AntiPrompts
                : new[] { "</s>" },
            OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = config.Sampling.Temperature,
                TopP = config.Sampling.TopP,
                TopK = config.Sampling.TopK,
                RepeatPenalty = config.Sampling.RepeatPenalty
            }
        };
    }

    /// <summary>
    /// Create InferenceParams with explicit values.
    /// Used by sub-agents and secondary models that may override config defaults.
    /// </summary>
    public static InferenceParams Create(
        int maxTokens,
        string[]? antiPrompts = null,
        float temperature = 0.8f,
        float topP = 0.9f,
        int topK = 40,
        float repeatPenalty = 1.1f)
    {
        return new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = antiPrompts ?? new[] { "</s>" },
            OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = temperature,
                TopP = topP,
                TopK = topK,
                RepeatPenalty = repeatPenalty
            }
        };
    }
}