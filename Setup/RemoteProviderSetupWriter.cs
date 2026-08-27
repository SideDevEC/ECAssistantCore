using System.Text.Json;
using System.Text.Json.Serialization;
using ECAssistant.Core.Config;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Writes remote (OpenAI-compatible) provider settings chosen during first-run
/// into appsettings.json: switches the provider mode to "remote" and registers
/// the provider as the single default entry in the multi-provider section.
/// The API key is encrypted into the SecureKeyStore key files immediately —
/// appsettings.json only receives a "keyfile:" reference, never the literal key.
/// </summary>
public sealed class RemoteProviderSetupWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _appsettingsPath;
    private readonly string _keysDirectory;

    /// <param name="appsettingsPath">Full path to appsettings.json (its directory is the app root).</param>
    /// <param name="keysDirectory">
    /// Key store directory; "keys" (relative to the app root) by default, matching
    /// the multi-provider default so the registry resolves the reference at startup.
    /// </param>
    public RemoteProviderSetupWriter(string appsettingsPath, string? keysDirectory = null)
    {
        _appsettingsPath = appsettingsPath;
        _keysDirectory = keysDirectory
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(appsettingsPath)) ?? ".", "keys");
    }

    /// <summary>
    /// Persist the remote provider. Sets llm_provider.mode = "remote" with the
    /// given endpoint/model, encrypts the API key into the key store immediately,
    /// and writes it as the default llm_providers entry.
    /// </summary>
    public void Write(RemoteProviderConfig provider)
    {
        provider.IsDefault = true;

        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            var keyFileName = SanitizeName(provider.Name) + ".key";
            var keyStore = new SecureKeyStore(_keysDirectory);
            keyStore.SetKey(keyFileName, provider.ApiKey!);
            provider.ApiKey = $"keyfile:{keyFileName}";
        }

        var config = Load();
        config.LlmProvider.Mode = "remote";
        config.LlmProvider.Endpoint = provider.Endpoint;
        config.LlmProvider.ModelId = provider.ModelId;
        config.LlmProvider.ApiKey = provider.ApiKey;
        // Remote providers need a REAL embedding model id — the default "embeddings"
        // only exists on the local ECAssistantLLM server and would 404 remotely.
        config.LlmProvider.EmbeddingModelId = provider.EmbeddingModelId;

        config.LlmProviders ??= new MultiLlmProvidersConfig();
        config.LlmProviders.DefaultProvider = provider.Name;
        config.LlmProviders.FallbackEnabled = false;
        config.LlmProviders.KeysDirectory ??= "keys";
        // Replace any existing entries — first-run choice is authoritative.
        config.LlmProviders.Providers = new List<RemoteProviderConfig> { provider };

        File.WriteAllText(_appsettingsPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    // Stateless utility — no mutable state.
    private static string SanitizeName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray();
        var cleaned = new string(chars).Trim('-');
        return cleaned.Length > 0 ? cleaned : "remote-provider";
    }

    private EAgentConfig Load()
    {
        if (!File.Exists(_appsettingsPath))
            return new EAgentConfig();

        var json = File.ReadAllText(_appsettingsPath);
        return JsonSerializer.Deserialize<EAgentConfig>(json, JsonOptions) ?? new EAgentConfig();
    }
}
