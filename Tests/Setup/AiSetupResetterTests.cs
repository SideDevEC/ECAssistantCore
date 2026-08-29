using ECAssistant.Core.Setup;
using ECAssistant.Core.Config;

namespace ECAssistant.Core.Tests;

public class AiSetupResetterTests
{
    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aisetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Reset_RestoresDefaultProvider_AndClearsProviders()
    {
        var dir = MakeTempDir();
        try
        {
            var config = new EAgentConfig
            {
                LlmProvider = new LlmProviderConfig { Mode = "remote", Endpoint = "http://example" }
            };
            File.WriteAllText(Path.Combine(dir, "appsettings.json"),
                System.Text.Json.JsonSerializer.Serialize(config));

            new AiSetupResetter().Reset(dir);

            var reloaded = System.Text.Json.JsonSerializer.Deserialize<EAgentConfig>(
                File.ReadAllText(Path.Combine(dir, "appsettings.json")));
            Assert.NotNull(reloaded);
            Assert.Null(reloaded!.LlmProviders);
            Assert.Equal("local", reloaded.LlmProvider?.Mode);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Reset_DeletesKeysDir_AndServerConfig()
    {
        var dir = MakeTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "keys"));
            File.WriteAllText(Path.Combine(dir, "keys", "k.key"), "secret");
            Directory.CreateDirectory(Path.Combine(dir, "llm"));
            File.WriteAllText(Path.Combine(dir, "llm", "llm-server.json"), "{}");

            new AiSetupResetter().Reset(dir);

            Assert.False(Directory.Exists(Path.Combine(dir, "keys")));
            Assert.False(File.Exists(Path.Combine(dir, "llm", "llm-server.json")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Reset_MissingFiles_DoesNotThrow()
    {
        var dir = MakeTempDir();
        try
        {
            new AiSetupResetter().Reset(dir);
            Assert.True(Directory.Exists(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Reset_KeepsModelFiles()
    {
        var dir = MakeTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "models"));
            File.WriteAllText(Path.Combine(dir, "models", "m.gguf"), "fake");

            new AiSetupResetter().Reset(dir);

            Assert.True(File.Exists(Path.Combine(dir, "models", "m.gguf")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
