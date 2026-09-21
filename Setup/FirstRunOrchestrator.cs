using System.Text.Json;
using ECAssistant.Core;
using ECAssistant.Core.Setup;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Unified first-run / reinstall orchestration, shared by ALL hosts (Console, TUI):
/// detect installation state from disk truth → run the staged wizard when needed.
/// The LLM server binary is NOT installed here — the wizard installs it exactly at
/// the stage that first needs it (local chat model OR local embeddings), so pure
/// remote users get zero LLM footprint.
///
/// State is always derived from disk (no flag files): gguf files in models/,
/// model entries in llm-server.json, server binary presence. Crash-safe and
/// resume-friendly by construction.
/// </summary>
public sealed class FirstRunOrchestrator
{
    private readonly string _userConfigDir;
    private readonly string _llmRoot;
    private readonly string _llmModelsDir;
    private readonly string _llmServerConfigPath;
    private readonly string _llmServerBinaryPath;
    private readonly ISetupUi _ui;

    public FirstRunOrchestrator(string userConfigDir, ISetupUi ui)
    {
        _userConfigDir = userConfigDir ?? throw new ArgumentNullException(nameof(userConfigDir));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _llmRoot = PathExpander.Default.Expand("~/.ECAssistantLLM");
        _llmModelsDir = Path.Combine(_llmRoot, "models");
        _llmServerConfigPath = Path.Combine(_llmRoot, "llm-server.json");
        _llmServerBinaryPath = Path.Combine(_llmRoot, "server", "ECAssistant.LLM.dll");
    }

    /// <summary>Paths into the shared LLM root for hosts that need them.</summary>
    public string LlmRoot => _llmRoot;
    public string ServerConfigPath => _llmServerConfigPath;
    public string ServerBinaryPath => _llmServerBinaryPath;

