using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Services;
using ECAssistant.Core.Services.Http;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Session;

/// <summary>
/// Session Manager — creates, tracks, and manages all sessions.
/// Uses ECAssistantLLM server via HTTP. No LLamaSharp dependency.
/// </summary>
public class SessionManager : IAsyncDisposable
{
    private readonly Dictionary<string, AgentSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _workingDir;
    private readonly SubAgentConfig _subAgentConfig;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private readonly SessionDiscovery _sessionDiscovery = new();

    /// <summary>The currently active session.</summary>
    public AgentSession? ActiveSession { get; private set; }

    /// <summary>The main session (always exists, always key "main").</summary>
    public AgentSession Main { get; private set; } = null!;

    private readonly EAgentConfig _config;
    private int _sessionCounter = 0;
    private readonly ILogger _logger;

    // HTTP infrastructure (shared across all sessions)
    private readonly ServerLauncher _serverLauncher;
    private readonly LlmServerClient _serverClient;
    private readonly OpenAIClient _httpClient;
    private readonly InferenceParamsFactory _inferenceParamsFactory;
    private RemoteTokenizer _remoteTokenizer;

    /// <summary>Called when a session is being loaded.</summary>
    public Action<string>? OnSessionLoading { get; set; }

    /// <summary>Called when a session has finished loading.</summary>
    public Action<string>? OnSessionLoaded { get; set; }

    /// <summary>
    /// Create session manager. Ensures LLM server is running, registers as client.
    /// </summary>
    public SessionManager(EAgentConfig config, string resolvedModelPath, string workingDir, ILogger? logger = null)
    {
        _logger = logger ?? new Logger();
        _config = config;
        _workingDir = workingDir;
        _subAgentConfig = config.SubAgent;

        // Setup HTTP infrastructure
        var endpoint = config.LlmServer.Endpoint;
        _serverLauncher = new ServerLauncher(config.LlmServer);
        _serverClient = new LlmServerClient(endpoint);
        _httpClient = new OpenAIClient(endpoint);
        _inferenceParamsFactory = InferenceParamsFactory.Default;

        // Validate config
        var validator = new ModelParamValidator(_logger);
        var validationError = validator.Validate(config, resolvedModelPath);
        if (validationError != null)
        {
            _logger.Error("SessionManager", validationError.Message);
            throw validationError;
        }
    }

    /// <summary>
    /// Ensure LLM server is running and client is registered.
    /// Call this before creating sessions.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.Info("SessionManager", "Ensuring LLM server is running...");
        var ok = await _serverLauncher.EnsureServerRunningAsync(ct);
        if (!ok)
            throw new InvalidOperationException("Failed to start LLM server");

        _logger.Info("SessionManager", "Registering client with LLM server...");
        var connected = await _serverClient.ConnectAsync("ECAssistant", "1.0.0", ct);
        if (!connected)
            throw new InvalidOperationException("Failed to register with LLM server");

        // Setup tokenizer
        _remoteTokenizer = new RemoteTokenizer(_httpClient, _config.LlmServer.ModelId);

        // Start heartbeat
        _serverClient.StartHeartbeat(_config.LlmServer.HeartbeatIntervalSec, () => _sessions.Count);

