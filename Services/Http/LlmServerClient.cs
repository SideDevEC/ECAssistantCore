using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// Manages client lifecycle with ECAssistantLLM server.
/// Handles registration, reconnection, and disconnection. Heartbeat removed
/// (2026-09-23): the server never evicts idle clients anymore.
/// </summary>
public sealed class LlmServerClient : ILlmServerClient
{
    private OpenAIClient _client;
    private readonly string _endpoint;
    private bool _disposed;
    // In-flight HTTP guard: when the client is swapped/disposed, wait for in-flight
    // calls to drain instead of pulling the HttpClient out from under them.
    private int _inFlightCalls;
    private DateTime _lastReconnectAttemptUtc = DateTime.MinValue;
    private static readonly TimeSpan ReconnectCooldown = TimeSpan.FromSeconds(30);
    private string _clientName = "";
    private string? _clientVersion;
    private readonly ILogger? _logger;

    private const int DefaultMaxFailures = 3; // kept for ctor signature compat (unused — heartbeat removed)

    /// <summary>Raised when the client reconnects after a connection loss. Receives new clientId.</summary>
    public event Func<string, Task>? OnReconnected;

    public string ClientId { get; private set; } = "";
    public bool IsConnected => !string.IsNullOrEmpty(ClientId) && !_disposed;

    public LlmServerClient(string endpoint, string? clientId = null, int maxHeartbeatFailures = DefaultMaxFailures, ILogger? logger = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        ClientId = clientId ?? "";
        _client = new OpenAIClient(_endpoint, ClientId);
        _logger = logger;
    }

    /// <summary>Swap the underlying client. The old instance is only disposed once no in-flight call still holds a reference to it.</summary>
    private void ReplaceClient(OpenAIClient next)
    {
        var old = Interlocked.Exchange(ref _client, next);
        if (old == null) return;
        if (Interlocked.CompareExchange(ref _inFlightCalls, 0, 0) == 0)
        {
            old.Dispose();
            return;
        }
        // Drain in background — do not block the reconnect path.
        _ = Task.Run(async () =>
        {
            while (Interlocked.CompareExchange(ref _inFlightCalls, 0, 0) > 0)
                await Task.Delay(50);
            old.Dispose();
        });
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

        var json = await CallAsync(client => client.PostJsonAsync("/eca/clients", body, ct), ct);
        var resp = JsonSerializer.Deserialize<RegisterResponse>(json, JsonOptions);

        if (resp?.ClientId != null)
        {
            ClientId = resp.ClientId;
            ReplaceClient(new OpenAIClient(_endpoint, ClientId));            return true;
        }

        return false;
    }

    /// <summary>Run an HTTP call against a stable client reference, tracking in-flight usage so Dispose cannot race it.</summary>
    private async Task<T> CallAsync<T>(Func<OpenAIClient, Task<T>> op, CancellationToken ct)
    {
        var client = Volatile.Read(ref _client);
        Interlocked.Increment(ref _inFlightCalls);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LlmServerClient));
            return await op(client);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightCalls);
        }
    }

    /// <summary>
    /// <summary>
    /// Attempt to re-register with the server after losing connection.
    /// Preserves the client name and version from the original registration.
    /// </summary>
    public async Task<bool> TryReconnectAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;

        // Hysteresis: after a failed reconnect, wait out a cooldown before trying again —
        // prevents socket churn when the server is down (heartbeat fires every interval).
        var now = DateTime.UtcNow;
        if (now - _lastReconnectAttemptUtc < ReconnectCooldown) return false;
        _lastReconnectAttemptUtc = now;

        try
        {
            _logger?.Info("LlmServerClient", $"Reconnecting to LLM server as {_clientName}...");

            ReplaceClient(new OpenAIClient(_endpoint));

            var body = JsonSerializer.Serialize(new
            {
                client_name = _clientName,
                version = _clientVersion ?? "1.0.0"
            });
            var json = await CallAsync(client => client.PostJsonAsync("/eca/clients", body, ct), ct);
            var resp = JsonSerializer.Deserialize<RegisterResponse>(json, JsonOptions);

            if (resp?.ClientId != null)
            {
                ClientId = resp.ClientId;
                ReplaceClient(new OpenAIClient(_endpoint, ClientId));                _logger?.Info("LlmServerClient", $"Reconnected as client {ClientId}");
                _lastReconnectAttemptUtc = DateTime.MinValue; // allow immediate reconnects again
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

        try
        {
            var json = await CallAsync(client => client.DeleteJsonAsync($"/eca/clients/{ClientId}", ct), ct);
            ClientId = "";
            return json.Contains("\"ok\":true") || json.Contains("\"ok\": true");
        }
        catch
        {
            ClientId = "";
            return false;
        }
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        // Disconnect BEFORE marking disposed — IsConnected must still be true,
        // otherwise DisconnectAsync returns early and the server never learns we left.
        try { await DisconnectAsync(); }
        catch { /* best effort */ }

        _disposed = true;
        // Guard against in-flight calls holding the client — drain before disposing.
        var client = Interlocked.Exchange(ref _client, null!);
        if (client != null)
        {
            if (Interlocked.CompareExchange(ref _inFlightCalls, 0, 0) == 0)
                client.Dispose();
            else
                _ = Task.Run(async () =>
                {
                    while (Interlocked.CompareExchange(ref _inFlightCalls, 0, 0) > 0)
                        await Task.Delay(50);
                    client.Dispose();
                });
        }
    }
}