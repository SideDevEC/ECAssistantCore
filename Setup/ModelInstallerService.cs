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
    private readonly string? _appsettingsPath;

    /// <summary>Timeout per download request. Multi-GB files need long streams; this caps stalls, not total time.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /// <param name="appsettingsPath">Optional — when set, ApplyToServerConfig also updates llm.model_path so local startup is valid after install.</param>
    public ModelInstallerService(HttpClient http, string modelsDir, string serverConfigPath, string? appsettingsPath = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _modelsDir = modelsDir ?? throw new ArgumentNullException(nameof(modelsDir));
        _serverConfigPath = serverConfigPath ?? throw new ArgumentNullException(nameof(serverConfigPath));
        _appsettingsPath = appsettingsPath;
    }

    /// <summary>Quick reachability probe for HuggingFace (5s timeout). Downloads need this.</summary>
    public static async Task<bool> IsInternetAvailableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var resp = await http.GetAsync("https://huggingface.co", HttpCompletionOption.ResponseHeadersRead);
            return true; // any HTTP response means DNS+TLS+routing work
        }
        catch
        {
            return false;
        }
    }

    /// <summary>All GGUF files in the models folder as (name, sizeGB), sorted by name.</summary>
    public IReadOnlyList<(string Filename, double SizeGb)> ListModelFiles()
    {
        if (!Directory.Exists(_modelsDir)) return Array.Empty<(string, double)>();
        return Directory.EnumerateFiles(_modelsDir, "*.gguf")
            .Select(f => (Path.GetFileName(f), new FileInfo(f).Length / 1073741824.0))
            .OrderBy(t => t.Item1, StringComparer.OrdinalIgnoreCase)
            .Select(t => (t.Item1, t.Item2))
            .ToList();
    }

    /// <summary>Delete a model GGUF from the models folder. Name-only (no path segments).</summary>
    public bool RemoveModelFile(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename.Contains('/') || filename.Contains('\\') || filename.Contains(".."))
            return false;
        var path = Path.Combine(_modelsDir, filename);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Download all files of the entry (skipping files already present),
    /// provision the external backend runtime when the entry requires one,
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
                if (!string.IsNullOrWhiteSpace(file.Sha256))
                    VerifySha256(target, file.Sha256);
                downloaded.Add(file.Filename);
            }
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "Download cancelled.", downloaded);
        }
        catch (InvalidOperationException ex)
        {
            // Checksum mismatch / verification failure — delete partial file, report per-model failure
            // instead of letting it abort the whole wizard flow.
            try { var partPath = Path.Combine(_modelsDir, entry.Files.First().Filename); if (File.Exists(partPath)) File.Delete(partPath); } catch { }
            return new InstallResult(false, $"Install failed for {entry.DisplayName}: {ex.Message}", downloaded);
        }
        catch (HttpRequestException ex)
        {
            return new InstallResult(false,
                $"Download failed: {ex.Message}\nCheck the catalog entry '{entry.Id}' (repo/file names are user-editable in model-catalog.json).",
                downloaded);
        }

        var backendMessage = string.Empty;
        if (string.Equals(entry.SuggestedConfig.Backend?.Trim(), "process", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var llmServerDir = Path.GetDirectoryName(Path.GetFullPath(_serverConfigPath))!;
                var backendsRoot = Path.Combine(llmServerDir, "backends");
                var manifestPath = Path.Combine(llmServerDir, "server", "install-manifest.json");
                var installer = new ServerAssetInstaller(_http, backendsRoot, PlatformKey());
                var binary = await installer.InstallFromManifestAsync(manifestPath, progress, ct);
                backendMessage = $" Backend runtime installed: {binary}";
            }
            catch (OperationCanceledException)
            {
                return new InstallResult(false, "Download cancelled.", downloaded);
            }
            catch (Exception ex)
            {
                return new InstallResult(false,
                    $"Model files downloaded, but backend runtime installation failed: {ex.Message}", downloaded);
            }
        }

        // Hardware-adaptive tuning: the catalog suggests, this machine decides.
        // Catalog installs are tuned; explicit local registrations keep their config.
        var configMessage = ApplyToServerConfigTuned(entry);
        return new InstallResult(true,
            $"Installed {entry.DisplayName}.{backendMessage} {configMessage}", downloaded);
    }

    /// <summary>Platform key for ServerAssetInstaller: osx-arm64, win-x64, linux-x64.</summary>
    // Stateless utility — no mutable state.
    private static string PlatformKey()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            return "osx-arm64";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            return "win-x64";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            return "linux-x64";
        throw new PlatformNotSupportedException("Unsupported OS for backend provisioning");
    }

    // Stateless utility — no mutable state.
    private static void VerifySha256(string path, string expectedHex)
    {
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(hash, expectedHex.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Checksum mismatch for '{Path.GetFileName(path)}': expected {expectedHex}, got {hash}");
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

        // Server ignored our Range request (200 with full content) — appending would
        // corrupt the file. Reset and rewrite from scratch instead.
        if (existing > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            existing = 0;
        }

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
    /// <summary>
    /// Register a model GGUF that already exists in the models folder (not in the catalog)
    /// into llm-server.json as a chat model. No download, config only.
    /// Defaults: CPU inference (gpu_layers 0), 64k context, batch 512; a matching
    /// sibling mmproj projector is auto-detected and linked for vision.
    /// </summary>
    /// <param name="isEmbedding">Register as embedding model (is_embedding + mean pooling) instead of chat.</param>
    public string RegisterLocalModelFile(string filename, bool isEmbedding = false)
        => ApplyToServerConfig(BuildLocalModelEntry(filename, isEmbedding));

    /// <summary>
    /// Build a catalog entry for an existing GGUF in the models folder (not in the
    /// catalog) — selectable in the wizard like any catalog model. No download.
    /// Defaults: CPU inference, 64k context, batch 512; a sibling mmproj projector is
    /// auto-detected and linked for vision.
    /// </summary>
    public ModelCatalogEntry BuildLocalModelEntry(string filename, bool isEmbedding = false)
    {
        var entry = new ModelCatalogEntry
        {
            Id = "local-" + Path.GetFileNameWithoutExtension(filename).ToLowerInvariant().Replace('.', '-'),
            DisplayName = Path.GetFileNameWithoutExtension(filename),
            Category = isEmbedding ? CatalogModelCategory.Embedding : CatalogModelCategory.Chat,
            HfRepo = "local",
            Quant = "local",
            Notes = "Existing local model file.",
            // Conservative defaults: CPU inference + 64k context (2k for embeddings) — users can tune in config
            SuggestedConfig = new CatalogSuggestedConfig
            {
                GpuLayers = 0,
                ContextSize = isEmbedding ? 2048u : 65536u,
                BatchSize = isEmbedding ? 0 : 512
            },
            MmprojFile = isEmbedding ? null : DetectSiblingMmproj(filename)
        };
        entry.Files.Add(new CatalogModelFile { Filename = filename, SizeGb = 0 });
        return entry;
    }

    /// <summary>
    /// Hardware-tuned config apply: the catalog suggests, THIS machine decides
    /// (GPU layers / context / batch per HardwareProfile). Used for every catalog
    /// install and on-disk re-registration; explicit local registrations keep their
    /// conservative defaults unless routed through here.
    /// </summary>
    public string ApplyToServerConfigTuned(ModelCatalogEntry entry)
    {
        var tunedEntry = new ModelCatalogEntry
        {
            Id = entry.Id,
            DisplayName = entry.DisplayName,
            Category = entry.Category,
            HfRepo = entry.HfRepo,
            Files = new List<CatalogModelFile>(entry.Files),
            MmprojFile = entry.MmprojFile,
            Recommended = entry.Recommended,
            Quant = entry.Quant,
            License = entry.License,
            Notes = entry.Notes,
            SuggestedConfig = HardwareProfile.Detect().Adjust(entry)
        };
        return ApplyToServerConfig(tunedEntry);
    }

    /// <summary>
    /// Look for a mmproj*.gguf projector in the models dir that shares enough of the
    /// model's name tokens to plausibly belong to it (e.g. mmproj-Qwen2.5-VL-7B-Instruct
    /// ↔ Qwen2.5-VL-7B-Instruct-Q4_K_M.gguf). Returns the file name or null.
    /// </summary>
    public string? DetectSiblingMmproj(string modelFilename)
    {
        if (!Directory.Exists(_modelsDir)) return null;

        var candidates = Directory.EnumerateFiles(_modelsDir, "mmproj*.gguf")
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();
        if (candidates.Count == 0) return null;

        var modelTokens = Tokenize(modelFilename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (modelTokens.Count == 0) return null;

        string? best = null;
        var bestScore = 0.0;
        foreach (var c in candidates)
        {
            var tokens = Tokenize(c).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var overlap = modelTokens.Count(t => tokens.Contains(t));
            var score = (double)overlap / modelTokens.Count;
            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        // Require a clear majority of the model's name tokens in the projector name
        return bestScore >= 0.5 ? best : null;
    }

    // Stateless utility — no mutable state.
    private static IEnumerable<string> Tokenize(string filename) =>
        filename.Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !IsQuantToken(t));

    // Stateless utility — no mutable state.
    public static bool LooksLikeEmbeddingModel(string filename) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            Path.GetFileName(filename),
            "minilm|embed|e5-|bge|gte-|nomic-embed|arctic-embed|jina-embed",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsQuantToken(string token) =>
        token.Equals("gguf", StringComparison.OrdinalIgnoreCase) ||
        token.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("f16", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("f32", StringComparison.OrdinalIgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(token, "^(i?q|q)[0-9].*", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
        token.All(char.IsDigit);

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
                ["server"] = new JsonObject { ["host"] = "localhost", ["port"] = 48217 },
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
        if (entry.SuggestedConfig.MaxTokens > 0)
            newEntry["max_tokens"] = entry.SuggestedConfig.MaxTokens;
        if (!string.IsNullOrWhiteSpace(entry.SuggestedConfig.Backend))
            newEntry["backend"] = entry.SuggestedConfig.Backend;
        // NOTE: no download fields — the server never downloads; the wizard installs everything up front.
        if (entry.Category == CatalogModelCategory.Embedding)
            newEntry["pooling_type"] = "mean";
        if (entry.SuggestedConfig.BatchSize > 0)
            newEntry["batch_size"] = entry.SuggestedConfig.BatchSize;
        if (!string.IsNullOrEmpty(entry.MmprojFile))
            newEntry["mmproj_path"] = Path.Combine(_modelsDir, entry.MmprojFile);

        if (existing != null)
            models[models.IndexOf(existing)!] = newEntry;
        else
            models.Add(newEntry);

        File.WriteAllText(_serverConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // Keep appsettings.json llm.model_path (+ vision flag) in sync so local startup validates after install
        UpdateAppsettingsModelPath(newEntry, hasVision: !string.IsNullOrEmpty(entry.MmprojFile));

        return $"Config updated: '{entry.Id}' added to {_serverConfigPath}.";
    }

    /// <summary>Point appsettings.json llm.model_path at the installed primary file (skip embedding entries).</summary>
    private void UpdateAppsettingsModelPath(JsonObject serverEntry, bool hasVision)
    {
        if (_appsettingsPath == null) return;
        if (serverEntry["is_embedding"]?.GetValue<bool>() == true)
            return;

        try
        {
            // Missing appsettings (first-run runs before Build creates it) → minimal file;
            // AgentConfigBuilder.Build treats an existing file as source of truth.
            JsonObject? appRoot = null;
            if (File.Exists(_appsettingsPath) &&
                JsonNode.Parse(File.ReadAllText(_appsettingsPath)) is JsonObject parsed)
                appRoot = parsed;

            appRoot ??= new JsonObject();
            if (appRoot["llm"] is not JsonObject llm) appRoot["llm"] = llm = new JsonObject();
            llm["model_path"] = serverEntry["path"]?.GetValue<string>() ?? "";

            var isEmbeddingEntry = serverEntry["is_embedding"]?.GetValue<bool>() == true;
            var entryId = serverEntry["id"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(entryId))
            {
                // Keep llm_provider model ids aligned with llm-server.json so local startup
                // loads exactly the models the wizard installed.
                if (appRoot["llm_provider"] is not JsonObject provider2) appRoot["llm_provider"] = provider2 = new JsonObject();
                if (isEmbeddingEntry)
                    provider2["embedding_model_id"] = entryId;
                else
                    provider2["model_id"] = entryId;
            }

            // Vision capability flag lives on the provider (llm_provider) — read by integrating apps
            if (appRoot["llm_provider"] is not JsonObject provider) appRoot["llm_provider"] = provider = new JsonObject();
            if (serverEntry["is_embedding"]?.GetValue<bool>() != true)
                provider["vision_enabled"] = hasVision;
            File.WriteAllText(_appsettingsPath, appRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* config sync is best-effort — llm-server.json remains authoritative for the server */ }
    }
}
