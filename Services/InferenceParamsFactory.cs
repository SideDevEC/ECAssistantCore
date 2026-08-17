using LLama.Common;
using LLama.Sampling;
using ECAssistant.Core.Config;

namespace ECAssistant.Core.Services;

/// <summary>
/// Factory for creating InferenceParams from EAgentConfig.
/// Centralizes all LLamaSharp-specific construction so callers don't need LLamaSharp references.
/// Instance class (not static) for OOP compliance. Use Default or inject your own.
/// </summary>
public class InferenceParamsFactory
{
    /// <summary>Default shared instance for convenience.</summary>
    public static readonly InferenceParamsFactory Default = new();

    /// <summary>
    /// Create InferenceParams from an EAgentConfig's Inference and Sampling settings.
    /// Uses TruncateAndReprefill overflow strategy (handles context window gracefully).
    /// </summary>
    public InferenceParams Create(EAgentConfig config)
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
    public InferenceParams Create(
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