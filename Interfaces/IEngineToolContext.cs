using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Read-only view of the engine's tool surface used by planning components
/// (e.g. StepMapper). Decouples planners from the concrete AgentEngine,
/// breaking the circular <c>this</c> reference in the object graph.
/// </summary>
public interface IEngineToolContext
{
    /// <summary>Registered tools (thread-safe snapshot).</summary>
    IReadOnlyList<ToolBase> Tools { get; }

    /// <summary>Generate a short execution plan using the main LLM (stateless).</summary>
    Task<string?> GeneratePlanAsync(string userRequest);
}
