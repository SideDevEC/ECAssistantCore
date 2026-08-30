using System.Text.Json;
using System.Text.Json.Nodes;
using ECAssistant.Core.Config;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// Core owns the LLM server config. Before the server process is launched, this writer
/// guarantees that {llmRoot}/llm-server.json exists and contains entries for exactly the
/// model ids selected in appsettings.json (llm_provider.model_id / embedding_model_id),
/// with paths resolved INSIDE the app root ({llmRoot}/models). The server is then launched
/// with that explicit config path so it never invents its own defaults.
/// </summary>
public static class ServerConfigWriter
{
    /// <summary>Canonical server config path: {llmRoot}/llm-server.json.</summary>
    public static string GetConfigPath(string llmRoot) => Path.Combine(llmRoot, "llm-server.json");

    /// <summary>
    /// Ensure llm-server.json exists and covers the configured chat + embedding model ids.
    /// Returns true when a usable config is in place. Never writes paths outside appRoot.
    /// </summary>
    public static bool EnsureServerConfig(string appRoot, string llmRoot, LlmProviderConfig provider)
    {
        try
        {
            var configPath = GetConfigPath(llmRoot);
            var modelsDir = Path.Combine(llmRoot, "models");
            var appsettingsPath = Path.Combine(appRoot, "appsettings.json");

            var root = LoadOrCreate(configPath, provider.Port);
            var models = EnsureModelsArray(root);

            var appsettings = LoadJson(appsettingsPath);
            var chatFile = ResolveChatModelFile(appsettings, modelsDir, appRoot);
            var embedFile = ResolveEmbeddingModelFile(appsettings, modelsDir, appRoot);

            if (!string.IsNullOrWhiteSpace(provider.ModelId))
                EnsureEntry(models, provider.ModelId, chatFile, isEmbedding: false,
                    contextSize: ReadUInt(appsettings, "llm", "context_size", 32768));

            if (!string.IsNullOrWhiteSpace(provider.EmbeddingModelId))
                EnsureEntry(models, provider.EmbeddingModelId, embedFile, isEmbedding: true,
                    contextSize: 2048);

            Directory.CreateDirectory(llmRoot);
            File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ── Internals ──

    private static JsonObject LoadOrCreate(string configPath, int port)
    {
        if (File.Exists(configPath) &&
            LoadJson(configPath) is JsonObject parsed)
            return parsed;

        return new JsonObject
        {
            ["server"] = new JsonObject { ["host"] = "localhost", ["port"] = port },
            ["models"] = new JsonArray(),
            ["inference"] = new JsonObject(),
            ["logging"] = new JsonObject()
        };
    }

    private static JsonArray EnsureModelsArray(JsonObject root)
    {
        if (root["models"] is JsonArray models) return models;
        var array = new JsonArray();
        root["models"] = array;
        return array;
    }

    /// <summary>Add or update the entry for `id`, using a root-contained path when the
    /// model file can be located inside the root. Existing entries keep their tuning
    /// (gpu_layers/context) unless their path is missing or points outside the root.</summary>
    private static void EnsureEntry(JsonArray models, string id, string? rootContainedFile, bool isEmbedding, uint contextSize)
    {
        JsonObject? existing = null;
        foreach (var node in models)
        {
            if (node is JsonObject o && string.Equals(o["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
            {
                existing = o;
                break;
            }
        }

        if (existing != null)
        {
            // Replace the path only when it is missing on disk (stale) and a
            // root-contained candidate exists. Valid paths are left untouched.
            var current = existing["path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(current) || (!File.Exists(current) && rootContainedFile != null))
                existing["path"] = rootContainedFile;
            if (existing["is_embedding"] == null) existing["is_embedding"] = isEmbedding;
            return;
        }

        if (rootContainedFile == null) return; // nothing to register — server will report the gap

        var entry = new JsonObject
        {
            ["id"] = id,
            ["path"] = rootContainedFile,
            ["gpu_layers"] = isEmbedding ? 0 : 99,
            ["context_size"] = contextSize,
            ["threads"] = -1,
            ["is_embedding"] = isEmbedding
        };
        if (isEmbedding) entry["pooling_type"] = "mean";
        models.Add(entry);
    }

    /// <summary>Chat model file from appsettings llm.model_path, resolved INSIDE {llmRoot}/models.</summary>
    private static string? ResolveChatModelFile(JsonObject? appsettings, string modelsDir, string appRoot)
    {
        var configured = ReadString(appsettings, "llm", "model_path");
        return ResolveInsideModelsDir(configured, modelsDir, appRoot);
    }

    /// <summary>Embedding model file from appsettings embedding.model_path, resolved INSIDE {llmRoot}/models.</summary>
    private static string? ResolveEmbeddingModelFile(JsonObject? appsettings, string modelsDir, string appRoot)
    {
        var configured = ReadString(appsettings, "embedding", "model_path");
        return ResolveInsideModelsDir(configured, modelsDir, appRoot);
    }

    private static string? ResolveInsideModelsDir(string? configured, string modelsDir, string appRoot)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;

        // Preferred: the file already lives in {llmRoot}/models (installer layout)
        var inModels = Path.Combine(modelsDir, Path.GetFileName(configured));
        if (File.Exists(inModels)) return Path.GetFullPath(inModels);

        // Accept the configured path only when it already sits inside the app root
        var full = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(appRoot, configured));
        if (File.Exists(full) && IsUnder(full, appRoot)) return full;

        return null;
    }

    private static bool IsUnder(string path, string root)
    {
        var rel = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !rel.StartsWith("..") && !Path.IsPathRooted(rel);
    }

    private static JsonObject? LoadJson(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private static string? ReadString(JsonObject? root, string section, string key)
    {
        if (root?[section] is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s))
            return string.IsNullOrWhiteSpace(s) ? null : s;
        return null;
    }

    private static uint ReadUInt(JsonObject? root, string section, string key, uint fallback)
    {
        if (root?[section] is JsonObject o && o[key] is JsonValue v && v.TryGetValue<double>(out var d) && d > 0)
            return (uint)d;
        return fallback;
    }
}
