using ECAssistant.Core.Config;
using ECAssistant.Core.Setup;
using Xunit;

namespace ECAssistant.Core.Tests;

/// <summary>v12.8 regression: the installer must detect models already on disk so
/// reinstalls re-register config instead of hiding/re-downloading 20 GB models.</summary>
public class WizardOnDiskDetectionTests : IDisposable
{
    private readonly string _modelsDir;
    private readonly string _userConfigDir;

    public WizardOnDiskDetectionTests()
    {
        _modelsDir = Path.Combine(Path.GetTempPath(), "wizdisk-" + Guid.NewGuid().ToString("N"), "models");
        _userConfigDir = Path.Combine(Path.GetTempPath(), "wizdisk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_modelsDir);
        Directory.CreateDirectory(_userConfigDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_modelsDir)!, true); } catch { }
        try { Directory.Delete(_userConfigDir, true); } catch { }
    }

    private static ModelCatalogEntry Entry(params string[] files)
    {
        var e = new ModelCatalogEntry
        {
            Id = "test-model",
            DisplayName = "Test Model",
            Category = CatalogModelCategory.Vision,
            HfRepo = "org/repo"
        };
        foreach (var f in files) e.Files.Add(new CatalogModelFile { Filename = f, SizeGb = 1 });
        return e;
    }

    private WizardContext Context(ModelCatalogEntry entry) => new()
    {
        AppsettingsPath = Path.Combine(_userConfigDir, "appsettings.json"),
        UserConfigDir = _userConfigDir,
        Catalog = new ModelCatalogDocument { Models = { entry } },
        InstalledEntryIds = Array.Empty<string>(),
        Installer = null!, // not touched by IsEntryOnDisk
        Probe = null!,
        ModelsDir = _modelsDir
    };

    [Fact]
    public void AllFilesPresent_Detected()
    {
        var entry = Entry("model.gguf", "mmproj-f16.gguf");
        File.WriteAllText(Path.Combine(_modelsDir, "model.gguf"), "x");
        File.WriteAllText(Path.Combine(_modelsDir, "mmproj-f16.gguf"), "x");
        Assert.True(SetupWizard.IsEntryOnDisk(Context(entry), entry));
    }

    [Fact]
    public void MissingMmproj_NotDetected()
    {
        var entry = Entry("model.gguf", "mmproj-f16.gguf");
        File.WriteAllText(Path.Combine(_modelsDir, "model.gguf"), "x");
        Assert.False(SetupWizard.IsEntryOnDisk(Context(entry), entry));
    }

    [Fact]
    public void NothingOnDisk_NotDetected()
    {
        var entry = Entry("model.gguf");
        Assert.False(SetupWizard.IsEntryOnDisk(Context(entry), entry));
    }
}

/// <summary>v12.8 regression: cold start with multiple large models needs more than 60 s.</summary>
public class StartupTimeoutDefaultsTests
{
    [Fact]
    public void ProviderConfig_Default_IsAtLeast240Seconds()
    {
        Assert.True(new LlmProviderConfig().StartupTimeoutSec >= 240,
            "startup_timeout_sec default must cover cold start of multi-GB model sets (21:27 bug)");
    }

    [Fact]
    public void ServerEndpointConfig_Default_IsAtLeast240Seconds()
    {
        Assert.True(new LlmServerEndpointConfig().StartupTimeoutSec >= 240);
    }
}
