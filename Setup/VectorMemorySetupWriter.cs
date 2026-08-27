using System.Text.Json.Nodes;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Persists the user's vector-memory choice from first-run/install into
/// appsettings.json ("vector_memory.enabled"). Off = no embeddings model is
/// wired and semantic search is disabled for the installation.
/// Edits the node in place so all other sections/fields are preserved untouched.
/// </summary>
public sealed class VectorMemorySetupWriter
{
    private readonly string _appsettingsPath;

    public VectorMemorySetupWriter(string appsettingsPath)
    {
        _appsettingsPath = appsettingsPath;
    }

    public void SetEnabled(bool enabled)
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

        if (root["vector_memory"] is not JsonObject vm)
            root["vector_memory"] = vm = new JsonObject();

        vm["enabled"] = enabled;

        File.WriteAllText(_appsettingsPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
