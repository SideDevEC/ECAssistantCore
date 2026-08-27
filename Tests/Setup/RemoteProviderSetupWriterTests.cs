using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Setup;

namespace ECAssistant.Core.Tests.Setup;

public class RemoteProviderSetupWriterTests
{
    private static string TempPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "eca-rpsw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "appsettings.json");
    }

    [Fact]
    public void Write_OnMissingFile_CreatesRemoteConfig()
    {
        var path = TempPath();
        var writer = new RemoteProviderSetupWriter(path);

        writer.Write(new RemoteProviderConfig
        {
            Name = "api.openai.com",
            Endpoint = "https://api.openai.com/v1",
            ApiKey = "sk-test",
            ModelId = "gpt-4o-mini",
            EmbeddingModelId = "text-embedding-3-small"
        });

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<EAgentConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(config);
        Assert.Equal("remote", config!.LlmProvider.Mode);
        Assert.Equal("https://api.openai.com/v1", config.LlmProvider.Endpoint);
        Assert.Equal("gpt-4o-mini", config.LlmProvider.ModelId);
        Assert.Equal("keyfile:api-openai-com.key", config.LlmProvider.ApiKey);
        Assert.Equal("text-embedding-3-small", config.LlmProvider.EmbeddingModelId);
        Assert.NotNull(config.LlmProviders);
        Assert.Equal("api.openai.com", config.LlmProviders!.DefaultProvider);
        Assert.Single(config.LlmProviders.Providers);
        Assert.True(config.LlmProviders.Providers[0].IsDefault);

        // Key must be encrypted on disk immediately — never plaintext
        var keyFile = Path.Combine(Path.GetDirectoryName(path)!, "keys", "api-openai-com.key");
        Assert.True(File.Exists(keyFile), "key file should exist right after setup");
        var keyContent = File.ReadAllText(keyFile);
        Assert.StartsWith("ECAKEY1:", keyContent);
        Assert.DoesNotContain("sk-test", keyContent);
        Assert.DoesNotContain("sk-test", json); // and not in appsettings.json either
    }

    [Fact]
    public void Write_OnExistingFile_PreservesOtherSections()
    {
        var path = TempPath();
        var existing = new EAgentConfig
        {
            RootPath = "/tmp/eca",
            Memory = new MemoryConfig { MaxEntries = 123 }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(existing));

        var writer = new RemoteProviderSetupWriter(path);
        writer.Write(new RemoteProviderConfig
        {
            Name = "openrouter.ai",
            Endpoint = "https://openrouter.ai/api/v1",
            ModelId = "test/model"
        });

        var config = JsonSerializer.Deserialize<EAgentConfig>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(config);
        Assert.Equal("/tmp/eca", config!.RootPath);
        Assert.Equal(123, config.Memory.MaxEntries);
        Assert.Equal("remote", config.LlmProvider.Mode);
        // No key provided → no keyfile reference
        Assert.Null(config.LlmProviders!.Providers[0].ApiKey);
    }
}
