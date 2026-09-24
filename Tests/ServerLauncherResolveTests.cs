using ECAssistant.Core.Config;
using ECAssistant.Core.Services.Http;
using Xunit;

namespace ECAssistant.Core.Tests;

/// <summary>
/// Tests for the standalone ServerLauncher: resolves the server binary from
/// the shared location (~/ECALLM/server/) only. No dev-tree scanning,
/// no binary copying, no app-relative path resolution.
/// </summary>
public class ServerLauncherResolveTests : IDisposable
{
    private readonly string _root;

    public ServerLauncherResolveTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sltest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static string WriteMarker(string dir)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "ECAssistant.LLM.dll");
        File.WriteAllText(p, "stub");
        return p;
    }

    [Fact]
    public void LlmRoot_ExpandsTilde_ToUserHome()
    {
        var config = new LlmProviderConfig { ServerRootPath = "~/ECALLM" };
        var launcher = new ServerLauncher(config);

        var llmRoot = launcher.LlmRoot;
        Assert.Contains("ECALLM", llmRoot);
        Assert.False(llmRoot.StartsWith("~"));
        Assert.True(Path.IsPathRooted(llmRoot));
    }

    [Fact]
    public void LlmRoot_AbsolutePath_UsedAsIs()
    {
        var config = new LlmProviderConfig { ServerRootPath = _root };
        var launcher = new ServerLauncher(config);

        Assert.Equal(_root, launcher.LlmRoot);
    }

    [Fact]
    public void LlmRoot_DefaultsToSharedLocation_WhenNull()
    {
        var config = new LlmProviderConfig { ServerRootPath = null };
        var launcher = new ServerLauncher(config);

        var llmRoot = launcher.LlmRoot;
        Assert.Contains("ECALLM", llmRoot);
        Assert.True(Path.IsPathRooted(llmRoot));
    }

    [Fact]
    public void IsServerBinaryInstalled_True_WhenDllExists()
    {
        var serverDir = Path.Combine(_root, "server");
        WriteMarker(serverDir);

        var config = new LlmProviderConfig { ServerRootPath = _root };
        var launcher = new ServerLauncher(config);

        Assert.True(launcher.IsServerBinaryInstalled());
    }

    [Fact]
    public void IsServerBinaryInstalled_False_WhenDllMissing()
    {
        var config = new LlmProviderConfig { ServerRootPath = _root };
        var launcher = new ServerLauncher(config);

        Assert.False(launcher.IsServerBinaryInstalled());
    }

    [Fact]
    public void BuildServerArguments_AlwaysPassesExplicitConfigPath()
    {
        var llmRoot = "/app/root/llm";
        var configPath = ServerConfigWriter.GetConfigPath(llmRoot);

        var args = ServerLauncher.BuildServerArguments(llmRoot, configPath, portOverride: 48217);

        Assert.Equal($"--root \"{llmRoot}\" \"{configPath}\" --port 48217", args);
        Assert.Contains("llm-server.json", args);
    }

    [Fact]
    public void BuildServerArguments_RemoteEmbedding_OmitsPort()
    {
        var args = ServerLauncher.BuildServerArguments("/app/llm", "/app/llm/llm-server.json", null);
        Assert.DoesNotContain("--port", args);
    }

    // Regression: port 0 in config crashed the server with "Invalid port in prefix."
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void BuildServerArguments_NonPositivePort_OmitsOverride(int port)
    {
        var args = ServerLauncher.BuildServerArguments("/app/llm", "/app/llm/llm-server.json", port);
        Assert.DoesNotContain("--port", args);
    }
}