namespace ECAssistant.Core.Setup;

/// <summary>
/// Unified first-run / reinstall orchestration seam, shared by ALL hosts
/// (Console, TUI, embedded consumers). Detects installation state from disk
/// truth and runs the staged wizard when needed.
///
/// Implementations must never throw for expected I/O failures from
/// <see cref="RunIfNeededAsync"/> — setup must not block application startup.
/// Pure static utilities (IsLocalModelUsable, IsRemoteProviderConfigured,
/// IsLocalEmbeddingsRequested) remain on <see cref="FirstRunOrchestrator"/> —
/// stateless functions, no interface needed.
/// </summary>
public interface IFirstRunOrchestrator
{
    /// <summary>Path into the shared LLM root.</summary>
    string LlmRoot { get; }

    /// <summary>Path to the LLM server config (llm-server.json).</summary>
    string ServerConfigPath { get; }

    /// <summary>Path to the installed LLM server binary.</summary>
    string ServerBinaryPath { get; }

    /// <summary>
    /// Runs setup when configs are missing or resolve to no usable model/provider.
    /// No-op when a usable provider is already configured.
    /// </summary>
    Task RunIfNeededAsync();
}