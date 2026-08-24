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
/// Supports two provider modes:
/// - local: spawns/connects to ECAssistantLLM server (full KV cache, tokenizer, session management)
/// - remote: connects to any OpenAI-compatible API (no KV cache, stateless inference)
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
    private readonly ServerLauncher? _serverLauncher;       // local mode only
    private readonly LlmServerClient? _serverClient;        // local mode only
    private readonly OpenAIClient _httpClient;
    private readonly InferenceParamsFactory _inferenceParamsFactory;
    private RemoteTokenizer? _remoteTokenizer;               // local mode only

    /// <summary>True if running in local mode (ECAssistantLLM with KV cache).</summary>
    public bool IsLocalMode => _config.LlmProvider.IsLocal;

    /// <summary>True if running in remote mode (cloud API, no KV cache).</summary>
    public bool IsRemoteMode => _config.LlmProvider.IsRemote;

    /// <summary>Called when a session is being loaded.</summary>
    public Action<string>? OnSessionLoading { get; set; }

    /// <summary>Called when a session has finished loading.</summary>
    public Action<string>? OnSessionLoaded { get; set; }

    /// <summary>
    /// Create session manager. In local mode, ensures LLM server is running and registers as client.
    /// In remote mode, just sets up the HTTP client with API key.
    /// </summary>
    public SessionManager(EAgentConfig config, string resolvedModelPath, string workingDir, ILogger? logger = null)
    {
        _logger = logger ?? new Logger();
        _config = config;
        _workingDir = workingDir;
        _subAgentConfig = config.SubAgent;

        var provider = config.LlmProvider;
        _inferenceParamsFactory = InferenceParamsFactory.Default;

        if (provider.IsLocal)
        {
            // ── Local mode: ECAssistantLLM server ──
            _serverLauncher = new ServerLauncher(provider);
            _serverClient = new LlmServerClient(provider.Endpoint);
            _httpClient = new OpenAIClient(provider.Endpoint);
        }
        else
        {
            // ── Remote mode: OpenAI-compatible API ──
            _httpClient = new OpenAIClient(provider.Endpoint, apiKey: provider.ApiKey);
        }

        // Validate config (local mode needs model path; remote mode skips file validation)
        if (provider.IsLocal)
        {
            var validator = new ModelParamValidator(_logger);
            var validationError = validator.Validate(config, resolvedModelPath);
            if (validationError != null)
            {
                _logger.Error("SessionManager", validationError.Message);
                throw validationError;
            }
        }
    }

    /// <summary>
    /// Initialize provider connection.
    /// Local mode: ensure server running, register client, start heartbeat, setup tokenizer.
    /// Remote mode: no-op (connection is per-request via HTTP).
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (IsLocalMode)
        {
            _logger.Info("SessionManager", "Ensuring LLM server is running...");
            var ok = await _serverLauncher!.EnsureServerRunningAsync(ct);
            if (!ok)
                throw new InvalidOperationException("Failed to start LLM server");

            _logger.Info("SessionManager", "Registering client with LLM server...");
            var connected = await _serverClient!.ConnectAsync("ECAssistant", "1.0.0", ct);
            if (!connected)
                throw new InvalidOperationException("Failed to register with LLM server");

            // Setup tokenizer (local mode only — remote APIs don't expose /eca/tokenize)
            _remoteTokenizer = new RemoteTokenizer(_httpClient, _config.LlmProvider.ModelId);

            // Start heartbeat
            _serverClient.StartHeartbeat(_config.LlmProvider.HeartbeatIntervalSec, () => _sessions.Count);

            _logger.Info("SessionManager", $"Connected to LLM server at {_config.LlmProvider.Endpoint}");
        }
        else
        {
            _logger.Info("SessionManager", $"Remote mode: {_config.LlmProvider.Endpoint} (model: {_config.LlmProvider.ModelId})");
        }
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

    /// <summary>Server client (local mode only, null in remote mode).</summary>
    public LlmServerClient? ServerClient => _serverClient;

    /// <summary>Client ID for server session namespacing (local mode only).</summary>
    public string? ClientId => _serverClient?.ClientId;

    // ── Session lifecycle ──────────────────────────────────

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
    /// Local mode: session gets its own KV cache on the LLM server.
    /// Remote mode: session uses stateless HTTP inference (no KV cache).
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
            endpoint: _config.LlmProvider.Endpoint,
            clientId: IsLocalMode ? _serverClient!.ClientId : null,
            apiKey: IsRemoteMode ? _config.LlmProvider.ApiKey : null,
            inferenceParams: inferenceParams,
            workingDir: _workingDir,
            inferenceLock: _inferenceLock,
            subAgentConfig: _subAgentConfig,
            label: label,
            logger: _logger,
            config: _config,
            httpClient: _httpClient,
            remoteTokenizer: _remoteTokenizer,
            isLocalMode: IsLocalMode);

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

    /// <summary>Stop all sessions.</summary>
    public void StopAll()
    {
        foreach (var session in _sessions.Values)
            session.Stop();
    }

    /// <summary>Delete a session.</summary>
    public void DeleteSession(string key)
    {
        if (!_sessions.TryGetValue(key, out var session)) return;
        session.Stop();
        _sessions.Remove(key);
        // Clean up session directory
        var sessionDir = Path.Combine(_workingDir, ".sessions", key);
        if (Directory.Exists(sessionDir))
        {
            try { Directory.Delete(sessionDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Stop all sessions
        StopAll();

        // Local mode: disconnect from server + trigger graceful shutdown
        if (IsLocalMode)
        {
            try { await _serverClient!.DisconnectAsync(); }
            catch { /* best effort */ }
            await _serverLauncher!.StopServerAsync();
        }

        _httpClient.Dispose();
    }
}