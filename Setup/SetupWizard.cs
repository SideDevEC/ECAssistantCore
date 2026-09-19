using ECAssistant.Core.Config;
using ECAssistant.Core.Setup;

namespace ECAssistant.Core.Setup;

/// <summary>Paths and services the wizard needs; assembled by the host.</summary>
public sealed class WizardContext
{
    public required string AppsettingsPath { get; init; }
    public required string UserConfigDir { get; init; }
    public required ModelCatalogDocument Catalog { get; init; }
    public required IReadOnlyList<string> InstalledEntryIds { get; init; }
    public required ModelInstallerService Installer { get; init; }
    public required IRemoteModelProbe Probe { get; init; }

    /// <summary>gpu_layers applied to every installed model (v12.7: always 0 — users tune it in config afterwards).</summary>
    public int GpuLayers { get; init; } = 0;

    /// <summary>Model files directory — used to detect entries whose files already exist (re-register instead of re-download).</summary>
    public required string ModelsDir { get; init; }

    /// <summary>Interactive LLM-server installer (version checks, foreign-install prompts).
    /// Called by the wizard exactly when a local path is chosen (local chat model OR local
    /// embeddings) — pure remote users never trigger it and get zero LLM footprint.</summary>
    public ServerInstallCoordinator? ServerInstaller { get; init; }
}

/// <summary>
/// Staged first-run installation wizard. Each stage only shows what it needs:
/// 1. LLM      — local or remote; remote asks endpoint → key → model (auto vision check via API),
///               local asks vision, then lists chat/vision catalog models (never embeddings).
/// 2. Memory   — embeddings enabled? If yes: remote or local, configured and verified.
/// The host then starts the LLM, connects and enters a session as usual.
/// </summary>
public sealed class SetupWizard
{
    private readonly ISetupUi _ui;

    public SetupWizard(ISetupUi ui)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    /// <summary>Runs the full staged installation flow.</summary>
    public async Task RunAsync(WizardContext ctx)
    {
        _ui.WriteLine();
        _ui.WriteLine("════════ ECAssistant — Installation ════════");
        _ui.WriteLine();

        var llmRemote = await SetupLlmStageAsync(ctx);
        await SetupEmbeddingsStageAsync(ctx, llmRemote);

        _ui.WriteLine();
        _ui.WriteLine("✔ Installation complete — starting ECAssistant…");
        _ui.WriteLine("  (First start loads the model into memory — this can take a minute or more.");
        _ui.WriteLine("   When the chat prompt appears, just start typing. No further setup needed.)");
    }

    // ── Stage 1: LLM ────────────────────────────────────────────────

    /// <returns>True when the user configured a remote provider.</returns>
    private async Task<bool> SetupLlmStageAsync(WizardContext ctx)
    {
        _ui.WriteLine("How should ECAssistant run its AI?");
        _ui.WriteLine("  [1] Local models  (GGUF on this machine)");
        _ui.WriteLine("  [2] Remote AI     (OpenAI-compatible API: OpenAI, OpenRouter, Ollama cloud, …)");
        _ui.Write("Choose [1/2, Enter = 1]: ");
        var remote = (_ui.ReadLine()?.Trim() ?? "") == "2";
        _ui.WriteLine();

        if (!remote)
        {
            await SetupLocalLlmAsync(ctx);
            return false;
        }
        await SetupRemoteLlmAsync(ctx);
        return true;
    }

