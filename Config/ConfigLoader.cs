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

            MigrateLegacyToolKeys(root);

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

    /// <summary>
    /// v14.10: one-time migration inside the merged JSON — renames legacy tool
    /// config sections (DotnetBuild → EDotnetBuild, AskUser → EAskUser) and
    /// prunes sections of removed tools (EWebSearch/EWebFetch). Migrated keys
    /// never overwrite a newer explicit section. Runs before deserialization;
    /// on-disk files are rewritten by the normal config persist path.
    /// </summary>
    private static void MigrateLegacyToolKeys(System.Text.Json.Nodes.JsonObject root)
    {
        var toolsObj = root["Tools"] as System.Text.Json.Nodes.JsonObject
                       ?? root["tools"] as System.Text.Json.Nodes.JsonObject;
        if (toolsObj is null)
            return;

        RenameToolSection(toolsObj, "DotnetBuild", "EDotnetBuild");
        RenameToolSection(toolsObj, "AskUser", "EAskUser");

        // Removed tools — drop their sections
        foreach (var removed in new[] { "EWebSearch", "EWebFetch" })
            toolsObj.Remove(removed);
    }

    private static void RenameToolSection(
        System.Text.Json.Nodes.JsonObject toolsObj, string from, string to)
    {
        if (toolsObj[from] is null)
            return;
        if (toolsObj[to] is null)
            toolsObj[to] = toolsObj[from]!.DeepClone(); // new section wins when already present
        toolsObj.Remove(from); // legacy key always goes
    }
}