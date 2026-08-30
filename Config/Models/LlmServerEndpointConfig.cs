using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// Core-side config for connecting to ECAssistantLLM server.
/// Only knows endpoint + auto-start settings. All model/GPU config lives in the server.
/// </summary>
public sealed class LlmServerEndpointConfig
{
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = "http://localhost:58777";

    /// <summary>If true, Core launches ECAssistantLLM as a child process when server not detected.</summary>
    [JsonPropertyName("auto_start")]
    public bool AutoStart { get; set; } = true;

    /// <summary>Path to the ECAssistant.LLM executable (relative or absolute).</summary>
    [JsonPropertyName("server_executable_path")]
    public string ServerExecutablePath { get; set; } = "server/ECAssistant.LLM";

    /// <summary>Max seconds to wait for server startup.</summary>
    [JsonPropertyName("startup_timeout_sec")]
    public int StartupTimeoutSec { get; set; } = 240;

    /// <summary>Heartbeat interval in seconds.</summary>
    [JsonPropertyName("heartbeat_interval_sec")]
    public int HeartbeatIntervalSec { get; set; } = 30;

    /// <summary>Model ID to use for chat completions (must match a model in llm-server.json).</summary>
    [JsonPropertyName("model_id")]
    public string ModelId { get; set; } = "main";

    /// <summary>Model ID for embeddings (must match a model in llm-server.json).</summary>
    [JsonPropertyName("embedding_model_id")]
    public string EmbeddingModelId { get; set; } = "embeddings";

    /// <summary>Path to the server's llm-server.json (for auto-start, passed as arg).</summary>
    [JsonPropertyName("server_config_path")]
    public string? ServerConfigPath { get; set; }
}