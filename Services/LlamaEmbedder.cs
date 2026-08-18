using System;
using System.Linq;
using System.Threading;
using LLama;
using LLama.Common;
using LLama.Native;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Config;

namespace ECAssistant.Core.Services;

/// <summary>
/// Real LLamaSharp-based text embedder.
/// Loads a GGUF embedding model (e.g. all-MiniLM-L6-v2) and produces
/// dense vector embeddings suitable for cosine similarity search.
///
/// Falls back to TfidfEmbedder if:
/// - Embedding is disabled in config
/// - Model file is not found
/// - Model loading fails
///
/// Thread-safe: embedding calls are serialized via a lock because
/// LLamaEmbedder is not documented as thread-safe.
/// </summary>
public class LlamaEmbedder : IVectorEmbedder, IDisposable
{
    private LLamaWeights? _weights;
    private LLamaEmbedder? _embedder;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private bool _initialized;
    private bool _disposed;
    private int _vectorDim;
    private TfidfEmbedder? _fallback;

    /// <summary>
    /// Create a LlamaEmbedder from config. Lazy-loads the model on first Embed() call.
    /// </summary>
    /// <param name="config">Embedding config section</param>
    /// <param name="logger">Logger</param>
    public LlamaEmbedder(EmbeddingConfig config, ILogger? logger = null)
    {
        _logger = logger ?? new Logger();
        Config = config;

        if (!config.Enabled)
        {
            _logger.Info("Embedder", "Embedding disabled in config — using TF-IDF fallback");
            _fallback = new TfidfEmbedder();
            _vectorDim = 128;
        }
    }

    /// <summary>Current embedding config.</summary>
    public EmbeddingConfig Config { get; }

    /// <summary>Whether the embedder is using a real LLM model (not TF-IDF fallback).</summary>
    public bool IsUsingLlama => _weights != null;

    /// <summary>Vector dimension. 0 until first embed call (determined from model).</summary>
    public int VectorDim => _vectorDim;

    /// <summary>
    /// Embed text into a dense vector.
    /// Thread-safe: calls are serialized.
    /// </summary>
    public float[] Embed(string text)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LlamaEmbedder));

        // Fast path: fallback already set
        if (_fallback != null)
            return _fallback.Embed(text);

        lock (_lock)
        {
            EnsureInitialized();
            return EmbedInternal(text);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        try
        {
            var modelPath = ResolveModelPath();

            if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
            {
                _logger.Warn("Embedder", $"Embedding model not found: {modelPath} — falling back to TF-IDF");
                _fallback = new TfidfEmbedder();
                _initialized = true;
                return;
            }

            var pooling = Config.PoolingType.ToLower() switch
            {
                "mean" => LLamaPoolingType.Mean,
                "none" => LLamaPoolingType.None,
                "cls" => LLamaPoolingType.CLS,
                "last" => LLamaPoolingType.Last,
                _ => LLamaPoolingType.Mean
            };

            var parameters = new ModelParams(modelPath)
            {
                PoolingType = pooling,
                BatchSize = Config.BatchSize,
                Threads = Config.Threads,
                GpuLayerCount = 0  // Embedding models are small, CPU is fine
            };

            _weights = LLamaWeights.LoadFromFile(parameters);
            _embedder = new LLamaEmbedder(_weights, parameters);

            // Determine vector dimension from a test embedding
            var testEmbedding = _embedder.GetEmbeddings("dimension test").Result;
            var testVector = testEmbedding.Single();
            _vectorDim = testVector.Length;

            _initialized = true;
            _logger.Info("Embedder", $"LLamaSharp embedder loaded: dim={_vectorDim}, model={modelPath}");
        }
        catch (Exception ex)
        {
            _logger.Error("Embedder", $"Failed to load embedding model: {ex.GetType().Name}: {ex.Message} — falling back to TF-IDF");
            _fallback = new TfidfEmbedder();
            _initialized = true;
        }
    }

    private float[] EmbedInternal(string text)
    {
        if (_fallback != null)
            return _fallback.Embed(text);

        if (_embedder == null)
            return Array.Empty<float>();

        if (string.IsNullOrWhiteSpace(text))
            return new float[_vectorDim];

        var embeddings = _embedder.GetEmbeddings(text).Result;
        return embeddings.Single();
    }

    private string ResolveModelPath()
    {
        var path = Config.ModelPath;

        if (System.IO.Path.IsPathRooted(path))
            return path;

        // Try relative to ~/ECAssistant/ (user config dir)
        var userConfigDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "ECAssistant");
        var inUserDir = System.IO.Path.Combine(userConfigDir, path);
        if (System.IO.File.Exists(inUserDir))
            return inUserDir;

        // Try relative to app base directory
        var inBuildDir = System.IO.Path.Combine(AppContext.BaseDirectory, path);
        if (System.IO.File.Exists(inBuildDir))
            return inBuildDir;

        return inUserDir; // Return most likely path even if not found (for error message)
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _embedder?.Dispose();
        _weights?.Dispose();
    }
}