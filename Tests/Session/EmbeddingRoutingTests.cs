using System.Text.Json;
using ECAssistant.Core;
using ECAssistant.Core.Config;
using ECAssistant.Core.Setup;
using Xunit;

namespace ECAssistant.Core.Tests.Session;

/// <summary>
/// Embedding routing: embedding.mode is independent of the main LLM mode.
/// local  → local server endpoint (llm_provider host:port, or embedding.endpoint override)
/// remote → main provider endpoint + provider embedding model id
/// </summary>
public class EmbeddingRoutingTests
{
    private static EAgentConfig RemoteMainConfig() => new()
    {
        LlmProvider = new LlmProviderConfig
        {
            Mode = "remote",
            Endpoint = "https://api.example.com/v1",
            EmbeddingModelId = "text-embedding-3-small",
            Port = 58777
        }
    };

    private static EAgentConfig LocalMainConfig(int port = 58777) => new()
    {
        LlmProvider = new LlmProviderConfig
        {
            Mode = "local",
            Host = "127.0.0.1",
            Port = port
        }
    };

    private static SessionBuilder Builder(EAgentConfig config) =>
        new(config, Path.GetTempPath(), Path.GetTempPath());

    // ── remote main + local embeddings (the combo that motivated the feature) ──

    [Fact]
    public void RemoteMain_LocalEmbeddings_RoutesToLocalServer()
    {
        var config = RemoteMainConfig();
        config.Embedding = new EmbeddingConfig { Enabled = true, Mode = "local" };

        var endpoint = Builder(config).ResolveEmbeddingEndpoint();
        var modelId = Builder(config).ResolveEmbeddingModelId();

        Assert.Equal("http://localhost:58777", endpoint);
        Assert.Equal("embeddings", modelId);
    }

    [Fact]
    public void RemoteMain_LocalEmbeddings_WithOverrides_UsesOverrides()
    {
        var config = RemoteMainConfig();
        config.Embedding = new EmbeddingConfig
        {
            Enabled = true,
            Mode = "local",
            Endpoint = "http://127.0.0.1:59001",
            ModelId = "minilm"
        };

        Assert.Equal("http://127.0.0.1:59001", Builder(config).ResolveEmbeddingEndpoint());
        Assert.Equal("minilm", Builder(config).ResolveEmbeddingModelId());
    }

    // ── remote main + remote embeddings ──

    [Fact]
    public void RemoteMain_RemoteEmbeddings_RoutesToProvider()
    {
        var config = RemoteMainConfig();
        config.Embedding = new EmbeddingConfig { Enabled = true, Mode = "remote" };

        var builder = Builder(config);
        Assert.Equal("https://api.example.com/v1", builder.ResolveEmbeddingEndpoint());
        Assert.Equal("text-embedding-3-small", builder.ResolveEmbeddingModelId());
    }

    [Fact]
    public void RemoteMain_EmptyMode_DefaultsToRemote()
    {
        var config = RemoteMainConfig();
        config.Embedding = new EmbeddingConfig { Enabled = true, Mode = "" };

        Assert.Equal("https://api.example.com/v1", Builder(config).ResolveEmbeddingEndpoint());
    }

    // ── local main combos ──

    [Fact]
    public void LocalMain_LocalEmbeddings_RoutesToLocalServerPort()
    {
        var config = LocalMainConfig(59123);
        config.Embedding = new EmbeddingConfig { Enabled = true, Mode = "local" };

        Assert.Equal("http://localhost:59123", Builder(config).ResolveEmbeddingEndpoint());
    }

    [Fact]
    public void LocalMain_LocalEmbeddings_CustomModelId_Honored()
    {
        var config = LocalMainConfig();
        config.Embedding = new EmbeddingConfig { Enabled = true, Mode = "local", ModelId = "bge-small" };

        Assert.Equal("bge-small", Builder(config).ResolveEmbeddingModelId());
    }

    [Fact]
    public void LocalMain_RemoteEmbeddingsMode_StillLocalBecauseMainIsLocal()
    {
        var config = LocalMainConfig(59200);
        config.Embedding = new EmbeddingConfig { Enabled = true, Mode = "remote" };

        // mode "remote" means "the main AI provider" — with a local main, that's the local server
        Assert.Equal("http://127.0.0.1:59200", Builder(config).ResolveEmbeddingEndpoint());
    }

    // ── mode parsing is case-insensitive ──

    [Theory]
    [InlineData("LOCAL")]
    [InlineData("Local")]
    [InlineData("local")]
    public void Mode_CaseInsensitive_TreatedAsLocal(string mode)
    {
        var config = RemoteMainConfig();
        config.Embedding = new EmbeddingConfig { Enabled = true, Mode = mode };

        Assert.Equal("http://localhost:58777", Builder(config).ResolveEmbeddingEndpoint());
    }

    // ── persistence round-trip: writer output feeds the resolver ──

    [Fact]
    public void PersistedEmbeddingMode_RoundTripsThroughConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "eca-emb-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(path, """{"llm_provider":{"mode":"remote","endpoint":"https://api.example.com/v1"}}""");
            new EmbeddingSetupWriter(path).SetMode("local", modelId: "minilm");

            var config = JsonSerializer.Deserialize<EAgentConfig>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(config);
            Assert.Equal("local", config.Embedding.Mode);
            Assert.Equal("minilm", config.Embedding.ModelId);

            var builder = new SessionBuilder(config, dir, dir);
            Assert.Equal("http://localhost:58777", builder.ResolveEmbeddingEndpoint());
            Assert.Equal("minilm", builder.ResolveEmbeddingModelId());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
