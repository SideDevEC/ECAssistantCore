using System.Text.Json.Serialization;

namespace ECAssistant.Core.Setup;

/// <summary>Model category — drives config generation and UI grouping.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CatalogModelCategory
{
    /// <summary>General text chat model.</summary>
    Chat,
    /// <summary>Vision-capable model (ships with an mmproj projector).</summary>
    Vision,
    /// <summary>Embedding model (is_embedding = true).</summary>
    Embedding
}

/// <summary>A single downloadable file (GGUF) from a HuggingFace repository.</summary>
public sealed class CatalogModelFile
{
    /// <summary>File name as placed in models/ and referenced by the config.</summary>
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    /// <summary>Path inside the HF repo (defaults to filename when empty).</summary>
    [JsonPropertyName("hf_path")]
    public string? HfPath { get; set; }

    /// <summary>Approximate download size in GB (for display + disk checks).</summary>
    [JsonPropertyName("size_gb")]
    public double SizeGb { get; set; }
}

/// <summary>Suggested server config values for this model.</summary>
public sealed class CatalogSuggestedConfig
{
    [JsonPropertyName("gpu_layers")]
    public int GpuLayers { get; set; } = 99;

    [JsonPropertyName("context_size")]
    public uint ContextSize { get; set; } = 8192;

    /// <summary>Prompt-processing batch size. 0 = omit (library default).</summary>
    [JsonPropertyName("batch_size")]
    public int BatchSize { get; set; } = 0;
}

/// <summary>
/// One curated model in the catalog. Fully data-driven — adding a model
/// requires only a new entry in model-catalog.json, no code changes.
/// </summary>
public sealed class ModelCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("category")]
    public CatalogModelCategory Category { get; set; } = CatalogModelCategory.Chat;

    /// <summary>HuggingFace repository id (org/name).</summary>
    [JsonPropertyName("hf_repo")]
    public string HfRepo { get; set; } = "";

    /// <summary>Files to download (main GGUF; mmproj files included in the list).</summary>
    [JsonPropertyName("files")]
    public List<CatalogModelFile> Files { get; set; } = new();

    /// <summary>The mmproj sidecar filename, when Category == Vision.</summary>
    [JsonPropertyName("mmproj_file")]
    public string? MmprojFile { get; set; }

    /// <summary>Suggested values written into llm-server.json for this model.</summary>
    [JsonPropertyName("suggested_config")]
    public CatalogSuggestedConfig SuggestedConfig { get; set; } = new();

    /// <summary>Recommended = highlighted by the first-run wizard.</summary>
    [JsonPropertyName("recommended")]
    public bool Recommended { get; set; } = false;

    [JsonPropertyName("quant")]
    public string Quant { get; set; } = "Q4_K_M";

    /// <summary>License type shown in selection lists (e.g. Apache-2.0). Empty = not shown.</summary>
    [JsonPropertyName("license")]
    public string License { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    /// <summary>Total download size across all files.</summary>
    [JsonIgnore]
    public double TotalSizeGb => Files.Sum(f => f.SizeGb);

    /// <summary>Resolve the HF download URL for a file entry.</summary>
    // Stateless factory-style helper — pure string composition, no state.
    public string GetDownloadUrl(CatalogModelFile file)
    {
        var path = string.IsNullOrEmpty(file.HfPath) ? file.Filename : file.HfPath;
        return $"https://huggingface.co/{HfRepo}/resolve/main/{path}";
    }
}