    /// <summary>
    /// Runs setup when configs are missing or resolve to no usable model/provider.
    /// No-op when a usable provider is already configured. Never throws for expected
    /// I/O failures — setup must not block application startup.
    /// </summary>
    public async Task RunIfNeededAsync()
    {
        try
        {
            Directory.CreateDirectory(_userConfigDir);

            var catalogPath = Path.Combine(_userConfigDir, "model-catalog.json");
            var catalog = await LoadCatalogAsync(catalogPath).ConfigureAwait(false);
            var validationError = catalog.Validate();
            if (validationError != null)
            {
                _ui.WriteLine($"[Setup] model-catalog.json is invalid: {validationError} — skipping setup.");
                return;
            }

            var appsettingsPath = Path.Combine(_userConfigDir, "appsettings.json");
            await RunSetupIfNeededAsync(appsettingsPath, catalog, catalogPath).ConfigureAwait(false);
        }
        // Deliberate boundary: first-run setup must never block application startup.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or InvalidOperationException or OperationCanceledException)
        {
            _ui.WriteLine($"[Setup] First-run setup skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Catalog resolution order: (1) live fetch from GitHub — keeps links/availability
    /// current without app releases, (2) existing user copy, (3) embedded default.
    /// A successful remote fetch replaces the local copy; any failure falls through.
    /// </summary>
    private async Task<ModelCatalogDocument> LoadCatalogAsync(string catalogPath)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var remote = await new CatalogFetcher(http).TryFetchAsync().ConfigureAwait(false);
            if (remote != null)
            {
                var local = File.Exists(catalogPath)
                    ? JsonSerializer.Deserialize<ModelCatalogDocument>(File.ReadAllText(catalogPath), ModelCatalogDocument.Options)
                    : null;
                if (local == null || remote.Version > local.Version)
                {
                    File.WriteAllText(catalogPath, JsonSerializer.Serialize(remote, ModelCatalogDocument.Options));
                    _ui.WriteLine($"[Setup] Model catalog updated from GitHub ({remote.Models.Count} models).");
                    return remote;
                }
                _ui.WriteLine("[Setup] Local model catalog is up to date — keeping user copy.");
                return ModelCatalogDocument.Load(catalogPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            _ui.WriteLine($"[Setup] Remote catalog unavailable ({ex.Message}) — using local catalog.");
        }

        return ModelCatalogDocument.Load(catalogPath);
    }

    private async Task RunSetupIfNeededAsync(
        string appsettingsPath, ModelCatalogDocument catalog, string catalogPath)
    {
        QuarantineBrokenAppsettings(appsettingsPath);

        var detector = new FirstRunDetector(_llmModelsDir, _llmServerConfigPath, _llmServerBinaryPath, appsettingsPath);
        var status = detector.Evaluate(catalog.Models);

        var remoteConfigured = File.Exists(appsettingsPath) && IsRemoteProviderConfigured(appsettingsPath);

        var localUsable = IsLocalModelUsable(appsettingsPath, _llmServerConfigPath);
        // The server binary is only required when something local actually runs on it
        // (a local chat model or local embeddings). Pure-remote installs never need it.
        var needsBinary = status.NeedsServerBinary && (localUsable || IsLocalEmbeddingsRequested(appsettingsPath));
        if (!status.NeedsSetup && !needsBinary && (remoteConfigured || localUsable)) return;

        if (!status.NeedsSetup && needsBinary && !remoteConfigured)
            _ui.WriteLine("[Setup] Server binary not installed — running installation.");
        else if (!status.NeedsSetup)
            _ui.WriteLine("[Setup] Config exists but no usable model or provider found — running installation.");

        // NOTE: no unconditional LLM directory creation and no server install here —
        // the wizard does both exactly when the user picks a local path.

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ECAssistant-Installer/1.0");
        var installer = new ModelInstallerService(
            http, _llmModelsDir,
            _llmServerConfigPath,
            Path.Combine(_userConfigDir, "appsettings.json"));

        var coordinator = new ServerInstallCoordinator(_llmRoot, _ui);
        var wizard = new SetupWizard(_ui);
        await wizard.RunAsync(new WizardContext
        {
            AppsettingsPath = appsettingsPath,
            UserConfigDir = _userConfigDir,
            Catalog = catalog,
            InstalledEntryIds = status.InstalledEntryIds,
            Installer = installer,
            Probe = new RemoteModelProbe(),
            ModelsDir = _llmModelsDir,
            ServerInstaller = coordinator
        }).ConfigureAwait(false);
    }

    /// <summary>Invalid appsettings.json → back it up so the wizard can regenerate it.</summary>
    private void QuarantineBrokenAppsettings(string appsettingsPath)
    {
        if (!File.Exists(appsettingsPath)) return;
        try { JsonDocument.Parse(File.ReadAllText(appsettingsPath)); }
        catch (JsonException)
        {
            var backup = appsettingsPath + ".broken." + DateTime.UtcNow.Ticks;
            File.Move(appsettingsPath, backup);
            _ui.WriteLine($"[Setup] appsettings.json is invalid — backed up to {Path.GetFileName(backup)}, regenerating.");
        }
    }

    /// <summary>
    /// True when appsettings.json requests local embeddings (embedding.enabled
    /// with mode="local" and no remote endpoint). The server binary is only
    /// required for a first-run install when something local runs on it.
    /// </summary>
    // Stateless utility — no mutable state.
    public static bool IsLocalEmbeddingsRequested(string appsettingsPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            if (!doc.RootElement.TryGetProperty("embedding", out var emb) ||
                emb.ValueKind != JsonValueKind.Object) return false;

            var enabled = emb.TryGetProperty("enabled", out var en) &&
                          en.ValueKind == JsonValueKind.True;
            var modeIsLocal = emb.TryGetProperty("mode", out var mode) &&
                              mode.ValueKind == JsonValueKind.String &&
                              string.Equals(mode.GetString(), "local", StringComparison.OrdinalIgnoreCase);
            var endpointSet = emb.TryGetProperty("endpoint", out var ep) &&
                              ep.ValueKind == JsonValueKind.String &&
                              !string.IsNullOrWhiteSpace(ep.GetString());

            return enabled && (modeIsLocal || !endpointSet);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when appsettings.json configures a usable remote provider.
    /// Delegates to <see cref="FirstRunDetector.IsRemoteProviderConfigured"/>.
    /// </summary>
    // Stateless utility — no mutable state.
    public static bool IsRemoteProviderConfigured(string appsettingsPath) =>
        FirstRunDetector.IsRemoteProviderConfigured(appsettingsPath);

    /// <summary>
    /// True when a local model is usable: appsettings.json llm.model_path resolves
    /// against the user config root (composition root resolves relative paths there —
    /// File.Exists alone would check the CWD and falsely report an installed model as
    /// missing), or llm-server.json has a model entry whose file exists.
    /// </summary>
    // Stateless utility — no mutable state.
    public static bool IsLocalModelUsable(string appsettingsPath, string serverConfigPath)
    {
        var userConfigDir = Path.GetDirectoryName(Path.GetFullPath(appsettingsPath))!;

        bool ExistsResolved(string? p) =>
            !string.IsNullOrEmpty(p) &&
            (File.Exists(p) || File.Exists(Path.Combine(userConfigDir, p)));

        try
        {
            if (File.Exists(appsettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
                if (doc.RootElement.TryGetProperty("llm", out var llm) &&
                    llm.TryGetProperty("model_path", out var mp) &&
                    mp.ValueKind == JsonValueKind.String &&
                    ExistsResolved(mp.GetString()))
                    return true;
            }
        }
        catch (JsonException) { /* malformed handled earlier → not usable */ }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable → not usable */ }

        try
        {
            if (File.Exists(serverConfigPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(serverConfigPath));
                if (doc.RootElement.TryGetProperty("models", out var models))
                    foreach (var m in models.EnumerateArray())
                        if (m.TryGetProperty("path", out var p) &&
                            p.ValueKind == JsonValueKind.String &&
                            ExistsResolved(p.GetString()))
                            return true;
            }
        }
        catch (JsonException) { /* malformed server config → not usable */ }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable → not usable */ }

        return false;
    }
}
