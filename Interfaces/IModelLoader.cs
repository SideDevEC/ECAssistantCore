using LLama;
using LLama.Common;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Abstracts GGUF model loading.
/// </summary>
public interface IModelLoader
{
    LLamaWeights LoadWeights(string path, ModelParams parameters);
    void Dispose();
}