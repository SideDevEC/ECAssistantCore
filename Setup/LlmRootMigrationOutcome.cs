namespace ECAssistant.Core.Setup;

/// <summary>Outcome of a legacy LLM root migration attempt.</summary>
public enum LlmRootMigrationOutcome
{
    /// <summary>Nothing to do: no legacy directory present (fresh install or already migrated).</summary>
    NotNeeded,

    /// <summary>Legacy directory was moved to the new root successfully.</summary>
    Migrated,

    /// <summary>Both legacy and new root exist — ambiguous state; the user must resolve manually.</summary>
    Conflict,

    /// <summary>Move failed (IO error); legacy data left untouched.</summary>
    Failed
}
