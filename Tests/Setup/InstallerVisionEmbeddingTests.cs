using System.Text.Json;
using System.Text.Json.Nodes;
using ECAssistant.Core.Setup;

namespace ECAssistant.Core.Tests.Setup;

/// <summary>
/// Vision-capability + embeddings-mode wiring: mmproj pairing, vision_enabled flag,
/// appsettings sync (model_path + minimal-file creation), orphan defaults, and
/// embedding-mode persistence.
/// </summary>
public class InstallerVisionEmbeddingTests : IDisposable
{
    private readonly string _dir;
    private readonly string _modelsDir;
    private readonly string _serverConfigPath;
    private readonly string _appsettingsPath;

    public InstallerVisionEmbeddingTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "eca-vision-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        _modelsDir = Path.Combine(_dir, "models");
        Directory.CreateDirectory(_modelsDir);
        _serverConfigPath = Path.Combine(_dir, "llm-server.json");
        _appsettingsPath = Path.Combine(_dir, "appsettings.json");
    }

    private ModelInstallerService CreateInstaller() =>
        new(new HttpClient(), _modelsDir, _serverConfigPath, _appsettingsPath);

    // ── mmproj sibling detection ──

    [Fact]
    public void DetectSiblingMmproj_MatchingProjector_Found()
    {
        File.WriteAllText(Path.Combine(_modelsDir, "Qwen2.5-VL-7B-Instruct-Q4_K_M.gguf"), "x");
        File.WriteAllText(Path.Combine(_modelsDir, "mmproj-Qwen2.5-VL-7B-Instruct-f16.gguf"), "x");
        var installer = CreateInstaller();

        Assert.Equal("mmproj-Qwen2.5-VL-7B-Instruct-f16.gguf",
            installer.DetectSiblingMmproj("Qwen2.5-VL-7B-Instruct-Q4_K_M.gguf"));
    }

    [Fact]
    public void DetectSiblingMmproj_UnrelatedProjector_NotMatched()
    {
        File.WriteAllText(Path.Combine(_modelsDir, "Llama-3-8B-Q4_K_M.gguf"), "x");
        File.WriteAllText(Path.Combine(_modelsDir, "mmproj-Qwen2.5-VL-7B-Instruct-f16.gguf"), "x");
        var installer = CreateInstaller();

        Assert.Null(installer.DetectSiblingMmproj("Llama-3-8B-Q4_K_M.gguf"));
    }

    [Fact]
    public void DetectSiblingMmproj_EmptyModelsDir_ReturnsNull()
    {
        var installer = CreateInstaller();
        Assert.Null(installer.DetectSiblingMmproj("any-model.gguf"));
    }

    // ── Vision flag + appsettings sync via ApplyToServerConfig ──

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

    private static ModelCatalogEntry ChatEntry() => new()
    {
        Id = "test-chat", DisplayName = "Test Chat", Category = CatalogModelCategory.Chat,
        HfRepo = "org/test",
        Files = { new CatalogModelFile { Filename = "test-chat.gguf", SizeGb = 0.001 } }
    };

    [Fact]
    public void Apply_VisionEntry_CreatesAppsettings_WithVisionTrue_AndModelPath()
    {
        var installer = CreateInstaller();
        installer.ApplyToServerConfig(VisionEntry());

        // appsettings.json did not exist → minimal file created by the installer
        Assert.True(File.Exists(_appsettingsPath), "installer must create appsettings.json when missing");
        var root = JsonNode.Parse(File.ReadAllText(_appsettingsPath))!.AsObject();
        Assert.Equal(true, root["llm_provider"]!["vision_enabled"]!.GetValue<bool>());
        Assert.Contains("test-vision.gguf", root["llm"]!["model_path"]!.GetValue<string>());
        // mmproj wired into the server config too
        var server = JsonNode.Parse(File.ReadAllText(_serverConfigPath))!;
        Assert.Contains("mmproj-test.gguf", server["models"]!.AsArray()[0]!["mmproj_path"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_ChatEntry_SetsVisionFalse()
    {
        var installer = CreateInstaller();
        installer.ApplyToServerConfig(ChatEntry());

        var root = JsonNode.Parse(File.ReadAllText(_appsettingsPath))!.AsObject();
        Assert.Equal(false, root["llm_provider"]!["vision_enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void Apply_VisionEntry_OnExistingAppsettings_PreservesSections_AndFlipsFlag()
    {
        File.WriteAllText(_appsettingsPath,
            """{"root_path":"/tmp/eca","llm_provider":{"mode":"remote","vision_enabled":false},"memory":{"enabled":true}}""");
        var installer = CreateInstaller();

        installer.ApplyToServerConfig(VisionEntry());

        var root = JsonNode.Parse(File.ReadAllText(_appsettingsPath))!.AsObject();
        Assert.Equal("/tmp/eca", root["root_path"]!.GetValue<string>());          // preserved
        Assert.Equal(true, root["llm_provider"]!["vision_enabled"]!.GetValue<bool>()); // flipped
        Assert.Equal("remote", root["llm_provider"]!["mode"]!.GetValue<string>());     // mode untouched
        Assert.Equal(true, root["memory"]!["enabled"]!.GetValue<bool>());              // untouched
    }

    // ── Orphan local model registration defaults ──

    [Fact]
    public void RegisterLocalModelFile_ChatOrphan_CpuDefaults_64kCtx_Batch1024()
    {
        File.WriteAllText(Path.Combine(_modelsDir, "my-orphan-model.gguf"), "x");
        var installer = CreateInstaller();

        installer.RegisterLocalModelFile("my-orphan-model.gguf");

        var server = JsonNode.Parse(File.ReadAllText(_serverConfigPath))!;
        var entry = server["models"]!.AsArray()[0]!.AsObject();
        Assert.Equal("local-my-orphan-model", entry["id"]!.GetValue<string>());
        Assert.Equal(0, entry["gpu_layers"]!.GetValue<int>());
        Assert.Equal(65536, entry["context_size"]!.GetValue<int>());
        // v15 context tier: ApplyToServerConfig bumps chat-model batch to >= 1024
        Assert.Equal(1024, entry["batch_size"]!.GetValue<int>());
        Assert.Null(entry["mmproj_path"]);
    }

    [Fact]
    public void RegisterLocalModelFile_EmbeddingOrphan_MeanPooling_SmallContext()
    {
        File.WriteAllText(Path.Combine(_modelsDir, "my-embedder.gguf"), "x");
        var installer = CreateInstaller();

        installer.RegisterLocalModelFile("my-embedder.gguf", isEmbedding: true);

        var server = JsonNode.Parse(File.ReadAllText(_serverConfigPath))!;
        var entry = server["models"]!.AsArray()[0]!.AsObject();
        Assert.Equal(true, entry["is_embedding"]!.GetValue<bool>());
        Assert.Equal("mean", entry["pooling_type"]!.GetValue<string>());
        Assert.Equal(2048, entry["context_size"]!.GetValue<int>());
    }

    // ── LooksLikeEmbeddingModel classification ──

    [Theory]
    [InlineData("all-MiniLM-L6-v2-Q5_K_M.gguf", true)]
    [InlineData("bge-small-en-v1.5.gguf", true)]
    [InlineData("nomic-embed-text-v1.5.gguf", true)]
    [InlineData("Qwen_Qwen3-8B-Q4_K_M.gguf", false)]
    [InlineData("mmproj-Qwen2.5-VL-f16.gguf", false)]
    public void LooksLikeEmbeddingModel_ClassifiesCorrectly(string filename, bool expected)
    {
        Assert.Equal(expected, ModelInstallerService.LooksLikeEmbeddingModel(filename));
    }

    // ── EmbeddingSetupWriter ──

    [Fact]
    public void EmbeddingWriter_LocalMode_Persists()
    {
        new EmbeddingSetupWriter(_appsettingsPath).SetMode("local");

        var root = JsonNode.Parse(File.ReadAllText(_appsettingsPath))!.AsObject();
        Assert.Equal("local", root["embedding"]!["mode"]!.GetValue<string>());
        Assert.Equal(true, root["embedding"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void EmbeddingWriter_RemoteMode_WithModelId()
    {
        new EmbeddingSetupWriter(_appsettingsPath).SetMode("remote", modelId: "text-embedding-3-small");

        var root = JsonNode.Parse(File.ReadAllText(_appsettingsPath))!.AsObject();
        Assert.Equal("remote", root["embedding"]!["mode"]!.GetValue<string>());
        Assert.Equal("text-embedding-3-small", root["embedding"]!["model_id"]!.GetValue<string>());
    }

    [Fact]
    public void EmbeddingWriter_PreservesOtherSections()
    {
        File.WriteAllText(_appsettingsPath, """{"root_path":"/tmp/eca","llm":{"model_path":"a.gguf"}}""");
        new EmbeddingSetupWriter(_appsettingsPath).SetMode("local");

        var root = JsonNode.Parse(File.ReadAllText(_appsettingsPath))!.AsObject();
        Assert.Equal("/tmp/eca", root["root_path"]!.GetValue<string>());
        Assert.Equal("a.gguf", root["llm"]!["model_path"]!.GetValue<string>());
        Assert.Equal("local", root["embedding"]!["mode"]!.GetValue<string>());
    }

    // ── RemoveModelFile safety ──

    [Fact]
    public void RemoveModelFile_RejectsPathTraversal()
    {
        var installer = CreateInstaller();
        Assert.False(installer.RemoveModelFile("../escape.gguf"));
        Assert.False(installer.RemoveModelFile("sub/dir.gguf"));
        Assert.False(installer.RemoveModelFile(""));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
