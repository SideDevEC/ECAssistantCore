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

    public RemoteKvCacheController(OpenAIClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<bool> CreateSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { session_id = sessionId });
        var json = await _client.PostJsonAsync("/eca/sessions", body, ct);
        return json.Contains("\"session_id\"");
    }

    public async Task<bool> DestroySessionAsync(string sessionId, CancellationToken ct = default)
    {
        var json = await _client.DeleteJsonAsync($"/eca/sessions/{sessionId}", ct);
        return json.Contains("\"ok\":true") || json.Contains("\"ok\": true");
    }

    public async Task<bool> PrefillAsync(string sessionId, string text, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { text });
        var json = await _client.PostJsonAsync($"/eca/sessions/{sessionId}/prefill", body, ct);
        return json.Contains("\"prefilled\":true") || json.Contains("\"prefilled\": true");
    }

    public async Task<bool> SaveStateAsync(string sessionId, CancellationToken ct = default)
    {
        var json = await _client.PostJsonAsync($"/eca/sessions/{sessionId}/save-state", "{}", ct);
        return json.Contains("\"saved\":true") || json.Contains("\"saved\": true");
    }

    public async Task<bool> RewindAsync(string sessionId, CancellationToken ct = default)
    {
        var json = await _client.PostJsonAsync($"/eca/sessions/{sessionId}/rewind", "{}", ct);
        return json.Contains("\"rewound\":true") || json.Contains("\"rewound\": true");
    }

    public async Task<bool> ResetAsync(string sessionId, CancellationToken ct = default)
    {
        var json = await _client.PostJsonAsync($"/eca/sessions/{sessionId}/reset", "{}", ct);
        return json.Contains("\"ok\":true") || json.Contains("\"ok\": true");
    }

    public async Task<KvCacheStatus?> GetStatusAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var json = await _client.GetJsonAsync($"/eca/sessions/{sessionId}/status", ct);
            return JsonSerializer.Deserialize<KvCacheStatus>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
}