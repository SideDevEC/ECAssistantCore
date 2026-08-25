using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// Manages client lifecycle with ECAssistantLLM server.
/// Handles registration, heartbeat, reconnection, and disconnection.
/// </summary>
public sealed class LlmServerClient : ILlmServerClient
{
    private OpenAIClient _client;
    private readonly string _endpoint;
    private Timer? _heartbeatTimer;
    private int _activeSessions;
    private bool _disposed;
    private int _consecutiveHeartbeatFailures;
    private readonly int _maxHeartbeatFailures;
    private string _clientName = "";
    private string? _clientVersion;
    private DateTime _lastHeartbeatSuccess;
    private readonly ILogger? _logger;

    private const int DefaultMaxFailures = 3;

    /// <summary>Raised when the client reconnects after a heartbeat failure. Receives new clientId.</summary>
    public event Func<string, Task>? OnReconnected;

    /// <summary>Raised when heartbeat fails. Receives consecutive failure count.</summary>
    public event Func<int, Task>? OnHeartbeatFailed;

    public string ClientId { get; private set; } = "";
    public bool IsConnected => !string.IsNullOrEmpty(ClientId) && !_disposed;
    public DateTime LastHeartbeatSuccess => _lastHeartbeatSuccess;
    public int ConsecutiveFailures => _consecutiveHeartbeatFailures;

    public LlmServerClient(string endpoint, string? clientId = null, int maxHeartbeatFailures = DefaultMaxFailures, ILogger? logger = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        ClientId = clientId ?? "";
        _client = new OpenAIClient(_endpoint, ClientId);
        _maxHeartbeatFailures = maxHeartbeatFailures;
        _lastHeartbeatSuccess = DateTime.UtcNow;
        _logger = logger;
    }

    /// <summary>
    /// Register with the server. Stores client name for reconnection.
    /// </summary>
    public async Task<bool> ConnectAsync(string clientName, string? version = null, CancellationToken ct = default)
    {
        _clientName = clientName;
        _clientVersion = version;

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
            _client.Dispose();
            _client = new OpenAIClient(_endpoint, ClientId);
            _consecutiveHeartbeatFailures = 0;
            _lastHeartbeatSuccess = DateTime.UtcNow;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Send heartbeat. Tracks failures and triggers reconnection when threshold exceeded.
    /// </summary>
    public async Task<bool> HeartbeatAsync(int activeSessions = 0, CancellationToken ct = default)
    {
        if (!IsConnected) return false;
        _activeSessions = activeSessions;

        try
        {
            var body = JsonSerializer.Serialize(new { active_sessions = activeSessions });
            var json = await _client.PostJsonAsync($"/eca/clients/{ClientId}/heartbeat", body, ct);
            var ok = json.Contains("\"ok\":true") || json.Contains("\"ok\": true");
            if (ok)
            {
                _consecutiveHeartbeatFailures = 0;
                _lastHeartbeatSuccess = DateTime.UtcNow;
                return true;
            }
            return false;
        }
        catch
        {
            _consecutiveHeartbeatFailures++;
            OnHeartbeatFailed?.Invoke(_consecutiveHeartbeatFailures);

            if (_consecutiveHeartbeatFailures >= _maxHeartbeatFailures)
            {
                _logger?.Warn("LlmServerClient", $"Heartbeat failed {_consecutiveHeartbeatFailures}x — attempting reconnection");
                await TryReconnectAsync(ct);
            }
            return false;
        }
    }

    /// <summary>
    /// Attempt to re-register with the server after losing connection.
    /// Preserves the client name and version from the original registration.
    /// </summary>
    public async Task<bool> TryReconnectAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;

        try
        {
            _logger?.Info("LlmServerClient", $"Reconnecting to LLM server as {_clientName}...");

            _client.Dispose();
            _client = new OpenAIClient(_endpoint);

            var body = JsonSerializer.Serialize(new
            {
                client_name = _clientName,
                version = _clientVersion ?? "1.0.0"
            });
            var json = await _client.PostJsonAsync("/eca/clients", body, ct);
            var resp = JsonSerializer.Deserialize<RegisterResponse>(json, JsonOptions);

            if (resp?.ClientId != null)
            {
                ClientId = resp.ClientId;
                _client.Dispose();
                _client = new OpenAIClient(_endpoint, ClientId);
                _consecutiveHeartbeatFailures = 0;
                _lastHeartbeatSuccess = DateTime.UtcNow;
                _logger?.Info("LlmServerClient", $"Reconnected as client {ClientId}");
                OnReconnected?.Invoke(ClientId);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("LlmServerClient", $"Reconnection failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Disconnect from server.
    /// </summary>
    public async Task<bool> DisconnectAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return false;

        _heartbeatTimer?.Dispose();
        try
        {
            var json = await _client.DeleteJsonAsync($"/eca/clients/{ClientId}", ct);
            ClientId = "";
            return json.Contains("\"ok\":true") || json.Contains("\"ok\": true");
        }
        catch
        {
            ClientId = "";
            return false;
        }
    }

    /// <summary>
    /// Start automatic heartbeat timer with failure tracking and auto-reconnection.
    /// </summary>
    public void StartHeartbeat(int intervalSec, Func<int> getActiveSessions)
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new Timer(async _ =>
        {
            try { await HeartbeatAsync(getActiveSessions()); }
            catch { /* best effort — HeartbeatAsync handles failures internally */ }
        }, null, TimeSpan.FromSeconds(intervalSec), TimeSpan.FromSeconds(intervalSec));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class RegisterResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("client_id")]
        public string? ClientId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("server_version")]
        public string? ServerVersion { get; set; }
    }

    /// <summary>
    /// Stop the heartbeat timer (e.g. before idle disconnect).
    /// </summary>
    public void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
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