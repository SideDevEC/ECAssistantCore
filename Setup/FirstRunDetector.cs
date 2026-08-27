using System.Text.Json;

namespace ECAssistant.Core.Setup;

/// <summary>First-run / installed-model state.</summary>
public sealed class FirstRunStatus
{
    /// <summary>True when no usable models exist in the models dir and no config has real model entries.</summary>
    public bool NeedsSetup { get; init; }

    /// <summary>Model files present in the models dir (gguf).</summary>
    public IReadOnlyList<string> InstalledFiles { get; init; } = Array.Empty<string>();

    /// <summary>Catalog entries whose primary file already exists in models/.</summary>
    public IReadOnlyList<string> InstalledEntryIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Detects whether first-run setup is needed: no GGUF files in the models dir
/// (or none matching the catalog) and no llm-server.json with model entries.
/// </summary>
public sealed class FirstRunDetector
{
    private readonly string _modelsDir;
    private readonly string _serverConfigPath;

    public FirstRunDetector(string modelsDir, string serverConfigPath)
    {
        _modelsDir = modelsDir;
        _serverConfigPath = serverConfigPath;
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
                    // Only counts as "configured" when at least one referenced file actually exists
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

        return new FirstRunStatus
        {
            NeedsSetup = installedFiles.Count == 0 && !configHasModels,
            InstalledFiles = installedFiles,
            InstalledEntryIds = installedIds
        };
    }
}
