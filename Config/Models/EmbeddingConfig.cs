using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// Configuration for the embedding model used by vector memory.
/// When enabled, uses a real embedding model via HTTP to ECAssistantLLM server.
/// When disabled or model missing, falls back to TF-IDF hashing.
/// </summary>
public class EmbeddingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Where embeddings are computed, independent of the main LLM mode:
    /// "local"  → the local ECAssistantLLM server serves embeddings (spawned even when
    ///            the main AI is remote — it runs only for embedding workloads).
    /// "remote" → the main provider's embedding endpoint/model (default when empty).
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "";

    /// <summary>Override endpoint for local embedding mode (default: local server, port from llm_provider).</summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    /// <summary>Override model id for local embedding mode (default: "embeddings").</summary>
    [JsonPropertyName("model_id")]
    public string? ModelId { get; init; }

    /// <summary>
    /// API key for remote embedding mode. Supports "keyfile:&lt;name&gt;" references into
    /// the SecureKeyStore keys directory. Null/empty = no auth (or inherited default key handling).
    /// </summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }

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