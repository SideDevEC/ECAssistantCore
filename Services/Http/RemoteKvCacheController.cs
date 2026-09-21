using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// HTTP-based KV cache controller. Manages server-side sessions (prefill, rewind, save, reset)
/// via ECAssistant extension endpoints.
/// </summary>
public sealed class RemoteKvCacheController : IKvCacheController
{
    private readonly OpenAIClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RemoteKvCacheController(OpenAIClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>Session IDs can contain characters that are illegal in URL paths — escape them.</summary>
    private static string Enc(string sessionId) => Uri.EscapeDataString(sessionId);

    public async Task<bool> CreateSessionAsync(string sessionId, string? modelId = null, CancellationToken ct = default)
    {
        // model_id is required for process-backend models — without it the server
        // resolves the default model itself, which is fine, but sending it keeps the
        // session bound to the configured chat model explicitly.
        var body = modelId != null
            ? JsonSerializer.Serialize(new { session_id = sessionId, model_id = modelId })
            : JsonSerializer.Serialize(new { session_id = sessionId });
        var json = await _client.PostJsonAsync("/eca/sessions", body, ct);
        // Proper JSON parsing instead of substring matching — substring checks can
        // match fields we don't care about (e.g. an error body mentioning the field).
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("session_id", out var sid)
                && sid.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(sid.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<bool> DestroySessionAsync(string sessionId, CancellationToken ct = default)
    {
        var json = await _client.DeleteJsonAsync($"/eca/sessions/{Enc(sessionId)}", ct);
        return IsOk(json);
    }

    public async Task<bool> PrefillAsync(string sessionId, string text, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { text });
        var json = await _client.PostJsonAsync($"/eca/sessions/{Enc(sessionId)}/prefill", body, ct);
        return IsFlagTrue(json, "prefilled");
    }

    public async Task<bool> SaveStateAsync(string sessionId, CancellationToken ct = default)
    {
        var json = await _client.PostJsonAsync($"/eca/sessions/{Enc(sessionId)}/save-state", "{}", ct);
        return IsFlagTrue(json, "saved");
    }

    public async Task<bool> RewindAsync(string sessionId, CancellationToken ct = default)
    {
        var json = await _client.PostJsonAsync($"/eca/sessions/{Enc(sessionId)}/rewind", "{}", ct);
        return IsFlagTrue(json, "rewound");
    }

    public async Task<bool> ResetAsync(string sessionId, CancellationToken ct = default)
    {
        var json = await _client.PostJsonAsync($"/eca/sessions/{Enc(sessionId)}/reset", "{}", ct);
        return IsOk(json);
    }

    public async Task<KvCacheStatus?> GetStatusAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var json = await _client.GetJsonAsync($"/eca/sessions/{Enc(sessionId)}/status", ct);
            return JsonSerializer.Deserialize<KvCacheStatus>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parse the response body and check the boolean "ok" property (no substring matching).</summary>
    private static bool IsOk(string json)
        => IsFlagTrue(json, "ok");

    private static bool IsFlagTrue(string json, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out var flag)
                && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                && flag.GetBoolean();
        }
        catch (JsonException)
        {
            return false;
        }
    }
}