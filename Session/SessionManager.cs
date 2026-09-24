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
    private DateTime _lastConnectivityCheck = DateTime.MinValue;
    private int _idleTimeoutMin;
    private readonly int _idleCheckIntervalSec = 60;
    private int _reconnectInProgress;

    /// <summary>The currently active session.</summary>
    public AgentSession? ActiveSession { get; private set; }

    /// <summary>The main session (always exists, always key "main").</summary>
    public AgentSession Main { get; private set; } = null!;

    private readonly AppConfig _config;
    private int _sessionCounter = 0;
    private readonly ILogger _logger;
    private readonly string _appRoot;

    // HTTP infrastructure (shared across all sessions)
    private ServerConnection? _connection;                  // v13c: unified connection state
    private readonly ServerLauncher? _serverLauncher;       // local mode only
    private ServerLauncher? _embeddingServerLauncher;       // remote main + local embeddings
    private readonly LlmServerClient? _serverClient;        // local mode only
    private OpenAIClient _httpClient;
    private readonly InferenceParamsFactory _inferenceParamsFactory;
    private RemoteTokenizer? _remoteTokenizer;               // local mode only

    // Multi-provider remote selection (null values = fall back to llm_provider section)
    private ILlmProviderRegistry? _registry;
    private SecureKeyStore? _keyStore;
    private readonly Func<string, string?, string?, OpenAIClient> _newClient;
    private string? _effectiveEndpoint;
    private string? _effectiveApiKey;
    private string? _effectiveModelId;

    /// <summary>True if running in local mode (ECAssistantLLM with KV cache).</summary>
    public bool IsLocalMode => _config.LlmProvider.IsLocal;

    /// <summary>v13c: runtime ECA-extension gating — capability-based once connected, config-based before.</summary>
    private bool HasEcaExtensions => _connection?.HasEcaExtensions ?? IsLocalMode;

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
            using var probe = _newClient(candidate.Endpoint, candidate.ApiKey, null);
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
    public SessionManager(
        AppConfig config,
        string resolvedModelPath,
        string workingDir,
        ILogger? logger = null,
        Func<string, string?, string?, OpenAIClient>? openAIClientFactory = null,
        Func<LlmProviderConfig, ServerLauncher>? serverLauncherFactory = null,
        LlmServerClient? serverClient = null,
        SecureKeyStore? keyStore = null,
        ILlmProviderRegistry? providerRegistry = null,
        IModelParamValidator? modelParamValidator = null)
    {
        _logger = logger ?? new Logger();
        _config = config;
        _workingDir = workingDir;
        _appRoot = workingDir; // app root is the working directory
        _subAgentConfig = config.SubAgent;

        var provider = config.LlmProvider;
        _inferenceParamsFactory = InferenceParamsFactory.Default;

        // v12 DI: collaborators injectable for tests/composition; default to the production implementations.
        _newClient = openAIClientFactory ?? ((endpoint, apiKey, clientId) => new OpenAIClient(endpoint, apiKey: apiKey, clientId: clientId));
        var launcherFactory = serverLauncherFactory ?? ((providerConfig) => new ServerLauncher(providerConfig));
        var validator = modelParamValidator ?? new ModelParamValidator(_logger);

        if (provider.IsLocal)
        {
            // ── Local mode: ECAssistantLLM server (shared standalone location) ──
            _serverLauncher = launcherFactory(provider);
            _serverClient = serverClient ?? new LlmServerClient(provider.ResolvedEndpoint);
            _httpClient = _newClient(provider.ResolvedEndpoint, null, null);
        }
        else
        {
            // ── Remote mode: multi-provider registry, else single llm_provider ──
            var keysDir = config.LlmProviders?.KeysDirectory ?? "keys";
            var keysPath = Path.IsPathRooted(keysDir) ? keysDir : Path.Combine(_appRoot, keysDir);
            _keyStore = keyStore ?? new SecureKeyStore(keysPath, _logger);
            _registry = providerRegistry ?? new LlmProviderRegistry(config.LlmProviders, _logger, _keyStore);
            foreach (var err in _registry.ValidationErrors)
                _logger.Warn("SessionManager", $"llm_providers: {err}");

            var selected = SelectRemoteProviderAsync().GetAwaiter().GetResult();
            if (selected != null)
            {
                _effectiveEndpoint = selected.Endpoint;
                _effectiveApiKey = selected.ApiKey;
                _effectiveModelId = selected.ModelId;
                _logger.Info("SessionManager", $"Remote mode via provider '{selected.Name}' ({selected.Endpoint}, model: {selected.ModelId})");

                // Local embeddings + remote main AI → spawn the local LLM server solely
                // for embedding workloads (vector memory). Main inference stays remote.
                var embedding = config.Embedding;
                if (embedding != null && string.Equals(embedding.Mode, "local", StringComparison.OrdinalIgnoreCase))
                {
                    var embeddingProvider = new LlmProviderConfig
                    {
                        Mode = "local",
                        Endpoint = embedding.Endpoint ?? $"http://localhost:{config.LlmProvider.Port}",
                        ServerRootPath = config.LlmProvider.ServerRootPath ?? "~/ECALLM"
                    };
                    _embeddingServerLauncher = launcherFactory(embeddingProvider);
                    var embedOk = _embeddingServerLauncher.EnsureServerRunningAsync().GetAwaiter().GetResult();
                    if (embedOk)
                        _logger.Info("SessionManager", "Embedding server started locally (main AI stays remote)");
                    else
                        _logger.Warn("SessionManager", "Local embedding server failed to start — vector memory may be unavailable");
                }
            }

            _httpClient = _newClient(
                _effectiveEndpoint ?? _config.LlmProvider.ResolvedEndpoint,
                _effectiveApiKey ?? _config.LlmProvider.ApiKey,
                null);
        }

        // Validate config (local mode needs model path; remote mode skips file validation)
        if (provider.IsLocal)
        {
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
        // v13c unified connection model: local ECAssistantLLM and remote providers go
        // through the SAME connect path. The server's capability set decides which
        // extensions run — not the config mode.
        if (IsLocalMode)
        {
            _logger.Info("SessionManager", "Ensuring LLM server is running...");
            var ok = await _serverLauncher!.EnsureServerRunningAsync(ct);
            if (!ok)
                throw new InvalidOperationException("Failed to start LLM server");
        }

        var endpoint = IsLocalMode ? _config.LlmProvider.ResolvedEndpoint : EffectiveEndpoint;
        _connection = await new ServerConnection().ConnectAsync(endpoint, ct);

        if (_connection.HasEcaExtensions)
        {
            _logger.Info("SessionManager", "ECA server detected — registering client...");
            var connected = await _serverClient!.ConnectAsync("ECAssistant", "1.0.0", ct);
            if (!connected)
                throw new InvalidOperationException("Failed to register with LLM server");

            // Recreate HTTP client with the registered client ID so X-Client-Id header is sent
            _httpClient.Dispose();
            _httpClient = _newClient(_config.LlmProvider.ResolvedEndpoint, null, _serverClient.ClientId);

            // Setup tokenizer (ECA extension — remote APIs don't expose /eca/tokenize)
            _remoteTokenizer = new RemoteTokenizer(_httpClient, _config.LlmProvider.ModelId);

            _logger.Info("SessionManager", $"Connected to LLM server at {_config.LlmProvider.ResolvedEndpoint} [{_connection.Capabilities}]");
        }
        else
        {
            _logger.Info("SessionManager", $"OpenAI-compatible backend: {endpoint} (model: {EffectiveModelId}) [{_connection.Capabilities}]");
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
    public AppConfig Config => _config;

    /// <summary>Server client (ECA-extension servers only, null for plain OpenAI backends).</summary>
    public LlmServerClient? ServerClient => _serverClient;

    /// <summary>v13c: capabilities discovered at connect (null before InitializeAsync).</summary>
    public ServerConnection? Connection => _connection;

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

        // v14.19 (Emre): sessions run VERBOSE by default — users see tool status,
        // playbook captures, verify lines, policy flow. Silent stays available via
        // the TUI /verbosity toggle for minimal output.
        session.SetVerbosity(SessionVerbosity.Verbose);

        // Audit fix: re-check under the add lock — two concurrent CreateSession(key)
        // calls could both pass the initial ContainsKey check. The loser would throw
        // KeyExists on Add, but more importantly the orphan session's StreamWriter and
        // engine would leak. Re-check and return the winner instead.
        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(key, out var existing))
                return existing;
            _sessions.Add(key, session);
        }
        Interlocked.Increment(ref _sessionCounter);

        // Wire mid-request connection recovery: when a chat request hits a
        // connection-refused error (server went down outside the idle watchdog),
        // the engine calls back here to restore the server and retry once instead
        // of surfacing the error as model output.
        if (HasEcaExtensions)
            session.Engine.ConnectionRecovery = RecoverConnectionAsync;

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
        // Fire-and-forget form. The message-submit path must use MarkUserActivityAsync
        // so input waits for the connection to be restored (v12.6 race fix).
        _ = MarkUserActivityAsync();
    }

    /// <summary>
    /// Marks user activity; if idle-disconnected, reconnects and COMPLETES only after the
    /// client and KV sessions are fully restored. Await this before processing a message —
    /// v12.6: messages processed during a background reconnect raced the disposed client and were lost.
    /// </summary>
    public async Task MarkUserActivityAsync()
     {
        _lastUserActivity = DateTime.UtcNow;

        if (!HasEcaExtensions)
            return;

        if (_isIdleDisconnected)
         {
            await ReconnectAfterIdleAsync();
            return;
         }

        // The server can also go down outside our idle watchdog — its own
        // shutdown-on-last-client, a crash, or a fresh install that never started
        // it. Proactively restore the connection so requests don't fail with
        // "Connection refused" being fed into the LLM response/parse pipeline.
        if ((DateTime.UtcNow - _lastConnectivityCheck).TotalSeconds < 30) return;
        _lastConnectivityCheck = DateTime.UtcNow;
        if (!await _httpClient.PingAsync())
         {
            _logger.Warn("SessionManager", "LLM server unreachable at user activity — recovering connection");
            await RecoverConnectionAsync();
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
        if (_isIdleDisconnected || !HasEcaExtensions || _idleTimeoutMin <= 0) return;

        var idleFor = DateTime.UtcNow - _lastUserActivity;
        if (idleFor.TotalMinutes < _idleTimeoutMin) return;

        // User has been idle past the threshold — disconnect to free VRAM
        _logger.Info("SessionManager", $"User idle for {idleFor.TotalMinutes:F0} min — disconnecting from LLM server to free resources");
        _isIdleDisconnected = true;

        try
        {
            // Send shutdown WITH our registered client id (server 401s anonymous /eca/*).
            // HandleShutdownAsync disconnects the requesting client itself, so the
            // explicit DELETE below is only a best-effort fallback afterwards.
            await _serverLauncher!.StopServerAsync(_serverClient?.ClientId);
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

    /// <summary>
    /// Mid-request recovery hook (ConnectionRecovery for engines): the local server
    /// went down outside the idle watchdog — its own shutdown-on-last-client, a
    /// crash, or a first-run install that never started it. Reuses the idle-reconnect
    /// flow (ensure server running, re-register, restore KV sessions). Returns true
    /// when the server answers again and the request should be retried.
    /// </summary>
    public async Task<bool> RecoverConnectionAsync()
     {
        if (!IsLocalMode || _serverLauncher == null) return false;

        // Fast path — server already answering (blip was elsewhere). Even so,
        // it may have RESTARTED under us and dropped all KV sessions; restore
        // them before the caller retries, or the retry 404s on a stale session.
        if (await _httpClient.PingAsync())
         {
            await RestoreSessionsAsync();
            return true;
         }

        // Reuse the idle-reconnect flow: EnsureServerRunningAsync + re-register +
        // recreate KV sessions. ReconnectAfterIdleCoreAsync only runs while
        // _isIdleDisconnected is set, so flag it (force mode).
        _isIdleDisconnected = true;
        await ReconnectAfterIdleAsync();
        return !_isIdleDisconnected;
     }

    /// <summary>Recreate server-side KV sessions for all live sessions (after server restart).</summary>
    private async Task RestoreSessionsAsync()
     {
        List<AgentSession> sessions;
        lock (_sessionsLock)
            sessions = _sessions.Values.ToList();
        foreach (var session in sessions)
        {
            try
            {
                session.UpdateClientId(_serverClient!.ClientId, _httpClient);
                await session.RecreateKvCacheSessionAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn("SessionManager", $"Failed to restore session {session.Key}: {ex.Message}");
            }
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
        _httpClient = _newClient(_config.LlmProvider.ResolvedEndpoint, null, _serverClient.ClientId);
        _remoteTokenizer = new RemoteTokenizer(_httpClient, _config.LlmProvider.ModelId);

        // Recreate KV cache sessions on the server and re-prefill
        await RestoreSessionsAsync();

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
        try { await _serverLauncher.StopServerAsync(_serverClient?.ClientId); }
        catch { /* best effort */ }
        try { await _serverClient!.DisconnectAsync(); }
        catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        // Stop idle watchdog
        _idleTimer?.Dispose();

        // Stop all sessions (signals execution halt)
        StopAll();

        // Audit fix: dispose each session's resources (StreamWriter, engine, etc.)
        // before tearing down the server — previously only StopAll() was called which
        // signals halt but never disposes session-owned IDisposable resources.
        List<AgentSession> sessionsToDispose;
        lock (_sessionsLock)
            sessionsToDispose = _sessions.Values.ToList();
        foreach (var session in sessionsToDispose)
        {
            try { await session.DisposeAsync(); }
            catch { /* best effort — continue disposing others */ }
        }

        // Local mode: trigger graceful shutdown FIRST, while our client id is still
        // registered — /eca/shutdown requires a valid X-Client-Id and disconnects the
        // requesting client itself (winding the server down if it was the last one).
        if (IsLocalMode)
        {
            try { await _serverLauncher!.StopServerAsync(_serverClient?.ClientId); }
            catch { /* best effort */ }
            try { await _serverClient!.DisconnectAsync(); }
            catch { /* best effort */ }
        }

        // Remote main + local embeddings: stop the embedding server as well
        if (_embeddingServerLauncher != null)
        {
            try { await _embeddingServerLauncher.StopServerAsync(); }
            catch { /* best effort */ }
        }

        try { _httpClient.Dispose(); }
        catch { /* best effort */ }
    }
}