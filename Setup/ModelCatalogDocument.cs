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

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Load the catalog; writes the built-in default first when the file is missing.
    /// Older deployed catalogs may lack newer display fields (e.g. license) — missing
    /// metadata is backfilled from the built-in default so selection lists stay current.</summary>
    // Stateless factory on immutable-ish data class.
    public static ModelCatalogDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaultDoc = CreateDefault();
            File.WriteAllText(path, JsonSerializer.Serialize(defaultDoc, Options));
            return defaultDoc;
        }

        var doc = JsonSerializer.Deserialize<ModelCatalogDocument>(File.ReadAllText(path), Options)
               ?? throw new InvalidOperationException($"Model catalog is empty or invalid: {path}");

        // Stale user copy: when the shipped default catalog is NEWER, replace the
        // user file wholesale (users who customized should raise Version themselves).
        var defaults = CreateDefault();
        if (doc.Version < defaults.Version)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, Options));
            return defaults;
        }

        // Backfill display metadata missing from older catalog files (user-editable —
        // only fills EMPTY fields, never overwrites user customizations).
        var defaultLicenses = defaults.Models.Where(d => !string.IsNullOrWhiteSpace(d.License))
                             .ToDictionary(d => d.Id, d => d.License, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in doc.Models)
        {
            if (string.IsNullOrWhiteSpace(entry.License) &&
                defaultLicenses.TryGetValue(entry.Id, out var license))
                entry.License = license;
        }
        return doc;
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
            .Single(n => n.EndsWith("model-catalog.json", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<ModelCatalogDocument>(reader.ReadToEnd(), Options)
               ?? throw new InvalidOperationException("Embedded default model catalog is invalid.");
    }
}
