using ECAssistant.Core;
using ECAssistant.Core.Config;
using Xunit;

namespace ECAssistant.Core.Tests.Config;

/// <summary>
/// SupportsVision is the single, mode-independent capability answer for
/// integrating applications: local installs (mmproj) and remote installs
/// (wizard question) both surface the same boolean.
/// </summary>
public class SupportsVisionTests
{
    private static AppConfig Config(Action<LlmProviderConfig> setup)
    {
        var cfg = new AppConfig();
        var provider = new LlmProviderConfig();
        setup(provider);
        cfg.LlmProvider = provider;
        return cfg;
    }

    [Fact]
    public void LocalMode_VisionEnabled_SupportsVisionTrue()
    {
        var cfg = Config(p => { p.Mode = "local"; p.VisionEnabled = true; });
        Assert.True(cfg.SupportsVision);
    }

    [Fact]
    public void LocalMode_VisionDisabled_SupportsVisionFalse()
    {
        var cfg = Config(p => { p.Mode = "local"; p.VisionEnabled = false; });
        Assert.False(cfg.SupportsVision);
    }

    [Fact]
    public void RemoteMode_VisionEnabled_SupportsVisionTrue()
    {
        var cfg = Config(p => { p.Mode = "remote"; p.Endpoint = "https://api.example.com"; p.VisionEnabled = true; });
        Assert.True(cfg.SupportsVision);
    }

    [Fact]
    public void RemoteMode_VisionDisabled_SupportsVisionFalse()
    {
        var cfg = Config(p => { p.Mode = "remote"; p.Endpoint = "https://api.example.com"; p.VisionEnabled = false; });
        Assert.False(cfg.SupportsVision);
    }

    [Fact]
    public void LlmProviderNull_SupportsVisionFalse_NoThrow()
    {
        var cfg = new AppConfig { LlmProvider = null! };
        Assert.False(cfg.SupportsVision);
    }

    [Fact]
    public void VisionFlag_IsIndependentOfEmbeddingMode()
    {
        // The two setup decisions must not bleed into each other
        var cfg = Config(p => { p.Mode = "remote"; p.VisionEnabled = true; });
        cfg.Embedding = new EmbeddingConfig { Enabled = true, Mode = "local" };

        Assert.True(cfg.SupportsVision);
        Assert.Equal("local", cfg.Embedding.Mode);
    }

    [Fact]
    public void VisionEnabled_PersistsAs_llm_provider_vision_enabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "eca-vis-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "appsettings.json");
            var cfg = Config(p => { p.Mode = "remote"; p.VisionEnabled = true; });
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(cfg));

            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(true, json["llm_provider"]!["vision_enabled"]!.GetValue<bool>());

            var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(
                File.ReadAllText(path), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.True(roundTrip!.SupportsVision);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
