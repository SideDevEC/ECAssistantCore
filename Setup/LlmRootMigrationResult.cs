namespace ECAssistant.Core.Setup;

/// <summary>Result of one migration attempt; safe to show to the user.</summary>
public sealed record LlmRootMigrationResult(LlmRootMigrationOutcome Outcome, string? Error = null)
{
    public bool Performed => Outcome == LlmRootMigrationOutcome.Migrated;
}
