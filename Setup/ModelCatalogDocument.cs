using System.Text.Json;
using System.Text.Json.Serialization;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Root document for model-catalog.json. Lives in the app root dir;
/// user-editable — new models are added by appending entries.
/// </summary>
public sealed class ModelCatalogDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("models")]
    public List<ModelCatalogEntry> Models { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Load the catalog; writes the built-in default first when the file is missing.</summary>
    // Stateless factory on immutable-ish data class.
    public static ModelCatalogDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaultDoc = CreateDefault();
            File.WriteAllText(path, JsonSerializer.Serialize(defaultDoc, Options));
        }
        return JsonSerializer.Deserialize<ModelCatalogDocument>(File.ReadAllText(path), Options)
               ?? throw new InvalidOperationException($"Model catalog is empty or invalid: {path}");
    }

    /// <summary>Validate catalog invariants; returns error message or null.</summary>
    public string? Validate()
    {
        if (Models.Count == 0) return "Catalog contains no models.";
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Models)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) return "An entry is missing 'id'.";
            if (!ids.Add(m.Id)) return $"Duplicate id: {m.Id}";
            if (string.IsNullOrWhiteSpace(m.HfRepo)) return $"{m.Id}: missing 'hf_repo'.";
            if (m.Files.Count == 0) return $"{m.Id}: no files listed.";
            if (m.Category == CatalogModelCategory.Vision && string.IsNullOrEmpty(m.MmprojFile))
                return $"{m.Id}: vision entry missing 'mmproj_file'.";
            foreach (var f in m.Files)
                if (string.IsNullOrWhiteSpace(f.Filename))
                    return $"{m.Id}: a file entry is missing 'filename'.";
        }
        return null;
    }

    /// <summary>The built-in starter catalog (embedded resource, written to disk on first run so it stays user-editable).</summary>
    public static ModelCatalogDocument CreateDefault()
    {
        var asm = typeof(ModelCatalogDocument).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("ModelCatalog.default.json", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<ModelCatalogDocument>(reader.ReadToEnd(), Options)
               ?? throw new InvalidOperationException("Embedded default model catalog is invalid.");
    }
}
