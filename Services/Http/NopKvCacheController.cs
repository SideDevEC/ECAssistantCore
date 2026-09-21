using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// No-op KV cache controller for remote mode (cloud API).
/// All operations return success without doing anything — there's no server-side
/// KV cache in remote mode. The engine already null-checks IKvCacheController,
/// but this provides a safe non-null implementation for when a non-null reference
/// is required by DI containers or testing.
/// </summary>
public sealed class NopKvCacheController : IKvCacheController
{
    public Task<bool> CreateSessionAsync(string sessionId, string? modelId = null, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> DestroySessionAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> PrefillAsync(string sessionId, string text, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> SaveStateAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> RewindAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> ResetAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<KvCacheStatus?> GetStatusAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<KvCacheStatus?>(null);
}