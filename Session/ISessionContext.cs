using ECAssistant.Engine;
using ECAssistant.Memory;
using ECAssistant.Tools;

namespace ECAssistant.Session;

/// <summary>
/// Read-only session context exposed to tools.
///
/// Tools receive this via EToolBase.Session when they are registered.
/// It provides access to session info, memory, and the secondary LLM
/// — but NOT the main engine's inference (GenerateAsync, Prompt, etc.).
///
/// This prevents tools from interfering with the orchestration loop
/// while still giving them useful capabilities (secondary model, memory).
/// </summary>
public interface ISessionContext
{
    /// <summary>Session key (e.g., "main", "session-1").</summary>
    string Key { get; }

    /// <summary>User-provided session label.</summary>
    string? Label { get; }

    /// <summary>Tool policy — check permissions for tool execution.</summary>
    ToolPolicy Policy { get; }

    /// <summary>Current context window token usage.</summary>
    int ContextTokens { get; }

    /// <summary>Max context window tokens.</summary>
    uint MaxTokens { get; }

    /// <summary>Secondary LLM — smaller model for analysis, summarization, etc.</summary>
    SecondaryModelLoader? SecondaryModel { get; }

    /// <summary>Keyword-based memory manager.</summary>
    EMemoryManager Memory { get; }

    /// <summary>Vector memory store (semantic search), if initialized.</summary>
    VectorMemoryStore? VectorMemory { get; }
}