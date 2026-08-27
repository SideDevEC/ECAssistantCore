namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Cross-platform self-encrypting API key store.
/// Reads keys by file name from a managed folder; plaintext files are
/// transparently encrypted in place on first read (Data Protection keyring).
/// </summary>
public interface ISecureKeyStore
{
    /// <summary>Directory holding the managed key files.</summary>
    string KeysDirectory { get; }

    /// <summary>Read a key by file name relative to the store directory.</summary>
    string GetKey(string fileName);

    /// <summary>Encrypt and store a key under the given file name immediately (no plaintext residue).</summary>
    void SetKey(string fileName, string plaintext);
}
