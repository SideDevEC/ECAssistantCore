namespace ECAssistant.Core.Setup;

/// <summary>
/// Migrates an existing legacy LLM root directory (<c>~/.ECAssistantLLM</c>, hidden)
/// to the new visible root (<c>~/ECALLM</c>) so upgrades keep models, server
/// binaries, and config intact. One-time, idempotent, no-op on fresh installs.
/// </summary>
public interface ILlmRootMigrator
{
    /// <summary>
    /// Performs the legacy → new root migration.
    /// Never throws: failures are reported in the result for the caller to surface.
    /// </summary>
    LlmRootMigrationResult Migrate();
}
