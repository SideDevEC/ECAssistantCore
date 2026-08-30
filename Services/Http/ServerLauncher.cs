using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Config;
using ECAssistant.Core.Transport;

using System.Linq;
namespace ECAssistant.Core.Services.Http;

/// <summary>
/// Detects if ECAssistantLLM server is running. If not, launches it as a child process.
/// Waits for health check to pass before returning.
/// Local mode only — not used in remote mode.
/// </summary>
public sealed class ServerLauncher
{
    private readonly LlmProviderConfig _config;
    private readonly string _appRoot;
    private readonly OpenAIClient _probeClient;
    private Process? _serverProcess;

    /// <summary>
    /// Create the server launcher.
    /// </summary>
    /// <param name="config">LLM provider config</param>
    /// <param name="appRoot">Application root directory (e.g. ~/ECAssistant). The LLM server home is {appRoot}/llm.</param>
    public ServerLauncher(LlmProviderConfig config, string appRoot)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _appRoot = appRoot ?? throw new ArgumentNullException(nameof(appRoot));
        _probeClient = new OpenAIClient(_config.ResolvedEndpoint);
    }

    /// <summary>
    /// The LLM server root directory: {appRoot}/llm.
    /// </summary>
    public string LlmRoot => string.IsNullOrEmpty(_config.ServerRootPath)
        ? Path.Combine(_appRoot, "llm")
        : _config.ServerRootPath;

    /// <summary>
    /// Ensure server is running. If not detected and auto_start is true, launch it.
    /// Returns true if server is ready.
    /// </summary>
    public async Task<bool> EnsureServerRunningAsync(CancellationToken ct = default)
    {
        // Check if already running
        if (await _probeClient.PingAsync(ct))
            return true;

        if (!_config.AutoStart)
        {
            // Server not running, auto_start disabled
            return false;
        }

        // v12.11 ROOT-ONLY CONTRACT: the server runtime lives INSIDE the root (root/server/).
        // If missing/stale it is copied from the app binary's own directory (BaseDirectory/server,
        // staged there at build time). No CWD fallback, no dev-environment fallback — ever.
        EnsureServerBinaryCopied(ResolveServerSourceDirectory(AppContext.BaseDirectory), _appRoot);

        // Launch server process
        var exePath = ResolveExecutablePath();
        if (exePath == null)
            return false;

        // Ensure LLM root directory exists
        Directory.CreateDirectory(LlmRoot);

        // Core owns the server config: write/update {llmRoot}/llm-server.json from the
        // appsettings model selections BEFORE launching, so the server never invents defaults.
        var configPath = ServerConfigWriter.GetConfigPath(LlmRoot);
        if (!ServerConfigWriter.EnsureServerConfig(_appRoot, LlmRoot, _config) && !File.Exists(configPath))
        {
            System.Diagnostics.Debug.WriteLine($"[ServerLauncher] Could not prepare server config at {configPath}");
            return false;
        }

        var args = BuildServerArguments(LlmRoot, configPath, _config.IsLocal ? _config.Port : null);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            _serverProcess = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start LLM server process.");
            
            // Discard stdout/stderr — the LLM server writes to its own log file
            // via ServerLogger. Without this, llama.cpp output floods the terminal.
            _serverProcess.OutputDataReceived += (_, _) => { };
            _serverProcess.ErrorDataReceived += (_, e) =>
            {
                // Only capture fatal errors to our own log
                if (e.Data != null && e.Data.Contains("FATAL"))
                    System.Diagnostics.Debug.WriteLine($"[ServerLauncher] LLM fatal: {e.Data}");
            };
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();
        }
        catch (Exception)
        {
            return false;
        }

        // Wait for health check to pass
        var deadline = DateTime.UtcNow.AddSeconds(_config.StartupTimeoutSec);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(1000, ct);

            if (await _probeClient.PingAsync(ct))
                return true;
        }

        // Timeout — server didn't start in time
        return false;
    }

    /// <summary>
    /// Stop the server gracefully. Sends /eca/shutdown request first,
    /// then waits for the process to exit. Falls back to Kill if needed.
    /// If there are other clients connected, the server will stay running for them.
    /// </summary>
    public async Task StopServerAsync(CancellationToken ct = default)
    {
        // Always send graceful shutdown via HTTP endpoint — even if we didn't start the server
        try
        {
            using var shutdownClient = new OpenAIClient(_config.ResolvedEndpoint);
            await shutdownClient.PostJsonAsync("/eca/shutdown", "{}", ct);
        }
        catch { /* server may already be down */ }

        // If we launched the process, wait for it and force-kill if needed
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            // Wait for process to exit gracefully
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && _serverProcess != null && !_serverProcess.HasExited)
            {
                await Task.Delay(200, ct);
            }

            // Force kill if still running (e.g. other clients kept it alive — but we launched it)
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try
                {
                    _serverProcess.Kill();
                    _serverProcess.WaitForExit(5000);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ServerLauncher] Non-critical error ignored: {ex.Message}"); }
            }
        }
        _serverProcess?.Dispose();
        _serverProcess = null;
    }

    /// <summary>
    /// Stop the server (synchronous wrapper).
    /// </summary>
    public void StopServer()
    {
        try
        {
            StopServerAsync().Wait(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ServerLauncher] Non-critical error ignored: {ex.Message}"); }
    }

    /// <summary>Server process arguments: root, the explicit config path (the server must
    /// read exactly this config, never generate defaults), and optional port override.</summary>
    internal static string BuildServerArguments(string llmRoot, string configPath, int? portOverride)
    {
        var args = $"--root \"{llmRoot}\" \"{configPath}\"";
        if (portOverride.HasValue)
            args += $" --port {portOverride.Value}";
        return args;
    }

    private string? ResolveExecutablePath()
    {
        EnsureServerBinaryCopied(ResolveServerSourceDirectory(AppContext.BaseDirectory), _appRoot);
        return ResolveExecutablePath(_config.ServerExecutablePath, _appRoot);
    }

    /// <summary>Resolves the LLM server runtime source directory. ROOT-ONLY contract (v12.11):
    /// the source is the app's own output — {baseDirectory}/server (staged at build time) first,
    /// then the publish layout (server files next to the app binary). Never scans dev trees
    /// (ECAssistantLLM/bin siblings) or the CWD. Pure apart from File.Exists.</summary>
    internal static string? ResolveServerSourceDirectory(string baseDirectory)
    {
        // Staged layout: server runtime staged under the app output's server/ folder
        var staged = Path.Combine(baseDirectory, "server");
        if (File.Exists(Path.Combine(staged, "ECAssistant.LLM.dll")))
            return staged;

        // Publish layout: server built into the same folder as the app
        if (File.Exists(Path.Combine(baseDirectory, "ECAssistant.LLM.dll")))
            return baseDirectory;

        return null;
    }

    /// <summary>
    /// v12.10: guarantees root/server/ contains a runnable LLM server. Copies the server
    /// runtime from the newest available build (publish folder or dev output) when missing
    /// or stale. After this, the runtime only ever executes from within the root.
    /// </summary>
    internal static void EnsureServerBinaryCopied(string? sourceDir, string appRoot)
    {
        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir)) return;
        if (string.Equals(Path.GetFullPath(sourceDir), Path.GetFullPath(appRoot), StringComparison.OrdinalIgnoreCase)) return;

        var target = Path.Combine(appRoot, "server");
        var sourceMarker = Path.Combine(sourceDir, "ECAssistant.LLM.dll");
        var targetMarker = Path.Combine(target, "ECAssistant.LLM.dll");
        if (!File.Exists(sourceMarker)) return;
        if (File.Exists(targetMarker) &&
            File.GetLastWriteTimeUtc(targetMarker) >= File.GetLastWriteTimeUtc(sourceMarker))
            return; // up to date

        CopyDirectory(sourceDir, target);
        // File.Copy preserves the source mtime — stamp the target marker so the
        // staleness comparison compares against copy time, not build time.
        File.SetLastWriteTimeUtc(targetMarker, DateTime.UtcNow);
    }

    // Stateless utility — no mutable state.
    internal static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            if (Path.GetFileName(dir).Equals("logs", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }

    internal static string? ResolveExecutablePath(
        string serverExecutablePath, string appRoot)
    {
        var exeName = Path.GetFileName(serverExecutablePath);
        var candidates = new List<string>();

        // 1. The managed copy inside the root (primary location)
        candidates.Add(Path.Combine(appRoot, "server", exeName));

        // 2. Root scan: the executable directly under the root (legacy installs)
        foreach (var sub in new[] { "", "ECAssistantLLM" })
            candidates.Add(Path.Combine(appRoot, sub, exeName));

        // ROOT-ONLY contract: no CWD, no dev-tree, no app-binary-directory fallback.
        // If the executable is not inside the root, the copy step above must have run first.
        return candidates.FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        StopServer();
        _probeClient.Dispose();
    }
}