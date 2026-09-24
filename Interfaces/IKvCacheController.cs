using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// KV cache control over HTTP. Replaces direct LLamaSharp executor state management.
/// Each ECAssistantCore session maps to a server-side session with its own KV cache.
/// </summary>
public interface IKvCacheController
{
    /// <summary>Create a new inference session (own KV cache) on the server.
    /// modelId routes process-backend models into the server's process-session registry;
    /// omit to let the server use its default (main) model.</summary>
    Task<bool> CreateSessionAsync(string sessionId, string? modelId = null, CancellationToken ct = default);

    /// <summary>Destroy a session (frees KV cache on the server).</summary>
    Task<bool> DestroySessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Prefill static prefix into KV cache.</summary>
    Task<bool> PrefillAsync(string sessionId, string text, CancellationToken ct = default);

    /// <summary>Save current KV cache state (for later rewind).</summary>
    Task<bool> SaveStateAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Rewind KV cache to last saved state.</summary>
    Task<bool> RewindAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Reset KV cache completely (must re-prefill after).</summary>
    Task<bool> ResetAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Get KV cache status (token count, prefill state, etc.).</summary>
    Task<KvCacheStatus?> GetStatusAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>
/// KV cache status response from the server.
/// </summary>
public sealed class KvCacheStatus
{
    // Server returns snake_case — explicit mapping required, default STJ is
    // case-sensitive and silently deserialized everything to 0/false (audit
    // 2026-09-24): server-truth compaction never saw real KV usage.
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }
    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }
    [JsonPropertyName("is_prefilled")]
    public bool IsPrefilled { get; set; }
    [JsonPropertyName("approx_tokens")]
    public int ApproxTokens { get; set; }
    [JsonPropertyName("context_size")]
    public uint ContextSize { get; set; }
    [JsonPropertyName("estimated_vram_mb")]
    public double EstimatedVramMb { get; set; }
}