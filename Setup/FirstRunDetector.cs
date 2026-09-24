using System.Text.Json;

namespace ECAssistant.Core.Setup;

/// <summary>First-run / installed-model state.</summary>
public sealed class FirstRunStatus
{
    /// <summary>True when no usable models exist in the models dir and no config has real model entries.</summary>
    public bool NeedsSetup { get; init; }

    /// <summary>True when the server binary is not installed at the shared location.</summary>
    public bool NeedsServerBinary { get; init; }

    /// <summary>Model files present in the models dir (gguf).</summary>
    public IReadOnlyList<string> InstalledFiles { get; init; } = Array.Empty<string>();

    /// <summary>Catalog entries whose primary file already exists in models/.</summary>
    public IReadOnlyList<string> InstalledEntryIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Detects whether first-run setup is needed: no GGUF files in the models dir
/// (or none matching the catalog) and no llm-server.json with model entries.
/// Pure-remote installs: when appsettings.json configures a usable remote
/// provider, NeedsSetup is false even with an empty models dir — remote users
/// install zero local models by design.
/// Also checks if the server binary is installed at the shared location.
/// All paths point to the shared LLM root (~/ECALLM/).
/// </summary>
public sealed class FirstRunDetector
{
    private readonly string _modelsDir;
    private readonly string _serverConfigPath;
    private readonly string _serverBinaryPath;
    private readonly string? _appsettingsPath;

    public FirstRunDetector(string modelsDir, string serverConfigPath)
        : this(modelsDir, serverConfigPath, serverBinaryPath: null)
    {
    }

    /// <summary>
    /// Create the detector with all paths.
    /// </summary>
    /// <param name="modelsDir">The shared models directory (~/ECALLM/models/)</param>
    /// <param name="serverConfigPath">The shared server config (~/ECALLM/llm-server.json)</param>
    /// <param name="serverBinaryPath">The server DLL path (~/ECALLM/server/ECAssistant.LLM.dll). If null, binary check is skipped.</param>
    /// <param name="appsettingsPath">Optional appsettings.json path; when present,
    /// a configured remote provider suppresses NeedsSetup.</param>
    public FirstRunDetector(string modelsDir, string serverConfigPath, string? serverBinaryPath, string? appsettingsPath = null)
    {
        _modelsDir = modelsDir;
        _serverConfigPath = serverConfigPath;
        _serverBinaryPath = serverBinaryPath ?? "";
        _appsettingsPath = appsettingsPath;
    }

    /// <summary>Evaluate current installation state.</summary>
    public FirstRunStatus Evaluate(IEnumerable<ModelCatalogEntry> catalogEntries)
    {
        var installedFiles = Directory.Exists(_modelsDir)
            ? Directory.EnumerateFiles(_modelsDir, "*.gguf")
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Cast<string>()
                .ToList()
            : new List<string>();

        var installedIds = new List<string>();
        foreach (var entry in catalogEntries)
        {
            var primary = entry.Files.FirstOrDefault();
            if (primary != null && installedFiles.Contains(primary.Filename, StringComparer.OrdinalIgnoreCase))
                installedIds.Add(entry.Id);
        }

        var configHasModels = false;
        if (File.Exists(_serverConfigPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_serverConfigPath));
                if (doc.RootElement.TryGetProperty("models", out var models) &&
                    models.ValueKind == JsonValueKind.Array && models.GetArrayLength() > 0)
                {
                    foreach (var m in models.EnumerateArray())
                    {
                        if (!m.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String) continue;
                        var path = p.GetString() ?? "";
                        if (File.Exists(path) || File.Exists(Path.Combine(_modelsDir, Path.GetFileName(path))))
                        {
                            configHasModels = true;
                            break;
                        }
                    }
                }
            }
            catch (JsonException) { /* broken config → treat as not configured */ }
        }

        var remoteConfigured = !string.IsNullOrEmpty(_appsettingsPath)
            && File.Exists(_appsettingsPath)
            && IsRemoteProviderConfigured(_appsettingsPath);

        var needsServerBinary = !string.IsNullOrEmpty(_serverBinaryPath) && !File.Exists(_serverBinaryPath);

        return new FirstRunStatus
        {
            NeedsSetup = installedFiles.Count == 0 && !configHasModels && !remoteConfigured,
            NeedsServerBinary = needsServerBinary,
            InstalledFiles = installedFiles,
            InstalledEntryIds = installedIds
        };
    }

    /// <summary>
    /// True when appsettings.json configures a usable remote provider.
    /// Matches what SetupWizard/RemoteProviderSetupWriter writes: an
    /// llm_provider section with mode="remote" and an endpoint, plus a
    /// non-empty llm_providers section (either key is sufficient if only one
    /// is present, since the writer always emits both).
    /// </summary>
    // Stateless utility — no mutable state.
    public static bool IsRemoteProviderConfigured(string appsettingsPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            bool hasRemoteMode = false, hasEndpoint = false, hasProviders = false;

            if (root.TryGetProperty("llm_provider", out var llm) && llm.ValueKind == JsonValueKind.Object)
            {
                hasRemoteMode = llm.TryGetProperty("mode", out var mode) &&
                                mode.ValueKind == JsonValueKind.String &&
                                string.Equals(mode.GetString(), "remote", StringComparison.OrdinalIgnoreCase);
                hasEndpoint = llm.TryGetProperty("endpoint", out var endpoint) &&
                              endpoint.ValueKind == JsonValueKind.String &&
                              !string.IsNullOrWhiteSpace(endpoint.GetString());
            }

            if (root.TryGetProperty("llm_providers", out var providers) && providers.ValueKind == JsonValueKind.Object)
            {
                hasProviders =
                    (providers.TryGetProperty("default_provider", out var def) &&
                     def.ValueKind == JsonValueKind.String &&
                     !string.IsNullOrWhiteSpace(def.GetString())) ||
                    (providers.TryGetProperty("providers", out var list) &&
                     list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0);
            }

            return hasRemoteMode && hasEndpoint && hasProviders;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable/invalid config: treat as not configured.
            return false;
        }
    }
}
