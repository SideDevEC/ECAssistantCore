using ECAssistant.Core.Setup;

namespace ECAssistant.Core.Tests.Setup;

public sealed class LlmRootMigratorTests : IDisposable
{
    private readonly string _tempDir;

    public LlmRootMigratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ecallm-migrator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Migrate_WhenNoLegacyDir_ReturnsNotNeeded()
    {
        var migrator = new LlmRootMigrator(
            Path.Combine(_tempDir, "ECALLM"),
            Path.Combine(_tempDir, ".legacy"));

        var result = migrator.Migrate();

        Assert.Equal(LlmRootMigrationOutcome.NotNeeded, result.Outcome);
        Assert.False(result.Performed);
    }

    [Fact]
    public void Migrate_WhenLegacyExists_MovesAllContent()
    {
        var legacy = Path.Combine(_tempDir, ".legacy");
        var target = Path.Combine(_tempDir, "ECALLM");
        Directory.CreateDirectory(Path.Combine(legacy, "models"));
        Directory.CreateDirectory(Path.Combine(legacy, "server"));
        File.WriteAllText(Path.Combine(legacy, "llm-server.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "models", "model.gguf"), "fake");

        var result = new LlmRootMigrator(target, legacy).Migrate();

        Assert.Equal(LlmRootMigrationOutcome.Migrated, result.Outcome);
        Assert.True(result.Performed);
        Assert.False(Directory.Exists(legacy));
        Assert.True(File.Exists(Path.Combine(target, "llm-server.json")));
        Assert.True(File.Exists(Path.Combine(target, "models", "model.gguf")));
        Assert.True(Directory.Exists(Path.Combine(target, "server")));
    }

    [Fact]
    public void Migrate_WhenBothRootsExist_ReturnsConflictAndLeavesData()
    {
        var legacy = Path.Combine(_tempDir, ".legacy");
        var target = Path.Combine(_tempDir, "ECALLM");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(legacy, "old.txt"), "old");

        var result = new LlmRootMigrator(target, legacy).Migrate();

        Assert.Equal(LlmRootMigrationOutcome.Conflict, result.Outcome);
        Assert.False(result.Performed);
        Assert.True(Directory.Exists(legacy));
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void Migrate_IsIdempotent_SecondRunNotNeeded()
    {
        var legacy = Path.Combine(_tempDir, ".legacy");
        var target = Path.Combine(_tempDir, "ECALLM");
        Directory.CreateDirectory(legacy);

        Assert.Equal(LlmRootMigrationOutcome.Migrated, new LlmRootMigrator(target, legacy).Migrate().Outcome);
        Assert.Equal(LlmRootMigrationOutcome.NotNeeded, new LlmRootMigrator(target, legacy).Migrate().Outcome);
    }

    [Fact]
    public void Migrate_WhenTargetIsFile_ReportsConflict()
    {
        var legacy = Path.Combine(_tempDir, ".legacy");
        var target = Path.Combine(_tempDir, "ECALLM-file");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(target, "not a dir");

        var result = new LlmRootMigrator(target, legacy).Migrate();

        Assert.NotEqual(LlmRootMigrationOutcome.Migrated, result.Outcome);
        Assert.True(Directory.Exists(legacy));
    }
}
