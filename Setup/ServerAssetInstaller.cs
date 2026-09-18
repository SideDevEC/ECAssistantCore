using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ECAssistant.Core.Setup;

/// <summary>One downloadable asset entry from an install manifest.</summary>
public sealed class InstallManifestAsset
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}

/// <summary>One runtime block from an install manifest.</summary>
public sealed class InstallManifestRuntime
{
    [JsonPropertyName("runtime_id")]
    public string RuntimeId { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<InstallManifestAsset> Assets { get; set; } = new();

    [JsonPropertyName("archive_root")]
    public string ArchiveRoot { get; set; } = "";

    [JsonPropertyName("binary_relative_path")]
    public string BinaryRelativePath { get; set; } = "";
}

/// <summary>Root document of an install manifest.</summary>
public sealed class InstallManifest
{
    [JsonPropertyName("platforms")]
    public Dictionary<string, List<InstallManifestRuntime>> Platforms { get; set; } = new();
}

/// <summary>
/// Dumb file installer for server asset bundles (e.g. external backend runtimes).
/// Reads an install manifest JSON shipped WITH the server package, downloads the
/// listed assets, verifies checksums, and extracts archives next to the server.
/// This class knows NOTHING about what the assets are or how the server uses them —
/// the manifest is owned by ECAssistantLLM.
/// </summary>
public sealed class ServerAssetInstaller
{
    private readonly HttpClient _http;
    private readonly string _backendsRootDir;
    private readonly string _platformKey;

    /// <param name="http">HTTP client used for downloads.</param>
    /// <param name="backendsRootDir">Directory where runtime bundles are extracted.</param>
    /// <param name="platformKey">One of: osx-arm64, win-x64, linux-x64.</param>
    public ServerAssetInstaller(HttpClient http, string backendsRootDir, string platformKey)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _backendsRootDir = string.IsNullOrWhiteSpace(backendsRootDir)
            ? throw new ArgumentException("Backends root is required", nameof(backendsRootDir))
            : backendsRootDir;
        _platformKey = string.IsNullOrWhiteSpace(platformKey)
            ? throw new ArgumentException("Platform key is required", nameof(platformKey))
            : platformKey;
    }

    /// <summary>
    /// Installs the first runtime listed for the platform in the manifest, if not
    /// already present. Returns the llama-server binary path. Idempotent.
    /// </summary>
    public async Task<string> InstallFromManifestAsync(
        string manifestPath,
        Action<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                $"Install manifest not found: {manifestPath}. It ships with the ECAssistantLLM server package.");

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer
            .DeserializeAsync<InstallManifest>(stream, (JsonSerializerOptions?)null, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Install manifest is not valid JSON: {manifestPath}");

        if (!manifest.Platforms.TryGetValue(_platformKey, out var runtimes) || runtimes.Count == 0)
            throw new PlatformNotSupportedException(
                $"No runtime for platform '{_platformKey}' in {Path.GetFileName(manifestPath)}");

        var runtime = runtimes[0];
        var runtimeDir = Path.Combine(_backendsRootDir, runtime.RuntimeId);
        var binary = Path.Combine(
            runtimeDir,
            runtime.BinaryRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(binary))
            return binary; // already installed

        Directory.CreateDirectory(runtimeDir);
        foreach (var asset in runtime.Assets)
        {
            // Unique temp name: parallel installs / same-named assets must not collide.
            var archivePath = Path.Combine(
                Path.GetTempPath(),
                $".{Guid.NewGuid():N}-{Path.GetFileName(asset.Url)}");
            await DownloadAsync(asset.Url, archivePath, progress, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(asset.Sha256))
                VerifySha256(archivePath, asset.Sha256);
            Extract(archivePath, _backendsRootDir);
            File.Delete(archivePath);
        }

        // The manifest declares the folder name inside the archive (archive_root).
        // Normalize it to the runtime_id layout the server expects.
        if (!string.IsNullOrWhiteSpace(runtime.ArchiveRoot))
        {
            var archiveDir = Path.Combine(_backendsRootDir, runtime.ArchiveRoot);
            if (Directory.Exists(archiveDir) && runtime.ArchiveRoot != runtime.RuntimeId)
            {
                if (!Directory.Exists(runtimeDir))
                    Directory.Move(archiveDir, runtimeDir);
                else
                {
                    foreach (var child in Directory.GetFileSystemEntries(archiveDir))
                    {
                        var target = Path.Combine(runtimeDir, Path.GetFileName(child));
                        if (Directory.Exists(child))
                            Directory.Move(child, target);
                        else
                            File.Move(child, target, overwrite: true);
                    }
                    Directory.Delete(archiveDir, recursive: true);
                }
            }
        }

        if (!File.Exists(binary))
            throw new InvalidOperationException(
                $"Runtime installed but binary not found at {binary} — manifest layout mismatch.");

        if (!OperatingSystem.IsWindows())
            TryMarkExecutable(binary);
        return binary;
    }

    private async Task DownloadAsync(string url, string target, Action<DownloadProgress>? progress, CancellationToken ct)
    {
        // Hidden temp name so Spotlight never touches a half-written multi-GB file.
        var temp = Path.Combine(
            Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + ".part");

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dest = File.Create(temp))
        {
            var buffer = new byte[81920];
            long total = response.Content.Headers.ContentLength ?? -1;
            long read = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                progress?.Invoke(new DownloadProgress(
                    Path.GetFileName(target),
                    read,
                    total,
                    total > 0 ? read * 100.0 / total : 0,
                    0));
            }
        }

        File.Move(temp, target, overwrite: true);
    }

    private static void Extract(string archivePath, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (ext == ".zip")
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);
            return;
        }
        if (ext == ".gz" || ext == ".tgz")
        {
            // tar.gz — shell out to tar (available on macOS/Linux; Windows 10+ bundles bsdtar)
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{targetDir}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120_000);
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"tar extraction failed: {err}");
            return;
        }
        throw new NotSupportedException($"Unsupported archive format: {archivePath}");
    }

    private static void TryMarkExecutable(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(10_000);
        }
        catch
        {
            // best effort
        }
    }

    // Stateless utility — no mutable state.
    private static void VerifySha256(string path, string expectedHex)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        if (!string.Equals(hash, expectedHex.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Checksum mismatch for {path}: expected {expectedHex}, got {hash}");
    }
}
