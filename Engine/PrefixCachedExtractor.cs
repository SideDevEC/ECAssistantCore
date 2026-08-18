using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace ECAssistant.Core.Engine;

/// <summary>
/// One-shot LLM extraction with a persistent KV cache that retains the
/// static system prompt between calls. Only the variable portion of the
/// prompt is prefilled on each invocation; the cached prefix is rewound
/// via SaveState/LoadState after generation completes.
/// </summary>
///
/// <remarks>
/// <para>
/// <b>Lifecycle & Ownership</b><br/>
/// This class owns a persistent <c>LLamaContext</c> (native KV cache buffers)
/// and a <c>LLamaContext.State</c> snapshot. The caller <b>must hold a
/// reference</b> to the instance for as long as it wants the cache to stay
/// alive. If the instance is garbage collected, the native KV cache is
/// freed (via <c>LLamaContext</c> finalizer), but GC may delay this
/// because native memory doesn't trigger managed-heap pressure.
/// </para>
/// <para>
/// <b>Disposal</b><br/>
/// Always call <see cref="DisposeAsync"/> when done. This immediately
/// frees the native KV cache buffers and the state snapshot. Failing to
/// dispose can leak tens of MB of native memory (for a 4096-token context),
/// which competes with the main session's KV cache on GPU-constrained
/// systems.
/// </para>
/// <para>
/// <b>When to use</b><br/>
/// Use for <b>long-lived consumers</b> that call extraction repeatedly
/// over the lifetime of the application (e.g. <c>PatternExtractor</c>,
/// background summarizers, intent classifiers). The one-time prefill cost
/// is amortized across many calls.
/// </para>
/// <para>
/// <b>When NOT to use</b><br/>
/// Do <b>not</b> use in short-lived scopes (sub-agents, one-shot tasks,
/// fire-and-forget background work). Sub-agents typically run 3-5 turns
/// with a 120s timeout — the prefill savings don't justify the native
/// memory overhead and disposal complexity. Use <c>StatelessExecutor</c>
/// for those cases instead.
/// </para>
/// <para>
/// If you must use this class in a short-lived scope, wrap it with
/// <c>await using</c> to guarantee disposal:
/// </para>
/// <code>
/// await using var extractor = new PrefixCachedExtractor(weights, params, 4096);
/// await extractor.PrefillPrefixAsync(staticPrefix);
/// var result = await extractor.ExtractAsync(variablePrompt);
/// // DisposeAsync called automatically at scope exit
/// </code>
/// <para>
/// <b>Thread-safety</b><br/>
/// All public methods are guarded by a <c>SemaphoreSlim</c> — only one
/// extraction can run at a time. The underlying <c>LLamaContext</c> is
/// not safe for concurrent access.
/// </para>
/// </remarks>
public class PrefixCachedExtractor : IAsyncDisposable
{
    private readonly LLamaWeights _weights;
    private readonly ModelParams _modelParams;
    private readonly uint _contextSize;
    private readonly int _gpuLayers;

    // Persistent context + executor — kept alive across calls
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;

    // Snapshot of KV cache state after prefilling the static prefix.
    // Restored after each extraction to "rewind" to the clean prefix.
    private LLamaContext.State? _prefixState;

    // The static prefix prompt that was prefilled
    private string? _prefilledPrefix;

    // Sync lock — LLamaContext is single-threaded
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Track whether the prefix has been prefilled
    private bool _isPrefilled;

    // Inference params for extraction calls
    private readonly InferenceParams _inferenceParams;

    /// <summary>
    /// Create a PrefixCachedExtractor.
    /// </summary>
    /// <param name="weights">Shared model weights (no extra RAM for model loading).</param>
    /// <param name="modelParams">Shared model params. ContextSize is used to create a dedicated context.</param>
    /// <param name="contextSize">Context size for this extractor's dedicated KV cache (e.g. 4096).</param>
    /// <param name="gpuLayers">GPU layers for the dedicated context.</param>
    public PrefixCachedExtractor(
        LLamaWeights weights,
        ModelParams modelParams,
        uint contextSize = 4096,
        int gpuLayers = 0)
    {
        _weights = weights;
        _modelParams = modelParams;
        _contextSize = contextSize;
        _gpuLayers = gpuLayers;

        _inferenceParams = new InferenceParams
        {
            MaxTokens = 200,
            AntiPrompts = new[] { "User:", "Question:", "\n\n\n" },
            OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = 0.1f,
                TopP = 0.8f,
                TopK = 40,
                RepeatPenalty = 1.1f
            }
        };
    }

    /// <summary>
    /// Prefill the KV cache with the static system-prompt prefix.
    /// Call once at startup (or when the prefix changes).
    /// After this, only variable tokens are fed per ExtractAsync call.
    /// </summary>
    public async Task PrefillPrefixAsync(string prefixPrompt)
    {
        if (_isPrefilled && _prefilledPrefix == prefixPrompt)
            return; // already prefilled with same prefix

        await _lock.WaitAsync();
        try
        {
            // If prefix changed or context not yet created, (re)create
            if (_context == null || _prefilledPrefix != prefixPrompt)
            {
                _context?.Dispose();

                var ctxParams = new ModelParams(_modelParams.ModelPath)
                {
                    ContextSize = _contextSize,
                    GpuLayerCount = Math.Clamp(_gpuLayers, 0, 100),
                    Threads = _modelParams.Threads,
                };

                _context = _weights.CreateContext(ctxParams);
                _executor = new InteractiveExecutor(_context, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            }

            // Prefill: feed the prefix prompt through the executor.
            // We only need the prefill to happen — generated tokens are discarded.
            _prefilledPrefix = prefixPrompt;
            var sb = new System.Text.StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                await foreach (var token in _executor!.InferAsync(prefixPrompt, _inferenceParams, cts.Token))
                {
                    sb.Append(token);
                    // Stop as soon as the model starts generating — we just want prefill
                    if (sb.ToString().Contains('\n'))
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Prefill timed out — continue without cache optimization
            }
            catch (Exception)
            {
                // Prefill failed — continue without cache optimization
            }

            // Snapshot the KV cache state after prefill
            _prefixState = _context.GetState();
            _isPrefilled = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Extract a summary from the given variable prompt (e.g. SQL + metadata).
    /// The static prefix must already be prefilled via PrefillPrefixAsync.
    /// After generation, the KV cache is rewound to the post-prefix state
    /// so the next call only needs to prefill its own variable tokens.
    /// </summary>
    public async Task<string?> ExtractAsync(string variablePrompt)
    {
        if (!_isPrefilled || _executor == null || _context == null)
            return null;

        await _lock.WaitAsync();
        try
        {
            // Restore KV cache to the clean prefix state
            if (_prefixState != null)
            {
                _context.LoadState(_prefixState);
            }

            // Generate — only the variable portion is prefilled
            var sb = new System.Text.StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await foreach (var token in _executor.InferAsync(variablePrompt, _inferenceParams, cts.Token))
            {
                sb.Append(token);
            }

            var result = sb.ToString().Trim();
            // Strip any XML-like tags
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
    /// Use if the system prompt changes (e.g. new tools registered).
    /// </summary>
    public async Task UpdatePrefixAsync(string newPrefix)
    {
        await PrefillPrefixAsync(newPrefix);
    }

    /// <summary>Whether the static prefix has been prefilled and the cache is ready.</summary>
    public bool IsPrefilled => _isPrefilled;

    public async ValueTask DisposeAsync()
    {
        _prefixState?.Dispose();
        _context?.Dispose();
        _lock.Dispose();
        await ValueTask.CompletedTask;
    }
}
