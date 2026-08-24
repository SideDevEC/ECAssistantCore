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
    private readonly bool _ownsClient;

    public string Endpoint => _client.BaseUrl;

    /// <summary>
    /// Create with an existing OpenAIClient (shared with other services).
    /// </summary>
    public HttpStreamingEngine(OpenAIClient client, string defaultModelId = "main", string? defaultSessionId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _defaultModelId = defaultModelId;
        _defaultSessionId = defaultSessionId;
        _ownsClient = false;
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
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        await foreach (var token in SseParser.ParseTokenStreamAsync(stream, ct))
            yield return token;
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
        catch { }

        return "";
    }

    private string BuildRequestBody(string prompt, InferenceRequestParams parameters, bool stream)
    {
        var req = new
        {
            model = parameters.ModelId ?? _defaultModelId,
            messages = new[]
            {
                new { role = "user", content = prompt }
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}