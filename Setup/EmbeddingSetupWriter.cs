using System.Text.Json.Nodes;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Persists the user's embeddings choice from first-run/install into appsettings.json:
/// "embedding.mode" = "local" | "remote".
/// local  → embeddings served by the local ECAssistantLLM server (spawned even when the
///          main AI is remote; it runs only for embedding workloads).
/// remote → the main AI provider's embedding endpoint/model.
/// Edits the JSON node in place so all other sections are preserved untouched.
/// </summary>
public sealed class EmbeddingSetupWriter
{
    private readonly string _appsettingsPath;

    public EmbeddingSetupWriter(string appsettingsPath)
    {
        _appsettingsPath = appsettingsPath;
    }

    public void SetMode(string mode, string? modelId = null, string? endpoint = null, string? apiKey = null)
    {
        JsonObject root;
        if (File.Exists(_appsettingsPath) &&
            JsonNode.Parse(File.ReadAllText(_appsettingsPath)) is JsonObject parsed)
        {
            root = parsed;
        }
        else
        {
            root = new JsonObject();
        }

        if (root["embedding"] is not JsonObject emb)
            root["embedding"] = emb = new JsonObject();

        emb["enabled"] = true;
        emb["mode"] = mode;
        if (modelId != null) emb["model_id"] = modelId;
        if (endpoint != null) emb["endpoint"] = endpoint;
        if (apiKey != null) emb["api_key"] = apiKey;

        File.WriteAllText(_appsettingsPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Marks embeddings as disabled (embedding.enabled = false) without touching other sections.</summary>
    public void Disable()
    {
        JsonObject root;
        if (File.Exists(_appsettingsPath) &&
            JsonNode.Parse(File.ReadAllText(_appsettingsPath)) is JsonObject parsed)
        {
            root = parsed;
        }
        else
        {
            root = new JsonObject();
        }

        if (root["embedding"] is not JsonObject emb)
            root["embedding"] = emb = new JsonObject();
        emb["enabled"] = false;

        File.WriteAllText(_appsettingsPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
