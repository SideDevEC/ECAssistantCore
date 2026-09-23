using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Interface for validating model parameters before loading.
/// </summary>
public interface IModelParamValidator
{
    ModelLoadException? Validate(string modelPath, uint contextSize);
    ModelLoadException? Validate(AppConfig config, string resolvedModelPath);
}