    private async Task SetupLocalLlmAsync(WizardContext ctx)
    {
        // Local provider path → the server binary is required (provider itself runs on it),
        // regardless of which models get downloaded afterwards.
        if (ctx.ServerInstaller != null && !await ctx.ServerInstaller.EnsureServerAsync().ConfigureAwait(false))
        {
            _ui.WriteLine("  Continuing without a local server — local models will not start until it is installed.");
        }

        // One flat list: catalog models + GGUFs already in the models folder (marked local).
        // Vision is a model property (mmproj present), never a question.
        var selectable = new List<ModelCatalogEntry>();
        foreach (var m in ctx.Catalog.Models.Where(m => m.Category != CatalogModelCategory.Embedding))
            selectable.Add(m);

        var knownFiles = selectable.SelectMany(m => m.Files).Select(f => f.Filename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(ctx.ModelsDir))
        {
            foreach (var gguf in Directory.EnumerateFiles(ctx.ModelsDir, "*.gguf")
                         .Select(Path.GetFileName)
                         .Where(f => f is not null && !knownFiles.Contains(f))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
                selectable.Add(ctx.Installer.BuildLocalModelEntry(gguf!));
        }

        if (selectable.Count == 0)
        {
            _ui.WriteLine("No models available — catalog is empty and the models folder has no GGUFs.");
            return;
        }

        _ui.WriteLine();
        _ui.WriteLine("── Available models ──");
        for (var i = 0; i < selectable.Count; i++)
        {
            var m = selectable[i];
            var onDisk = IsEntryOnDisk(ctx, m);
            var origin = m.HfRepo == "local" ? "  [local]" : "";
            var vision = string.IsNullOrEmpty(m.MmprojFile) ? "" : "  (vision)";
            var line = $"  [{i + 1}] {m.DisplayName}{(m.Recommended ? " ★" : "")}  ({m.TotalSizeGb:0.##} GB{(LicenseLabel(m).Length > 0 ? ", " + LicenseLabel(m) : "")}){vision}{origin}{(onDisk ? "  ✓ already on disk" : "")}";
            if (onDisk) _ui.WriteLineGreen(line);
            else _ui.WriteLine(line);
        }

        _ui.Write("Numbers to install (e.g. 1,3 / 'a' = all ★ / Enter = skip): ");
        var picks = ParsePicks(_ui.ReadLine()?.Trim() ?? "", selectable);
        await DownloadPicksAsync(ctx, picks);
    }

    private async Task SetupRemoteLlmAsync(WizardContext ctx)
    {
        _ui.WriteLine("── Remote AI setup ──");
        _ui.Write("  Endpoint (e.g. https://openrouter.ai/api/v1): ");
        var endpoint = _ui.ReadLine()?.Trim() ?? "";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            _ui.WriteLine("  Invalid endpoint — remote setup skipped.");
            return;
        }

        _ui.Write("  API key: ");
        var apiKey = ReadSecret();

        _ui.WriteLine("  Checking API…");
        var probe = await ctx.Probe.ProbeAsync(endpoint, apiKey);
        if (!probe.Reachable)
        {
            _ui.WriteLine($"  ✘ Endpoint not reachable ({probe.Error}).");
            _ui.Write("  Model ID (manual entry): ");
            var manual = _ui.ReadLine()?.Trim() ?? "";
            if (manual.Length == 0) { _ui.WriteLine("  Remote setup skipped."); return; }
            WriteRemoteProvider(ctx, endpoint, apiKey, manual, vision: AskVisionFallback());
            return;
        }

        _ui.WriteLine($"  ✔ Endpoint reachable — {probe.Models.Count} models available.");
        var modelId = PickRemoteModel(probe.Models, "  Model");
        if (modelId == null) { _ui.WriteLine("  Remote setup skipped."); return; }

        var vision = probe.Models.FirstOrDefault(m => m.Id == modelId)?.SupportsVision ?? AskVisionFallback();
        _ui.WriteLine($"  Vision: {(vision ? "✔ supported (detected via API)" : "✘ not detected")}");
        WriteRemoteProvider(ctx, endpoint, apiKey, modelId, vision);
    }

    private void WriteRemoteProvider(WizardContext ctx, string endpoint, string apiKey, string modelId, bool vision)
    {
        var provider = new RemoteProviderConfig
        {
            Name = new UriBuilder(endpoint).Host,
            Endpoint = endpoint,
            ApiKey = apiKey.Length > 0 ? apiKey : null,
            ModelId = modelId,
            VisionEnabled = vision
            // EmbeddingModelId intentionally unset — embeddings are a separate stage.
        };
        new RemoteProviderSetupWriter(ctx.AppsettingsPath).Write(provider);
        _ui.WriteLine($"✔ Remote AI configured: {modelId} @ {endpoint}");
    }

    // ── Stage 2: Embeddings / memory ────────────────────────────────

    private async Task SetupEmbeddingsStageAsync(WizardContext ctx, bool llmRemote)
    {
        _ui.WriteLine();
        _ui.Write("Enable memory embeddings (semantic recall)? [Y/n]: ");
        if ((_ui.ReadLine()?.Trim() ?? "").ToLowerInvariant() == "n")
        {
            new EmbeddingSetupWriter(ctx.AppsettingsPath).Disable();
            _ui.WriteLine("Memory embeddings disabled.");
            return;
        }

        _ui.WriteLine("  Where should embeddings run?");
        _ui.WriteLine("    [1] Local  (download a small embedding model — runs on the local server)");
        _ui.WriteLine("    [2] Remote (an embedding model on an OpenAI-compatible API)");
        _ui.Write("  Choose [1/2, Enter = 1]: ");
        var remote = (_ui.ReadLine()?.Trim() ?? "").ToLowerInvariant() == "2";
        _ui.WriteLine();

        if (!remote)
        {
            await SetupLocalEmbeddingsAsync(ctx);
            return;
        }
        await SetupRemoteEmbeddingsAsync(ctx, llmRemote);
    }

