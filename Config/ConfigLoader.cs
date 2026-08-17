using System;
using System.IO;
using System.Text.Json;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Config;

/// <summary>
/// Loads EAgentConfig from JSON files.
/// Falls back to embedded default appsettings.json from Core.dll if file not found.
/// </summary>
public class ConfigLoader
{
    private readonly IFileSystem _fileSystem;

    public ConfigLoader(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public EAgentConfig Load(string filePath = "appsettings.json")
    {
        // Try user-provided file first
        try
        {
            if (_fileSystem.FileExists(filePath))
            {
                var json = _fileSystem.ReadFile(filePath);
                var config = JsonSerializer.Deserialize<EAgentConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config != null) return config;
            }
        }
        catch (Exception)
        {
            // File read/parse failed — continue to embedded fallback
        }

        // Fallback: embedded default config from Core.dll
        try
        {
            var embeddedJson = ResourceLoader.Default.LoadText("appsettings.json");
            if (embeddedJson != null)
            {
                var config = JsonSerializer.Deserialize<EAgentConfig>(embeddedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config != null) return config;
            }
        }
        catch (Exception)
        {
            // Embedded parse failed — continue to defaults
        }

        return new EAgentConfig();
    }
}