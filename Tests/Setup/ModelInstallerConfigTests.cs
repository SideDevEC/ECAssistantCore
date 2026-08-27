using ECAssistant.Core.Setup;
using Xunit;

namespace ECAssistant.Core.Tests.Setup;

/// <summary>Config merge behaviour — mmproj wiring, id replacement, config preservation.</summary>
public class ModelInstallerConfigTests : IDisposable
{
    private readonly string _dir;
    private readonly string _modelsDir;
    private readonly string _configPath;

    public ModelInstallerConfigTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "installer-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        _modelsDir = Path.Combine(_dir, "models");
        Directory.CreateDirectory(_modelsDir);
        _configPath = Path.Combine(_dir, "llm-server.json");
    }

    [Fact]
    public void ApplyToServerConfig_NoConfig_CreatesMinimalConfig()
    {
        var installer = new ModelInstallerService(new HttpClient(), _modelsDir, _configPath);
        installer.ApplyToServerConfig(ChatEntry());

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_configPath));
        var models = doc.RootElement.GetProperty("models");
        Assert.Equal(1, models.GetArrayLength());
        var m = models[0];
        Assert.Equal("test-chat", m.GetProperty("id").GetString());
        Assert.False(m.GetProperty("is_embedding").GetBoolean());
        Assert.False(m.TryGetProperty("mmproj_path", out _));
    }

    [Fact]
    public void ApplyToServerConfig_VisionEntry_SetsMmprojPath()
    {
        var installer = new ModelInstallerService(new HttpClient(), _modelsDir, _configPath);
        installer.ApplyToServerConfig(VisionEntry());

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_configPath));
        var m = doc.RootElement.GetProperty("models")[0];
        Assert.Contains("mmproj-test.gguf", m.GetProperty("mmproj_path").GetString());
    }

    [Fact]
    public void ApplyToServerConfig_SameId_ReplacesInsteadOfDuplicating()
    {
        var installer = new ModelInstallerService(new HttpClient(), _modelsDir, _configPath);
        installer.ApplyToServerConfig(ChatEntry());
        var modified = ChatEntry();
        modified.SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 10, ContextSize = 2048 };
        installer.ApplyToServerConfig(modified);

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_configPath));
        Assert.Equal(1, doc.RootElement.GetProperty("models").GetArrayLength());
        Assert.Equal(10, doc.RootElement.GetProperty("models")[0].GetProperty("gpu_layers").GetInt32());
    }

    [Fact]
    public void ApplyToServerConfig_PreservesOtherEntriesAndSections()
    {
        File.WriteAllText(_configPath,
            """{"server":{"port":9999},"models":[{"id":"keep","path":"/x/y.gguf"}],"logging":{"level":"warn"}}""");
        var installer = new ModelInstallerService(new HttpClient(), _modelsDir, _configPath);
        installer.ApplyToServerConfig(ChatEntry());

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_configPath));
        Assert.Equal(9999, doc.RootElement.GetProperty("server").GetProperty("port").GetInt32());
        Assert.Equal("warn", doc.RootElement.GetProperty("logging").GetProperty("level").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("models").GetArrayLength());
        Assert.Equal("keep", doc.RootElement.GetProperty("models")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task InstallAsync_SkipsExistingFiles_AndWritesConfig()
    {
        // Pre-place the primary file so no network call happens
        var entry = ChatEntry();
        File.WriteAllText(Path.Combine(_modelsDir, entry.Files[0].Filename), "fake");
        var installer = new ModelInstallerService(new HttpClient(), _modelsDir, _configPath);

        var result = await installer.InstallAsync(entry);

        Assert.True(result.Success);
        Assert.Empty(result.DownloadedFiles);
        Assert.True(File.Exists(_configPath));
    }

    [Fact]
    public void RegisterLocalModelFile_AutoDetectsMmproj_AndWritesDefaults()
    {
        File.WriteAllText(Path.Combine(_modelsDir, "Qwen2.5-VL-7B-Instruct-Q4_K_M.gguf"), "fake");
        File.WriteAllText(Path.Combine(_modelsDir, "mmproj-Qwen2.5-VL-7B-Instruct-f16.gguf"), "fake");
        using var http = new HttpClient();
        var installer = new ModelInstallerService(http, _modelsDir, _configPath);

        installer.RegisterLocalModelFile("Qwen2.5-VL-7B-Instruct-Q4_K_M.gguf");

        var json = File.ReadAllText(_configPath);
        Assert.Contains("\"batch_size\": 512", json);
        Assert.Contains("\"context_size\": 65536", json);
        Assert.Contains("\"gpu_layers\": 0", json);
        Assert.Contains("mmproj-Qwen2.5-VL-7B-Instruct-f16.gguf", json);

        // A different model with no token overlap must NOT get the projector linked
        File.WriteAllText(Path.Combine(_modelsDir, "Llama-3-8B-Instruct-Q4_K_M.gguf"), "fake");
        installer.RegisterLocalModelFile("Llama-3-8B-Instruct-Q4_K_M.gguf");
        var second = File.ReadAllText(_configPath);
        Assert.Contains("local-llama-3-8b-instruct", second);
        // exactly one mmproj_path in the whole config → only the Qwen entry got one
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(second, "mmproj_path").Count);
    }

    private static ModelCatalogEntry ChatEntry() => new()
    {
        Id = "test-chat", DisplayName = "Test Chat", Category = CatalogModelCategory.Chat,
        HfRepo = "org/test",
        Files = { new CatalogModelFile { Filename = "test-chat.gguf", SizeGb = 0.001 } },
        SuggestedConfig = new CatalogSuggestedConfig { GpuLayers = 99, ContextSize = 4096 }
    };

    private static ModelCatalogEntry VisionEntry() => new()
    {
        Id = "test-vision", DisplayName = "Test Vision", Category = CatalogModelCategory.Vision,
        HfRepo = "org/test",
        Files =
        {
            new CatalogModelFile { Filename = "test-vision.gguf" },
            new CatalogModelFile { Filename = "mmproj-test.gguf" }
        },
        MmprojFile = "mmproj-test.gguf"
    };

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
