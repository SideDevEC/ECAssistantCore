using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// HTTP-based model loader. Calls /eca/models endpoints on the server.
/// </summary>
public sealed class RemoteModelLoader : IModelLoader
{
    private readonly OpenAIClient _client;

    public RemoteModelLoader(OpenAIClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<RemoteModelInfo>> GetLoadedModelsAsync(CancellationToken ct = default)
    {
        var json = await _client.GetJsonAsync("/eca/models", ct);
        var models = JsonSerializer.Deserialize<RemoteModelsResponse>(json, JsonOptions);
        return models?.Models ?? new List<RemoteModelInfo>();
    }

    public async Task<bool> LoadModelAsync(string modelId, string path, RemoteModelLoadOptions options, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            id = modelId,
            path,
            gpu_layers = options.GpuLayers,
            context_size = options.ContextSize,
            threads = options.Threads,
            is_embedding = options.IsEmbedding
        });
        var json = await _client.PostJsonAsync("/eca/models/load", body, ct);
        return json.Contains("\"ok\":true") || json.Contains("\"ok\": true");
    }

    public async Task<bool> UnloadModelAsync(string modelId, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { id = modelId });
        var json = await _client.PostJsonAsync("/eca/models/unload", body, ct);
        return json.Contains("\"ok\":true") || json.Contains("\"ok\": true");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class RemoteModelsResponse
    {
        public List<RemoteModelInfo> Models { get; set; } = new();
    }
}