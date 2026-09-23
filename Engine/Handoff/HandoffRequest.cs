namespace ECAssistant.Core.Engine;

/// <summary>
/// Describes a handoff: the specialist's persona, constraints, and the context
/// the parent agent passes along. All fields are supplied by the model at call
/// time (ephemeral) or by the host via <see cref="AgentSession.RegisterHandoff"/>.
/// Nothing is persisted — the specialist lives only for the duration of the handoff.
/// </summary>
public sealed record HandoffRequest
{
    /// <summary>Human-readable name for logging/UI (e.g. "sql-expert").</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Full system prompt for the specialist agent. The model writes this at
    /// handoff time — "You are a SQL optimization specialist. The schema has…"
    /// This replaces the main agent's system prompt entirely.
    /// </summary>
    public string SystemPrompt { get; init; } = "";

    /// <summary>
    /// Comma-separated tool names the specialist may use. Empty = all built-in tools.
    /// Restricting tools focuses the specialist and prevents scope creep.
    /// </summary>
    public string AllowedTools { get; init; } = "";

    /// <summary>Why the parent is handing off (for logging/UI only).</summary>
    public string Reason { get; init; } = "";

    /// <summary>
    /// Context summary the parent passes to the specialist — what's been done,
    /// what files exist, what the user wants. Becomes the opening user message.
    /// </summary>
    public string ContextSummary { get; init; } = "";

    /// <summary>Optional: different model ID for the specialist (same endpoint).</summary>
    public string? ModelOverride { get; init; }

    /// <summary>Max turns for the specialist (default: 8).</summary>
    public int MaxTurns { get; init; } = 8;

    /// <summary>Timeout in seconds (default: 180).</summary>
    public int TimeoutSeconds { get; init; } = 180;
}