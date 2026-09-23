using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Pre-flight validation for model loading parameters.
/// Catches common misconfigurations BEFORE connecting to ECAssistantLLM server,
/// so users get a clear message instead of an opaque server error.
/// Note: GPU layers, threads, batch size are server-side concerns
/// (validated by ECAssistantLLM, not Core).
/// </summary>
public class ModelParamValidator : IModelParamValidator
{
    private readonly ILogger? _logger;

    public ModelParamValidator(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validate model loading parameters. Returns null if OK, or a ModelLoadException
    /// with a diagnostic message if something is wrong.
    /// Only validates Core-side concerns (model path, context size).
    /// GPU/threads/batch are server-side — not validated here.
    /// </summary>
    public ModelLoadException? Validate(string modelPath, uint contextSize)
    {
        // ── Model file checks ──
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return new ModelLoadException(
                ModelLoadPhase.Validate, modelPath, 0, contextSize,
                "Model path is empty. Set llm.model_path in appsettings.json.");
        }

        if (!File.Exists(modelPath))
        {
            return new ModelLoadException(
                ModelLoadPhase.Validate, modelPath, 0, contextSize,
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
                    ModelLoadPhase.Validate, modelPath, 0, contextSize,
                    $"Model file is suspiciously small ({fileInfo.Length} bytes) — may be corrupted or incomplete.");
            }
        }
        catch { /* can't check — proceed anyway */ }

        // ── Context size sanity ──
        if (contextSize == 0)
        {
            return new ModelLoadException(
                ModelLoadPhase.Validate, modelPath, 0, contextSize,
                "Context size is 0 — must be > 0. Set llm.context_size in appsettings.json.");
        }

        if (contextSize > 65536)
        {
            _logger?.Warn("Validate",
                $"Context size {contextSize} is very large — ensure the server has enough VRAM. " +
                $"If you get OOM errors, reduce llm.context_size or server max_vram_mb.");
        }

        // All checks passed
        return null;
    }

    /// <summary>
    /// Validate from an AppConfig. Convenience overload.
    /// </summary>
    public ModelLoadException? Validate(AppConfig config, string resolvedModelPath)
    {
        return Validate(
            resolvedModelPath,
            config.Llm.ContextSize);
    }
}