    private async Task SetupLocalEmbeddingsAsync(WizardContext ctx)
    {
        // Local embeddings run on the local server too — a remote-AI user choosing local
        // embeddings triggers the (one-time) server install exactly here.
        if (ctx.ServerInstaller != null && !await ctx.ServerInstaller.EnsureServerAsync().ConfigureAwait(false))
        {
            _ui.WriteLine("  Local embeddings need the local server — continuing without installing it.");
            return;
        }

        var selectable = ctx.Catalog.Models
            .Where(m => m.Category == CatalogModelCategory.Embedding)
            .ToList();

        if (selectable.Count == 0)
        {
            _ui.WriteLine("  No embedding models in the catalog — embeddings stay on defaults.");
            return;
        }

        _ui.WriteLine("  ── Available embedding models ──");
        for (var i = 0; i < selectable.Count; i++)
        {
            var m = selectable[i];
            var installedMark = IsEntryOnDisk(ctx, m) ? "  ✓ already on disk" : "";
            var line = $"    [{i + 1}] {m.DisplayName}{(m.Recommended ? " ★" : "")}  ({m.TotalSizeGb:0.##} GB{(LicenseLabel(m).Length > 0 ? ", " + LicenseLabel(m) : "")}){installedMark}";
            if (installedMark.Length > 0) _ui.WriteLineGreen(line);
            else _ui.WriteLine(line);
        }

        _ui.Write("  Numbers to install (e.g. 1 / 'a' = all ★ / Enter = skip): ");
        var picks = ParsePicks(_ui.ReadLine()?.Trim() ?? "", selectable);
        var installed = await DownloadPicksAsync(ctx, picks);

        var first = installed.FirstOrDefault();
        if (first != null)
        {
            new EmbeddingSetupWriter(ctx.AppsettingsPath).SetMode("local", modelId: first.Id);
            _ui.WriteLine($"  ✔ Local embeddings registered: {first.Id}");
        }
    }

