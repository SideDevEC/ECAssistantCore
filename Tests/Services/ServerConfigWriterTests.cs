using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Services.Http;
using Xunit;

namespace ECAssistant.Core.Tests.Services;

/// <summary>
/// Core owns the LLM server config: before launch, {llmRoot}/llm-server.json must exist
/// and contain entries for the appsettings model selections, with root-contained paths.
/// The launcher always passes that explicit config path so the server never invents defaults.
/// </summary>
public class ServerConfigWriterTests : IDisposable
{
    private readonly string _appRoot;
    private readonly string _llmRoot;
    private readonly string _modelsDir;

    public ServerConfigWriterTests()
    {
        _appRoot = Path.Combine(Path.GetTempPath(), "scw-" + Guid.NewGuid().ToString("N")[..8]);
        _llmRoot = Path.Combine(_appRoot, "llm");
        _modelsDir = Path.Combine(_llmRoot, "models");
        Directory.CreateDirectory(_modelsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_appRoot, true); } catch { }
    }

    private LlmProviderConfig Provider(string modelId = "main", string? embeddingId = "embeddings") =>
        new() { Mode = "local", Port = 58777, ModelId = modelId, EmbeddingModelId = embeddingId };

    private void WriteAppsettings(string? chatModelPath = null, string? embeddingModelPath = null)
    {
        var obj = new Dictionary<string, object>();
        if (chatModelPath != null)
            obj["llm"] = new Dictionary<string, object> { ["model_path"] = chatModelPath };
        if (embeddingModelPath != null)
            obj["embedding"] = new Dictionary<string, object> { ["model_path"] = embeddingModelPath };
        var json = JsonSerializer.Serialize(obj);
        File.WriteAllText(Path.Combine(_appRoot, "appsettings.json"), json);
    }

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "stub");
        return path;
    }

    private static JsonElement ParseModels(string configPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        return doc.RootElement.GetProperty("models").Clone();
    }

    // ── Config creation from appsettings selections ──

    [Fact]
    public void EnsureServerConfig_NoConfig_WritesEntriesFromAppsettings()
    {
        var chat = Touch(Path.Combine(_modelsDir, "chat-model.gguf"));
        var embed = Touch(Path.Combine(_modelsDir, "embed-model.gguf"));
        WriteAppsettings(chat, embed);

        Assert.True(ServerConfigWriter.EnsureServerConfig(_appRoot, _llmRoot, Provider()));

        var configPath = ServerConfigWriter.GetConfigPath(_llmRoot);
        Assert.True(File.Exists(configPath));
        var models = ParseModels(configPath);
        Assert.Equal(2, models.GetArrayLength());

        var main = models[0];
        Assert.Equal("main", main.GetProperty("id").GetString());
        Assert.False(main.GetProperty("is_embedding").GetBoolean());
        Assert.Equal(chat, main.GetProperty("path").GetString());

        var embeddings = models[1];
        Assert.Equal("embeddings", embeddings.GetProperty("id").GetString());
        Assert.True(embeddings.GetProperty("is_embedding").GetBoolean());
        Assert.Equal(embed, embeddings.GetProperty("path").GetString());
    }

    [Fact]
    public void EnsureServerConfig_MissingEntry_ModelOnDisk_GetsAdded()
    {
        // Pre-existing config with only chat model; embedding model exists on disk
        var configPath = ServerConfigWriter.GetConfigPath(_llmRoot);
        var chat = Touch(Path.Combine(_modelsDir, "chat-model.gguf"));
        File.WriteAllText(configPath, $$"""
            { "server": { "host": "localhost", "port": 58777 },
              "models": [ { "id": "main", "path": "{{chat.Replace("\\", "\\\\")}}", "gpu_layers": 10, "context_size": 8192 } ],
              "inference": {}, "logging": {} }
            """);
        var embed = Touch(Path.Combine(_modelsDir, "embed-model.gguf"));
        WriteAppsettings(embeddingModelPath: embed);

        Assert.True(ServerConfigWriter.EnsureServerConfig(_appRoot, _llmRoot, Provider()));

        var models = ParseModels(configPath);
        Assert.Equal(2, models.GetArrayLength());

        // Existing entry keeps its tuning
        Assert.Equal(10, models[0].GetProperty("gpu_layers").GetInt32());
        Assert.Equal(8192u, models[0].GetProperty("context_size").GetUInt32());
        // Missing embedding entry was added from disk
        Assert.Equal("embeddings", models[1].GetProperty("id").GetString());
    }

    [Fact]
    public void EnsureServerConfig_StalePath_UpdatedToRootContained()
    {
        var configPath = ServerConfigWriter.GetConfigPath(_llmRoot);
        var currentChat = Touch(Path.Combine(_modelsDir, "chat-model.gguf"));
        File.WriteAllText(configPath, """
            { "server": { "host": "localhost", "port": 58777 },
              "models": [ { "id": "main", "path": "/does/not/exist/old-model.gguf" } ],
              "inference": {}, "logging": {} }
            """);
        WriteAppsettings(currentChat);

        Assert.True(ServerConfigWriter.EnsureServerConfig(_appRoot, _llmRoot, Provider()));

        var models = ParseModels(configPath);
        Assert.Equal(currentChat, models[0].GetProperty("path").GetString());
    }

    [Fact]
    public void EnsureServerConfig_ModelOutsideRoot_NotWritten()
    {
        // appsettings points outside the app root and the file is not in llm/models
        WriteAppsettings("/tmp/somewhere-else/outside.gguf");

        Assert.True(ServerConfigWriter.EnsureServerConfig(_appRoot, _llmRoot, Provider()));

        var models = ParseModels(ServerConfigWriter.GetConfigPath(_llmRoot));
        Assert.Equal(0, models.GetArrayLength());
    }

    // ── Launcher arguments ──

    [Fact]
    public void BuildServerArguments_AlwaysPassesExplicitConfigPath()
    {
        var llmRoot = "/app/root/llm";
        var configPath = ServerConfigWriter.GetConfigPath(llmRoot);

        var args = ServerLauncher.BuildServerArguments(llmRoot, configPath, portOverride: 58777);

        Assert.Equal($"--root \"{llmRoot}\" \"{configPath}\" --port 58777", args);
        Assert.Contains("llm-server.json", args);
    }

    [Fact]
    public void BuildServerArguments_RemoteEmbedding_OmitsPort()
    {
        var args = ServerLauncher.BuildServerArguments("/app/llm", "/app/llm/llm-server.json", null);
        Assert.DoesNotContain("--port", args);
    }
}
