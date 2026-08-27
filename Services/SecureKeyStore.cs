using System.Text;
using ECAssistant.Core.Interfaces;
// ISecureKeyStore implemented below
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace ECAssistant.Core.Services;

/// <summary>
/// Cross-platform encrypted-at-rest API key store.
/// Key files live in a single folder; the config references them by file name only.
///
/// Self-encrypting behavior: on first read, a plaintext key file is transparently
/// replaced with an encrypted version (atomic write, owner-only permissions).
/// Subsequent reads decrypt. Encryption uses ASP.NET Core Data Protection with a
/// persistent key ring in a "keyring" subfolder — DPAPI-backed on Windows,
/// hardened key-ring files elsewhere. Cross-platform, no user interaction required.
///
/// File format: "ECAKEY1:" + Base64(protected payload). Anything else is treated
/// as plaintext and migrated. A header-prefixed payload that fails to decrypt
/// (different machine / deleted key ring) throws — provider is skipped with a
/// clear error instead of silently misusing a garbage key.
/// </summary>
public sealed class SecureKeyStore : ISecureKeyStore
{
    public const string HeaderPrefix = "ECAKEY1:";
    private const string Purpose = "ECAssistant.ApiKeys.v1";

    private readonly string _directory;
    private readonly IDataProtector _protector;
    private readonly ILogger? _logger;

    public SecureKeyStore(string directory, ILogger? logger = null)
    {
        _directory = Path.GetFullPath(directory);
        _logger = logger;
        System.IO.Directory.CreateDirectory(_directory);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("ECAssistant")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_directory, "keyring")));

        var provider = services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();

        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>Directory holding the key files.</summary>
    public string KeysDirectory => _directory;

    /// <summary>Read a key by file name relative to the store directory.</summary>
    public string GetKey(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Key file name must not be empty", nameof(fileName));

        // Name-only reference: never let names escape the keys directory
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
            throw new ArgumentException($"Key file name '{fileName}' must be a plain file name (no path segments)", nameof(fileName));

        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"API key file not found: {path}");

        var content = File.ReadAllText(path).Trim();
        if (content.Length == 0)
            throw new InvalidDataException($"API key file is empty: {path}");

        if (content.StartsWith(HeaderPrefix, StringComparison.Ordinal))
        {
            var protected64 = content[HeaderPrefix.Length..];
            try
            {
                return Encoding.UTF8.GetString(_protector.Unprotect(Convert.FromBase64String(protected64)));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"API key file '{fileName}' is encrypted but cannot be decrypted here " +
                    "(copied from another machine, or keyring deleted?). Re-provision the plaintext key.", ex);
            }
        }

        // Plaintext — migrate to encrypted in place
        WriteEncrypted(path, content);
        _logger?.Info("SecureKeyStore", $"API key '{fileName}' was stored in plaintext — encrypted in place");
        return content;
    }

    /// <summary>Encrypt-and-write with atomic replace + owner-only permissions (non-Windows).</summary>
    private void WriteEncrypted(string path, string plaintext)
    {
        var protected64 = HeaderPrefix + Convert.ToBase64String(
            _protector.Protect(Encoding.UTF8.GetBytes(plaintext)));

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, protected64 + Environment.NewLine);
        RestrictToOwner(tmp);
        File.Move(tmp, path, overwrite: true);
        RestrictToOwner(path); // overwrite:true may create fresh target perms on some filesystems
    }

    private static void RestrictToOwner(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort — filesystem may not support POSIX modes (e.g. some network mounts)
        }
    }
}
