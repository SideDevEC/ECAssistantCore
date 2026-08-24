using System.Text.Json.Serialization;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Exception thrown when model loading or context creation fails.
/// Contains structured diagnostic info so callers can display a helpful error
/// instead of a raw server error. Errors are returned by ECAssistantLLM server
/// during model load or session creation.
/// </summary>
public class ModelLoadException : Exception
{
    /// <summary>What phase failed: LoadWeights, CreateContext, Prefill, or Validate.</summary>
    public ModelLoadPhase Phase { get; }

    /// <summary>The model path that was attempted.</summary>
    public string ModelPath { get; }

    /// <summary>GPU layers requested (server-side param, 0 = not applicable in Core).</summary>
    public int GpuLayers { get; }

    /// <summary>Context size requested.</summary>
    public uint ContextSize { get; }

    /// <summary>The inner native exception (if any).</summary>
    public Exception? NativeError => InnerException;

    public ModelLoadException(
        ModelLoadPhase phase,
        string modelPath,
        int gpuLayers,
        uint contextSize,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        Phase = phase;
        ModelPath = modelPath;
        GpuLayers = gpuLayers;
        ContextSize = contextSize;
    }

    /// <summary>Build a user-friendly diagnostic string with actionable suggestions.</summary>
    public string ToDiagnosticString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"╔══════════════════════════════════════════════════════════╗");
        sb.AppendLine($"║  MODEL LOAD FAILURE — {Phase}");
        sb.AppendLine($"╠══════════════════════════════════════════════════════════╣");
        sb.AppendLine($"║  Error: {Message}");
        sb.AppendLine($"║  Model: {ModelPath}");
        sb.AppendLine($"║  GPU Layers: {GpuLayers}  |  Context: {ContextSize} tokens");
        if (NativeError != null)
        {
            sb.AppendLine($"║  Native error: {NativeError.GetType().Name}: {NativeError.Message}");
            if (NativeError.Message.Length > 200)
            {
                // Truncate long native errors
                sb.AppendLine($"║  (full error in log file)");
            }
        }
        sb.AppendLine($"╠══════════════════════════════════════════════════════════╣");
        sb.AppendLine($"║  POSSIBLE FIXES:");
        sb.AppendLine(GetSuggestions());
        sb.AppendLine($"╚══════════════════════════════════════════════════════════╝");
        return sb.ToString();
    }

    private string GetSuggestions()
    {
        var sb = new System.Text.StringBuilder();

        switch (Phase)
        {
            case ModelLoadPhase.Validate:
                sb.AppendLine($"║  • Check the model path exists and is a valid .gguf file");
                sb.AppendLine($"║  • Check appsettings.json → llm.model_path is correct");
                break;

            case ModelLoadPhase.LoadWeights:
                sb.AppendLine($"║  • GPU layers = {GpuLayers} — if this machine has no GPU or");
                sb.AppendLine($"║    insufficient VRAM, set llm.gpu_layers to 0 in appsettings.json");
                sb.AppendLine($"║  • Check the .gguf file is not corrupted (re-download if needed)");
                sb.AppendLine($"║  • Check the model format is supported by the ECAssistantLLM server");
                break;

            case ModelLoadPhase.CreateContext:
                sb.AppendLine($"║  • Context size = {ContextSize} — if too large for available VRAM,");
                sb.AppendLine($"║    reduce llm.context_size in appsettings.json (try 4096 or 2048)");
                sb.AppendLine($"║  • Check server VRAM and max_vram_mb in llm-server.json");
                break;

            case ModelLoadPhase.Prefill:
                sb.AppendLine($"║  • Prefill timed out — model may be too large for CPU-only mode");
                sb.AppendLine($"║  • Try reducing context_size or using a smaller model");
                sb.AppendLine($"║  • Check system memory availability");
                break;
        }

        return sb.ToString();
    }
}

/// <summary>Phase of model loading that failed.</summary>
public enum ModelLoadPhase
{
    /// <summary>Pre-flight validation (file exists, params sanity).</summary>
    Validate,

    /// <summary>Loading GGUF weights from disk into RAM.</summary>
    LoadWeights,

/// <summary>Creating server-side session (allocates KV cache on ECAssistantLLM).</summary>
    CreateContext,

    /// <summary>Prefilling the static prefix into KV cache.</summary>
    Prefill,
}