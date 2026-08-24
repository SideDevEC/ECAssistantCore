using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Engine;

/// <summary>
/// One-shot LLM extraction with a persistent KV cache via HTTP.
/// Uses IKvCacheController + IInferenceEngine to manage a server-side session
/// with prefix caching. Only the variable portion of the prompt is sent per call;
/// the cached prefix is rewound via SaveState/Rewind after generation.
/// </summary>
public class PrefixCachedExtractor : IAsyncDisposable
{
    private readonly IInferenceEngine _engine;
    private readonly IKvCacheController _kvCache;
    private readonly string _sessionId;
    private readonly InferenceParamsFactory _paramsFactory;

    private string? _prefilledPrefix;
    private bool _isPrefilled;
    private bool _disposed;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PrefixCachedExtractor(
        IInferenceEngine engine,
        IKvCacheController kvCache,
        string sessionId,
        InferenceParamsFactory? paramsFactory = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _kvCache = kvCache ?? throw new ArgumentNullException(nameof(kvCache));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _paramsFactory = paramsFactory ?? InferenceParamsFactory.Default;
    }

    /// <summary>
    /// Prefill the KV cache with the static system-prompt prefix.
    /// Call once at startup (or when the prefix changes).
    /// </summary>
    public async Task PrefillPrefixAsync(string prefixPrompt)
    {
        if (_isPrefilled && _prefilledPrefix == prefixPrompt)
            return;

        await _lock.WaitAsync();
        try
        {
            // Create session if not exists, then prefill
            await _kvCache.CreateSessionAsync(_sessionId);
            await _kvCache.PrefillAsync(_sessionId, prefixPrompt);
            await _kvCache.SaveStateAsync(_sessionId);

            _prefilledPrefix = prefixPrompt;
            _isPrefilled = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Extract a summary from the given variable prompt.
    /// The static prefix must already be prefilled.
    /// After generation, the KV cache is rewound to the post-prefix state.
    /// </summary>
    public async Task<string?> ExtractAsync(string variablePrompt)
    {
        if (!_isPrefilled || _disposed)
            return null;

        await _lock.WaitAsync();
        try
        {
            // Rewind to clean prefix state
            await _kvCache.RewindAsync(_sessionId);

            // Generate — only the variable portion
            var parameters = _paramsFactory.Create(
                maxTokens: 200,
                stop: new[] { "User:", "Question:", "\n\n\n" },
                temperature: 0.1f,
                topP: 0.8f);

            parameters.SessionId = _sessionId;

            var sb = new System.Text.StringBuilder();
            await foreach (var token in _engine.StreamAsync(variablePrompt, parameters))
            {
                sb.Append(token);
            }

            var result = sb.ToString().Trim();
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<[^>]+>", "");
            return result;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Update the static prefix (rebuilds the KV cache).
    /// </summary>
    public async Task UpdatePrefixAsync(string newPrefix)
    {
        await _kvCache.ResetAsync(_sessionId);
        _isPrefilled = false;
        await PrefillPrefixAsync(newPrefix);
    }

    public bool IsPrefilled => _isPrefilled;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await _kvCache.DestroySessionAsync(_sessionId); }
        catch { }

        _lock.Dispose();
        await ValueTask.CompletedTask;
    }
}