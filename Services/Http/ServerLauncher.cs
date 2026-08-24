using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Config;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// Detects if ECAssistantLLM server is running. If not, launches it as a child process.
/// Waits for health check to pass before returning.
/// </summary>
public sealed class ServerLauncher
{
    private readonly LlmServerEndpointConfig _config;
    private readonly OpenAIClient _probeClient;
    private Process? _serverProcess;

    public ServerLauncher(LlmServerEndpointConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _probeClient = new OpenAIClient(_config.Endpoint);
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
        if (!string.IsNullOrEmpty(_config.ServerConfigPath))
            args = $"\"{_config.ServerConfigPath}\"";

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
    /// Stop the server if we started it.
    /// </summary>
    public void StopServer()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try
            {
                _serverProcess.Kill();
                _serverProcess.WaitForExit(5000);
            }
            catch { }
        }
        _serverProcess?.Dispose();
        _serverProcess = null;
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