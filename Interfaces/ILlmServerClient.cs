using System.Threading;
using System.Threading.Tasks;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Client lifecycle management for ECAssistantLLM server.
/// Handles registration, heartbeat, and disconnection.
/// </summary>
public interface ILlmServerClient : IAsyncDisposable
{
    /// <summary>Client ID assigned by the server.</summary>
    string ClientId { get; }

    /// <summary>Whether the client is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Register with the server. Returns true on success.</summary>
    Task<bool> ConnectAsync(string clientName, string? version = null, CancellationToken ct = default);

    /// <summary>Send heartbeat (keeps sessions alive).</summary>
    Task<bool> HeartbeatAsync(int activeSessions = 0, CancellationToken ct = default);

    /// <summary>Disconnect from server (frees all sessions).</summary>
    Task<bool> DisconnectAsync(CancellationToken ct = default);
}