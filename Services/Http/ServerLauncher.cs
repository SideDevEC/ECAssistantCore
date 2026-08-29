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

    private string? ResolveExecutablePath() =>
        ResolveExecutablePath(_config.ServerExecutablePath, _appRoot, AppContext.BaseDirectory, Directory.GetCurrentDirectory());

    /// <summary>Resolves the LLM server executable. v12.9 runtime contract: candidates derive from the
    /// app root (root-relative, root scan, publish layout) and the CWD (dev), never from dev trees like
    /// bin/Debug siblings of the repo. Pure apart from File.Exists — fully unit-testable.</summary>
    internal static string? ResolveExecutablePath(
        string serverExecutablePath, string appRoot, string baseDirectory, string currentDirectory)
    {
        // Absolute path wins
        if (Path.IsPathRooted(serverExecutablePath) && File.Exists(serverExecutablePath))
            return serverExecutablePath;

        // All candidates are normalized up-front so ".." segments resolve BEFORE existence
        // checks and the returned path is canonical.
        var candidates = new List<string>();
        void Add(string c)
        {
            try { candidates.Add(Path.GetFullPath(c)); } catch { /* malformed — skip */ }
        }

        var exeName = Path.GetFileName(serverExecutablePath);

        // 1. Root-relative (as configured — deployment: root/<server_executable_path>)
        Add(Path.Combine(appRoot, serverExecutablePath));

        // 2. Inside the root: ECAssistantLLM/bin/{Debug,Release}/net8.0 and shallow variants
        foreach (var sub in new[] { "", "ECAssistantLLM", "server" })
        {
            Add(Path.Combine(appRoot, sub, exeName));
            Add(Path.Combine(appRoot, sub, "bin", "Debug", "net8.0", exeName));
            Add(Path.Combine(appRoot, sub, "bin", "Release", "net8.0", exeName));
        }

        // 3. Publish layout: server binary next to the app binary
        Add(Path.Combine(baseDirectory, exeName));
        Add(Path.Combine(baseDirectory, serverExecutablePath));

        // 4. Development fallback: relative to CWD; if it points at Release, also consider the
        //    sibling Debug build and prefer the NEWER one (never serve a stale build).
        var devPath = Path.GetFullPath(Path.Combine(currentDirectory, serverExecutablePath));
        Add(devPath);
        if (devPath.Contains("Release", StringComparison.OrdinalIgnoreCase))
        {
            var debugPath = devPath.Replace("Release", "Debug", StringComparison.OrdinalIgnoreCase);
            if (File.Exists(devPath) && File.Exists(debugPath) &&
                File.GetLastWriteTimeUtc(debugPath) > File.GetLastWriteTimeUtc(devPath))
            {
                candidates.Remove(devPath);
                candidates.Insert(0, debugPath);
            }
            else
            {
                Add(debugPath);
            }
        }

        var found = candidates.FirstOrDefault(File.Exists);
        return found;
    }

    public void Dispose()
    {
        StopServer();
        _probeClient.Dispose();
    }
}