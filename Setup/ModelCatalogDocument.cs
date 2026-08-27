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

    /// <summary>The built-in starter catalog (written to disk on first run so it stays user-editable).</summary>
    // Stateless factory on immutable data class.
    public static ModelCatalogDocument CreateDefault() => new()
    {
        Version = 1,
        Models = new List<ModelCatalogEntry>
        {
            new()
            {
                Id = "qwen3-8b", DisplayName = "Qwen3 8B Instruct", Category = CatalogModelCategory.Chat,
                HfRepo = "bartowski/Qwen_Qwen3-8B-GGUF", Recommended = true, Quant = "Q4_K_M",
                Files = { new CatalogModelFile { Filename = "Qwen_Qwen3-8B-Q4_K_M.gguf", SizeGb = 5.0 } },
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 32768 },
                Notes = "Great all-round chat model; runs fast on Apple Silicon."
            },
            new()
            {
                Id = "qwen25-vl-7b", DisplayName = "Qwen2.5-VL 7B Instruct (Vision)", Category = CatalogModelCategory.Vision,
                HfRepo = "ggml-org/Qwen2.5-VL-7B-Instruct-GGUF", Recommended = true, Quant = "Q4_K_M",
                Files =
                {
                    new CatalogModelFile { Filename = "Qwen2.5-VL-7B-Instruct-Q4_K_M.gguf", SizeGb = 5.8 },
                    new CatalogModelFile { Filename = "mmproj-Qwen2.5-VL-7B-Instruct-f16.gguf", SizeGb = 0.8 }
                },
                MmprojFile = "mmproj-Qwen2.5-VL-7B-Instruct-f16.gguf",
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 16384 },
                Notes = "Best small vision model: OCR, screenshots, charts, documents."
            },
            new()
            {
                Id = "gemma3-4b", DisplayName = "Gemma 3 4B IT (Vision)", Category = CatalogModelCategory.Vision,
                HfRepo = "ggml-org/gemma-3-4b-it-GGUF", Recommended = false, Quant = "Q4_K_M",
                Files =
                {
                    new CatalogModelFile { Filename = "gemma-3-4b-it-Q4_K_M.gguf", SizeGb = 2.5 },
                    new CatalogModelFile { Filename = "mmproj-model-f16.gguf", SizeGb = 0.6 }
                },
                MmprojFile = "mmproj-model-f16.gguf",
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 8192 },
                Notes = "Compact + fast; strong text quality, decent vision, lighter OCR."
            },
            new()
            {
                Id = "smolvlm2-2b", DisplayName = "SmolVLM2 2.2B Instruct (Vision, tiny)", Category = CatalogModelCategory.Vision,
                HfRepo = "ggml-org/SmolVLM2-2.2B-Instruct-GGUF", Recommended = false, Quant = "Q4_K_M" ,
                Files =
                {
                    new CatalogModelFile { Filename = "smolvlm2-2.2b-instruct-q4_k_m.gguf", SizeGb = 1.5 },
                    new CatalogModelFile { Filename = "mmproj-smolvlm2-2.2b-instruct-f16.gguf", SizeGb = 0.4 }
                },
                MmprojFile = "mmproj-smolvlm2-2.2b-instruct-f16.gguf",
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 4096 },
                Notes = "Tiny footprint; simple image description only."
            },
            new()
            {
                Id = "all-minilm", DisplayName = "all-MiniLM-L6-v2 (Embeddings)", Category = CatalogModelCategory.Embedding,
                HfRepo = "ggml-org/all-MiniLM-L6-v2-GGUF", Recommended = true, Quant = "Q5_K_M",
                Files = { new CatalogModelFile { Filename = "all-MiniLM-L6-v2-Q5_K_M.gguf", SizeGb = 0.05 } },
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 0, ContextSize = 2048 },
                Notes = "Vector memory / KB embeddings. Required for vector memory features."
            }
        }
    };
}
