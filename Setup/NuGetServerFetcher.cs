using System.IO.Compression;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Downloads the ECAssistant.LLM.Server NuGet package from nuget.org and extracts the
/// server runtime to a staging directory for installation into the shared location
/// (~/.ECAssistantLLM/server/).
///
/// The tool/application packages stay thin: the server is NEVER embedded. This fetch
/// runs ONCE at wizard time when a local GGUF model (chat or embeddings) is involved —
/// never during chat (boundary rule).
///
/// This class is pure download+extract: the decision of WHEN/WETHER to fetch, version
/// mismatch prompts and the final copy belong to <see cref="ServerInstallCoordinator"/>.
/// </summary>
public sealed class NuGetServerFetcher
{
    private const string NuGetFlatContainerBase = "https://api.nuget.org/v3-flatcontainer/";
    private const string PackageIdLower = "ecassistant.llm.server";

    private readonly HttpClient _http;
    private readonly string _version;
    private readonly string _tempRoot;

    /// <param name="http">HttpClient owned by the caller (wizard scope).</param>
    /// <param name="version">ECAssistant.LLM.Server package version to fetch
    /// (e.g. "14.7.8" — must match the version the application was built against).</param>
    /// <param name="tempRoot">Root for the temporary download/extract directory.</param>
    public NuGetServerFetcher(HttpClient http, string version, string? tempRoot = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _version = string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Version must not be empty.", nameof(version))
            : version;
        _tempRoot = tempRoot ?? Path.GetTempPath();
    }

    /// <summary>Package version this fetcher downloads.</summary>
    public string Version => _version;

    /// <summary>
    /// Downloads the server package and extracts it. Returns the directory containing the
    /// server runtime (content/server inside the nupkg). Caller must delete the returned
    /// directory tree when done. Throws on network/extract failure.
    /// </summary>
    /// <param name="progress">Optional progress reporter: (bytesReceived, totalBytes).</param>
    public async Task<string> FetchAsync(
        Action<long, long?>? progress,
        CancellationToken cancellationToken = default)
    {
        var url = $"{NuGetFlatContainerBase}{PackageIdLower}/{_version}/{PackageIdLower}.{_version}.nupkg";

        var tempDir = Path.Combine(_tempRoot, $"ecassistant-server-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nupkgPath = Path.Combine(tempDir, $"{PackageIdLower}.{_version}.nupkg");
            await DownloadAsync(url, nupkgPath, progress, cancellationToken).ConfigureAwait(false);

            var extractDir = Path.Combine(tempDir, "pkg");
            ZipFile.ExtractToDirectory(nupkgPath, extractDir);

            // Server runtime lives under content/server/ inside the nupkg.
            var contentServerDir = Path.Combine(extractDir, "content", "server");
            if (!Directory.Exists(contentServerDir))
                throw new InvalidOperationException(
                    $"Unexpected package layout: '{contentServerDir}' not found in ECAssistant.LLM.Server {_version}.");
            return contentServerDir;
        }
        catch
        {
            TryDelete(tempDir);
            throw;
        }
    }

    private async Task DownloadAsync(
        string url, string destinationPath, Action<long, long?>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(destinationPath);

        var buffer = new byte[1 << 16];
        long written = 0;
        long nextReport = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
            if (progress != null && written >= nextReport)
            {
                progress(written, total);
                nextReport = written + (10L << 20); // report every ~10 MB
            }
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