    private async Task SetupRemoteEmbeddingsAsync(WizardContext ctx, bool llmRemote)
    {
        _ui.Write("  Endpoint [Enter = same as AI provider]: ");
        var endpoint = _ui.ReadLine()?.Trim() ?? "";

        _ui.Write("  API key [Enter = same as AI provider]: ");
        var apiKey = ReadSecret();

        var effectiveEndpoint = endpoint.Length > 0 ? endpoint : ReadConfiguredEndpoint(ctx.AppsettingsPath);
        _ui.WriteLine("  Checking API…");
        var probe = await ctx.Probe.ProbeAsync(effectiveEndpoint ?? "", apiKey.Length > 0 ? apiKey : null);
        if (!probe.Reachable)
        {
            _ui.WriteLine($"  ✘ Endpoint not reachable ({probe.Error}) — embeddings stay on defaults.");
            return;
        }

        _ui.WriteLine($"  ✔ Endpoint reachable — {probe.Models.Count} models available.");
        var modelId = PickRemoteModel(probe.Models, "  Embedding model");
        if (modelId == null) { _ui.WriteLine("  Embeddings stay on defaults."); return; }

        string? keyRef = null;
        if (apiKey.Length > 0)
        {
            var keyStore = new ECAssistant.Core.Services.SecureKeyStore(
                Path.Combine(ctx.UserConfigDir, "keys"));
            keyStore.SetKey("embeddings.key", apiKey);
            keyRef = "keyfile:embeddings.key";
        }

        new EmbeddingSetupWriter(ctx.AppsettingsPath).SetMode("remote", modelId: modelId, endpoint: effectiveEndpoint, apiKey: keyRef);
        _ui.WriteLine($"  ✔ Remote embeddings configured: {modelId} @ {effectiveEndpoint}");
        await Task.CompletedTask;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private string? ReadConfiguredEndpoint(string appsettingsPath)
    {
        try
        {
            if (!File.Exists(appsettingsPath)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            if (doc.RootElement.TryGetProperty("llm_provider", out var p) &&
                p.TryGetProperty("endpoint", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String)
                return e.GetString();
        }
        catch (System.Text.Json.JsonException) { }
        catch (IOException) { }
        return null;
    }

    private string? PickRemoteModel(IReadOnlyList<RemoteModelInfo> models, string label)
    {
        if (models.Count == 0)
        {
            _ui.Write($"{label} ID (manual entry): ");
            return _ui.ReadLine()?.Trim() is { Length: > 0 } manual ? manual : null;
        }

        const int maxShown = 30;
        for (var i = 0; i < Math.Min(models.Count, maxShown); i++)
            _ui.WriteLine($"    [{i + 1}] {models[i].Id}{(models[i].SupportsVision ? "  (vision)" : "")}");
        if (models.Count > maxShown)
            _ui.WriteLine($"    … {models.Count - maxShown} more");

        _ui.Write($"{label} — number or ID [Enter = 1]: ");
        var input = _ui.ReadLine()?.Trim() ?? "";
        if (input.Length == 0) return models[0].Id;
        if (int.TryParse(input, out var n) && n >= 1 && n <= models.Count) return models[n - 1].Id;
        return models.Any(m => m.Id == input) ? input : input; // allow unlisted custom ids
    }

    private bool AskVisionFallback()
    {
        // Vision is a model property — never a question. When the API probe cannot
        // detect it, assume no (safe: image features stay off until verified).
        _ui.WriteLine("  Vision capability could not be detected — assuming no.");
        return false;
    }

    /// <summary>License label for selection lines; empty when unset.</summary>
    // Stateless utility — no mutable state.
    internal static string LicenseLabel(ModelCatalogEntry entry) => entry.License.Trim();

    /// <summary>True when every catalog file for the entry already exists in the models directory.</summary>
    // Stateless utility — no mutable state.
    internal static bool IsEntryOnDisk(WizardContext ctx, ModelCatalogEntry entry) =>
        entry.Files.All(f => File.Exists(Path.Combine(ctx.ModelsDir, f.Filename)));

    private async Task<IReadOnlyList<ModelCatalogEntry>> DownloadPicksAsync(
        WizardContext ctx, IReadOnlyList<ModelCatalogEntry> picks)
    {
        var installed = new List<ModelCatalogEntry>();
        foreach (var entry in picks)
        {
            entry.SuggestedConfig.GpuLayers = ctx.GpuLayers; // v12.7: GPU off by default — users tune config later

            // v12.8: files already on disk (reinstall keeps models) — skip download, re-register config.
            // Hardware-adaptive tuning for on-disk re-registration too (download path
            // tunes inside InstallAsync) — the machine decides, the catalog suggests.
            if (IsEntryOnDisk(ctx, entry))
            {
                ctx.Installer.ApplyToServerConfigTuned(entry);
                _ui.WriteLine($"✓ {entry.DisplayName} — files already on disk, registered in llm-server.json");
                installed.Add(entry);
                continue;
            }

            _ui.WriteLine($"▼ Downloading {entry.DisplayName} ({entry.TotalSizeGb:0.##} GB)");
            var result = await ctx.Installer.InstallAsync(entry, p =>
            {
                _ui.Write($"\r  {p.Percent,5:0}%  {p.BytesReceived / 1048576.0:0} MB  {p.MbPerSecond:0.#} MB/s   ");
            });
            _ui.WriteLine();
            _ui.WriteLine(result.Success ? $"✔ {result.Message}" : $"✘ {result.Message}");
            if (result.Success) installed.Add(entry);
        }
        return installed;
    }

    private string ReadSecret()
    {
        var input = _ui.ReadLine()?.Trim() ?? "";
        return input;
    }

    /// <summary>Parses user picks against the selectable list; 'a' selects all recommended entries.</summary>
    // Stateless utility — no mutable state.
    public static IReadOnlyList<ModelCatalogEntry> ParsePicks(string input, IReadOnlyList<ModelCatalogEntry> selectable)
    {
        if (input.Length == 0) return Array.Empty<ModelCatalogEntry>();
        if (input.Equals("a", StringComparison.OrdinalIgnoreCase))
            return selectable.Where(m => m.Recommended).ToList();

        var picks = new List<ModelCatalogEntry>();
        foreach (var token in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(token, out var n) && n >= 1 && n <= selectable.Count && !picks.Contains(selectable[n - 1]))
                picks.Add(selectable[n - 1]);
        return picks;
    }
}
