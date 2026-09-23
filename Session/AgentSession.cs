using System.Text;
using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Memory;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Services;
using ECAssistant.Core.Services.Http;
using ECAssistant.Core.Transport;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Session;

/// <summary>
/// A fully isolated agent session.
///
/// Each session has:
/// - Its own AgentEngine (own server-side KV cache via HTTP, own session)
/// - Its own orchestrator
/// - Its own tools (registered independently)
/// - Its own memory
/// - Its own file-based output buffer (JSONL, append-only, persistent)
/// - Its own prompt queue
/// - Its own runner thread (background Task)
/// - Its own stop/cancellation
///
/// Sessions share the same ECAssistantLLM server (one model loaded in VRAM) but are
/// otherwise completely independent. No shared state, no inter-session communication.
///
/// The session is the central hub — all components (orchestrator, engine, tools)
/// get an ISessionOutput reference and call Write/WriteLine/StartStream/RequestApproval.
/// The session writes to a JSONL file (always) and notifies attached IOutputListener(s).
/// </summary>
public class AgentSession : ISessionOutput, ISessionContext, IAsyncDisposable
{
    // ── Identity ──────────────────────────────────────
    public string Key { get; }
    public string? Label { get; set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    // ── Engine & Orchestration ───────────────────────
    private AgentEngine _engine;
    private AgentOrchestrator _orchestrator;
    private readonly string _workingDir;
    private readonly string _sessionDir;
    private readonly SubAgentConfig _subAgentConfig;
    private readonly bool _isLocalMode;

    // ── Output Buffer (file-based JSONL) ─────────────
    private readonly string _outputFilePath;
    private readonly StreamWriter _outputFile;
    private readonly object _fileLock = new();

    // ── Stream buffer (for token-by-token streaming) ──
    private readonly StringBuilder _streamBuffer = new();
    private OutputState _currentState = OutputState.Raw;
    private bool _streaming;
    private readonly object _bufferLock = new();

    // ── Output buffer + Listeners ─────────────────────
    private readonly List<OutputEntry> _outputBuffer = new();
    private readonly List<IOutputListener> _listeners = new();
    private readonly object _listenerLock = new();
    private readonly object _uiLock = new();

    // ── Prompt Queue ───────────────────────────────────
    private readonly Queue<string> _promptQueue = new();
    private readonly object _queueLock = new();

    // ── Run State ─────────────────────────────────────
    private SessionRunState _runState = SessionRunState.Idle;
    private CancellationTokenSource? _executionCts;
    private Task? _runnerTask;
    private readonly object _stateLock = new();
    public SessionRunState RunState => _runState;

    // ── Inference scheduler (shared across all sessions) ──
    private readonly SemaphoreSlim _inferenceLock;

    // ── Last prompt (for status display) ──────────────
    private string _lastPrompt = "";
    public string LastPrompt => _lastPrompt;
    public DateTime RunStartedAt { get; private set; }

    // ── Tool policy ───────────────────────────────────
    private readonly ECAssistant.Core.Tools.ToolPolicy _toolPolicy;
    private readonly AppConfig? _config;  // v10.24: for tool config registration

    /// <summary>UI verbosity. Silent (default) filters diagnostic lines from the UI;
    /// everything is still written to the session transcript file.</summary>
    private volatile SessionVerbosity _verbosity = SessionVerbosity.Silent;

    /// <summary>Current UI verbosity of this session.</summary>
    public SessionVerbosity Verbosity => _verbosity;

    /// <summary>Change UI verbosity at runtime (/verbose, /silent).</summary>
    public void SetVerbosity(SessionVerbosity verbosity) => _verbosity = verbosity;
    private readonly ILogger _logger;

    /// <summary>
    /// Create a new fully isolated session.
    /// Local mode: own server-side KV cache via ECAssistantLLM.
    /// Remote mode: stateless HTTP inference (no KV cache).
    /// </summary>
    public AgentSession(
        string key,
        string sessionId,
        string endpoint,
        string? clientId,
        InferenceRequestParams inferenceParams,
        string workingDir,
        SemaphoreSlim inferenceLock,
        SubAgentConfig? subAgentConfig = null,
        string? label = null,
        ILogger? logger = null,
        AppConfig? config = null,
        OpenAIClient? httpClient = null,
        RemoteTokenizer? remoteTokenizer = null,
        string? apiKey = null,
        bool isLocalMode = true)
    {
        _logger = logger ?? new Logger();
        Key = key;
        Label = label;
        _workingDir = workingDir;
        _inferenceLock = inferenceLock;
        _subAgentConfig = subAgentConfig ?? new SubAgentConfig();
        _isLocalMode = isLocalMode;
        _toolPolicy = new ECAssistant.Core.Tools.ToolPolicy();
        if (config?.SystemTools != null)
            _toolPolicy.LoadSystemTools(config.SystemTools);
        if (config?.ToolPermissions != null)
            _toolPolicy.LoadFromConfig(config.ToolPermissions);
        _config = config;

        // Verbosity from config: silent=true → Silent; verbose=true → Verbose; both unset → Silent.
        if (config?.Interface.Silent == true)
            _verbosity = SessionVerbosity.Silent;
        else if (config?.Interface.Verbose == true)
            _verbosity = SessionVerbosity.Verbose;

        // Create session directory
        _sessionDir = Path.Combine(workingDir, ".sessions", key);
        Directory.CreateDirectory(_sessionDir);

        // Open output file
        _outputFilePath = Path.Combine(_sessionDir, "ui_output.jsonl");
        _outputFile = new StreamWriter(_outputFilePath, append: true, Encoding.UTF8) { AutoFlush = true };

        // Create HTTP client + inference engine + KV cache controller
        var client = httpClient ?? new OpenAIClient(endpoint, clientId, apiKey);
        var modelId = config?.LlmProvider.ModelId ?? "main";
        var inferenceEngine = new HttpStreamingEngine(client, modelId, isLocalMode ? sessionId : null);

        // KV cache only in local mode; remote mode uses no-op controller
        IKvCacheController kvCacheController = isLocalMode
            ? new RemoteKvCacheController(client)
            : new NopKvCacheController();

        _engine = new AgentEngine(
            sessionId: sessionId,
            inferenceEngine: inferenceEngine,
            kvCacheController: kvCacheController,
            inferenceParams: inferenceParams,
            contextSize: config?.Llm.ContextSize > 0 ? config.Llm.ContextSize : 16384,
            workingDir: workingDir,
            logger: _logger,
            tokenizer: remoteTokenizer,
            config: _config);

        _engine.LoadContext();
        _engine.WireSummaryService();

        // Create orchestrator
        // Turn limit from config — interface.max_turns (0 = unlimited is clamped by the orchestrator).
        var orchestratorTurns = config?.Interface.MaxTurns > 0 ? config.Interface.MaxTurns : 10;
        _orchestrator = new AgentOrchestrator(_engine, sessionOutput: this, maxTurns: orchestratorTurns, maxFailures: 3, toolPolicy: _toolPolicy, logger: _logger, config: config);

        // Wire engine output through this session
        _engine.SetSessionOutput(this);

        // Initialize self-correction, project context, task planner
        _engine.InitializeSelfCorrection(workingDir);
        _engine.InitializePlaybooks(workingDir);
        _engine.InitializeContextPinning();
        _engine.InitializeTaskPlanner();

        WriteSystem($"Session '{key}' created.");
    }

    /// <summary>
    /// Update the client ID and HTTP client after reconnection.
    /// Called by SessionManager.ReconnectAfterIdleAsync to rewire the session
    /// to the new server connection.
    /// </summary>
    public void UpdateClientId(string newClientId, OpenAIClient newHttpClient)
    {
        _engine.UpdateHttpClient(newHttpClient, newClientId);
    }

    /// <summary>
    /// Recreate the KV cache session on the LLM server and re-prefill the static prefix.
    /// Called after reconnection to restore the session's server-side state.
    /// </summary>
    public async Task RecreateKvCacheSessionAsync()
    {
        if (!_isLocalMode) return;
        try
        {
            // The server may have restarted under us — force a full session
            // recreate + re-prefill instead of trusting stale KV state.
            _engine.InvalidateKvSessionState();
            await _engine.PrefillStaticPrefix();
        }
        catch (Exception ex)
        {
            _logger?.Warn("AgentSession", $"Re-prefill failed for {Key}: {ex.Message}");
        }
    }

    /// <summary>The engine powering this session.</summary>
    public AgentEngine Engine => _engine;



    /// <summary>ISessionContext: Background task config (decompose + summarize).</summary>
    public BackgroundTasksConfig? BackgroundTasks => _engine.BackgroundTasks;

    /// <summary>ISessionContext: Keyword memory.</summary>
    public MemoryManager Memory => _engine.Memory;

    /// <summary>ISessionContext: Vector memory (semantic search).</summary>
    public VectorMemoryStore? VectorMemory => _engine.VectorMemory;

    /// <summary>The orchestrator managing multi-step execution.</summary>
    public AgentOrchestrator Orchestrator => _orchestrator;

    /// <summary>v14.12.2: queue mid-run steering — drained by the orchestrator at the
    /// next turn boundary and injected as fresh instructions. Safe when idle (drained
    /// on the next run) and safe mid-run.</summary>
    public void Steer(string message) => _orchestrator?.Steering.Steer(message);

    /// <summary>The tool policy for this session.</summary>
    public ECAssistant.Core.Tools.ToolPolicy Policy => _toolPolicy;

    /// <summary>Number of messages in the conversation.</summary>
    public int MessageCount => _engine.Transcript.MessageCount;

    /// <summary>Total tokens used in context window.</summary>
    public int ContextTokens => _engine.ContextWindow.GetTotalTokens();

    /// <summary>Max token budget for this session.</summary>
    public uint MaxTokens => _engine.ContextWindow.MaxTokens;

    /// <summary>Number of queued prompts.</summary>
    public int QueueCount
    {
        get
        {
            lock (_queueLock) return _promptQueue.Count;
        }
    }

    /// <summary>Rename this session's label.</summary>
    public void Rename(string newLabel)
    {
        Label = newLabel;
        LastActivity = DateTime.UtcNow;
        WriteSystem($"Session renamed to '{newLabel}'.");
    }

    /// <summary>Get detailed session info: KV cache, context, memory, run state.</summary>
    public string GetDetailedInfo()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Session '{Key}' ===");
        if (Label != null) sb.AppendLine($"Label: {Label}");
        sb.AppendLine($"State: {_runState}");
        sb.AppendLine($"Created: {CreatedAt:O}");
        sb.AppendLine($"Last Activity: {LastActivity:O}");
        sb.AppendLine();
        sb.AppendLine($"── Context ──");
        sb.AppendLine($"  Tokens: {Engine.UsedTokens}/{Engine.MaxContextTokens} ({Engine.ContextUsagePercent}%)");
        sb.AppendLine($"  Until summarize: {Engine.TokensUntilSummarize} tokens");
        sb.AppendLine($"  Near overflow: {(Engine.IsContextNearOverflow ? "⚠️ Yes" : "No")}");
        sb.AppendLine();
        sb.AppendLine($"── KV Cache ──");
        sb.AppendLine($"  Context size: {Engine.KVCacheContextSize} tokens");
        sb.AppendLine($"  Prefilled: {(Engine.IsKVCachePrefilled ? "Yes" : "No")}");
        sb.AppendLine($"  Usage ratio: {Engine.KVCacheUsageRatio:P1}");
        sb.AppendLine($"  Est. memory: {Engine.KVCacheEstimatedMB} MB (approx)");
        sb.AppendLine();
        sb.AppendLine($"── Session ──");
        sb.AppendLine($"  Messages: {MessageCount}");
        sb.AppendLine($"  Prompt queue: {QueueCount}");
        if (_runState == SessionRunState.Running)
        {
            var elapsed = DateTime.UtcNow - RunStartedAt;
            sb.AppendLine($"  Running for: {elapsed.TotalSeconds:F0}s");
            sb.AppendLine($"  Last prompt: {TruncatePrompt(LastPrompt)}");
        }
        sb.AppendLine();
        sb.AppendLine($"── Tools ──");
        sb.AppendLine($"  Registered: {Engine.Tools.Count}");
        foreach (var t in Engine.Tools)
            sb.AppendLine($"    {t.Name}");
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════
    //  ISessionOutput — STREAMING METHODS
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Begin a stream: empty the buffer, set state, notify listeners.
    /// </summary>
    public void StartStream(OutputState state)
    {
        lock (_bufferLock)
        {
            _streamBuffer.Clear();
            _currentState = state;
            _streaming = true;
        }

        lock (_uiLock)
        {
            foreach (var l in _listeners) { try { l.OnStreamStart(); } catch (Exception ex) { _logger?.Debug("Session", $"Non-critical error ignored: {ex.Message}"); } }
        }
    }

    /// <summary>
    /// Append a token to the stream buffer. No file I/O, no listener notification.
    /// </summary>
    public void Write(string token)
    {
        lock (_bufferLock)
        {
            _streamBuffer.Append(token);
        }
    }

    /// <summary>
    /// End the stream: notify listeners that streaming stopped.
    /// The caller should follow this with WriteLine(buffer, Raw) to flush.
    /// </summary>
    public void StopStream()
    {
        lock (_bufferLock)
        {
            _streaming = false;
        }

        lock (_uiLock)
        {
            foreach (var l in _listeners) { try { l.OnStreamStop(); } catch (Exception ex) { _logger?.Debug("Session", $"Non-critical error ignored: {ex.Message}"); } }
        }
    }

    // ═══════════════════════════════════════════════════
    //  ISessionOutput — DISCRETE OUTPUT
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// True when a line should be hidden from the UI in Silent mode.
    /// Diagnostic chatter ([Orchestrator]/[Engine]/[KVCache]/[Memory]/[Plan] status,
    /// Dim debug lines) is filtered; errors, warnings, answers and stream output show.
    /// The session transcript file always receives everything.
    /// </summary>
    private bool IsHiddenInSilentMode(string text, OutputState state)
    {
        if (state == OutputState.Error || state == OutputState.Warning) return false;
        if (state == OutputState.Raw || state == OutputState.Bold || state == OutputState.Success) return false;

        // All Dim lines are diagnostics
        if (state == OutputState.Dim) return true;

        // Info/System lines: hide known diagnostic prefixes only
        if (text.Length > 1 && text[0] == '[')
        {
            foreach (var prefix in DiagnosticPrefixes)
            {
                if (text.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    /// <summary>Line prefixes treated as internal diagnostics (hidden in Silent mode).</summary>
    private static readonly string[] DiagnosticPrefixes =
    {
        "[Orchestrator]", "[Engine]", "[KVCache]", "[Memory]", "[StepMapper]",
        "[Plan]", "[Last session]", "[Vision]", "[Tool] Registered", "[Decompose]",
    };

    /// <summary>
    /// Write a line with a state. If a stream is active, stops it first,
    /// flushes the buffer as a stream entry, then writes the line.
    /// </summary>
    public void WriteLine(string text, OutputState state = OutputState.Info)
    {
        lock (_bufferLock)
        {
            // Always flush any remaining stream content, even if StopStream was already called
            if (_streaming)
                StopStream();
            FlushStreamBuffer();

            _currentState = state;

            var entry = new OutputEntry
            {
                Type = "line",
                Text = text,
                State = state,
                Ts = DateTime.UtcNow.ToString("O")
            };

            WriteEntryToFile(entry);

            // Silent mode: transcript gets everything, the UI gets only essentials.
            if (_verbosity == SessionVerbosity.Silent && IsHiddenInSilentMode(text, state))
                return;

            lock (_uiLock)
            {
                _outputBuffer.Add(entry);
                foreach (var listener in _listeners)
                {
                    try { listener.OnOutput(text, state); }
                    catch { /* don't let UI errors crash the session */ }
                }
            }
        }
    }

    /// <summary>Write a blank line.</summary>
    public void BlankLine() => WriteLine("", OutputState.Info);

    /// <summary>Write a system-level message.</summary>
    public void WriteSystem(string text) => WriteLine(text, OutputState.System);

    /// <summary>Write an info message.</summary>
    public void WriteInfo(string text) => WriteLine(text, OutputState.Info);

    /// <summary>Write a success message.</summary>
    public void WriteSuccess(string text) => WriteLine(text, OutputState.Success);

    /// <summary>Write a warning.</summary>
    public void WriteWarning(string text) => WriteLine(text, OutputState.Warning);

    /// <summary>Write an error.</summary>
    public void WriteError(string text) => WriteLine(text, OutputState.Error);

    /// <summary>Write dim text.</summary>
    public void WriteDim(string text) => WriteLine(text, OutputState.Dim);

    public void WriteTag(string tag, string message, OutputState state = OutputState.Info)
        => WriteLine($"[{tag}] {message}", state);

    // ═══════════════════════════════════════════════════
    //  ISessionOutput — STREAM BUFFER ACCESS
    // ═══════════════════════════════════════════════════

    /// <summary>Get the current stream buffer content (thread-safe).</summary>
    public string GetStreamBuffer()
    {
        lock (_bufferLock)
        {
            return _streamBuffer.ToString();
        }
    }

    /// <summary>Get the current stream state (thread-safe).</summary>
    public OutputState GetStreamState()
    {
        lock (_bufferLock)
        {
            return _currentState;
        }
    }

    // ═══════════════════════════════════════════════════
    //  ISessionOutput — USER APPROVAL
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Request user approval. Blocks until the attached listener responds.
    /// If no listener is attached, waits until one attaches and responds.
    /// </summary>
    public bool RequestApproval(string message)
        => RequestApprovalAsync(message).GetAwaiter().GetResult();

    /// <summary>v14.9: blocking choice request — see RequestChoiceAsync.</summary>
    public int? RequestChoice(string prompt, IReadOnlyList<string> options)
        => RequestChoiceAsync(prompt, options).GetAwaiter().GetResult();

    /// <summary>v14.10.1: publish an activity-status hint to all listeners
    /// (spinner label in the TUI). Fire-and-forget; null/empty clears.</summary>
    public void SetStatus(string? status)
    {
        List<IOutputListener> snapshot;
        lock (_listenerLock)
            snapshot = _listeners.ToList();
        foreach (var l in snapshot)
        {
            try { l.OnStatus(status); }
            catch (Exception ex) { _logger?.Debug("Session", $"Status dispatch ignored: {ex.Message}"); }
        }
    }

    /// <summary>
    /// v14.9: interactive checkpoint — present prompt + numbered options and block
    /// until a listener responds. Mirrors RequestApprovalAsync (write, immediate
    /// listener attempt, then bounded wait; null on timeout/cancel/no listener).
    /// </summary>
    public async Task<int?> RequestChoiceAsync(string prompt, IReadOnlyList<string> options)
    {
        // Render the checkpoint: prompt + numbered options
        WriteLine(prompt, OutputState.Warning);
        for (int i = 0; i < options.Count; i++)
            WriteLine($"  {i + 1}) {options[i]}", OutputState.Info);

        lock (_uiLock)
        {
            if (_listeners.Count > 0)
            {
                try { return _listeners[0].OnRequestChoice(prompt, options); }
                catch { return null; }
            }
        }

        var waitMs = 100;
        var maxWaitMs = 60000; // 60s — choices deserve more patience than y/n gates
        var waited = 0;
        while (waited < maxWaitMs)
        {
            await Task.Delay(waitMs);
            waited += waitMs;

            lock (_uiLock)
            {
                if (_listeners.Count > 0)
                {
                    try { return _listeners[0].OnRequestChoice(prompt, options); }
                    catch { return null; }
                }
            }

            if (_executionCts?.IsCancellationRequested == true)
                return null;
        }
        return null; // timeout — caller proceeds autonomously
    }

    /// <summary>
    /// Async approval polling: Task.Delay instead of Thread.Sleep keeps the waiting
    /// thread-pool thread free while polling for a listener.
    /// </summary>
    /// <summary>
    /// v14.10.2: scoped approval — forwards to the first listener's tri-state
    /// handler (y = AllowOnce, a = AllowSession, n = Deny in the TUI). Mirrors
    /// RequestApprovalAsync's listener/timeout semantics.
    /// </summary>
    public ApprovalScope RequestApprovalScoped(string message)
        => RequestApprovalScopedAsync(message).GetAwaiter().GetResult();

    public async Task<ApprovalScope> RequestApprovalScopedAsync(string message)
    {
        WriteLine(message, OutputState.Warning);

        lock (_uiLock)
        {
            if (_listeners.Count > 0)
            {
                try
                {
                    return _listeners[0].OnRequestApprovalScoped(message);
                }
                catch
                {
                    return ApprovalScope.Deny;
                }
            }
        }

        var waited = 0;
        while (waited < 30000)
        {
            await Task.Delay(100);
            waited += 100;
            lock (_uiLock)
            {
                if (_listeners.Count > 0)
                {
                    try
                    {
                        return _listeners[0].OnRequestApprovalScoped(message);
                    }
                    catch
                    {
                        return ApprovalScope.Deny;
                    }
                }
            }
        }
        return ApprovalScope.Deny; // no listener within timeout — auto-deny
    }

    public async Task<bool> RequestApprovalAsync(string message)
    {
        // Write the approval request as an output line first
        WriteLine(message, OutputState.Warning);

        lock (_uiLock)
        {
            if (_listeners.Count > 0)
            {
                // Ask the first listener — it handles user interaction
                try
                {
                    return _listeners[0].OnRequestApproval(message);
                }
                catch
                {
                    return false;
                }
            }
        }

        // No listener attached — wait for one (with timeout to prevent infinite hang)
        // This handles the case where a session runs in the background
        // and the user hasn't switched to it yet
        var waitMs = 100;
        var maxWaitMs = 30000; // 30 seconds max — then auto-deny
        var waited = 0;
        while (waited < maxWaitMs)
        {
            await Task.Delay(waitMs);
            waited += waitMs;

            lock (_uiLock)
            {
                if (_listeners.Count > 0)
                {
                    try
                    {
                        return _listeners[0].OnRequestApproval(message);
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            // Check if execution was cancelled while waiting
            if (_executionCts?.IsCancellationRequested == true)
            {
                return false;
            }
        }

        // Timed out waiting for a listener — auto-deny to prevent infinite hang
        return false;
    }

    // ═══════════════════════════════════════════════════
    //  INTERNAL: FLUSH + FILE I/O
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Flush the stream buffer as a "stream" entry to file + listeners.
    /// Called internally by WriteLine when a stream is active.
    /// </summary>
    private void FlushStreamBuffer()
    {
        if (_streamBuffer.Length == 0) return;

        var text = _streamBuffer.ToString();
        _streamBuffer.Clear();

        var entry = new OutputEntry
        {
            Type = "stream",
            Text = text,
            State = _currentState,
            Ts = DateTime.UtcNow.ToString("O")
        };

        WriteEntryToFile(entry);

        // Silent mode: transcript gets everything, the UI gets only essentials.
        if (_verbosity == SessionVerbosity.Silent && IsHiddenInSilentMode(text, _currentState))
            return;

        lock (_uiLock)
        {
            _outputBuffer.Add(entry);
            foreach (var listener in _listeners)
            {
                try { listener.OnOutput(text, _currentState); }
                catch (Exception ex) { _logger?.Debug("Session", $"Non-critical error ignored: {ex.Message}"); }
            }
        }
    }

    /// <summary>Write an output entry to the JSONL file (thread-safe).</summary>
    private void WriteEntryToFile(OutputEntry entry)
    {
        lock (_fileLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(entry);
                _outputFile.WriteLine(json);
            }
            catch { /* don't crash on file I/O errors */ }
        }
    }

    // ═══════════════════════════════════════════════════
    //  LISTENER ATTACHMENT
    // ═══════════════════════════════════════════════════

    /// <summary>Add a listener for live output notifications.</summary>
    public void AddListener(IOutputListener listener)
    {
        lock (_uiLock)
        {
            _listeners.Add(listener);
        }
    }

    /// <summary>Remove a listener.</summary>
    public void RemoveListener(IOutputListener listener)
    {
        lock (_uiLock)
        {
            _listeners.Remove(listener);
        }
    }

    /// <summary>Read the full output history from the JSONL file.</summary>
    public List<OutputEntry> ReadOutputHistory()
    {
        var entries = new List<OutputEntry>();
        try
        {
            lock (_bufferLock)
            {
                if (_streaming)
                {
                    StopStream();
                    FlushStreamBuffer();
                }
            }

            lock (_fileLock)
            {
                _outputFile.Flush();
                foreach (var line in File.ReadLines(_outputFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var entry = JsonSerializer.Deserialize<OutputEntry>(line);
                    if (entry != null) entries.Add(entry);
                }
            }
        }
        catch { /* return what we have */ }
        return entries;
    }

    /// <summary>Read the last N output entries (for peek command).</summary>
    public List<OutputEntry> ReadOutputHistory(int lastN)
    {
        var all = ReadOutputHistory();
        if (all.Count <= lastN) return all;
        return all.Skip(all.Count - lastN).ToList();
    }

    // ═══════════════════════════════════════════════════
    //  PROMPT QUEUE & EXECUTION
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Send a prompt to this session.
    /// If idle → starts execution in own thread immediately.
    /// If running → queues the prompt for after current execution.
    /// </summary>
    public void Prompt(string input)
    {
        LastActivity = DateTime.UtcNow;

        lock (_stateLock)
        {
            if (_runState == SessionRunState.Idle)
            {
                _lastPrompt = input;
                StartRunner(input);
            }
            else
            {
                // Session is running or stopping — queue the prompt
                lock (_queueLock)
                {
                    _promptQueue.Enqueue(input);
                }
                WriteSystem($"[Queued] Prompt added to queue (position {QueueCount})");
            }
        }
    }

    /// <summary>Start the runner thread for a prompt.</summary>
    private void StartRunner(string prompt)
    {
        SetRunState(SessionRunState.Running);
        RunStartedAt = DateTime.UtcNow;
        _executionCts = new CancellationTokenSource(
            // Configurable per-run timeout (AgentSettings.execution_timeout_minutes, default 10).
            TimeSpan.FromMinutes(Math.Max(1, _config?.AgentSettings.ExecutionTimeoutMinutes ?? 10)));

        _engine.StartExecution();

        var myCts = _executionCts;
        _runnerTask = Task.Run(async () =>
        {
            await RunExecutionLoop(prompt, myCts!);
        });
    }

    /// <summary>
    /// The runner loop — executes a prompt, then checks the queue for more.
    /// Continues until queue is empty, then goes idle.
    /// </summary>
    private async Task RunExecutionLoop(string prompt, CancellationTokenSource cts)
    {
        CancellationToken ct = cts.Token;
        string currentPrompt = prompt;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_inferenceLock.CurrentCount == 0)
                {
                    WriteDim("[Waiting] Another session is generating — waiting for model...");
                }
                await _inferenceLock.WaitAsync(ct);
                try
                {
                    await _engine.PrefillStaticPrefix();
                    var result = await _orchestrator.ExecuteMultiStep(currentPrompt);
                    if (!string.IsNullOrEmpty(result.FinalOutput))
                    {
                        WriteLine(result.FinalOutput, OutputState.Bold);
                    }
                }
                finally
                {
                    _inferenceLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                WriteWarning("Execution cancelled.");
                break;
            }
            catch (Exception ex)
            {
                WriteError($"Execution error: {ex.Message}");
                if (ex.InnerException != null)
                    WriteDim($"Detail: {ex.InnerException.Message}");
                break;
            }
            finally
            {
                _engine.EndExecution();
            }

            // Check queue for next prompt
            string? nextPrompt = null;
            lock (_queueLock)
            {
                if (_promptQueue.Count > 0)
                {
                    nextPrompt = _promptQueue.Dequeue();
                }
            }

            if (nextPrompt == null)
            {
                break;
            }

            WriteSystem($"[Queue] Starting next prompt: {TruncatePrompt(nextPrompt)}");
            currentPrompt = nextPrompt;
            _lastPrompt = currentPrompt;
            LastActivity = DateTime.UtcNow;
        }

        SetRunState(SessionRunState.Idle);

        // Dispose only the CTS this run created (captured in StartRunner). Audit fix:
        // the old code re-read _executionCts here, so a concurrent Prompt() that saw
        // Idle and installed a fresh CTS for the next run could have its CTS disposed
        // by the finishing run (later Stop() would throw ObjectDisposedException and
        // the new run's timeout would silently break).
        cts.Dispose();
        lock (_stateLock)
        {
            if (ReferenceEquals(_executionCts, cts))
                _executionCts = null;
        }
    }

    /// <summary>Stop the current execution (scoped to this session only).</summary>
    public void Stop()
    {
        lock (_stateLock)
        {
            if (_runState == SessionRunState.Running)
            {
                SetRunState(SessionRunState.Stopping);
                _engine.StopExecution();
                _executionCts?.Cancel();
                WriteWarning("Execution stopped by user.");
            }
        }
    }

    /// <summary>Get the current prompt queue.</summary>
    public List<string> GetQueue()
    {
        lock (_queueLock) return _promptQueue.ToList();
    }

    /// <summary>Remove a prompt from the queue by index.</summary>
    public bool RemoveFromQueue(int index)
    {
        lock (_queueLock)
        {
            var queueList = _promptQueue.ToList();
            if (index < 0 || index >= queueList.Count) return false;
            queueList.RemoveAt(index);

            _promptQueue.Clear();
            foreach (var p in queueList) _promptQueue.Enqueue(p);
            return true;
        }
    }

    /// <summary>Clear the entire prompt queue.</summary>
    public void ClearQueue()
    {
        lock (_queueLock)
        {
            _promptQueue.Clear();
        }
    }

    // ═══════════════════════════════════════════════════
    //  STATE MANAGEMENT
    // ═══════════════════════════════════════════════════

    private void SetRunState(SessionRunState state)
    {
        _runState = state;
    }

    // ═══════════════════════════════════════════════════
    //  REGISTRATION (tools, vector memory, project context)
    // ═══════════════════════════════════════════════════

    /// <summary>Register a tool for this session's engine.
    /// v10.24: If tool's config section is not in AppConfig.Tools, adds it via GetConfigSection()
    /// and calls AgentConfigBuilder.Default.Update() to persist to appsettings.json.
    /// </summary>
    public void RegisterTool(EToolBase tool)
    {
        // Set session context before registration so tools can use it
        tool.Session = this;

        // v10.24: Auto-register tool config section if not present
        if (_config != null)
        {
            if (!_config.Tools.ContainsKey(tool.Name))
            {
                var section = tool.GetConfigSection();
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(section);
                _config.Tools[tool.Name] = jsonElement;
                AgentConfigBuilder.Default.Update(_config);
            }
        }
        _engine.RegisterTool(tool);
    }

    /// <summary>Initialize vector memory for this session.</summary>
    public async Task InitializeVectorMemoryAsync(string storeDir, IVectorEmbedder? embedder = null)
    {
        await _engine.InitializeVectorMemoryAsync(storeDir, embedder);
    }

    /// <summary>Initialize project context for this session.</summary>
    public async Task InitializeProjectContextAsync()
    {
        await _engine.InitializeProjectContextAsync(_workingDir);
    }

    /// <summary>Initialize sub-agents for this session.</summary>
    public async Task InitializeSubAgentsAsync()
    {
        if (_subAgentConfig.Enabled)
        {
            await _orchestrator.InitializeSubAgentsAsync(_workingDir);
        }
    }

    /// <summary>v15: Initialize handoff support for this session. Registers the EHandoff tool.</summary>
    public async Task InitializeHandoffAsync()
    {
        await _orchestrator.InitializeHandoffAsync(_workingDir);
    }

    /// <summary>Set background task config for this session.</summary>
    public void SetBackgroundTasks(BackgroundTasksConfig config)
    {
        _engine.SetBackgroundTasks(config);
    }

    /// <summary>Clear conversation history for this session.</summary>
    public void ClearHistory()
    {
        _engine.ClearHistory();
    }

    /// <summary>Save transcript to disk.</summary>
    public void SaveTranscript()
    {
        var path = Path.Combine(_sessionDir, "transcript.json");
        _engine.SaveTranscript(path);
    }

    // ═══════════════════════════════════════════════════
    //  STATUS & DISPOSAL
    // ═══════════════════════════════════════════════════

    /// <summary>Get session status summary for display.</summary>
    public string GetStatusSummary()
    {
        var stateStr = _runState switch
        {
            SessionRunState.Idle => "idle",
            SessionRunState.Running => $"running ({(DateTime.UtcNow - RunStartedAt).TotalSeconds:F0}s)",
            SessionRunState.Stopping => "stopping",
            _ => _runState.ToString().ToLowerInvariant()
        };

        var queueStr = QueueCount > 0 ? $"  queue: {QueueCount}" : "";
        var labelStr = Label != null ? $" ({Label})" : "";
        var ctxStr = $"  ctx: {Engine.UsedTokens}/{Engine.MaxContextTokens}";
        return $"[{Key}]{labelStr}  {stateStr}{ctxStr}{queueStr}";
    }

    /// <summary>Truncate a prompt for display.</summary>
    private string TruncatePrompt(string prompt, int maxLen = 60)
    {
        if (string.IsNullOrEmpty(prompt)) return "";
        return prompt.Length <= maxLen ? prompt : prompt.Substring(0, maxLen) + "...";
    }

    public async ValueTask DisposeAsync()
    {
        Stop();

        if (_runnerTask != null)
        {
            try { await _runnerTask; } catch (Exception ex) { _logger?.Debug("Session", $"Non-critical error ignored: {ex.Message}"); }
        }

        try { SaveTranscript(); } catch (Exception ex) { _logger?.Debug("Session", $"Non-critical error ignored: {ex.Message}"); }

        lock (_bufferLock)
        {
            if (_streaming)
                StopStream();
            FlushStreamBuffer();
        }

        lock (_fileLock) { try { _outputFile.Dispose(); } catch (Exception ex) { _logger?.Debug("Session", $"Non-critical error ignored: {ex.Message}"); } }

        await _engine.DisposeAsync();
        await _orchestrator.DisposeAsync();
    }
}