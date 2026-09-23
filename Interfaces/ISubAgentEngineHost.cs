namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Host-side surface the SubAgentManager needs from the main engine.
/// Decouples SubAgentManager from the concrete AgentEngine, breaking the
/// circular <c>this</c> reference in the object graph.
/// </summary>
public interface ISubAgentEngineHost
{
    /// <summary>Inference engine of the host (used to derive sub-agent endpoints).</summary>
    IInferenceEngine? InferenceEngine { get; }

    /// <summary>Cancellation token of the host execution (ESC propagation).</summary>
    CancellationToken ExecutionToken { get; }

    /// <summary>
    /// The host's shared OpenAI HTTP client (carries the Bearer api key in remote
    /// mode and the X-Client-Id registration in local mode). Child engines MUST
    /// reuse it — constructing a fresh client loses remote auth (401).
    /// </summary>
    Transport.OpenAIClient? SharedHttpClient { get; }

    /// <summary>True when the host runs against a local ECAssistantLLM server (KV sessions available).</summary>
    bool IsLocalMode { get; }
}