        _logger.Info("SessionManager", $"Connected to LLM server at {_config.LlmServer.Endpoint}");
    }

    /// <summary>Get a session by key.</summary>
    public AgentSession? Get(string key)
        => _sessions.GetValueOrDefault(key);

    /// <summary>List all sessions.</summary>
    public IReadOnlyList<AgentSession> List()
        => _sessions.Values.ToList();

    /// <summary>Number of sessions.</summary>
    public int Count => _sessions.Count;

    /// <summary>Shared inference lock.</summary>
    public SemaphoreSlim InferenceLock => _inferenceLock;

    /// <summary>Working directory.</summary>
    public string WorkingDir => _workingDir;

    /// <summary>Sub-agent config.</summary>
    public SubAgentConfig SubAgentConfig => _subAgentConfig;

    /// <summary>Agent config.</summary>
    public EAgentConfig Config => _config;

    /// <summary>Server client.</summary>
    public LlmServerClient ServerClient => _serverClient;

    // ── Session lifecycle ──────────────────────────────

    /// <summary>
    /// Discover existing sessions on disk and load them.
    /// </summary>
    public async Task<string> LoadSessionsFromDiskAsync(
        Func<AgentSession, Task> initSessionAsync)
    {
        _sessionDiscovery.MigrateLegacyTranscript(_workingDir);
        _sessionDiscovery.EnsureSessionsDir(_workingDir);

        var discovered = _sessionDiscovery.DiscoverSessions(_workingDir);
        string activeKey;

        if (discovered.Count == 0)
        {
            activeKey = "main";
            OnSessionLoading?.Invoke(activeKey);
            Main = CreateSession(activeKey, label: "Main Session");
            await initSessionAsync(Main);
            OnSessionLoaded?.Invoke(activeKey);
        }
        else
        {
            activeKey = discovered[0];

            foreach (var key in discovered)
            {
                OnSessionLoading?.Invoke(key);
                var session = CreateSession(key, label: key == "main" ? "Main Session" : key);

                if (key == "main")
                    Main = session;

                await initSessionAsync(session);
                OnSessionLoaded?.Invoke(key);
            }

            if (Main == null)
            {
                Main = CreateSession("main", label: "Main Session");
                await initSessionAsync(Main);
            }
        }

        var activeSession = Get(activeKey) ?? Main;
        ActiveSession = activeSession;
        _sessionDiscovery.TouchSessionMeta(_workingDir, activeKey);

        return activeKey;
    }

    /// <summary>
    /// Create a new session with the given key.
    /// Session gets its own KV cache on the LLM server.
    /// </summary>
    public AgentSession CreateSession(string key, string? label = null)
    {
        if (_sessions.ContainsKey(key))
            throw new InvalidOperationException($"Session already exists: {key}");

        var inferenceParams = _inferenceParamsFactory.Create(_config);
        inferenceParams.SessionId = key;

        var session = new AgentSession(
            key: key,
            sessionId: key,
            endpoint: _config.LlmServer.Endpoint,
            clientId: _serverClient.ClientId,
            inferenceParams: inferenceParams,
            workingDir: _workingDir,
            inferenceLock: _inferenceLock,
            subAgentConfig: _subAgentConfig,
            label: label,
            logger: _logger,
            config: _config,
            httpClient: _httpClient,
            remoteTokenizer: _remoteTokenizer);

        _sessions[key] = session;
        _sessionCounter++;
        return session;
    }

    /// <summary>Create a new auto-named session.</summary>
    public AgentSession CreateSession(string? label = null)
    {
        var key = $"session-{++_sessionCounter}";
        return CreateSession(key, label);
    }

    /// <summary>Switch the active session to the one with this key.</summary>
    public bool SwitchTo(string key)
    {
        if (!_sessions.TryGetValue(key, out var session)) return false;
        ActiveSession = session;
        _sessionDiscovery.TouchSessionMeta(_workingDir, key);
        return true;
    }

    /// <summary>Switch active session by index (1-based).</summary>
    public bool SwitchTo(int index)
    {
        var list = _sessions.Values.ToList();
        if (index < 1 || index > list.Count) return false;
        ActiveSession = list[index - 1];
        _sessionDiscovery.TouchSessionMeta(_workingDir, ActiveSession.Key);
        return true;
    }

    /// <summary>Stop a specific session's execution.</summary>
    public void StopSession(string key)
    {
        if (_sessions.TryGetValue(key, out var session))
            session.Stop();
    }

    /// <summary>Stop all sessions gracefully.</summary>
    public async Task StopAllAsync()
    {
        foreach (var session in _sessions.Values)
            session.Stop();
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();
        _sessions.Clear();
    }

    /// <summary>Close and delete a session.</summary>
    public async Task CloseSessionAsync(string key)
    {
        if (!_sessions.TryGetValue(key, out var session)) return;
        if (session == Main) throw new InvalidOperationException("Cannot close the main session.");

        await session.DisposeAsync();
        _sessions.Remove(key);

        if (ActiveSession == session)
            ActiveSession = Main;

        try
        {
            var sessionDir = Path.Combine(_workingDir, ".sessions", key);
            if (Directory.Exists(sessionDir))
                Directory.Delete(sessionDir, recursive: true);
        }
        catch { }
    }

    /// <summary>Get a status report for all sessions.</summary>
    public string GetStatusReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Sessions ===");
        int i = 1;
        foreach (var session in _sessions.Values)
        {
            var active = session == ActiveSession ? " →" : "  ";
            sb.AppendLine($"{active}{i}. {session.GetStatusSummary()}");
            i++;
        }
        return sb.ToString();
    }

    /// <summary>Get session by index (1-based).</summary>
    public AgentSession? GetByIndex(int index)
    {
        var list = _sessions.Values.ToList();
        if (index < 1 || index > list.Count) return null;
        return list[index - 1];
    }

    /// <summary>Rename a session's label by key.</summary>
    public bool RenameSession(string key, string newLabel)
    {
        if (!_sessions.TryGetValue(key, out var session)) return false;
        session.Rename(newLabel);
        return true;
    }

    /// <summary>Rename a session's label by index (1-based).</summary>
    public bool RenameSession(int index, string newLabel)
    {
        var session = GetByIndex(index);
        if (session == null) return false;
        session.Rename(newLabel);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync();
        await _serverClient.DisposeAsync();
        _serverLauncher.Dispose();
        _httpClient.Dispose();
    }
}