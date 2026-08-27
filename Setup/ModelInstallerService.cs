using System.Text.Json;
using System.Text.Json.Nodes;

namespace ECAssistant.Core.Setup;

/// <summary>Progress callback payload for a running download.</summary>
public sealed record DownloadProgress(
    string Filename,
    long BytesReceived,
    long? TotalBytes,
    double Percent,
    double MbPerSecond);

/// <summary>Result of one model installation.</summary>
public sealed record InstallResult(bool Success, string Message, IReadOnlyList<string> DownloadedFiles);

/// <summary>
/// Downloads catalog models from HuggingFace into models/ and wires them into
/// llm-server.json (adds model entries with mmproj_path for vision entries).
/// HTTP-level only — the caller owns user interaction.
/// </summary>
public sealed class ModelInstallerService
{
    private readonly HttpClient _http;
    private readonly string _modelsDir;
    private readonly string _serverConfigPath;

    /// <summary>Timeout per download request. Multi-GB files need long streams; this caps stalls, not total time.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    public ModelInstallerService(HttpClient http, string modelsDir, string serverConfigPath)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _modelsDir = modelsDir ?? throw new ArgumentNullException(nameof(modelsDir));
        _serverConfigPath = serverConfigPath ?? throw new ArgumentNullException(nameof(serverConfigPath));
    }

    /// <summary>True when the primary file of the entry already exists in models/.</summary>
    public bool IsInstalled(ModelCatalogEntry entry)
    {
        var primary = entry.Files.FirstOrDefault();
        return primary != null && File.Exists(Path.Combine(_modelsDir, primary.Filename));
    }

    /// <summary>
    /// Download all files of the entry (skipping files already present),
    /// then merge the model into llm-server.json.
    /// </summary>
    public async Task<InstallResult> InstallAsync(
        ModelCatalogEntry entry,
        Action<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(_modelsDir);
        var downloaded = new List<string>();

        try
        {
            foreach (var file in entry.Files)
            {
                var target = Path.Combine(_modelsDir, file.Filename);
                if (File.Exists(target)) continue;

                var url = entry.GetDownloadUrl(file);
                await DownloadFileAsync(url, target, file.Filename, progress, ct);
                downloaded.Add(file.Filename);
            }
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "Download cancelled.", downloaded);
        }
        catch (HttpRequestException ex)
        {
            return new InstallResult(false,
                $"Download failed: {ex.Message}\nCheck the catalog entry '{entry.Id}' (repo/file names are user-editable in model-catalog.json).",
                downloaded);
        }

        var configMessage = ApplyToServerConfig(entry);
        return new InstallResult(true,
            $"Installed {entry.DisplayName}. {configMessage}", downloaded);
    }

    /// <summary>Stream a file to disk with resume support (.part file + Range header).</summary>
    private async Task DownloadFileAsync(
        string url, string targetPath, string displayName,
        Action<DownloadProgress>? progress, CancellationToken ct)
    {
        var partPath = targetPath + ".part";
        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(StallTimeout);

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength.HasValue
            ? response.Content.Headers.ContentLength + existing
            : null;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        long received = existing;

        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            partPath, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        int read;
        while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            cts.CancelAfter(StallTimeout); // reset stall timer on every chunk

            if (progress != null && total > 0)
            {
                var mbps = received / 1024.0 / 1024.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                progress(new DownloadProgress(displayName, received, total,
                    received * 100.0 / total!.Value, mbps));
            }
        }

        File.Move(partPath, targetPath, overwrite: true);
    }

    /// <summary>
    /// Merge this entry into llm-server.json — adds/replaces a model entry by id,
    /// sets mmproj_path for vision, and keeps all other config intact.
    /// </summary>
    public string ApplyToServerConfig(ModelCatalogEntry entry)
    {
        JsonNode root;
        if (File.Exists(_serverConfigPath))
        {
            try { root = JsonNode.Parse(File.ReadAllText(_serverConfigPath)) ?? new JsonObject(); }
            catch (JsonException)
            {
                return $"Config file is not valid JSON — model files downloaded, but '{_serverConfigPath}' needs manual review.";
            }
        }
        else
        {
            root = new JsonObject
            {
                ["server"] = new JsonObject { ["host"] = "localhost", ["port"] = 58777 },
                ["inference"] = new JsonObject(),
                ["logging"] = new JsonObject()
            };
        }

        if (root["models"] is not JsonArray models)
            root["models"] = models = new JsonArray();

        // Replace existing entry with the same id, or append
        JsonObject? existing = null;
        foreach (var node in models)
        {
            if (node is JsonObject o && (o["id"]?.GetValue<string>() ?? "").Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
            {
                existing = o;
                break;
            }
        }

        var primary = entry.Files.FirstOrDefault();
        if (primary == null) return "Catalog entry has no files — config not written.";

        var newEntry = new JsonObject
        {
            ["id"] = entry.Id,
            ["path"] = Path.Combine(_modelsDir, primary.Filename),
            ["gpu_layers"] = entry.SuggestedConfig.GpuLayers,
            ["context_size"] = entry.SuggestedConfig.ContextSize,
            ["threads"] = -1,
            ["is_embedding"] = entry.Category == CatalogModelCategory.Embedding
        };
        if (entry.Category == CatalogModelCategory.Embedding)
            newEntry["pooling_type"] = "mean";
        if (!string.IsNullOrEmpty(entry.MmprojFile))
            newEntry["mmproj_path"] = Path.Combine(_modelsDir, entry.MmprojFile);

        if (existing != null)
            models[models.IndexOf(existing)!] = newEntry;
        else
            models.Add(newEntry);

        File.WriteAllText(_serverConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return $"Config updated: '{entry.Id}' added to {_serverConfigPath}.";
    }
}
