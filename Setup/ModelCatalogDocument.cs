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
            },
            new()
            {
                Id = "qwen3-vl-4b", DisplayName = "Qwen3-VL 4B Instruct (Vision)", Category = CatalogModelCategory.Vision,
                HfRepo = "unsloth/Qwen3-VL-4B-Instruct-GGUF", Recommended = true, Quant = "Q4_K_M",
                Files =
                {
                    new CatalogModelFile { Filename = "Qwen3-VL-4B-Instruct-Q4_K_M.gguf", SizeGb = 2.33 },
                    new CatalogModelFile { Filename = "mmproj-F16.gguf", SizeGb = 0.78 }
                },
                MmprojFile = "mmproj-F16.gguf",
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 32768 },
                Notes = "NEW recommended vision model: latest Qwen3-VL gen, fast, strong OCR + charts + screenshots."
            },
            new()
            {
                Id = "qwen3-vl-8b", DisplayName = "Qwen3-VL 8B Instruct (Vision)", Category = CatalogModelCategory.Vision,
                HfRepo = "unsloth/Qwen3-VL-8B-Instruct-GGUF", Recommended = false, Quant = "Q4_K_M",
                Files =
                {
                    new CatalogModelFile { Filename = "Qwen3-VL-8B-Instruct-Q4_K_M.gguf", SizeGb = 4.68 },
                    new CatalogModelFile { Filename = "mmproj-F16.gguf", SizeGb = 1.08 }
                },
                MmprojFile = "mmproj-F16.gguf",
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 32768 },
                Notes = "Bigger Qwen3-VL: noticeably better reasoning over complex images and documents."
            },
            new()
            {
                Id = "qwen25-vl-3b", DisplayName = "Qwen2.5-VL 3B Instruct (Vision, light)", Category = CatalogModelCategory.Vision,
                HfRepo = "ggml-org/Qwen2.5-VL-3B-Instruct-GGUF", Recommended = false, Quant = "Q4_K_M",
                Files =
                {
                    new CatalogModelFile { Filename = "Qwen2.5-VL-3B-Instruct-Q4_K_M.gguf", SizeGb = 1.8 },
                    new CatalogModelFile { Filename = "mmproj-Qwen2.5-VL-3B-Instruct-f16.gguf", SizeGb = 1.25 }
                },
                MmprojFile = "mmproj-Qwen2.5-VL-3B-Instruct-f16.gguf",
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 16384 },
                Notes = "Lightest capable vision model; snappy on Apple Silicon, good for quick screenshots."
            },
            new()
            {
                Id = "qwen25-vl-32b", DisplayName = "Qwen2.5-VL 32B Instruct (Vision, max quality)", Category = CatalogModelCategory.Vision,
                HfRepo = "ggml-org/Qwen2.5-VL-32B-Instruct-GGUF", Recommended = false, Quant = "Q4_K_M",
                Files =
                {
                    new CatalogModelFile { Filename = "Qwen2.5-VL-32B-Instruct-Q4_K_M.gguf", SizeGb = 18.49 },
                    new CatalogModelFile { Filename = "mmproj-Qwen2.5-VL-32B-Instruct-f16.gguf", SizeGb = 1.28 }
                },
                MmprojFile = "mmproj-Qwen2.5-VL-32B-Instruct-f16.gguf",
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 32768 },
                Notes = "Maximum vision quality (~20 GB download). Best OCR, charts, multi-image reasoning."
            },
            new()
            {
                Id = "qwen35-35b-a3b", DisplayName = "Qwen3.5 35B-A3B (Vision + deep reasoning, MoE)", Category = CatalogModelCategory.Vision,
                HfRepo = "unsloth/Qwen3.5-35B-A3B-GGUF", Recommended = false, Quant = "UD-Q4_K_XL",
                Files =
                {
                    new CatalogModelFile { Filename = "Qwen3.5-35B-A3B-UD-Q4_K_XL.gguf", SizeGb = 20.71 },
                    new CatalogModelFile { Filename = "mmproj-F16.gguf", SizeGb = 0.84 }
                },
                MmprojFile = "mmproj-F16.gguf",
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 65536 },
                Notes = "Top pick for heavy analytics: 35B-class reasoning, only ~3B active (fast). Native vision, 256K context, thinking mode. ~21 GB RAM."
            },
            new()
            {
                Id = "multilingual-e5-large", DisplayName = "Multilingual E5 Large (Embedding, MIT)", Category = CatalogModelCategory.Embedding,
                HfRepo = "soichisumi/multilingual-e5-large-Q8_0-GGUF", Recommended = true, Quant = "Q8_0",
                Files = { new CatalogModelFile { Filename = "multilingual-e5-large-q8_0.gguf", SizeGb = 0.56 } },
                SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 2048 },
                Notes = "Multilingual embeddings (100+ languages), 1024-dim. Replaces English-only MiniLM. MIT license."
            }
        }
    };
}
