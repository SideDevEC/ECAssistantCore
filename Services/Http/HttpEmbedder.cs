using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// HTTP-based embedder. Calls /v1/embeddings on the server.
/// Falls back to null/empty if endpoint unavailable.
/// </summary>
public sealed class HttpEmbedder : IVectorEmbedder
{
    private readonly OpenAIClient _client;
    private readonly string _modelId;

    public HttpEmbedder(OpenAIClient client, string modelId = "embeddings")
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _modelId = modelId;
    }

    public float[] Embed(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<float>();

        try
        {
            var body = JsonSerializer.Serialize(new { model = _modelId, input = text });
            var json = _client.PostJsonAsync("/v1/embeddings", body).GetAwaiter().GetResult();

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.GetArrayLength() > 0)
            {
                var emb = data[0].GetProperty("embedding");
                var result = new float[emb.GetArrayLength()];
                for (int i = 0; i < result.Length; i++)
                    result[i] = emb[i].GetSingle();
                return result;
            }
        }
        catch
        {
            // Fallback — caller should handle empty vectors
        }

        return Array.Empty<float>();
    }
}