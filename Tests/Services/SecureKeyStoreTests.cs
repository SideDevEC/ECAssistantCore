using ECAssistant.Core.Config;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Tests.Services;

public class SecureKeyStoreTests
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-keystore").FullName;

    [Fact]
    public void PlaintextKey_IsEncryptedInPlace_AndRoundtrips()
    {
        var path = Path.Combine(_dir, "openrouter.key");
        File.WriteAllText(path, "***");

        var store = new SecureKeyStore(_dir);
        Assert.Equal("***", store.GetKey("openrouter.key")); // reads plaintext

        // File must now be encrypted — no plaintext, header present
        var onDisk = File.ReadAllText(path);
        Assert.StartsWith("ECAKEY1:", onDisk);
        Assert.DoesNotContain("***", onDisk);

        // New store instance (fresh protectors from same keyring) decrypts fine
        var store2 = new SecureKeyStore(_dir);
        Assert.Equal("***", store2.GetKey("openrouter.key"));
    }

    [Fact]
    public void EncryptedFile_StaysUnchanged_AfterRead()
    {
        var path = Path.Combine(_dir, "stable.key");
        File.WriteAllText(path, "plaintext-value");

        var store = new SecureKeyStore(_dir);
        _ = store.GetKey("stable.key");
        var encryptedOnce = File.ReadAllText(path);

        _ = store.GetKey("stable.key"); // second read must not rewrite
        _ = store.GetKey("stable.key");
        Assert.Equal(encryptedOnce, File.ReadAllText(path));
    }

    [Fact]
    public void NonWindows_KeyFile_GetsOwnerOnlyPermissions()
    {
        if (OperatingSystem.IsWindows()) return; // POSIX modes don't apply

        var path = Path.Combine(_dir, "perm.key");
        File.WriteAllText(path, "***");

        var store = new SecureKeyStore(_dir);
        _ = store.GetKey("perm.key");

        var mode = File.GetUnixFileMode(path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode & ~(UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit));
    }

    [Fact]
    public void NameOnly_Enforced_PathTraversalRejected()
    {
        var store = new SecureKeyStore(_dir);
        Assert.Throws<ArgumentException>(() => store.GetKey("../secrets/openrouter.key"));
        Assert.Throws<ArgumentException>(() => store.GetKey("sub/dir/key.txt"));
    }

    [Fact]
    public void MissingOrEmptyFile_Throws()
    {
        var store = new SecureKeyStore(_dir);
        Assert.Throws<FileNotFoundException>(() => store.GetKey("nope.key"));

        File.WriteAllText(Path.Combine(_dir, "empty.key"), "   ");
        Assert.Throws<InvalidDataException>(() => store.GetKey("empty.key"));
    }

    [Fact]
    public async Task Registry_Integrates_Keyfile_Scheme()
    {
        var keyDir = Directory.CreateTempSubdirectory("eca-reg-keys").FullName;
        try
        {
            var store = new SecureKeyStore(keyDir);
            var provider = new RemoteProviderConfig
            {
                Name = "managed", Endpoint = "https://m.example/", ModelId = "m",
                ApiKey = "keyfile:provider.key"
            };
            var reg = new LlmProviderRegistry(new MultiLlmProvidersConfig
            {
                KeysDirectory = keyDir,
                Providers = { provider }
            }, logger: null, keyStore: store);

            // First resolution reads the (missing) file → error surfaced, provider skipped.
            // Then write plaintext and re-create registry: self-encrypting migration kicks in.
            Assert.Single(reg.ValidationErrors);

            File.WriteAllText(Path.Combine(keyDir, "provider.key"), "***");
            var reg2 = new LlmProviderRegistry(new MultiLlmProvidersConfig
            {
                KeysDirectory = keyDir,
                Providers = { provider }
            }, logger: null, keyStore: store);

            Assert.Empty(reg2.ValidationErrors);
            Assert.Equal("***", reg2.Default!.ApiKey);

            await Task.CompletedTask;
        }
        finally
        {
            try { Directory.Delete(keyDir, recursive: true); } catch { }
        }
    }
}
