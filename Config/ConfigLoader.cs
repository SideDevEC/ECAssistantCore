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
public class ConfigLoader : IConfigLoader
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
                var merged = MergeOverEmbeddedDefaults(json);
                if (merged != null) return merged;
                // Malformed JSON falls through to embedded defaults below
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

    /// <summary>
    /// Deep-merge a (possibly partial) user file on top of the embedded default
    /// config, so untouched sections keep shipped defaults instead of being wiped.
    /// Returns null when the user JSON cannot be parsed at all.
    /// </summary>
    private static EAgentConfig? MergeOverEmbeddedDefaults(string userJson)
    {
        try
        {
            System.Text.Json.Nodes.JsonNode? userNode;
            try
            {
                userNode = System.Text.Json.Nodes.JsonNode.Parse(userJson);
            }
            catch (System.Text.Json.JsonException)
            {
                return null; // malformed — caller falls back to pure defaults
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var defaultsRaw = ResourceLoader.Default.LoadText("appsettings.json");
            var root = string.IsNullOrEmpty(defaultsRaw)
                ? new System.Text.Json.Nodes.JsonObject()
                : (System.Text.Json.Nodes.JsonObject)(System.Text.Json.Nodes.JsonNode.Parse(defaultsRaw)?.DeepClone()
                    ?? new System.Text.Json.Nodes.JsonObject());

            if (userNode is System.Text.Json.Nodes.JsonObject userObj)
                DeepMerge(root, userObj);

            return root.Deserialize<EAgentConfig>(opts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Recursive object merge: user values win; arrays/values replace wholesale.</summary>
    private static void DeepMerge(
        System.Text.Json.Nodes.JsonObject target,
        System.Text.Json.Nodes.JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is System.Text.Json.Nodes.JsonObject srcObj
                && target[key] is System.Text.Json.Nodes.JsonObject tgtObj)
            {
                DeepMerge(tgtObj, srcObj);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }
}