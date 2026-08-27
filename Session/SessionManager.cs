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
    private readonly object _sessionsLock = new();
    private readonly Dictionary<string, AgentSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _workingDir;
    private readonly SubAgentConfig _subAgentConfig;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private readonly SessionDiscovery _sessionDiscovery = new();

    // ── Idle timeout state ──
    private Timer? _idleTimer;
    private DateTime _lastUserActivity = DateTime.UtcNow;
    private bool _isIdleDisconnected;
    private int _idleTimeoutMin;
    private readonly int _idleCheckIntervalSec = 60;
    private int _reconnectInProgress;

    /// <summary>The currently active session.</summary>
    public AgentSession? ActiveSession { get; private set; }

    /// <summary>The main session (always exists, always key "main").</summary>
    public AgentSession Main { get; private set; } = null!;

    private readonly EAgentConfig _config;
    private int _sessionCounter = 0;
    private readonly ILogger _logger;
    private readonly string _appRoot;

    // HTTP infrastructure (shared across all sessions)
    private readonly ServerLauncher? _serverLauncher;       // local mode only
    private readonly LlmServerClient? _serverClient;        // local mode only
    private OpenAIClient _httpClient;
    private readonly InferenceParamsFactory _inferenceParamsFactory;
    private RemoteTokenizer? _remoteTokenizer;               // local mode only

    // Multi-provider remote selection (null values = fall back to llm_provider section)
    private ILlmProviderRegistry? _registry;
    private SecureKeyStore? _keyStore;
    private string? _effectiveEndpoint;
    private string? _effectiveApiKey;
    private string? _effectiveModelId;

    /// <summary>True if running in local mode (ECAssistantLLM with KV cache).</summary>
    public bool IsLocalMode => _config.LlmProvider.IsLocal;

    /// <summary>True if running in remote mode (cloud API, no KV cache).</summary>
    public bool IsRemoteMode => _config.LlmProvider.IsRemote;

    /// <summary>Endpoint of the selected remote provider (registry) or llm_provider config.</summary>
    public string EffectiveEndpoint => _effectiveEndpoint ?? _config.LlmProvider.ResolvedEndpoint;

    /// <summary>API key of the selected remote provider, else llm_provider config.</summary>
    public string? EffectiveApiKey => _effectiveApiKey ?? _config.LlmProvider.ApiKey;

    /// <summary>Model ID of the selected remote provider, else llm_provider config.</summary>
    public string EffectiveModelId => _effectiveModelId ?? _config.LlmProvider.ModelId;

    /// <summary>
    /// Pick the remote provider. Fallback disabled: just Default.
    /// Fallback enabled: first provider whose health endpoint answers within 5s.
    /// No registry/multi-config: returns null (single llm_provider path is used).
    /// </summary>
    private async Task<RemoteProvider?> SelectRemoteProviderAsync(CancellationToken ct = default)
    {
        if (_registry == null || _registry.Providers.Count == 0) return null;

        var candidates = _registry.OrderedCandidates();
        var fallbackOn = _config.LlmProviders?.FallbackEnabled == true;
        if (!fallbackOn || candidates.Count <= 1)
            return candidates.FirstOrDefault();

        foreach (var candidate in candidates)
        {
            using var probe = new OpenAIClient(candidate.Endpoint, apiKey: candidate.ApiKey);
            if (await probe.PingAsync(ct))
            {
                _logger.Info("SessionManager", $"Provider '{candidate.Name}' healthy — selected");
                return candidate;
            }
            _logger.Warn("SessionManager", $"Provider '{candidate.Name}' unreachable — trying next");
        }

        _logger.Error("SessionManager", $"All {candidates.Count} providers unreachable — using default anyway");
        return candidates[0];
    }

    /// <summary>True when client has disconnected from server due to idle timeout.</summary>
    public bool IsIdleDisconnected => _isIdleDisconnected;

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
        _appRoot = workingDir; // app root is the working directory
        _subAgentConfig = config.SubAgent;

        var provider = config.LlmProvider;
        _inferenceParamsFactory = InferenceParamsFactory.Default;

        if (provider.IsLocal)
        {
            // ── Local mode: ECAssistantLLM server ──
            _serverLauncher = new ServerLauncher(provider, _appRoot);
            _serverClient = new LlmServerClient(provider.ResolvedEndpoint);
            _httpClient = new OpenAIClient(provider.ResolvedEndpoint);
        }
        else
        {
            // ── Remote mode: multi-provider registry, else single llm_provider ──
            var keysDir = config.LlmProviders?.KeysDirectory ?? "keys";
            var keysPath = Path.IsPathRooted(keysDir) ? keysDir : Path.Combine(_appRoot, keysDir);
            _keyStore = new SecureKeyStore(keysPath, _logger);
            _registry = new LlmProviderRegistry(config.LlmProviders, _logger, _keyStore);
            foreach (var err in _registry.ValidationErrors)
                _logger.Warn("SessionManager", $"llm_providers: {err}");

            var selected = SelectRemoteProviderAsync().GetAwaiter().GetResult();
            if (selected != null)
            {
                _effectiveEndpoint = selected.Endpoint;
                _effectiveApiKey = selected.ApiKey;
                _effectiveModelId = selected.ModelId;
                _logger.Info("SessionManager", $"Remote mode via provider '{selected.Name}' ({selected.Endpoint}, model: {selected.ModelId})");
            }

            _httpClient = new OpenAIClient(_effectiveEndpoint!, apiKey: _effectiveApiKey);
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

            // Recreate HTTP client with the registered client ID so X-Client-Id header is sent
            _httpClient.Dispose();
            _httpClient = new OpenAIClient(_config.LlmProvider.ResolvedEndpoint, clientId: _serverClient.ClientId);

            // Setup tokenizer (local mode only — remote APIs don't expose /eca/tokenize)
            _remoteTokenizer = new RemoteTokenizer(_httpClient, _config.LlmProvider.ModelId);

            // Start heartbeat
            _serverClient.StartHeartbeat(_config.LlmProvider.HeartbeatIntervalSec, () => _sessions.Count);

            _logger.Info("SessionManager", $"Connected to LLM server at {_config.LlmProvider.ResolvedEndpoint}");
        }
        else
        {
            _logger.Info("SessionManager", $"Remote mode: {EffectiveEndpoint} (model: {EffectiveModelId})");
        }
    }

    /// <summary>Get a session by key.</summary>
    public AgentSession? Get(string key)
    {
        lock (_sessionsLock)
            return _sessions.GetValueOrDefault(key);
    }

    /// <summary>List all sessions.</summary>
    public IReadOnlyList<AgentSession> List()
    {
        lock (_sessionsLock)
            return _sessions.Values.ToList();
    }

    /// <summary>Number of sessions.</summary>
    public int Count { get { lock (_sessionsLock) return _sessions.Count; } }

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
        lock (_sessionsLock)
        {
            if (_sessions.ContainsKey(key))
                throw new InvalidOperationException($"Session already exists: {key}");
        }

        var inferenceParams = _inferenceParamsFactory.Create(_config);
        inferenceParams.SessionId = key;

        var session = new AgentSession(
            key: key,
            sessionId: key,
            endpoint: EffectiveEndpoint,
            clientId: IsLocalMode ? _serverClient!.ClientId : null,
            apiKey: IsRemoteMode ? EffectiveApiKey : null,
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

        lock (_sessionsLock)
            _sessions.Add(key, session);
        Interlocked.Increment(ref _sessionCounter);
        return session;
    }

    /// <summary>Create a new auto-named session.</summary>
    public AgentSession CreateSession(string? label = null)
    {
        var key = $"session-{Interlocked.Increment(ref _sessionCounter)}";
        return CreateSession(key, label);
    }

    /// <summary>Switch the active session to the one with this key.</summary>
    public bool SwitchTo(string key)
    {
        AgentSession? session;
        lock (_sessionsLock)
        {
            if (!_sessions.TryGetValue(key, out session)) return false;
        }
        ActiveSession = session;
        _sessionDiscovery.TouchSessionMeta(_workingDir, key);
        return true;
    }

    /// <summary>Switch active session by index (1-based).</summary>
    public bool SwitchTo(int index)
    {
        List<AgentSession> list;
        lock (_sessionsLock)
            list = _sessions.Values.ToList();
        if (index < 1 || index > list.Count) return false;
        ActiveSession = list[index - 1];
        _sessionDiscovery.TouchSessionMeta(_workingDir, ActiveSession.Key);
        return true;
    }

    /// <summary>Stop a specific session's execution.</summary>
    public void StopSession(string key)
    {
        AgentSession? session;
        lock (_sessionsLock)
            _sessions.TryGetValue(key, out session);
        session?.Stop();
    }

    /// <summary>Stop all sessions.</summary>
    public void StopAll()
    {
        List<AgentSession> sessions;
        lock (_sessionsLock)
            sessions = _sessions.Values.ToList();
        foreach (var session in sessions)
            session.Stop();
    }

    /// <summary>Delete a session.</summary>
    public void DeleteSession(string key)
    {
        AgentSession? session;
        lock (_sessionsLock)
        {
            if (!_sessions.TryGetValue(key, out session)) return;
            _sessions.Remove(key);
        }
        session.Stop();
        // Clean up session directory
        var sessionDir = Path.Combine(_workingDir, ".sessions", key);
        if (Directory.Exists(sessionDir))
        {
            try { Directory.Delete(sessionDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Mark user activity (resets idle timer). Called on any user input.
    /// If currently idle-disconnected, triggers reconnection.
    /// </summary>
    public void MarkUserActivity()
    {
        _lastUserActivity = DateTime.UtcNow;

        if (_isIdleDisconnected && IsLocalMode)
        {
            _ = Task.Run(async () =>
            {
                try { await ReconnectAfterIdleAsync(); }
                catch (Exception ex) { _logger.Error("SessionManager", $"Reconnect after idle failed: {ex.Message}"); }
            });
        }
    }

    /// <summary>
    /// Start the idle watchdog timer. Call after initialization.
    /// </summary>
    public void StartIdleWatchdog(int idleTimeoutMin)
    {
        _idleTimeoutMin = idleTimeoutMin;
        _idleTimer?.Dispose();
        _idleTimer = new Timer(CheckIdle, null,
            TimeSpan.FromSeconds(_idleCheckIntervalSec),
            TimeSpan.FromSeconds(_idleCheckIntervalSec));
        _logger.Info("SessionManager", $"Idle watchdog started — timeout {idleTimeoutMin} min");
    }

    private async void CheckIdle(object? state)
    {
        if (_isIdleDisconnected || !IsLocalMode || _idleTimeoutMin <= 0) return;

        var idleFor = DateTime.UtcNow - _lastUserActivity;
        if (idleFor.TotalMinutes < _idleTimeoutMin) return;

        // User has been idle past the threshold — disconnect to free VRAM
        _logger.Info("SessionManager", $"User idle for {idleFor.TotalMinutes:F0} min — disconnecting from LLM server to free resources");
        _isIdleDisconnected = true;

        try
        {
            // Stop heartbeat
            _serverClient?.StopHeartbeat();

            // Send shutdown request — server will wind down if we're the only client
            await _serverLauncher!.StopServerAsync();
        }
        catch (Exception ex)
        {
            _logger.Warn("SessionManager", $"Idle disconnect error: {ex.Message}");
        }
    }

    /// <summary>
    /// Reconnect to the LLM server after an idle disconnect.
    /// Re-registers as client, recreates KV cache sessions, and re-prefills.
    /// </summary>
    public async Task ReconnectAfterIdleAsync()
    {
        if (!_isIdleDisconnected || !IsLocalMode) return;

        // Reentrancy guard — rapid MarkUserActivity calls must not spawn parallel reconnects
        if (Interlocked.Exchange(ref _reconnectInProgress, 1) == 1) return;
        try
        {
            await ReconnectAfterIdleCoreAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectInProgress, 0);
        }
    }

    private async Task ReconnectAfterIdleCoreAsync()
    {
        if (!_isIdleDisconnected || !IsLocalMode) return;

        _logger.Info("SessionManager", "Reconnecting after idle — ensuring LLM server is running...");

        // Ensure server is back up
        var ok = await _serverLauncher!.EnsureServerRunningAsync();
        if (!ok)
        {
            _logger.Error("SessionManager", "Failed to restart LLM server after idle");
            return;
        }

        // Re-register as client
        var connected = await _serverClient!.ConnectAsync("ECAssistant", "1.0.0");
        if (!connected)
        {
            _logger.Error("SessionManager", "Failed to re-register with LLM server");
            return;
        }

        // Recreate HTTP client with new clientId
        _httpClient.Dispose();
        _httpClient = new OpenAIClient(_config.LlmProvider.ResolvedEndpoint, clientId: _serverClient.ClientId);
        _remoteTokenizer = new RemoteTokenizer(_httpClient, _config.LlmProvider.ModelId);

        // Restart heartbeat
        _serverClient.StartHeartbeat(_config.LlmProvider.HeartbeatIntervalSec, () => _sessions.Count);

        // Recreate KV cache sessions on the server and re-prefill
        List<AgentSession> sessions;
        lock (_sessionsLock)
            sessions = _sessions.Values.ToList();
        foreach (var session in sessions)
        {
            try
            {
                session.UpdateClientId(_serverClient.ClientId, _httpClient);
                await session.RecreateKvCacheSessionAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn("SessionManager", $"Failed to restore session {session.Key}: {ex.Message}");
            }
        }

        _isIdleDisconnected = false;
        _lastUserActivity = DateTime.UtcNow;
        _logger.Info("SessionManager", "Reconnected after idle — sessions restored");
    }

    /// <summary>
    /// Stop the local LLM server (heartbeat disconnect + graceful shutdown + force-kill fallback).
    /// Used by /reinstall to reset to a clean state.
    /// </summary>
    public async Task StopLocalServerAsync()
    {
        if (!IsLocalMode || _serverLauncher == null) return;
        try { await _serverClient!.DisconnectAsync(); }
        catch { /* best effort */ }
        try { await _serverLauncher.StopServerAsync(); }
        catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        // Stop idle watchdog
        _idleTimer?.Dispose();

        // Stop all sessions
        StopAll();

        // Local mode: disconnect from server + trigger graceful shutdown
        if (IsLocalMode)
        {
            try { await _serverClient!.DisconnectAsync(); }
            catch { /* best effort */ }
            try { await _serverLauncher!.StopServerAsync(); }
            catch { /* best effort */ }
        }

        try { _httpClient.Dispose(); }
        catch { /* best effort */ }
    }
}