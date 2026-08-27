using ECAssistant.Core.Setup;
using Xunit;

namespace ECAssistant.Core.Tests.Setup;

/// <summary>Catalog loading, default generation, validation, first-run detection.</summary>
public class ModelCatalogTests : IDisposable
{
    private readonly string _dir;

    public ModelCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "catalog-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void Load_MissingFile_WritesDefaultAndLoads()
    {
        var path = Path.Combine(_dir, "model-catalog.json");
        var doc = ModelCatalogDocument.Load(path);

        Assert.True(File.Exists(path), "default catalog should be written to disk (user-editable)");
        Assert.Contains(doc.Models, m => m.Category == CatalogModelCategory.Chat);
        Assert.Contains(doc.Models, m => m.Category == CatalogModelCategory.Vision);
        Assert.Contains(doc.Models, m => m.Category == CatalogModelCategory.Embedding);
        Assert.All(doc.Models.Where(m => m.Category == CatalogModelCategory.Vision),
            m => Assert.False(string.IsNullOrEmpty(m.MmprojFile)));
        Assert.Null(doc.Validate());
    }

    [Fact]
    public void Load_ExistingFile_Used()
    {
        var path = Path.Combine(_dir, "catalog.json");
        File.WriteAllText(path, """{"version":1,"models":[{"id":"x","name":"X","hf_repo":"org/x","files":[{"filename":"x.gguf"}]}]}""");
        var doc = ModelCatalogDocument.Load(path);
        Assert.Single(doc.Models);
        Assert.Equal("x", doc.Models[0].Id);
    }

    [Fact]
    public void Validate_DetectsVisionEntryWithoutMmproj()
    {
        var doc = new ModelCatalogDocument();
        doc.Models.Add(new ModelCatalogEntry
        {
            Id = "v", HfRepo = "org/v",
            Files = { new CatalogModelFile { Filename = "v.gguf" } },
            Category = CatalogModelCategory.Vision
        });
        Assert.NotNull(doc.Validate());
    }

    [Fact]
    public void DownloadUrl_ComposedFromRepoAndFile()
    {
        var entry = new ModelCatalogEntry
        {
            Id = "m", HfRepo = "org/repo",
            Files = { new CatalogModelFile { Filename = "m.gguf", HfPath = "sub/m.gguf" } }
        };
        Assert.Equal("https://huggingface.co/org/repo/resolve/main/sub/m.gguf", entry.GetDownloadUrl(entry.Files[0]));
    }

    [Fact]
    public void FirstRunDetector_NoModels_NeedsSetup()
    {
        var modelsDir = Path.Combine(_dir, "models");
        Directory.CreateDirectory(modelsDir);
        var configPath = Path.Combine(_dir, "llm-server.json");

        var status = new FirstRunDetector(modelsDir, configPath).Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.True(status.NeedsSetup);
        Assert.Empty(status.InstalledFiles);
    }

    [Fact]
    public void FirstRunDetector_PrimaryFilePresent_NoSetupNeeded()
    {
        var modelsDir = Path.Combine(_dir, "models");
        Directory.CreateDirectory(modelsDir);
        File.WriteAllText(Path.Combine(modelsDir, "Qwen_Qwen3-8B-Q4_K_M.gguf"), "fake");
        var configPath = Path.Combine(_dir, "llm-server.json");

        var status = new FirstRunDetector(modelsDir, configPath).Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.False(status.NeedsSetup);
        Assert.Contains("qwen3-8b", status.InstalledEntryIds);
    }

    [Fact]
    public void FirstRunDetector_ConfigWithExistingModelPath_NoSetupNeeded()
    {
        var modelsDir = Path.Combine(_dir, "models");
        Directory.CreateDirectory(modelsDir);
        var configPath = Path.Combine(_dir, "llm-server.json");
        var modelFile = Path.Combine(modelsDir, "custom.gguf");
        File.WriteAllText(modelFile, "fake");
        File.WriteAllText(configPath, $$"""{"models":[{"id":"c","path":"{{modelFile.Replace("\\", "\\\\")}}"}]}""");

        var status = new FirstRunDetector(modelsDir, configPath).Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.False(status.NeedsSetup);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
