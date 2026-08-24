using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Remote model loading via HTTP. Replaces the old LLamaSharp-based IModelLoader.
/// </summary>
public interface IModelLoader
{
    Task<IReadOnlyList<RemoteModelInfo>> GetLoadedModelsAsync(CancellationToken ct = default);
    Task<bool> LoadModelAsync(string modelId, string path, RemoteModelLoadOptions options, CancellationToken ct = default);
    Task<bool> UnloadModelAsync(string modelId, CancellationToken ct = default);
}

/// <summary>Model info from the server.</summary>
public sealed class RemoteModelInfo
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsLoaded { get; set; }
    public bool IsEmbedding { get; set; }
    public int GpuLayers { get; set; }
    public uint ContextSize { get; set; }
    public int EmbeddingDim { get; set; }
}

/// <summary>Options for loading a model at runtime.</summary>
public sealed class RemoteModelLoadOptions
{
    public int GpuLayers { get; set; } = 0;
    public uint ContextSize { get; set; } = 4096;
    public int Threads { get; set; } = -1;
    public bool IsEmbedding { get; set; } = false;
}