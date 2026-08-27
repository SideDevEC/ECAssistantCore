using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// A single remote OpenAI-compatible provider entry.
/// Part of the multi-provider section ("llm_providers") in appsettings.json.
/// </summary>
public sealed class RemoteProviderConfig
{
    /// <summary>Unique name used for references (default_provider, per-session overrides).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Base URL of the OpenAI-compatible API (e.g. https://api.openai.com/v1).</summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// API key. Either a literal or a "file:&lt;path&gt;" reference resolved at startup
    /// (path may start with ~/). The file must contain ONLY the key.
    /// Prefer file references over literals so keys never live in appsettings.json.
    /// </summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    /// <summary>Model ID used for chat completions on this provider.</summary>
    [JsonPropertyName("model_id")]
    public string ModelId { get; set; } = "";

    /// <summary>Optional embedding model ID on this provider (null = no embeddings here).</summary>
        /// <summary>Whether this remote model accepts image input (vision-capable).</summary>
    [JsonPropertyName("vision_enabled")]
    public bool? VisionEnabled { get; set; }

[JsonPropertyName("embedding_model_id")]
    public string? EmbeddingModelId { get; set; }

    /// <summary>If true, this provider is preferred when default_provider is not set.</summary>
    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }
}

/// <summary>
/// Multi-provider configuration section ("llm_providers").
/// When present and non-empty, takes precedence over single-provider "llm_provider"
/// for remote mode. Local mode is unaffected and keeps using "llm_provider".
/// Integrators only need to edit config — no code changes required.
/// </summary>
public sealed class MultiLlmProvidersConfig
{
    /// <summary>Name of the default provider. Empty = first provider marked is_default, else first entry.</summary>
    [JsonPropertyName("default_provider")]
    public string DefaultProvider { get; set; } = "";

    /// <summary>
    /// If true, providers are tried in order at startup and the first healthy one wins;
    /// unhealthy defaults cause failover to later entries. Default false = strict:
    /// always use default_provider exactly as configured.
    /// </summary>
    [JsonPropertyName("fallback_enabled")]
    public bool FallbackEnabled { get; set; } = false;

    /// <summary>The configured remote providers.</summary>
    [JsonPropertyName("providers")]
    public List<RemoteProviderConfig> Providers { get; set; } = new();

    /// <summary>
    /// Folder for API key files referenced via "keyfile:<name>".
    /// Relative paths resolve against the app root. Files are self-encrypting:
    /// plaintext keys are encrypted in place on first startup (cross-platform,
    /// owner-only permissions). Default: "keys".
    /// </summary>
    [JsonPropertyName("keys_directory")]
    public string? KeysDirectory { get; set; }
}
