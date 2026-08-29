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

        // Launch server process
        var exePath = ResolveExecutablePath();
        if (exePath == null)
            return false;

        // Ensure LLM root directory exists
        Directory.CreateDirectory(LlmRoot);

        var args = $"--root \"{LlmRoot}\"";
        // Pass port override so Core controls which port the LLM server listens on
        if (_config.IsLocal)
            args += $" --port {_config.Port}";

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

    private string? ResolveExecutablePath()
    {
        // Absolute path wins
        if (Path.IsPathRooted(_config.ServerExecutablePath) && File.Exists(_config.ServerExecutablePath))
            return _config.ServerExecutablePath;

        // v12.9: runtime deployment contract — the integrating app only guarantees the ROOT
        // directory. All candidates derive from the root; no dev trees (bin/Debug siblings,
        // CWD walks) are consulted. For development runs, a Debug build next to the configured
        // Release path is accepted only if it is NEWER (so stale builds never serve).
        var candidates = new List<string>();

        // 1. Root-relative (deployment layout: root/<server_executable_path>)
        candidates.Add(Path.GetFullPath(Path.Combine(_appRoot, _config.ServerExecutablePath)));

        // 2. Any ECAssistant.LLM within the root (2 levels deep)
        var exeName = Path.GetFileName(_config.ServerExecutablePath);
        foreach (var sub in new[] { "", "ECAssistantLLM", "server", "bin", "ECAssistantLLM\\bin", "server\\bin" })
        {
            var c = Path.Combine(_appRoot, sub, exeName);
            if (!candidates.Contains(c)) candidates.Add(c);
        }

        // 3. Publish layout: server binary next to the app binary
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _config.ServerExecutablePath)));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Path.GetFileName(_config.ServerExecutablePath))));

        // 4. Development fallback: relative to CWD — but if it points at Release, also consider
        //    the sibling Debug build and prefer whichever is newer (never serve a stale build).
        var devPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), _config.ServerExecutablePath));
        candidates.Add(devPath);
        if (devPath.Contains("Release", StringComparison.OrdinalIgnoreCase))
        {
            var debugPath = devPath.Replace("Release", "Debug", StringComparison.OrdinalIgnoreCase);
            candidates.Add(debugPath);
            if (File.Exists(devPath) && File.Exists(debugPath) &&
                File.GetLastWriteTimeUtc(debugPath) > File.GetLastWriteTimeUtc(devPath))
            {
                // Debug is fresher — serve it first
                candidates.Remove(debugPath);
                candidates.Insert(0, debugPath);
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        StopServer();
        _probeClient.Dispose();
    }
}