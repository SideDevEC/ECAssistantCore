using System.Text.Json;

namespace ECAssistant.Core.Setup;

/// <summary>Inspection result for an existing server directory.</summary>
public enum ServerInstallState
{
    /// <summary>No server directory (or no binary in it) — clean install.</summary>
    Missing,
    /// <summary>Installed server matches the required version — nothing to do.</summary>
    Current,
    /// <summary>An ECAssistant server with a DIFFERENT version is installed.</summary>
    VersionMismatch,
    /// <summary>The LLM root exists but holds no recognizable ECAssistant server (foreign product/layout).</summary>
    Foreign
}

/// <summary>
/// Decides WHEN the LLM server must be present and drives the interactive install:
/// missing → install; version mismatch → hint + ask; foreign install → hint + ask to
/// place alongside. Only ever writes into the shared root's own subpaths (server/,
/// models/, llm-server.json) — never deletes anything it does not own.
///
/// Runs exclusively at wizard time (first-run, /reinstall, or local-provider start) —
/// never during chat.
/// </summary>
public sealed class ServerInstallCoordinator
{
    /// <summary>ECAssistant.LLM.Server version the application was built against.
    /// Keep in sync with the release pipeline (llm-server-v* tag).</summary>
    public const string RequiredServerVersion = "14.9.3";

    private readonly string _llmRoot;
    private readonly string _serverDir;
    private readonly string _serverConfigPath;
    private readonly string _modelsDir;
    private readonly ISetupUi _ui;

    public ServerInstallCoordinator(string llmRoot, ISetupUi ui)
    {
        _llmRoot = llmRoot ?? throw new ArgumentNullException(nameof(llmRoot));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _serverDir = Path.Combine(llmRoot, "server");
        _serverConfigPath = Path.Combine(llmRoot, "llm-server.json");
        _modelsDir = Path.Combine(llmRoot, "models");
    }

    /// <summary>
    /// Ensure the server runtime is installed for a local-provider path. Idempotent.
    /// Returns true when the server is usable afterwards; false when the user declined
    /// (wizard continues with a warning — startup will report a clear error if needed).
    /// </summary>
    public async Task<bool> EnsureServerAsync(CancellationToken cancellationToken = default)
    {
        var state = Inspect();
        switch (state)
        {
            case ServerInstallState.Current:
                return true;

            case ServerInstallState.Missing:
                _ui.WriteLine($"[Setup] LLM server {RequiredServerVersion} is not installed — downloading (~170 MB, one time).");
                return await InstallAsync(cancellationToken).ConfigureAwait(false);

            case ServerInstallState.VersionMismatch:
                var installed = ReadInstalledVersion() ?? "unknown";
                _ui.WriteLine($"[Setup] Found ECAssistant LLM server {installed} — this build needs {RequiredServerVersion}.");
                _ui.Write($"Replace it with {RequiredServerVersion}? [Y/n]: ");
                if (!IsAffirmative(_ui.ReadLine(), defaultYes: true))
                {
                    _ui.WriteLine("[Setup] Keeping the existing server. Local models may fail to start if versions are incompatible.");
                    return false;
                }
                return await InstallAsync(cancellationToken).ConfigureAwait(false);

            case ServerInstallState.Foreign:
                _ui.WriteLine($"[Setup] {_llmRoot} exists but does not look like an ECAssistant server install.");
                _ui.Write("Install the ECAssistant server alongside it (only writes into server/)? [y/N]: ");
                if (!IsAffirmative(_ui.ReadLine(), defaultYes: false))
                {
                    _ui.WriteLine("[Setup] Skipped server installation.");
                    return false;
                }
                return await InstallAsync(cancellationToken).ConfigureAwait(false);

            default:
                return false;
        }
    }

    /// <summary>Classify the current state of the shared server directory.</summary>
    public ServerInstallState Inspect()
    {
        if (!File.Exists(Path.Combine(_serverDir, "ECAssistant.LLM.dll")))
            return Directory.Exists(_llmRoot) ? ServerInstallState.Foreign : ServerInstallState.Missing;

        var installedVersion = ReadInstalledVersion();
        if (installedVersion == null)
            return ServerInstallState.VersionMismatch; // ECAssistant binary but no stamp → treat as mismatched/unknown

        return CompareVersions(installedVersion, RequiredServerVersion) == 0
            ? ServerInstallState.Current
            : ServerInstallState.VersionMismatch;
    }

    private async Task<bool> InstallAsync(CancellationToken cancellationToken)
    {
        // The server must not be running while we replace files. At first-run there is
        // nothing to stop; /reinstall stops it explicitly before calling the wizard.
        // Best-effort guard: fail early with a hint if the binary is locked.
        var marker = Path.Combine(_serverDir, "ECAssistant.LLM.dll");
        if (File.Exists(marker))
        {
            try { using var _ = File.Open(marker, FileMode.Open, FileAccess.Read, FileShare.None); }
            catch (IOException)
            {
                _ui.WriteLine("[Setup] ✘ The server binary appears to be in use. Stop any running ECAssistant and try again.");
                return false;
            }
        }

        Directory.CreateDirectory(_llmRoot);
        Directory.CreateDirectory(_serverDir);
        Directory.CreateDirectory(_modelsDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ECAssistant-Installer/1.0");
        var fetcher = new NuGetServerFetcher(http, RequiredServerVersion);

        string stagedDir;
        try
        {
            stagedDir = await fetcher.FetchAsync((received, total) =>
            {
                if (total.HasValue)
                    _ui.Write($"\r[Setup]   {received / 1048576} / {total.Value / 1048576} MB   ");
                else
                    _ui.Write($"\r[Setup]   {received / 1048576} MB   ");
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            _ui.WriteLine($"[Setup] ✘ Download failed: {ex.Message}");
            return false;
        }
        _ui.WriteLine();

        try
        {
            var installer = new ServerBinaryInstaller(stagedDir, _serverDir);
            installer.Install();
            WriteVersionStamp(RequiredServerVersion);
            _ui.WriteLine($"[Setup] ✔ LLM server {RequiredServerVersion} installed to {_serverDir}");
            return true;
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(stagedDir)!, recursive: true); }
            catch (IOException) { }
        }
    }

    private string? ReadInstalledVersion()
    {
        var stamp = Path.Combine(_serverDir, "VERSION");
        try
        {
            return File.Exists(stamp) ? File.ReadAllText(stamp).Trim() : null;
        }
        catch (IOException) { return null; }
    }

    private void WriteVersionStamp(string version)
    {
        // VERSION is owned by the install pipeline (the nupkg's content/server has no
        // VERSION file of its own) — stamp after a successful copy.
        File.WriteAllText(Path.Combine(_serverDir, "VERSION"), version + Environment.NewLine);
    }

    /// <summary>Compare dotted numeric versions (e.g. "14.7.8"); non-numeric parts break ties as 0.</summary>
    // Stateless utility — no mutable state.
    internal static int CompareVersions(string left, string right)
    {
        var l = ParseParts(left);
        var r = ParseParts(right);
        for (var i = 0; i < Math.Max(l.Length, r.Length); i++)
        {
            var cmp = (i < l.Length ? l[i] : 0).CompareTo(i < r.Length ? r[i] : 0);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    private static int[] ParseParts(string version) =>
        version.Split('-', StringSplitOptions.TrimEntries)[0]
            .Split('.', StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, out var n) ? n : 0)
            .ToArray();

    private static bool IsAffirmative(string? answer, bool defaultYes)
    {
        var a = answer?.Trim().ToLowerInvariant() ?? "";
        if (a.Length == 0) return defaultYes;
        return a is "y" or "yes";
    }
}
