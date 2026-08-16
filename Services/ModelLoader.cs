using System;
using LLama;
using LLama.Common;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// Concrete GGUF model loader.
/// </summary>
public class ModelLoader : IModelLoader
{
    private LLamaWeights? _weights;

    public LLamaWeights LoadWeights(string path, ModelParams parameters)
    {
        _weights ??= LLamaWeights.LoadFromFile(parameters);

        return _weights;
    }

    public void Dispose()
    {
        _weights?.Dispose();
        _weights = null;
    }
}