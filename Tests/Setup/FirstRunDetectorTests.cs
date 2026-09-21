using ECAssistant.Core.Setup;

namespace ECAssistant.Core.Tests.Setup;

/// <summary>
/// Tests for FirstRunDetector remote-provider awareness: a configured remote
/// provider must suppress NeedsSetup even when the local models dir is empty
/// (pure-remote users install zero local models by design).
/// </summary>
public sealed class FirstRunDetectorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-firstrun").FullName;

    private string ModelsDir => Path.Combine(_dir, "models");
    private string ServerConfigPath => Path.Combine(_dir, "llm-server.json");
    private string AppsettingsPath => Path.Combine(_dir, "appsettings.json");

    public FirstRunDetectorTests()
    {
        Directory.CreateDirectory(ModelsDir);
    }

    [Fact]
    public void EmptyModelsDir_NoAppsettings_NeedsSetup()
    {
        var status = new FirstRunDetector(ModelsDir, ServerConfigPath).Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.True(status.NeedsSetup);
    }

    [Fact]
    public void EmptyModelsDir_RemoteConfigured_NoSetupNeeded()
    {
        WriteRemoteAppsettings();

        var status = new FirstRunDetector(ModelsDir, ServerConfigPath, serverBinaryPath: null, appsettingsPath: AppsettingsPath)
            .Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.False(status.NeedsSetup);
    }

    [Fact]
    public void EmptyModelsDir_RemoteConfigured_MissingServerBinary_StillNeedsBinary()
    {
        // Local embeddings on a remote-AI install still require the server binary.
        WriteRemoteAppsettings();
        var binaryPath = Path.Combine(_dir, "server", "ECAssistant.LLM.dll");

        var status = new FirstRunDetector(ModelsDir, ServerConfigPath, binaryPath, AppsettingsPath)
            .Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.False(status.NeedsSetup);
        Assert.True(status.NeedsServerBinary);
    }

    [Fact]
    public void EmptyModelsDir_RemoteConfigured_BinaryPresent_NoSetupNoBinary()
    {
        WriteRemoteAppsettings();
        var binaryPath = Path.Combine(_dir, "server", "ECAssistant.LLM.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(binaryPath)!);
        File.WriteAllText(binaryPath, "fake");

        var status = new FirstRunDetector(ModelsDir, ServerConfigPath, binaryPath, AppsettingsPath)
            .Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.False(status.NeedsSetup);
        Assert.False(status.NeedsServerBinary);
    }

    [Fact]
    public void RemoteModeWithoutProvidersSection_StillNeedsSetup()
    {
        // Remote mode + endpoint but no llm_providers section → incomplete config.
        File.WriteAllText(AppsettingsPath,
            """{"llm_provider":{"mode":"remote","endpoint":"https://openrouter.ai/api/v1"}}""");

        var status = new FirstRunDetector(ModelsDir, ServerConfigPath, serverBinaryPath: null, appsettingsPath: AppsettingsPath)
            .Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.True(status.NeedsSetup);
    }

    [Fact]
    public void LocalModeAppsettings_NeedsSetup()
    {
        File.WriteAllText(AppsettingsPath,
            """{"llm_provider":{"mode":"local","endpoint":"http://localhost:48217"},"llm_providers":{"default_provider":"local","providers":[{"name":"local","endpoint":"http://localhost:48217"}]}}""");

        var status = new FirstRunDetector(ModelsDir, ServerConfigPath, serverBinaryPath: null, appsettingsPath: AppsettingsPath)
            .Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.True(status.NeedsSetup);
    }

    [Fact]
    public void BrokenAppsettings_NeedsSetup()
    {
        File.WriteAllText(AppsettingsPath, "{ not valid json");

        var status = new FirstRunDetector(ModelsDir, ServerConfigPath, serverBinaryPath: null, appsettingsPath: AppsettingsPath)
            .Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.True(status.NeedsSetup);
    }


    [Fact]
    public void LocalUsableAppsettings_MissingBinary_NeedsBinary()
    {
        // Local chat model configured + no server binary → NeedsServerBinary stays true.
        File.WriteAllText(AppsettingsPath,
            """{"llm":{"model_path":"models/bonsai.gguf"},"llm_provider":{"mode":"local","endpoint":"http://localhost:48217"}}""");
        File.WriteAllText(Path.Combine(ModelsDir, "bonsai.gguf"), "fake");
        var binaryPath = Path.Combine(_dir, "server", "ECAssistant.LLM.dll");

        var status = new FirstRunDetector(ModelsDir, ServerConfigPath, binaryPath, AppsettingsPath)
            .Evaluate(ModelCatalogDocument.CreateDefault().Models);
        Assert.False(status.NeedsSetup);
        Assert.True(status.NeedsServerBinary);
    }

    private void WriteRemoteAppsettings()
    {
        // Shape written by RemoteProviderSetupWriter during a pure-remote wizard run.
        File.WriteAllText(AppsettingsPath,
            """{"llm_provider":{"mode":"remote","endpoint":"https://openrouter.ai/api/v1","model_id":"z-ai/glm-5.3-flash"},"llm_providers":{"default_provider":"openrouter.ai","providers":[{"name":"openrouter.ai","endpoint":"https://openrouter.ai/api/v1","model_id":"z-ai/glm-5.3-flash","is_default":true}]}}""");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}