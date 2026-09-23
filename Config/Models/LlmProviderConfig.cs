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
    /// Local: derived from Host + Port (e.g. http://localhost:48217).
    /// Remote: full URL from provider (e.g. https://api.openai.com).
    /// In local mode, setting Port + Host is preferred over setting Endpoint directly.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = "http://localhost:48217";

    /// <summary>
    /// Port for the local ECAssistantLLM server. Default: 48217.
    /// Chosen as an arbitrary port unlikely to collide with known services.
    /// In local mode, this port is passed to the LLM server on startup and used
    /// to build the endpoint URL if Endpoint is not explicitly set.
    /// Ignored in remote mode.
    /// </summary>
    [JsonPropertyName("port")]
    public int Port { get; set; } = 48217;

    /// <summary>
    /// Host for the local ECAssistantLLM server. Default: localhost.
    /// Used with Port to build the endpoint URL in local mode.
    /// Ignored in remote mode.
    /// </summary>
    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Resolved endpoint URL. In local mode, derives from Host + Port.
    /// In remote mode, returns Endpoint as-is.
    /// </summary>
    [JsonIgnore]
    public string ResolvedEndpoint => IsLocal
        ? $"http://{Host}:{Port}"
        : Endpoint;

    /// <summary>
    /// API key for remote mode. Null/empty for local mode (ECAssistantLLM needs no key).
    /// </summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; } = null;

    /// <summary>
    /// v15: reasoning effort for reasoning-capable models — off | low | medium | high.
    /// Like OpenAI's reasoning_effort. Only sent to the model backend; small/large
    /// tier-independent (user config decides). Null = provider default.
    /// </summary>
    [JsonPropertyName("reasoning_effort")]
    public string? ReasoningEffort { get; set; } = null;

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
    /// <summary>
    /// Whether the active provider supports vision (image input). For remote providers
    /// this is declared during setup (model-dependent); for local installs it is set
    /// automatically when a model with an mmproj projector is installed.
    /// Integrating applications read this to decide whether image input is offered.
    /// </summary>
    [JsonPropertyName("vision_enabled")]
    public bool VisionEnabled { get; set; } = false;

    [JsonPropertyName("embedding_model_id")]
    public string? EmbeddingModelId { get; set; } = "embeddings";

    // ── Local mode only (ignored in remote mode) ──

    /// <summary>If true, Core launches ECAssistantLLM as a child process when server not detected.</summary>
    [JsonPropertyName("auto_start")]
    public bool AutoStart { get; set; } = true;

    /// <summary>Path to the ECAssistant.LLM executable (relative or absolute).</summary>
    [JsonPropertyName("server_executable_path")]
    public string ServerExecutablePath { get; set; } = "server/ECAssistant.LLM";

    /// <summary>Max seconds to wait for server startup.</summary>
    [JsonPropertyName("startup_timeout_sec")]
    public int StartupTimeoutSec { get; set; } = 240;

    /// <summary>Heartbeat interval in seconds (local mode only).</summary>
    /// <summary>
    /// Root directory for the LLM server. If not set, defaults to ~/.ECAssistantLLM.
    /// The LLM server creates llm-server.json, logs, and models/ under this directory.
    /// Passed to the server via --root argument.
    /// Supports ~ expansion to the user's home directory.
    /// </summary>
    [JsonPropertyName("server_root_path")]
    public string? ServerRootPath { get; set; } = "~/.ECAssistantLLM";

    // ── Convenience properties ──

    /// <summary>True if running in local mode (ECAssistantLLM with KV cache support).</summary>
    public bool IsLocal => string.Equals(Mode, "local", StringComparison.OrdinalIgnoreCase);

    /// <summary>True if running in remote mode (cloud API, no KV cache).</summary>
    public bool IsRemote => string.Equals(Mode, "remote", StringComparison.OrdinalIgnoreCase);
}