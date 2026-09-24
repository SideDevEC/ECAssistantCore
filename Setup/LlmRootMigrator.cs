using System.IO;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Moves the legacy hidden LLM root (~/.ECAssistantLLM) to the new visible root (~/ECALLM).
/// A Directory.Move is used (same volume ⇒ atomic rename, contents untouched), so models,
/// server binaries, config, and logs survive without copying. Idempotent.
/// </summary>
public sealed class LlmRootMigrator : ILlmRootMigrator
{
    /// <summary>The pre-15.0 hidden root, kept for one-time migration detection.</summary>
    public const string LegacyRootPath = "~/.ECAssistantLLM";

    private readonly string _legacyRoot;
    private readonly string _targetRoot;

    /// <param name="targetRoot">Fully-expanded new root (e.g. /Users/x/ECALLM).</param>
    /// <param name="legacyRoot">Fully-expanded legacy root (e.g. /Users/x/.ECAssistantLLM).</param>
    public LlmRootMigrator(string targetRoot, string legacyRoot)
    {
        _targetRoot = targetRoot ?? throw new ArgumentNullException(nameof(targetRoot));
        _legacyRoot = legacyRoot ?? throw new ArgumentNullException(nameof(legacyRoot));
    }

    public LlmRootMigrationResult Migrate()
    {
        bool legacyExists = Directory.Exists(_legacyRoot);
        bool targetExists = Directory.Exists(_targetRoot);

        if (!legacyExists)
        {
            return new LlmRootMigrationResult(LlmRootMigrationOutcome.NotNeeded);
        }

        if (targetExists)
        {
            return new LlmRootMigrationResult(LlmRootMigrationOutcome.Conflict,
                $"Both {_legacyRoot} and {_targetRoot} exist. " +
                "Move models/, server/, and llm-server.json from the old directory manually, then delete it.");
        }

        try
        {
            Directory.Move(_legacyRoot, _targetRoot);
            return new LlmRootMigrationResult(LlmRootMigrationOutcome.Migrated);
        }
        catch (IOException ex)
        {
            return new LlmRootMigrationResult(LlmRootMigrationOutcome.Failed, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new LlmRootMigrationResult(LlmRootMigrationOutcome.Failed, ex.Message);
        }
    }
}
