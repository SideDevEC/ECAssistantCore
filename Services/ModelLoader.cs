using System;
using LLama;
using LLama.Common;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Services;

/// <summary>
/// Concrete GGUF model loader.
/// </summary>
public class ModelLoader : IModelLoader
{
    private LLamaWeights? _weights;
    private readonly ILogger? _logger;

    public ModelLoader(ILogger? logger = null)
    {
        _logger = logger;
    }

    public LLamaWeights LoadWeights(string path, ModelParams parameters)
    {
        if (_weights != null) return _weights;

        // Pre-flight validation
        var validator = new ModelParamValidator(_logger);
        var validationError = validator.Validate(
            path,
            parameters.GpuLayerCount,
            parameters.ContextSize ?? 4096,
            parameters.Threads ?? -1);
        if (validationError != null)
        {
            _logger?.Error("ModelLoader", validationError.Message);
            throw validationError;
        }

        try
        {
            _weights = LLamaWeights.LoadFromFile(parameters);
        }
        catch (Exception ex) when (ex is not ModelLoadException)
        {
            var mle = new ModelLoadException(
                ModelLoadPhase.LoadWeights, path,
                parameters.GpuLayerCount,
                parameters.ContextSize ?? 4096,
                $"Failed to load model weights: {ex.GetType().Name}: {ex.Message}",
                inner: ex);
            _logger?.Error("ModelLoader", mle.ToDiagnosticString());
            throw mle;
        }

        return _weights;
    }

    public void Dispose()
    {
        _weights?.Dispose();
        _weights = null;
    }
}