using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// LLM provider configuration. Supports two modes:
/// - local: spawns ECAssistantLLM server (full KV cache, session management, tokenizer)
/// - remote: connects to any OpenAI-compatible API (no KV cache, stateless inference)
/// </summary>
public sealed class LlmProviderConfig
{
    /// <summary>
    /// Provider mode: "local" (ECAssistantLLM) or "remote" (OpenAI-compatible API).
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "local";

    /// <summary>
    /// Server/API endpoint URL.
    /// Local: http://localhost:8420 (ECAssistantLLM)
    /// Remote: https://api.openai.com, https://api.deepseek.com, etc.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = "http://localhost:8420";

    /// <summary>
    /// API key for remote mode. Null/empty for local mode (ECAssistantLLM needs no key).
    /// </summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; } = null;

    /// <summary>
    /// Model ID for chat completions.
    /// Local: must match a model in llm-server.json (e.g. "main")
    /// Remote: model name from provider (e.g. "gpt-4o", "deepseek-chat")
    /// </summary>
    [JsonPropertyName("model_id")]
    public string ModelId { get; set; } = "main";

    /// <summary>
    /// Model ID for embeddings.
    /// Local: must match an embedding model in llm-server.json
    /// Remote: e.g. "text-embedding-3-small" (null = embeddings disabled)
    /// </summary>
    [JsonPropertyName("embedding_model_id")]
    public string? EmbeddingModelId { get; set; } = "embeddings";

    // ── Local mode only (ignored in remote mode) ──

    /// <summary>If true, Core launches ECAssistantLLM as a child process when server not detected.</summary>
    [JsonPropertyName("auto_start")]
    public bool AutoStart { get; set; } = true;

    /// <summary>Path to the ECAssistant.LLM executable (relative or absolute).</summary>
    [JsonPropertyName("server_executable_path")]
    public string ServerExecutablePath { get; set; } = "../ECAssistantLLM/bin/Release/net8.0/ECAssistant.LLM";

    /// <summary>Max seconds to wait for server startup.</summary>
    [JsonPropertyName("startup_timeout_sec")]
    public int StartupTimeoutSec { get; set; } = 60;

    /// <summary>Heartbeat interval in seconds (local mode only).</summary>
    [JsonPropertyName("heartbeat_interval_sec")]
    public int HeartbeatIntervalSec { get; set; } = 30;

    /// <summary>Path to the server's llm-server.json (for auto-start, passed as arg).</summary>
    [JsonPropertyName("server_config_path")]
    public string? ServerConfigPath { get; set; }

    // ── Convenience properties ──

    /// <summary>True if running in local mode (ECAssistantLLM with KV cache support).</summary>
    public bool IsLocal => string.Equals(Mode, "local", StringComparison.OrdinalIgnoreCase);

    /// <summary>True if running in remote mode (cloud API, no KV cache).</summary>
    public bool IsRemote => string.Equals(Mode, "remote", StringComparison.OrdinalIgnoreCase);
}