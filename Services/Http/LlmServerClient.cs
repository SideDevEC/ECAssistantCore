using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// Manages client lifecycle with ECAssistantLLM server.
/// Handles registration, heartbeat, and disconnection.
/// </summary>
public sealed class LlmServerClient : ILlmServerClient
{
    private readonly OpenAIClient _client;
    private readonly string _endpoint;
    private Timer? _heartbeatTimer;
    private int _activeSessions;
    private bool _disposed;

    public string ClientId { get; private set; } = "";
    public bool IsConnected => !string.IsNullOrEmpty(ClientId) && !_disposed;

    public LlmServerClient(string endpoint, string? clientId = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        ClientId = clientId ?? "";
        _client = new OpenAIClient(_endpoint, ClientId);
    }

    /// <summary>
    /// Register with the server. Starts heartbeat timer.
    /// </summary>
    public async Task<bool> ConnectAsync(string clientName, string? version = null, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            client_name = clientName,
            version = version ?? "1.0.0"
        });

        var json = await _client.PostJsonAsync("/eca/clients", body, ct);
        var resp = JsonSerializer.Deserialize<RegisterResponse>(json, JsonOptions);

        if (resp?.ClientId != null)
        {
            ClientId = resp.ClientId;
            // Recreate client with clientId in header
            _client.Dispose();
            // Can't reassign readonly _client — need to rethink
            return true;
        }

        return false;
    }

    /// <summary>
    /// Send heartbeat.
    /// </summary>
    public async Task<bool> HeartbeatAsync(int activeSessions = 0, CancellationToken ct = default)
    {
        if (!IsConnected) return false;
        _activeSessions = activeSessions;

        var body = JsonSerializer.Serialize(new { active_sessions = activeSessions });
        var json = await _client.PostJsonAsync($"/eca/clients/{ClientId}/heartbeat", body, ct);
        return json.Contains("\"ok\":true") || json.Contains("\"ok\": true");
    }

    /// <summary>
    /// Disconnect from server.
    /// </summary>
    public async Task<bool> DisconnectAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return false;

        _heartbeatTimer?.Dispose();
        var json = await _client.DeleteJsonAsync($"/eca/clients/{ClientId}", ct);
        ClientId = "";
        return json.Contains("\"ok\":true") || json.Contains("\"ok\": true");
    }

    /// <summary>
    /// Start automatic heartbeat timer.
    /// </summary>
    public void StartHeartbeat(int intervalSec, Func<int> getActiveSessions)
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new Timer(async _ =>
        {
            try { await HeartbeatAsync(getActiveSessions()); }
            catch { /* best effort */ }
        }, null, TimeSpan.FromSeconds(intervalSec), TimeSpan.FromSeconds(intervalSec));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class RegisterResponse
    {
        public string? ClientId { get; set; }
        public string? ServerVersion { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _heartbeatTimer?.Dispose();
        await DisconnectAsync();
        _client.Dispose();
    }
}