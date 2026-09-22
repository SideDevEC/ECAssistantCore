using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using Moq;

namespace ECAssistant.Core.Tests.Config;

public class ConfigLoaderTests
{
    private readonly Mock<IFileSystem> _mockFileSystem;

    public ConfigLoaderTests()
    {
        _mockFileSystem = new Mock<IFileSystem>();
    }

    [Fact]
    public void Constructor_WithValidFileSystem_CreatesInstance()
    {
        var loader = new ConfigLoader(_mockFileSystem.Object);
        Assert.NotNull(loader);
    }

    [Fact]
    public void Constructor_WithFileSystemAndColor_CreatesInstance()
    {
        var loader = new ConfigLoader(_mockFileSystem.Object);
        Assert.NotNull(loader);
    }

    [Fact]
    public void Constructor_WithNullFileSystem_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfigLoader(null!));
    }

    [Fact]
    public void Constructor_WithNullColorFormatter_CreatesInstance()
    {
        var loader = new ConfigLoader(_mockFileSystem.Object);
        Assert.NotNull(loader);
    }

    [Fact]
    public void Load_ValidJson_ReturnsConfig()
    {
        var json = """{"root_path":"MyProject","memory":{"data_path":"MyMemory"}}""";
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadFile(It.IsAny<string>())).Returns(json);

        var loader = new ConfigLoader(_mockFileSystem.Object);
        var config = loader.Load("appsettings.json");

        Assert.NotNull(config);
        Assert.Equal("MyProject", config.RootPath);
        Assert.Equal("MyMemory", config.Memory.DataPath);
    }

    [Fact]
    public void Load_MissingFile_FallsBackToEmbeddedDefault()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(false);

        var loader = new ConfigLoader(_mockFileSystem.Object);
        var config = loader.Load("nonexistent.json");

        // Should fall back to embedded appsettings.json from Core.dll
        Assert.NotNull(config);
        // Embedded config has RootPath = "ECAssistant"
        Assert.Equal("ECAssistant", config.RootPath);
    }

    [Fact]
    public void Load_InvalidJson_FallsBackToEmbeddedDefault()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadFile(It.IsAny<string>())).Returns("not valid json {{{");

        var loader = new ConfigLoader(_mockFileSystem.Object);
        var config = loader.Load("appsettings.json");

        Assert.NotNull(config);
        // File deserialization failed, falls back to embedded config
        Assert.Equal("ECAssistant", config.RootPath);
    }

    [Fact]
    public void Load_EmptyJson_FallsBackToEmbeddedDefault()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadFile(It.IsAny<string>())).Returns("");

        var loader = new ConfigLoader(_mockFileSystem.Object);
        var config = loader.Load("appsettings.json");

        Assert.NotNull(config);
        // Empty string deserializes to null, falls back to embedded config
        Assert.Equal("ECAssistant", config.RootPath);
    }

    [Fact]
    public void Load_NullJson_ReturnsDefaultConfig()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadFile(It.IsAny<string>())).Returns((string)null!);

        var loader = new ConfigLoader(_mockFileSystem.Object);
        var config = loader.Load("appsettings.json");

        Assert.NotNull(config);
    }

    [Fact]
    public void Load_FileExistsButDeserializesToNull_FallsBackToEmbeddedDefault()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadFile(It.IsAny<string>())).Returns("null");

        var loader = new ConfigLoader(_mockFileSystem.Object);
        var config = loader.Load("appsettings.json");

        Assert.NotNull(config);
        // File deserialized to null, falls back to embedded config
        Assert.Equal("ECAssistant", config.RootPath);
    }

    [Fact]
    public void Load_DefaultFilePath_UsesAppsettingsJson()
    {
        _mockFileSystem.Setup(fs => fs.FileExists("appsettings.json")).Returns(false);

        var loader = new ConfigLoader(_mockFileSystem.Object);
        loader.Load();

        _mockFileSystem.Verify(fs => fs.FileExists("appsettings.json"), Times.Once);
    }

    [Fact]
    public void Load_CustomFilePath_UsesSpecifiedPath()
    {
        _mockFileSystem.Setup(fs => fs.FileExists("custom.json")).Returns(false);

        var loader = new ConfigLoader(_mockFileSystem.Object);
        loader.Load("custom.json");

        _mockFileSystem.Verify(fs => fs.FileExists("custom.json"), Times.Once);
    }

    [Fact]
    public void Load_ReadFileThrows_FallsBackToEmbeddedDefault()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadFile(It.IsAny<string>())).Throws(new IOException("disk error"));

        var loader = new ConfigLoader(_mockFileSystem.Object);
        var config = loader.Load("appsettings.json");

        Assert.NotNull(config);
        // Read threw, falls back to embedded config
        Assert.Equal("ECAssistant", config.RootPath);
    }

    // ── v14.10 legacy tool section migration ──

    [Fact]
    public void Load_LegacyToolSections_RenamedAndRemoved()
    {
        var json = """
        {
          "tools": {
            "DotnetBuild": { "enabled": true, "timeout": 120 },
            "AskUser": { "enabled": true },
            "EWebSearch": { "enabled": true },
            "EWebFetch": { "enabled": true },
            "EShellAgent": { "enabled": true }
          }
        }
        """;
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadFile(It.IsAny<string>())).Returns(json);

        var loader = new ConfigLoader(_mockFileSystem.Object);
        var config = loader.Load("appsettings.json");

        Assert.True(config.Tools.ContainsKey("EDotnetBuild"), "keys were: " + string.Join(",", config.Tools.Keys));
        Assert.True(config.Tools.ContainsKey("EAskUser"));
        Assert.False(config.Tools.ContainsKey("DotnetBuild"));
        Assert.False(config.Tools.ContainsKey("AskUser"));
        Assert.False(config.Tools.ContainsKey("EWebSearch"));
        Assert.False(config.Tools.ContainsKey("EWebFetch"));
        // untouched sections preserved
        Assert.True(config.Tools.ContainsKey("EShellAgent"));
    }

    [Fact]
    public void Load_LegacyToolSections_NewSectionWins()
    {
        var json = """
        {
          "tools": {
            "DotnetBuild": { "enabled": true, "timeout": 120 },
            "EDotnetBuild": { "enabled": true, "timeout": 300 }
          }
        }
        """;
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadFile(It.IsAny<string>())).Returns(json);

        var loader = new ConfigLoader(_mockFileSystem.Object);
        var config = loader.Load("appsettings.json");

        Assert.True(config.Tools.ContainsKey("EDotnetBuild"));
        Assert.Equal(300, config.Tools["EDotnetBuild"].GetProperty("timeout").GetInt32());
        Assert.False(config.Tools.ContainsKey("DotnetBuild"));
    }
}
