using ECAssistant.Core.Config;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Tests.Config;

public class LlmProviderRegistryTests : IDisposable
{
    private readonly string _keyDir = Directory.CreateTempSubdirectory("eca-keys").FullName;
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        try { Directory.Delete(_keyDir, recursive: true); } catch { }
    }

    private string WriteKeyFile(string content)
    {
        var path = Path.Combine(_keyDir, $"key-{_tempFiles.Count}.txt");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void NullSection_NoProviders_DefaultNull()
    {
        var reg = new LlmProviderRegistry(null);
        Assert.Empty(reg.Providers);
        Assert.Null(reg.Default);
        Assert.Empty(reg.OrderedCandidates());
    }

    [Fact]
    public void DefaultProvider_Name_Wins()
    {
        var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
        {
            DefaultProvider = "b",
            Providers =
            [
                new RemoteProviderConfig { Name = "a", Endpoint = "https://a.example", ModelId = "m-a" },
                new RemoteProviderConfig { Name = "b", Endpoint = "https://b.example/", ModelId = "m-b" }
            ]
        });

        Assert.Equal("b", reg.Default!.Name);
        // trailing slash normalized
        Assert.Equal("https://b.example", reg.Default.Endpoint);
    }

    [Fact]
    public void NoDefaultName_IsDefaultFlag_Wins()
    {
        var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
        {
            Providers =
            [
                new RemoteProviderConfig { Name = "a", Endpoint = "https://a", ModelId = "m" },
                new RemoteProviderConfig { Name = "b", Endpoint = "https://b", ModelId = "m", IsDefault = true }
            ]
        });
        Assert.Equal("b", reg.Default!.Name);
    }

    [Fact]
    public void NothingMarked_FirstEntryIsDefault()
    {
        var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
        {
            Providers =
            [
                new RemoteProviderConfig { Name = "first", Endpoint = "https://f", ModelId = "m" },
                new RemoteProviderConfig { Name = "second", Endpoint = "https://s", ModelId = "m" }
            ]
        });
        Assert.Equal("first", reg.Default!.Name);
    }

    [Fact]
    public void FallbackDisabled_Strict_SingleCandidate_IgnoringUnknownPreferred()
    {
        var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
        {
            DefaultProvider = "a",
            FallbackEnabled = false,
            Providers =
            [
                new RemoteProviderConfig { Name = "a", Endpoint = "https://a", ModelId = "m" },
                new RemoteProviderConfig { Name = "b", Endpoint = "https://b", ModelId = "m" }
            ]
        });

        var candidates = reg.OrderedCandidates("unknown-name");
        var single = Assert.Single(candidates);
        Assert.Equal("a", single.Name);
    }

    [Fact]
    public void FallbackEnabled_PreferredFirst_ThenRemainingInOrder()
    {
        var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
        {
            DefaultProvider = "a",
            FallbackEnabled = true,
            Providers =
            [
                new RemoteProviderConfig { Name = "a", Endpoint = "https://a", ModelId = "m" },
                new RemoteProviderConfig { Name = "b", Endpoint = "https://b", ModelId = "m" },
                new RemoteProviderConfig { Name = "c", Endpoint = "https://c", ModelId = "m" }
            ]
        });

        var names = reg.OrderedCandidates().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "a", "b", "c" }, names);

        var pinnedNames = reg.OrderedCandidates("c").Select(p => p.Name).ToList();
        Assert.Equal(new[] { "c", "a", "b" }, pinnedNames);
    }

    [Fact]
    public void InvalidEntries_Skipped_AndReported()
    {
        var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
        {
            Providers =
            [
                new RemoteProviderConfig { Name = "", Endpoint = "https://x", ModelId = "m" },          // no name
                new RemoteProviderConfig { Name = "dup", Endpoint = "https://d1", ModelId = "m" },
                new RemoteProviderConfig { Name = "dup", Endpoint = "https://d2", ModelId = "m" },      // duplicate
                new RemoteProviderConfig { Name = "noend", Endpoint = "", ModelId = "m" },              // no endpoint
                new RemoteProviderConfig { Name = "ok", Endpoint = "https://ok", ModelId = "m-ok" }     // valid
            ]
        });

        Assert.Equal(2, reg.Providers.Count); // "dup" survives once; blank/duplicate/no-endpoint dropped
        Assert.Equal("dup", reg.Default!.Name);
        Assert.Equal(3, reg.ValidationErrors.Count);
    }

    [Fact]
    public void LiteralApiKey_PassesThrough_Unresolved()
    {
        var keyFile = WriteKeyFile(""); // unused, but prove no file needed for literal
        _ = keyFile;
        var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
        {
            Providers = [new RemoteProviderConfig { Name = "lit", Endpoint = "https://l", ModelId = "m", ApiKey = "sk-literal-123" }]
        });
        Assert.Equal("sk-literal-123", reg.Default!.ApiKey);
    }

    [Fact]
    public void FileReference_Key_ReadAndTrimmed_HomeExpanded()
    {
        var keyFile = WriteKeyFile("  sk-file-secret-456\n");
        var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
        {
            Providers = [new RemoteProviderConfig { Name = "filer", Endpoint = "https://f", ModelId = "m", ApiKey = $"file:{keyFile}" }]
        });
        Assert.Equal("sk-file-secret-456", reg.Default!.ApiKey);
    }

    [Fact]
    public void FileReference_TildePath_ExpandsToHome()
    {
        var homeFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".eca-test-key-temp");
        File.WriteAllText(homeFile, "sk-home-key");
        try
        {
            var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
            {
                Providers = [new RemoteProviderConfig { Name = "tilde", Endpoint = "https://t", ModelId = "m", ApiKey = "file:~/.eca-test-key-temp" }]
            });
            Assert.Equal("sk-home-key", reg.Default!.ApiKey);
        }
        finally
        {
            File.Delete(homeFile);
        }
    }

    [Fact]
    public void FileReference_MissingFile_ProviderSkippedWithError()
    {
        var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
        {
            Providers =
            [
                new RemoteProviderConfig { Name = "badref", Endpoint = "https://x", ModelId = "m", ApiKey = "file:/nonexistent/key.txt" },
                new RemoteProviderConfig { Name = "good", Endpoint = "https://g", ModelId = "m" }
            ]
        });

        Assert.Single(reg.Providers);
        Assert.Equal("good", reg.Default!.Name);
        Assert.Contains(reg.ValidationErrors, e => e.Contains("badref"));
    }

    [Fact]
    public async Task ResolveApiKey_EmptyValue_ReturnsNull()
    {
        Assert.Null(LlmProviderRegistry.ResolveApiKey(""));
        Assert.Null(LlmProviderRegistry.ResolveApiKey(null));
        Assert.Null(LlmProviderRegistry.ResolveApiKey("  "));
        await Task.CompletedTask;
    }
}
