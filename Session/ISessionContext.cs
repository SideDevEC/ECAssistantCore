using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Memory;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Session;

/// <summary>
/// Read-only session context exposed to tools.
///
/// Tools receive this via ToolBase.Session when they are registered.
/// It provides access to session info, memory, and background task config —
/// but NOT the main engine's inference.
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

    /// <summary>Background task config (decompose + summarize settings).</summary>
    BackgroundTasksConfig? BackgroundTasks { get; }

    /// <summary>Keyword-based memory manager.</summary>
    MemoryManager Memory { get; }

    /// <summary>Vector memory store (semantic search), if initialized.</summary>
    VectorMemoryStore? VectorMemory { get; }
}