using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// HTTP-based inference engine. Talks to ECAssistantLLM (or any OpenAI-compatible endpoint).
/// Supports streaming (SSE) and non-streaming generation.
/// </summary>
public sealed class HttpStreamingEngine : IInferenceEngine
{
    private readonly OpenAIClient _client;
    private readonly string _defaultModelId;
    private readonly string? _defaultSessionId;

    public string Endpoint => _client.BaseUrl;

    /// <summary>
    /// Create with an existing OpenAIClient (shared with other services).
    /// </summary>
    public HttpStreamingEngine(OpenAIClient client, string defaultModelId = "main", string? defaultSessionId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _defaultModelId = defaultModelId;
        _defaultSessionId = defaultSessionId;
    }

    /// <summary>
    /// Stream tokens via SSE.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        InferenceRequestParams parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(prompt, parameters, stream: true);
        var response = await _client.PostStreamAsync("/v1/chat/completions", body, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        try
        {
            await foreach (var token in SseParser.ParseTokenStreamAsync(stream, ct))
                yield return token;
        }
        finally
        {
            response.Dispose(); // release the connection even on mid-stream cancellation
        }
    }

    /// <summary>
    /// Generate full response (non-streaming, collects all tokens).
    /// </summary>
    public async Task<string> GenerateAsync(
        string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default)
    {
        var body = BuildRequestBody(prompt, parameters, stream: false);
        var json = await _client.PostJsonAsync("/v1/chat/completions", body, ct);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                return content ?? "";
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HttpStreamingEngine] Non-critical error ignored: {ex.Message}"); }

        return "";
    }

    private string BuildRequestBody(string prompt, InferenceRequestParams parameters, bool stream)
    {
        // Multimodal: when images are attached, send OpenAI content-parts array
        // (text part + one image_url data-URI part per image).
        object content = parameters.ImageDataUris is { Count: > 0 }
            ? BuildMultimodalContent(prompt, parameters.ImageDataUris)
            : prompt;

        var req = new
        {
            model = parameters.ModelId ?? _defaultModelId,
            messages = new[]
            {
                new { role = "user", content }
            },
            stream,
            temperature = parameters.Temperature,
            top_p = parameters.TopP,
            top_k = parameters.TopK,
            max_tokens = parameters.MaxTokens,
            repeat_penalty = parameters.RepeatPenalty,
            session_id = parameters.SessionId ?? _defaultSessionId,
            stop = parameters.Stop
        };

        return JsonSerializer.Serialize(req, JsonOptions);
    }

    /// <summary>
    /// Build OpenAI multimodal content parts. Images become data-URI image_url parts.
    /// </summary>
    private static System.Text.Json.JsonElement BuildMultimodalContent(string text, IReadOnlyList<string> imageDataUris)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", text);
            writer.WriteEndObject();
            foreach (var uri in imageDataUris)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "image_url");
                writer.WriteStartObject("image_url");
                writer.WriteString("url", uri);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return System.Text.Json.JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}