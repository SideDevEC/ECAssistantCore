using System.Runtime.InteropServices;


namespace ECAssistant.Core.Setup;

/// <summary>
/// Machine capabilities of the machine running the setup wizard. Used to tune
/// catalog-suggested server config (GPU layers, context, batch) so the installed
/// setup is optimized for THIS machine — OS independent. Pure data + rules;
/// detection is injectable for tests (see Create for the real probe).
/// </summary>
public sealed class HardwareProfile
{
    public double TotalRamGb { get; init; }
    public bool IsAppleSilicon { get; init; }
    public bool HasCuda { get; init; }

    private const double AppleSiliconUsableFraction = 0.70; // unified memory — OS reserves the rest
    private const double WeightOverheadFactor = 1.35;       // weights + KV cache + runtime overhead

    /// <summary>Real machine probe.</summary>
    // Stateless factory-style helper — reads environment only, no shared state.
    public static HardwareProfile Detect()
    {
        var ramGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1_073_741_824.0;
        var isMacArm = OperatingSystem.IsMacOS() &&
                       RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        return new HardwareProfile
        {
            TotalRamGb = ramGb,
            IsAppleSilicon = isMacArm,
            HasCuda = IsCudaDriverPresent()
        };
    }

    /// <summary>
    /// GPU layers for a model of the given download size on THIS machine:
    /// - Apple Silicon: full offload when weights+overhead fit in usable unified memory
    /// - CUDA: full offload for models that fit typical 16 GB-class cards (VRAM not
    ///   probe-able portably; larger models stay CPU to avoid load-time OOM)
    /// - Vulkan or CPU-only (Windows/Linux without CUDA): full offload only for small
    ///   models that fit typical 8 GB cards; the LLM server's Vulkan DeltaNet-MoE guard
    ///   clamps qwen3_5moe-family models to CPU regardless.
    /// </summary>
    public int ResolveGpuLayers(double modelSizeGb)
    {
        if (IsAppleSilicon)
        {
            var usableGb = TotalRamGb * AppleSiliconUsableFraction;
            return modelSizeGb * WeightOverheadFactor <= usableGb ? 99 : 0;
        }

        if (HasCuda)
            return modelSizeGb <= 13 ? 99 : 0;

        return modelSizeGb <= 8 ? 99 : 0;
    }

    /// <summary>Context size: catalog suggestion, downscaled on low-RAM machines.</summary>
    public uint ResolveContextSize(uint suggestedContextSize)
    {
        if (TotalRamGb < 8) return Math.Min(suggestedContextSize, 4096);
        if (TotalRamGb < 12) return Math.Min(suggestedContextSize, 8192);
        return suggestedContextSize;
    }

    /// <summary>Prompt-processing batch: larger on capable machines, embeddings omit.</summary>
    public int ResolveBatchSize(bool isEmbedding)
        => isEmbedding ? 0 : (TotalRamGb >= 16 ? 512 : 256);

    private static bool IsCudaDriverPresent()
    {
        // Stateless utility — pure environment probe, no shared state.
        try
        {
            if (OperatingSystem.IsWindows())
                return System.Runtime.InteropServices.NativeLibrary.TryLoad("nvcuda", out _);
            if (OperatingSystem.IsLinux())
                return System.Runtime.InteropServices.NativeLibrary.TryLoad("libcuda.so.1", out _);
            return false;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>
    /// Returns a tuned copy of the entry's suggested config for THIS machine:
    /// GPU layers, context size and batch size are hardware-adaptive; max_tokens
    /// is per-model (from the catalog) and passes through unchanged.
    /// </summary>
    public CatalogSuggestedConfig Adjust(ModelCatalogEntry entry)
    {
        var sizeGb = entry.TotalSizeGb;
        return new CatalogSuggestedConfig
        {
            GpuLayers = ResolveGpuLayers(sizeGb),
            ContextSize = ResolveContextSize(entry.SuggestedConfig.ContextSize),
            BatchSize = ResolveBatchSize(entry.Category == CatalogModelCategory.Embedding),
            Backend = entry.SuggestedConfig.Backend,
            MaxTokens = entry.SuggestedConfig.MaxTokens
        };
    }
}
