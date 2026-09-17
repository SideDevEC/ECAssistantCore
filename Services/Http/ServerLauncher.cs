using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Config;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// Detects if ECAssistantLLM server is running. If not, launches it from the
/// shared standalone location (~/.ECAssistantLLM/server/).
/// Waits for health check to pass before returning.
/// Local mode only — not used in remote mode.
///
/// The server binary is installed once by the wizard (via ServerBinaryInstaller)
/// and lives permanently at {LlmRoot}/server/. This class never copies binaries,
/// never scans dev trees, never references bin/Debug or bin/Release.
/// </summary>
public sealed class ServerLauncher
{
    private readonly LlmProviderConfig _config;
    private readonly OpenAIClient _probeClient;
    private Process? _serverProcess;

    /// <summary>
    /// Create the server launcher.
    /// </summary>
    /// <param name="config">LLM provider config (carries ServerRootPath, Port, etc.)</param>
    public ServerLauncher(LlmProviderConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _probeClient = new OpenAIClient(_config.ResolvedEndpoint);
    }

    /// <summary>
    /// The LLM server root directory. Expanded from ServerRootPath (default ~/.ECAssistantLLM).
    /// </summary>
    public string LlmRoot
    {
        get
        {
            var raw = string.IsNullOrEmpty(_config.ServerRootPath)
                ? "~/.ECAssistantLLM"
                : _config.ServerRootPath;
            return PathExpander.Default.Expand(raw);
        }
    }

    /// <summary>
    /// Ensure server is running. If not detected and auto_start is true, launch it
    /// from the shared standalone location.
    /// Returns true if server is ready.
    /// </summary>
    public async Task<bool> EnsureServerRunningAsync(CancellationToken ct = default)
    {
        // Check if already running
        if (await _probeClient.PingAsync(ct))
            return true;

        if (!_config.AutoStart)
            return false;

        // Resolve the server binary from the shared location ONLY
        var exePath = ResolveLaunchCommand(out var fullArgs);
        if (exePath == null)
            return false;

        // Ensure LLM root directory exists
        Directory.CreateDirectory(LlmRoot);

        // Core owns the server config: write/update {llmRoot}/llm-server.json from the
        // appsettings model selections BEFORE launching, so the server never invents defaults.
        var configPath = ServerConfigWriter.GetConfigPath(LlmRoot);
        if (!ServerConfigWriter.EnsureServerConfig(LlmRoot, _config) && !File.Exists(configPath))
        {
            System.Diagnostics.Debug.WriteLine($"[ServerLauncher] Could not prepare server config at {configPath}");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = fullArgs,
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
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && _serverProcess != null && !_serverProcess.HasExited)
            {
                await Task.Delay(200, ct);
            }

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

    /// <summary>
    /// Resolve the launch command from the shared standalone location ONLY.
    /// Returns the executable path and fills fullArgs with all arguments.
    /// Returns null if the binary is not installed (wizard hasn't run yet).
    /// Never scans dev trees, build output, or app-relative paths.
    /// </summary>
    private string? ResolveLaunchCommand(out string fullArgs)
    {
        var serverDir = Path.Combine(LlmRoot, "server");
        var dllPath = Path.Combine(serverDir, "ECAssistant.LLM.dll");

        var configPath = ServerConfigWriter.GetConfigPath(LlmRoot);
        var baseArgs = BuildServerArguments(LlmRoot, configPath, _config.IsLocal ? _config.Port : null);

        // Primary: launch via dotnet exec on the DLL (framework-dependent)
        if (File.Exists(dllPath))
        {
            fullArgs = $"\"{dllPath}\" {baseArgs}";
            return "dotnet";
        }

        // Fallback: self-contained executable
        var exeName = _config.ServerExecutablePath;
        var exePath = Path.Combine(serverDir, exeName ?? "ECAssistant.LLM");
        if (File.Exists(exePath))
        {
            fullArgs = baseArgs;
            return exePath;
        }

        fullArgs = "";
        return null;
    }

    /// <summary>
    /// Check if the server binary is installed at the shared location.
    /// </summary>
    public bool IsServerBinaryInstalled()
    {
        var serverDir = Path.Combine(LlmRoot, "server");
        return File.Exists(Path.Combine(serverDir, "ECAssistant.LLM.dll"));
    }

    public void Dispose()
    {
        StopServer();
        _probeClient.Dispose();
    }
}