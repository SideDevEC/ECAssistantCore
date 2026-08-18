using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// Configuration for the embedding model used by vector memory.
/// When enabled, uses a real GGUF embedding model via LLamaSharp.
/// When disabled or model missing, falls back to TF-IDF hashing.
/// </summary>
public class EmbeddingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("model_path")]
    public string ModelPath { get; init; } = "models/all-MiniLM-L6-v2-Q5_K_M.gguf";

    /// <summary>
    /// Pooling strategy for token embeddings → sentence embedding.
    /// "mean" (default) averages all token embeddings.
    /// "none" returns per-token embeddings (not useful for similarity search).
    /// </summary>
    [JsonPropertyName("pooling_type")]
    public string PoolingType { get; init; } = "mean";

    /// <summary>
    /// Batch size for embedding inference. Default 256.
    /// </summary>
    [JsonPropertyName("batch_size")]
    public uint BatchSize { get; init; } = 256;

    /// <summary>
    /// Number of CPU threads for embedding. -1 = auto.
    /// </summary>
    [JsonPropertyName("threads")]
    public int Threads { get; init; } = -1;
}