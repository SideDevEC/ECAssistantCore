using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Config;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// Detects if ECAssistantLLM server is running. If not, launches it as a child process.
/// Waits for health check to pass before returning.
/// Local mode only — not used in remote mode.
/// </summary>
public sealed class ServerLauncher
{
    private readonly LlmProviderConfig _config;
    private readonly OpenAIClient _probeClient;
    private Process? _serverProcess;

    public ServerLauncher(LlmProviderConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _probeClient = new OpenAIClient(_config.ResolvedEndpoint);
    }

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

        var args = "";
        // Pass port override so Core controls which port the LLM server listens on
        if (_config.IsLocal)
            args = $"--port {_config.Port}";
        if (!string.IsNullOrEmpty(_config.ServerConfigPath))
            args += $" \"{_config.ServerConfigPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        try
        {
            _serverProcess = Process.Start(psi);
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
        // Try graceful shutdown via HTTP endpoint
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try
            {
                // Send shutdown request — server will wind down if this is the last client
                using var shutdownClient = new OpenAIClient(_config.ResolvedEndpoint);
                await shutdownClient.PostJsonAsync("/eca/shutdown", "{}", ct);
            }
            catch { /* server may already be down */ }

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
                catch { }
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
        catch { }
    }

    private string? ResolveExecutablePath()
    {
        // Try absolute path
        if (Path.IsPathRooted(_config.ServerExecutablePath) && File.Exists(_config.ServerExecutablePath))
            return _config.ServerExecutablePath;

        // Try relative to working directory
        var dirs = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Path.Combine(Directory.GetCurrentDirectory(), "..", "ECAssistantLLM"),
            Path.Combine(AppContext.BaseDirectory, "..", "ECAssistantLLM"),
        };

        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, _config.ServerExecutablePath);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

            // Try with .dll extension (dotnet run)
            candidate = Path.ChangeExtension(candidate, ".dll");
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    public void Dispose()
    {
        StopServer();
        _probeClient.Dispose();
    }
}