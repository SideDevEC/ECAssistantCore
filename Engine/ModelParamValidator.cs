using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Pre-flight validation for model loading parameters.
/// Catches common misconfigurations BEFORE calling LLamaSharp native code,
/// so users get a clear message instead of an opaque native exception.
/// </summary>
public class ModelParamValidator
{
    private readonly ILogger? _logger;

    public ModelParamValidator(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validate model loading parameters. Returns null if OK, or a ModelLoadException
    /// with a diagnostic message if something is wrong.
    /// </summary>
    public ModelLoadException? Validate(string modelPath, int gpuLayers, uint contextSize, int threads)
    {
        // ── Model file checks ──
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return new ModelLoadException(
                ModelLoadPhase.Validate, modelPath, gpuLayers, contextSize,
                "Model path is empty. Set llm.model_path in appsettings.json.");
        }

        if (!File.Exists(modelPath))
        {
            return new ModelLoadException(
                ModelLoadPhase.Validate, modelPath, gpuLayers, contextSize,
                $"Model file not found: {modelPath}");
        }

        var ext = Path.GetExtension(modelPath).ToLowerInvariant();
        if (ext != ".gguf")
        {
            _logger?.Warn("Validate", $"Model file has unexpected extension '{ext}' — expected .gguf. Proceeding anyway.");
        }

        // ── File size sanity ──
        try
        {
            var fileInfo = new FileInfo(modelPath);
            if (fileInfo.Length < 1024 * 1024) // < 1MB
            {
                return new ModelLoadException(
                    ModelLoadPhase.Validate, modelPath, gpuLayers, contextSize,
                    $"Model file is suspiciously small ({fileInfo.Length} bytes) — may be corrupted or incomplete.");
            }
        }
        catch { /* can't check — proceed anyway */ }

        // ── GPU layers sanity ──
        // Note: We can't detect actual GPU VRAM, but we can catch obvious misconfigurations.
        if (gpuLayers < 0)
        {
            return new ModelLoadException(
                ModelLoadPhase.Validate, modelPath, gpuLayers, contextSize,
                $"GPU layers is {gpuLayers} — must be >= 0. Set llm.gpu_layers to 0 for CPU-only.");
        }

        if (gpuLayers > 99)
        {
            return new ModelLoadException(
                ModelLoadPhase.Validate, modelPath, gpuLayers, contextSize,
                $"GPU layers is {gpuLayers} — max is 100. This is likely a misconfiguration.");
        }

        // ── Context size sanity ──
        if (contextSize == 0)
        {
            return new ModelLoadException(
                ModelLoadPhase.Validate, modelPath, gpuLayers, contextSize,
                "Context size is 0 — must be > 0. Set llm.context_size in appsettings.json.");
        }

        if (contextSize > 65536)
        {
            _logger?.Warn("Validate",
                $"Context size {contextSize} is very large — ensure you have enough RAM. " +
                $"If you get OOM errors, reduce llm.context_size.");
        }

        // ── Threads sanity ──
        if (threads == 0)
        {
            return new ModelLoadException(
                ModelLoadPhase.Validate, modelPath, gpuLayers, contextSize,
                "Threads is 0 — must be > 0 or -1 (auto). Set llm.threads in appsettings.json.");
        }

        // All checks passed
        return null;
    }

    /// <summary>
    /// Validate from an EAgentConfig. Convenience overload.
    /// </summary>
    public ModelLoadException? Validate(EAgentConfig config, string resolvedModelPath)
    {
        return Validate(
            resolvedModelPath,
            config.Llm.GpuLayers,
            config.Llm.ContextSize,
            config.Llm.Threads);
    }
}