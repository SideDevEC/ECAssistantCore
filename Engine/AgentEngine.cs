using System.Text;
using ECAssistant.Core.Config;
using ECAssistant.Core.ContextPinning;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Memory;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Session;
using ECAssistant.Core.Services;
using ECAssistant.Core.Services.Http;
using ECAssistant.Core.Tools;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Engine;

/// <summary>
/// v10.30: Core engine. All inference + KV cache control is HTTP-based via the
/// injected IInferenceEngine / IKvCacheController / RemoteTokenizer (ECAssistantLLM
/// server). No in-process LLamaSharp weights or context.
/// Implements IEngine for testing/abstraction.
/// </summary>
public class AgentEngine : IEngine, IEngineToolContext, ISubAgentEngineHost
{
    protected readonly string _modelPath;
    protected readonly uint _contextSize;
    protected readonly AppConfig _config;

    /// <summary>v13: grammar-structured decision decoding enabled — local mode only
    /// (remote providers can't be grammar-constrained). Disabled permanently after
    /// the first unsupported/failure response (older server → text streaming).</summary>
    private bool _useStructuredDecoding;
    protected readonly string _workingDir;
    protected readonly ILogger _logger = new Logger();
    protected ISessionOutput? _out;
    public bool MockMode { get; private set; }

    /// <summary>Explicit mock-mode toggle (write-only flag; kept for test harness).</summary>
    public void SetMockMode(bool enabled) => MockMode = enabled;

     // ── HTTP transport — no in-process LLamaSharp ──
    protected IInferenceEngine? _inferenceEngine;
    protected IKvCacheController? _kvCacheController;
    protected RemoteTokenizer? _tokenizer;
    protected string _sessionId;
    protected InferenceRequestParams? _requestParams;
    protected string? _clientId;

    // ── Injectable service dependencies ──
    protected readonly IProcessRunner _processRunner;
    protected readonly IFileSystem _fileSystem;
    protected readonly IHttpClient _httpClient;

    /// <summary>v15: shared OpenAI client (auth + client-id) exposed to child engines via ISubAgentEngineHost.</summary>
    protected Transport.OpenAIClient? _sharedHttpClient;
    /// <summary>v15: true when running against the local ECAssistantLLM server (KV sessions available).</summary>
    protected readonly bool _isLocalMode;

     // ── Core components ──
    protected TokenCounter _tokenCounter;
    protected MemoryManager _memoryManager;
    protected ContextWindow _contextWindow;
    protected ConversationTranscript _transcript;

     // ── KV cache state (local mirror of server-side status) ──
    private readonly KvCacheState _kvState = new();

     // ── Execution lifecycle (thread-safe: CTS, ESC flag, turn counter) ──
    protected readonly ExecutionLifecycleState _lifecycle = new();
    private string? _systemPromptText;
    private string? _cachedStaticPrefix;
    private const int MaxRewindFailures = 2;

    // ── Output mode (verbose/silent) ──
    private bool _verbose = true;
    private bool _silent = false;

     // ── Injectable component overrides (for library consumers) ──
    private string? _systemPromptPathOverride;
    public string? SystemPromptPath
    {
        get => _systemPromptPathOverride;
        set => _systemPromptPathOverride = value;
     }
    private string? _systemPromptTextOverride;
    public string? SystemPromptText
    {
        get => _systemPromptTextOverride;
        set => _systemPromptTextOverride = value;
     }
    protected BackgroundTasksConfig? _backgroundTasks;
    public BackgroundTasksConfig? BackgroundTasks
    {
        get => _backgroundTasks;
        set => _backgroundTasks = value;
     }
    /// <summary>v14.12: True when the active model runs the large-model (slim) profile.</summary>
    /// <summary>v14.17: tier-tuned request params — small tier gets tighter sampling.</summary>
    private InferenceRequestParams CreateTieredParams(AppConfig cfg)
     {
        var isLocal = cfg.LlmProvider?.IsLocal ?? true;
        return InferenceParamsFactory.Default.CreateTiered(cfg, cfg.ModelTier?.IsLargeRuntime(isLocal) ?? !isLocal);
     }

    private bool IsLargeModelTier()
     {
        var isLocal = _config?.LlmProvider?.IsLocal ?? true;
        return _config?.ModelTier?.IsLargeRuntime(isLocal) ?? !isLocal;
     }

    /// <summary>
    /// v14.12: tier-aware structured envelope token budget. Small models get the
    /// tight cap (they ramble — 768 floor, 1024 ceiling); large models get reasoning
    /// headroom (1024 floor, 4096 ceiling). Pure function — extracted for tests.
    /// </summary>
    // Stateless utility — no mutable state
    private static int ApplyEnvelopeBudget(int configured, bool isLarge)
        => isLarge
            ? Math.Min(Math.Max(configured, 1024), 4096)
            : Math.Min(Math.Max(configured, 768), 1024);

    private ECAssistant.Core.Engine.SelfCorrectionManager? _injectedSelfCorrection;
    public ECAssistant.Core.Engine.SelfCorrectionManager? InjectedSelfCorrection
    {
        get => _injectedSelfCorrection;
        set => _injectedSelfCorrection = value;
     }
    private ECAssistant.Core.Engine.ProjectContextManager? _injectedProjectContext;
    public ECAssistant.Core.Engine.ProjectContextManager? InjectedProjectContext
    {
        get => _injectedProjectContext;
        set => _injectedProjectContext = value;
     }
    private ITaskPlanner? _injectedTaskPlanner;
    public ITaskPlanner? InjectedTaskPlanner
    {
        get => _injectedTaskPlanner;
        set => _injectedTaskPlanner = value;
     }
    private VectorMemoryStore? _vectorMemory;
    public VectorMemoryStore? VectorMemory
    {
        get => _vectorMemory;
        set => _vectorMemory = value;
     }

     // ── Shared components (lazily created / injected) ──
    private readonly List<EToolBase> _tools = new();
    private readonly object _toolsLock = new();
    private SubAgentManager? _subAgentManager;
    private ECAssistant.Core.Engine.SelfCorrectionManager? _selfCorrection;

    // v14.14: persistent success playbooks (tier-aware injection into turn-1 input)
    private ECAssistant.Core.Playbooks.IPlaybookStore? _injectedPlaybookStore;
    private ECAssistant.Core.Playbooks.IPlaybookStore? _playbookStore;

    /// <summary>v14.16: deterministic pinned-context store (null when disabled).</summary>
    private IContextPinner? _contextPinner;

    /// <summary>v14.16: test/dependency-injection seam for the pinner.</summary>
    public IContextPinner? InjectedContextPinner { get; set; }

    /// <summary>v14.17: active pinned-context store (null when pinning disabled/uninitialized) — read-only surface for hosts/tests.</summary>
    public IContextPinner? ContextPinner => _contextPinner;
    /// <summary>v14.14: success-procedure memory store (null until initialized).</summary>
    public ECAssistant.Core.Playbooks.IPlaybookStore? PlaybookStore => _playbookStore;
    private ECAssistant.Core.Engine.ProjectContextManager? _projectContext;
    private ITaskPlanner? _taskPlanner;
    private IStepMapper? _sharedStepMapper;

     // ── Public API ─────────────────────────────────────────────

    public ISessionOutput? SessionOutput { get; private set; }

    /// <summary>Attach or replace the session output renderer.</summary>
    public void SetSessionOutput(ISessionOutput? output)
     {
        SessionOutput = output;
        _out = output; // engine internals write via _out — keep both in sync (audit fix: _out was never assigned, so all engine console output was silently dropped)
     }

    /// <summary>
    /// Mid-request connection recovery hook (local mode only). When a chat request
    /// fails with a connection-level error, the engine invokes this to restore the
    /// local LLM server (restart + re-register + restore KV sessions) and retries
    /// the request once. Wired by SessionManager; null in remote/mock mode.
    /// </summary>
    public Func<Task<bool>>? ConnectionRecovery { get; set; }

    public IInferenceEngine? InferenceEngine => _inferenceEngine;
    public IKvCacheController? KvCacheController => _kvCacheController;
    public RemoteTokenizer? Tokenizer => _tokenizer;
    public string SessionId => _sessionId;

    /// <summary>Engine working directory (public surface for hosts/sub-agents; replaces reflection access).</summary>
    public string WorkingDir => _workingDir;

    /// <summary>
    /// Update HTTP client and client ID after server reconnection.
    /// Replaces the inference engine and KV cache controller with new instances
    /// bound to the new client ID.
    /// </summary>
    /// <summary>v15: ISubAgentEngineHost — shared client for child engines (remote auth correctness).</summary>
    public Transport.OpenAIClient? SharedHttpClient => _sharedHttpClient;
    /// <summary>v15: local-server mode flag (KV sessions available to child engines).</summary>
    public bool IsLocalMode => _isLocalMode;

    public void UpdateHttpClient(OpenAIClient newClient, string newClientId)
    {
        _clientId = newClientId;
        _sharedHttpClient = newClient; // keep child-engine auth in sync after reconnect
        // Recreate KV cache controller with new client
        if (_kvCacheController is RemoteKvCacheController)
        {
            _kvCacheController = new RemoteKvCacheController(newClient);
        }
        // Recreate inference engine with new client
        var modelId = _requestParams?.ModelId ?? _config.LlmProvider.ModelId;
        _inferenceEngine = new HttpStreamingEngine(newClient, modelId, _sessionId);
        // Reset KV session state so PrefillStaticPrefix recreates it.
        // BOTH flags must clear: SessionActive gates CreateSession, IsPrefilled
        // gates the whole prefill — leaving it set made reconnect a silent no-op
        // and every later generate 404 against the restarted server.
        _kvState.SessionActive = false;
        _kvState.IsPrefilled = false;
    }

    /// <summary>
    /// Invalidate client-side KV session state (call when the server may have
    /// restarted and dropped sessions). The next PrefillStaticPrefix recreates
    /// the session and re-prefills the static prefix.
    /// </summary>
    public void InvalidateKvSessionState()
     {
        _kvState.SessionActive = false;
        _kvState.IsPrefilled = false;
     }
    public InferenceRequestParams? InferenceParams => _requestParams;
    public CancellationToken ExecutionToken => _lifecycle.Token;
    public bool IsExecutionStopped => ExecutionToken.IsCancellationRequested;
    public string ModelPath => _modelPath;
    public int MaxIterations { get; private set; } = 10;

    /// <summary>Set the max-iterations guard for the main loop.</summary>
    public void SetMaxIterations(int max)
     {
        if (max > 0) MaxIterations = max;
     }

    public ContextWindow ContextWindow => _contextWindow;
    public ConversationTranscript Transcript => _transcript;
    public MemoryManager Memory => _memoryManager;
    public IReadOnlyList<EToolBase> Tools
     {
        get { lock (_toolsLock) return _tools.ToArray(); }
     }
    public SubAgentManager SubAgentManager => _subAgentManager ??= CreateSubAgentManager();
    public ECAssistant.Core.Engine.SelfCorrectionManager? SelfCorrection => _selfCorrection;
    public ECAssistant.Core.Engine.ProjectContextManager? ProjectContext => _projectContext;
    public ITaskPlanner TaskPlanner => _taskPlanner ??= CreateTaskPlanner();
    public IStepMapper? SharedStepMapper => _sharedStepMapper ??= CreateStepMapper();

     // ── KV cache status (sync — read from cached snapshot) ──
    public bool IsKVCachePrefilled => _kvState.IsPrefilled;
    public uint KVCacheContextSize => _kvState.ContextSize;
    public double KVCacheUsageRatio =>
        _kvState.ContextSize > 0 ? (double)_kvState.ApproxTokens / _kvState.ContextSize : 0.0;
    public double KVCacheEstimatedMB => _kvState.EstimatedMB;
    public int UsedTokens => _kvState.ApproxTokens;
    public int MaxContextTokens => (int)_kvState.ContextSize;
    public double ContextUsagePercent =>
        _kvState.ContextSize > 0 ? _kvState.ApproxTokens * 100.0 / _kvState.ContextSize : 0.0;
    public int TokensUntilSummarize
     {
        get
         {
            var threshold = (int)((_contextWindow?.MaxTokens ?? 0) * 0.5);
            return Math.Max(0, threshold - _kvState.ApproxTokens);
         }
     }
    public bool IsContextNearOverflow =>
        _kvState.ContextSize > 0 && _kvState.ApproxTokens > _kvState.ContextSize * CompactThreshold();

     // ── Constructor (HTTP transport) ───────────────────────────

    /// <summary>
     /// Primary constructor. Takes the HTTP transport (inference engine, KV cache
     /// controller, tokenizer) plus a session id. No in-process LLamaSharp model.
     /// </summary>
    public AgentEngine(
        string sessionId,
        IInferenceEngine inferenceEngine,
        IKvCacheController kvCacheController,
        RemoteTokenizer? tokenizer = null,
        InferenceRequestParams? inferenceParams = null,
        uint contextSize = 8192,
        string modelPath = "",
        AppConfig? config = null,
        string? workingDir = null,
        ILogger? logger = null,
        MemoryManager? memoryManager = null,
        ECAssistant.Core.Engine.SelfCorrectionManager? selfCorrection = null,
        ECAssistant.Core.Playbooks.IPlaybookStore? playbookStore = null,
        ECAssistant.Core.Engine.ProjectContextManager? projectContext = null,
        ITaskPlanner? taskPlanner = null,
        Transport.OpenAIClient? sharedHttpClient = null,
        bool isLocalMode = true)
     {
        if (logger != null) _logger = logger;
        _injectedSelfCorrection = selfCorrection;
        _injectedPlaybookStore = playbookStore;
        _injectedProjectContext = projectContext;
        _injectedTaskPlanner = taskPlanner;

        _inferenceEngine = inferenceEngine;
        _kvCacheController = kvCacheController;
        _tokenizer = tokenizer;
        _sessionId = sessionId;
        _sharedHttpClient = sharedHttpClient;
        _isLocalMode = isLocalMode;
        _requestParams = inferenceParams ?? CreateTieredParams(config ?? new AppConfig());
        _requestParams.SessionId = sessionId;

        _modelPath = modelPath;
        _contextSize = contextSize;
        _kvState.ContextSize = contextSize;
        _config = config ?? new AppConfig();
        // v13b: structured decisions on BOTH paths — local via grammar envelope,
        // remote via native OpenAI function calling. Neither needs tag instructions.
        _useStructuredDecoding = true;
        _backgroundTasks = _config.BackgroundTasks;
        _workingDir = string.IsNullOrEmpty(workingDir) ? AppContext.BaseDirectory : workingDir;

        // ── Read output mode from config ──
        _verbose = _config.Interface.Verbose;
        _silent = _config.Interface.Silent;
        MaxIterations = _config.Interface.MaxTurns > 0 ? _config.Interface.MaxTurns : 10;

        _processRunner = new ProcessRunner();
        _fileSystem = new FileSystemAdapter();
        _httpClient = new HttpClientAdapter();

        _tokenCounter = new TokenCounter();
        if (_tokenizer != null)
            _tokenCounter.Initialize(_tokenizer);

        _memoryManager = memoryManager ?? new MemoryManager();
        var summarySvc = _inferenceEngine != null
             ? new SummaryService(p => _inferenceEngine.GenerateAsync(p, BuildStatelessParams(), CancellationToken.None))
             : null;
        _contextWindow = new ContextWindow(contextSize, summarySvc, tokenCounter: null,
            // compact_threshold_percent now drives the summarize trigger too (was hardwired 0.50)
            CompactThreshold());
        _transcript = new ConversationTranscript();

        // Load per-session transcript for session resumption (v13 fix: was loading shared root transcript)
        var sessionDir = Path.Combine(_workingDir, ".sessions", _sessionId);
        var transcriptPath = Path.Combine(sessionDir, "transcript.json");
        if (!File.Exists(transcriptPath))
        {
            // Legacy fallback: migrate shared transcript to per-session (move, not copy)
            var legacyPath = Path.Combine(_workingDir, "transcript.json");
            if (File.Exists(legacyPath))
            {
                try
                {
                    Directory.CreateDirectory(sessionDir);
                    File.Move(legacyPath, transcriptPath);
                }
                catch { /* if move fails, fall back to reading legacy in place */ }
                transcriptPath = File.Exists(transcriptPath) ? transcriptPath : legacyPath;
            }
        }
        if (File.Exists(transcriptPath))
         {
            try
             {
                var loaded = ConversationTranscript.LoadFromDisk(transcriptPath);
                if (loaded != null && loaded.MessageCount > 0)
                 {
                    _transcript.Messages.AddRange(loaded.Messages);
                    foreach (var msg in loaded.Messages)
                     {
                        if (msg.Role == "user")
                            _contextWindow.AddUserMessage(msg.Content);
                     }
                    _out?.WriteInfo($"[Context] Loaded {loaded.MessageCount} messages from previous session.");
                    var userMsgs = loaded.Messages.Where(m => m.Role == "user").TakeLast(3);
                    if (userMsgs.Any())
                        _out?.WriteInfo($"[Last session] {string.Join(" | ", userMsgs.Select(m => StringUtil.Default.Truncate(m.Content, 60)))}");
                 }
             }
            catch (Exception ex)
             {
                _logger?.Error("Context", $"Failed to load transcript: {ex.Message}");
             }
         }

        _out?.WriteInfo($"[Engine] Session ready: {sessionId} | Model: {modelPath} | Context: {contextSize}");
        _logger?.Info("Engine", $"Session ready: {sessionId} | Model: {modelPath} | Context: {contextSize}");

        _memoryManager.Load();
        if (_memoryManager.Count > 0)
            _out?.WriteInfo($"[Memory] Active memories loaded: {_memoryManager.Count}");
        else
            _out?.WriteInfo("No prior memory entries found (first session).");

        // Load OS-specific system prompt at startup
        try
         {
            if (_systemPromptText != null)
             {
                _out?.WriteInfo($"[Config] System prompt provided programmatically ({_systemPromptText.Length} chars)");
             }
            else if (!string.IsNullOrEmpty(_systemPromptPathOverride))
             {
                if (File.Exists(_systemPromptPathOverride))
                 {
                    _systemPromptText = File.ReadAllText(_systemPromptPathOverride);
                    _out?.WriteInfo($"[Config] System prompt loaded from: {_systemPromptPathOverride} ({_systemPromptText.Length} chars)");
                 }
                else
                 {
                    _logger?.Warn("Engine", $"System prompt path not found: {_systemPromptPathOverride} — using empty system prompt.");
                    _systemPromptText = "";
                 }
             }
            else
             {
                var promptResourceName = OperatingSystem.IsMacOS() ? "SystemPrompt.Mac.md"
                     : OperatingSystem.IsWindows() ? "SystemPrompt.Windows.md"
                     : "SystemPrompt.Linux.md";

                var embedded = ResourceLoader.Default.LoadTextWithFallback(promptResourceName, "SystemPrompt.Linux.md");
                if (embedded != null)
                 {
                    _systemPromptText = embedded;
                    _out?.WriteInfo($"[Config] System prompt loaded from embedded resource: {promptResourceName} ({_systemPromptText.Length} chars)");
                 }
                else
                 {
                    var sysPromptPath = Path.Combine(_workingDir, promptResourceName);
                    if (!File.Exists(sysPromptPath)) sysPromptPath = Path.Combine(AppContext.BaseDirectory, promptResourceName);
                    if (!File.Exists(sysPromptPath))
                     {
                        var legacyPath = Path.Combine(_workingDir, "SystemPrompt.Linux.md");
                        if (!File.Exists(legacyPath)) legacyPath = Path.Combine(AppContext.BaseDirectory, "SystemPrompt.Linux.md");
                        if (File.Exists(legacyPath)) { sysPromptPath = legacyPath; promptResourceName = "SystemPrompt.Linux.md"; }
                     }
                    if (File.Exists(sysPromptPath))
                     {
                        _systemPromptText = File.ReadAllText(sysPromptPath);
                        _out?.WriteInfo($"[Config] System prompt loaded from: {promptResourceName} ({_systemPromptText.Length} chars)");
                     }
                    else
                     {
                         _logger?.Warn("Engine", "No system prompt found (embedded or file) — using empty system prompt.");
                         _systemPromptText = "";
                     }
                 }
             }
         }
        catch (Exception ex)
         {
            _logger?.Warn("Engine", $"Failed to load system prompt: {ex.Message}");
            _systemPromptText = "";
         }
     }

     // ── Wiring ────────────────────────────────────────────────

    /// <summary>Wire SummaryService so summarization uses the main model via HTTP.</summary>
    public void WireSummaryService()
     {
        if (_inferenceEngine == null)
            return;

        var summaryService = new SummaryService(
            p => _inferenceEngine.GenerateAsync(p, BuildStatelessParams(), CancellationToken.None),
            prompt =>
            {
                var warmParams = BuildWarmSessionParams();
                return warmParams != null
                    ? _inferenceEngine.GenerateAsync(prompt, warmParams, CancellationToken.None)
                    : _inferenceEngine.GenerateAsync(prompt, BuildStatelessParams(), CancellationToken.None);
            });
        _contextWindow.SetSummaryService(summaryService);
        _logger?.Info("Engine", "SummaryService wired with main model (HTTP transport).");
    }

    public void InitializeSelfCorrection(string workingDir)
     {
        _selfCorrection = _injectedSelfCorrection
            ?? new ECAssistant.Core.Engine.SelfCorrectionManager(workingDir, _logger);
    }

    /// <summary>v14.14: initialize persistent success-playbook memory (mirrors InitializeSelfCorrection).</summary>
    public void InitializePlaybooks(string workingDir)
    {
        _playbookStore = _injectedPlaybookStore
            ?? new ECAssistant.Core.Playbooks.PlaybookStore(workingDir, _logger);
    }

    /// <summary>v14.16: initialize proactive context pinning when enabled in config.</summary>
    public void InitializeContextPinning()
    {
        // Assigned only if not already resolved (test doubles may be set pre-init).
        if (_contextPinner != null) return;
        if (_config?.ContextPinning?.Enabled == true)
            _contextPinner = InjectedContextPinner ?? new ContextPinner(_config.ContextPinning);
    }


    public async Task InitializeVectorMemoryAsync(string storeDir, ECAssistant.Core.Interfaces.IVectorEmbedder? embedder = null)
     {
        if (_vectorMemory == null)
            _vectorMemory = new VectorMemoryStore(storeDir, _logger);

        if (embedder != null)
        {
            // Real embedder (HTTP/local) — hand it to the store as the generator
            await _vectorMemory.InitializeAsync(text => System.Threading.Tasks.Task.FromResult(embedder.Embed(text)));
        }

        else
        {
            // No embedder configured — TF-IDF keyword hashing keeps vector memory functional
            var tfidf = new ECAssistant.Core.Services.TfidfEmbedder();
            await _vectorMemory.InitializeAsync(text => System.Threading.Tasks.Task.FromResult(tfidf.Embed(text)));
        }
     }

    public async Task InitializeProjectContextAsync(string workingDir)
     {
        _projectContext = _injectedProjectContext
            ?? new ECAssistant.Core.Engine.ProjectContextManager(workingDir, _logger);
        await _projectContext.InitializeAsync();
    }

    public void InitializeTaskPlanner()
     {
        _taskPlanner = _injectedTaskPlanner
            ?? new TaskPlanner(_logger);
    }

    public void SetBackgroundTasks(BackgroundTasksConfig config)
     {
        _backgroundTasks = config;
     }

     // ── Stateless helpers (summary / plan / decompose / intent) ──

    /// <summary>Compaction trigger threshold — context_management.compact_threshold_percent (default 80%).</summary>
    private double CompactThreshold() =>
        _config?.ContextManagement?.CompactThresholdPercent is > 0 and <= 100
            ? _config.ContextManagement.CompactThresholdPercent / 100.0
            : 0.8;


    /// <summary>Build request params for a stateless (no session) call.</summary>
    private InferenceRequestParams BuildStatelessParams()
     {
        var p = new InferenceRequestParams();
        if (_requestParams != null)
         {
            p.Temperature = _requestParams.Temperature;
            p.TopP = _requestParams.TopP;
            p.TopK = _requestParams.TopK;
            p.RepeatPenalty = _requestParams.RepeatPenalty;
            p.Stop = _requestParams.Stop;
            p.ModelId = _requestParams.ModelId;
         }
        p.SessionId = null; // stateless — do not pollute a session's KV cache
        return p;
     }

    /// <summary>
    /// Build request params bound to the live main session (warm KV cache).
    /// Used by compaction/summarization so the already-prefilled conversation is reused
    /// (decode-only) instead of a cold stateless prefill.
    /// Returns null when no warm session is available — callers must fall back to stateless.
    /// </summary>
    private InferenceRequestParams? BuildWarmSessionParams()
    {
        if (!_kvState.SessionActive || _sessionId == null)
            return null;

        var p = BuildStatelessParams();
        p.SessionId = _sessionId; // warm path — reuse prefilled conversation in cache
        return p;
    }

     private InferenceRequestParams BuildStatelessParams(int maxTokens, string[]? stop, float temperature = 0.1f, float topP = 0.8f, int topK = 40, float repeatPenalty = 1.0f)
     {
        var p = BuildStatelessParams();
        p.MaxTokens = maxTokens;
        p.Stop = stop;
        p.Temperature = temperature;
        p.TopP = topP;
        p.TopK = topK;
        p.RepeatPenalty = repeatPenalty;
        return p;
     }

    /// <summary>Generate a short execution plan using the main LLM (stateless).</summary>
    public async Task<string?> GeneratePlanAsync(string userRequest)
     {
        if (_inferenceEngine == null)
            return null;

        var planPrompt = @"You are a task planner. Read the user request and output a concise execution plan.
Format: Numbered steps, one per line. Each step should be a single action (read, search, write, run, etc.).
Be specific about what each step does. Do NOT include commentary — just the plan.

User request: " + userRequest + @"

Execution plan:";

        try
         {
            var planParams = BuildStatelessParams(300, new[] { "User:", "Question:" });
            var result = await _inferenceEngine.GenerateAsync(planPrompt, planParams, CancellationToken.None);
            _out?.WriteInfo($"[StepMapper] Plan generated ({result.Length} chars)");
            return result.Trim();
         }
        catch (OperationCanceledException)
         {
            return null;
         }
     }

    /// <summary>Decompose a user request into sub-tasks (stateless). null = fall back to keywords.</summary>
    public async Task<List<string>?> DecomposeTaskAsync(string userRequest)
     {
        var decomposeConfig = _backgroundTasks?.Decompose;
        if (decomposeConfig == null || !decomposeConfig.UseLlm)
            return null;
        if (_inferenceEngine == null)
            return null;

        var prompt = @"You decompose tasks. Output ONLY numbered steps. Nothing else.

Count the distinct actions the user asked for. Output exactly that many steps. Stop. Do not add any more.

FORBIDDEN:
- Extra steps not requested (verify, check, clean up, create project, setup)
- Explanations, reasoning, or text outside numbered steps
- Steps the user did not explicitly ask for

Example:
User: read Program.cs then fix line 42 then rebuild
1. Read Program.cs
2. Fix the bug at line 42
3. Rebuild the project

Now decompose this task:
User: " + userRequest + "\n";

        try
         {
            var decomposeParams = BuildStatelessParams(
                decomposeConfig.MaxTokens,
                decomposeConfig.AntiPrompts,
                decomposeConfig.Temperature,
                decomposeConfig.TopP,
                decomposeConfig.TopK,
                decomposeConfig.RepeatPenalty);
            var raw = await _inferenceEngine.GenerateAsync(prompt, decomposeParams, CancellationToken.None);
            _logger?.Info("Decompose", $"Raw output:\n{raw}");

            // v14.10.2: envelope-trained models wrap even plain-text replies in
            // {"thinking","answer"} — unwrap before parsing numbered steps.
            raw = Engine.StructuredDecisionAdapter.TryExtractAnswer(raw) ?? raw;

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var steps = new List<string>();
            var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int expectedNumber = 1;
            foreach (var line in lines)
             {
                var trimmed = line.Trim();
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, $@"^{expectedNumber}\.\s*(.+)$");
                if (match.Success)
                 {
                    var step = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(step))
                        steps.Add(step);
                    expectedNumber++;
                 }
                else break;
             }

            if (steps.Count == 0)
             {
                _logger?.Warn("Decompose", "Decomposition produced no numbered steps — falling back to keywords.");
                return null;
             }

            _logger?.Info("Decompose", $"Decomposed into {steps.Count} steps: {string.Join(" | ", steps.Select(s => s.Substring(0, Math.Min(s.Length, 50))))}");
            return steps;
         }
        catch (OperationCanceledException)
         {
            return null;
         }
     }

    // ── System prompt building ─────────────────────────────────

    /// <summary>Build system+tools prompt — system prompt + runtime tool self-registration.</summary>
    private string BuildSystemToolsPrompt()
     {
        var sb = new StringBuilder();
        // v15 fix: programmatic override (e.g. handoff specialist prompts) was stored
        // but never consumed — prefer it over the file-based system prompt.
        var effectivePrompt = !string.IsNullOrEmpty(_systemPromptTextOverride)
            ? _systemPromptTextOverride
            : _systemPromptText;
        if (!string.IsNullOrEmpty(effectivePrompt))
            sb.AppendLine(effectivePrompt);

        if (_tools.Count > 0)
         {
            sb.AppendLine();
            sb.AppendLine("## REGISTERED TOOLS");
            sb.AppendLine();
            foreach (var tool in _tools)
             {
                sb.AppendLine(tool.ToSystemPromptBlock());
                sb.AppendLine();
             }
         }

        sb.Append(BuildHostEnvironmentSection());
        return sb.ToString();
     }

    /// <summary>
    /// v12.8: Self-awareness — the model learns the host application's runtime layout
    /// (config, models, sessions, memory, logs) so it can operate on the app itself
    /// (inspect configs, read transcripts, tune llm-server.json) with real paths.
    /// </summary>
    private string BuildHostEnvironmentSection()
     {
        var sb = new StringBuilder();
        sb.AppendLine("## HOST ENVIRONMENT");
        sb.AppendLine();
        sb.AppendLine("You are integrated into an application whose runtime files live at the paths below.");
        sb.AppendLine("Shell commands and file tools can access all of them directly:");
        sb.AppendLine();
        var llmRoot = PathExpander.Default.Expand("~/.ECAssistantLLM");
        sb.AppendLine($"- App root (user config directory): {_workingDir}");
        sb.AppendLine($"- appsettings.json (provider, embedding, vector memory settings): {Path.Combine(_workingDir, "appsettings.json")}");
        sb.AppendLine($"- llm-server.json (registered models, gpu_layers, context sizes): {Path.Combine(llmRoot, "llm-server.json")}");
        sb.AppendLine($"- Model files (GGUF + mmproj): {Path.Combine(llmRoot, "models")}");
        sb.AppendLine($"- Session transcripts: {Path.Combine(_workingDir, ".sessions")}");
        sb.AppendLine($"- Vector memory index: {Path.Combine(_workingDir, "vecmem")}");
        sb.AppendLine($"- Application log: {Path.Combine(_workingDir, "ECAssistant.log")}");
        sb.AppendLine();
        sb.AppendLine("When the user asks about the app itself — its configuration, models, memory or history — these are the paths to inspect. Do not modify config files unless the user explicitly asks for it.");

        // Rules file (AGENTS.md convention): project conventions live with the
        // project. Small models depend on this far more than large ones — they
        // cannot infer conventions from a few files.
        try
        {
            var rulesPath = Path.Combine(_workingDir, "AGENTS.md");
            if (File.Exists(rulesPath))
            {
                var rules = File.ReadAllText(rulesPath);
                const int MaxRulesChars = 6000;
                if (rules.Length > MaxRulesChars)
                    rules = rules.Substring(0, MaxRulesChars) + "\n[AGENTS.md truncated]";
                sb.AppendLine();
                sb.AppendLine("## PROJECT RULES (AGENTS.md)");
                sb.AppendLine();
                sb.AppendLine(rules);
            }
        }
        catch { /* non-critical — unreadable rules file must not break the prompt */ }

        var verifyCommand = _config?.Interface.VerifyCommand;
        if (!string.IsNullOrWhiteSpace(verifyCommand))
        {
            sb.AppendLine();
            sb.AppendLine($"VERIFIER: after making code changes, run `{verifyCommand}` to verify them and read its output. Fix failures and re-run until it passes — never claim success without running it.");
        }
        sb.AppendLine();
        return sb.ToString();
     }

     // ── KV cache lifecycle ─────────────────────────────────────

     /// <summary>Prefill the static prefix (system prompt + tools) into the server KV cache. Idempotent.</summary>
    public virtual async Task PrefillStaticPrefix()
     {
        if (_kvState.IsPrefilled) return;

        _cachedStaticPrefix = BuildSystemToolsPrompt();
        _out?.WriteInfo($"[KVCache] Prefilling static prefix ({_cachedStaticPrefix.Length} chars)...");
        var startMs = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

        try
         {
            if (!_kvState.SessionActive && _kvCacheController != null)
             {
                try
                 {
                    await _kvCacheController.CreateSessionAsync(_sessionId, _config.LlmProvider.ModelId);
                    _kvState.SessionActive = true;
                 }
                catch (Exception ex)
                 {
                    _logger?.Warn("KVCache", $"CreateSession failed: {ex.Message}");
                 }
             }

            if (_kvCacheController != null)
             {
                await _kvCacheController.PrefillAsync(_sessionId, _cachedStaticPrefix);
                await RefreshKvStatusAsync();
                _kvState.IsPrefilled = true;
             }
         }
        catch (Exception ex)
         {
            _logger?.Warn("Engine", $"Prefill failed: {ex.GetType().Name}: {ex.Message}");
             // best-effort — continue without KV cache optimization
         }

        var elapsedMs = (long)((DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) - startMs);
        _out?.WriteSuccess($"[KVCache] Static prefix prefilled in {elapsedMs}ms. KV cache active.");
    }

    private async Task RefreshKvStatusAsync()
     {
        if (_kvCacheController == null) return;
        try
         {
            var status = await _kvCacheController.GetStatusAsync(_sessionId);
            if (status != null)
             {
                _kvState.ContextSize = status.ContextSize > 0 ? status.ContextSize : _kvState.ContextSize;
                _kvState.ApproxTokens = status.ApproxTokens;
                _kvState.EstimatedMB = status.EstimatedVramMb;
                _kvState.IsPrefilled = status.IsPrefilled || _kvState.IsPrefilled;
             }
         }
        catch { /* status is best-effort */ }
     }

     /// <summary>Full KV cache reset + re-prefill. Used on context overflow / clean slate.</summary>
    public virtual async Task ResetAndRebuildCacheAsync()
     {
        if (_kvCacheController == null) return;

        _out?.WriteWarning("[KVCache] Full reset — rebuilding from scratch...");
        try
         {
            await _kvCacheController.ResetAsync(_sessionId);
            _kvState.IsPrefilled = false;
            _kvState.ApproxTokens = 0;
         }
        catch (Exception ex)
         {
            _logger?.Warn("KVCache", $"Reset failed: {ex.Message}");
         }

        await PrefillStaticPrefix();
        _out?.WriteSuccess("[KVCache] Cache rebuilt and prefilled.");
    }

     /// <summary>Rebuild the KV cache after a user stop. Same as a full reset + re-prefill.</summary>
    public virtual async Task RebuildCacheAfterStopAsync()
     {
        await ResetAndRebuildCacheAsync();
    }

     /// <summary>Format retry — remove the last assistant response and rewind the server KV cache.</summary>
    public virtual async Task RemoveLastAssistantResponseAsync()
     {
        _contextWindow.RemoveLastAssistantMessage();
        for (int i = _transcript.Messages.Count - 1; i >= 0; i--)
         {
            if (_transcript.Messages[i].Role == "assistant")
             {
                _transcript.Messages.RemoveAt(i);
                break;
             }
         }

         // Fast path: server-side rewind
        bool rewindOK = false;
        if (_kvState.ConsecutiveRewindFailures < MaxRewindFailures && _kvCacheController != null)
         {
            try
             {
                await _kvCacheController.RewindAsync(_sessionId);
                rewindOK = true;
                _kvState.ConsecutiveRewindFailures = 0;
                _out?.WriteInfo("[KVCache] Rewound to pre-generation state (format retry, fast path).");
             }
            catch (Exception ex)
             {
                _kvState.ConsecutiveRewindFailures++;
                _logger?.Warn("KVCache", $"Rewind failed (attempt {_kvState.ConsecutiveRewindFailures}/{MaxRewindFailures}): {ex.Message}");
             }
         }

         // Fallback: full KV cache rebuild
        if (!rewindOK)
         {
            _out?.WriteWarning("[KVCache] " + (_kvState.ConsecutiveRewindFailures >= MaxRewindFailures
                 ? $"Rewind failed {_kvState.ConsecutiveRewindFailures}x — forcing full rebuild."
                 : "No saved state — forcing full rebuild."));

            await ResetAndRebuildCacheAsync();

            var messages = _contextWindow.GetWindowMessages();
            if (messages.Count > 0)
             {
                _out?.WriteInfo($"[KVCache] Re-feeding {messages.Count} conversation messages into rebuilt cache...");
                var historySb = new StringBuilder();
                foreach (var msg in messages)
                 {
                    switch (msg.Role)
                     {
                        case "user":
                            historySb.AppendLine("<user>");
                            historySb.AppendLine(msg.Content);
                            historySb.AppendLine("</user>");
                            break;
                        case "assistant":
                            historySb.AppendLine("<assistant>");
                            historySb.AppendLine(msg.Content);
                            historySb.AppendLine("</assistant>");
                            break;
                        case "tool_output":
                            historySb.AppendLine($"<tooloutput>{msg.Source}<result>");
                            historySb.AppendLine(msg.Content);
                            historySb.AppendLine("</result></tooloutput>");
                            break;
                        case "system":
                            historySb.AppendLine($"<system>{msg.Content}</system>");
                            break;
                     }
                 }

                if (historySb.Length > 0 && _kvCacheController != null)
                 {
                    try
                     {
                        await _kvCacheController.PrefillAsync(_sessionId, historySb.ToString());
                     }
                    catch (Exception ex)
                     {
                        _out?.WriteWarning($"[KVCache] History re-feed failed: {ex.Message}");
                     }
                    _out?.WriteSuccess($"[KVCache] Re-fed {messages.Count} messages ({historySb.Length} chars) into cache.");
                 }
             }

            _kvState.ConsecutiveRewindFailures = 0;
            _out?.WriteSuccess("[KVCache] Cache rebuilt for format retry (fallback path).");
         }
    }

     /// <summary>Inject a format retry prompt as a user message.</summary>
    public virtual void InjectFormatRetry(string errorMessage)
     {
        _contextWindow.AddUserMessage(errorMessage);
        _transcript.AddUser(errorMessage);
    }

     /// <summary>Inject an execution plan as a system message.</summary>
    public void InjectExecutionPlan(string planText)
     {
        _contextWindow.AddSystemMessage(planText);
        _transcript.AddSystem(planText);
        _out?.WriteSuccess("[Plan] Execution plan injected into context.");
    }

     /// <summary>Clear context window and transcript.</summary>
    public virtual void ClearHistory()
     {
        _contextWindow.Clear();
        _transcript.Messages.Clear();
        _lifecycle.TurnCount = 0;
        _out?.WriteInfo("[Context] History and transcript cleared.");
    }

     /// <summary>Clear only the context window (not transcript) — used after ESC stop.</summary>
    public void ClearContextWindowOnly()
     {
        _contextWindow.Clear();
        _lifecycle.TurnCount = 0;
        _out?.WriteInfo("[Context] Context window cleared (transcript preserved).");
    }

          /// <summary>Reset KV cache dynamic context for a new user request (keeps static prefix).</summary>
    public virtual void ResetForNewRequest()
     {
        _lifecycle.TurnCount = 0;
        _lifecycle.EscPressed = false;
    }

     // ── Incremental input building ─────────────────────────────

     /// <summary>Build only the new tokens to feed since the last turn.</summary>
    private string BuildIncrementalInput(string userRequest)
     {
        var sb = new StringBuilder();

        if (_lifecycle.TurnCount == 1)
         {
            var memoryInject = GetMemoryInjection(userRequest);
            var projectCtx = GetProjectContextInjection(userRequest);
            var taskProgress = GetTaskProgressInjection();
            var failureCtx = GetFailureInjection();
            var playbookInject = GetPlaybookInjection(userRequest);

            var systemMessages = _contextWindow.GetWindowMessages().Where(m => m.Role == "system");
            foreach (var sysMsg in systemMessages)
             {
                if (!string.IsNullOrEmpty(sysMsg.Content))
                    sb.AppendLine(sysMsg.Content);
             }

            if (!string.IsNullOrEmpty(memoryInject))
             {
                sb.AppendLine("> PERSISTENT MEMORY — These are past decisions, patterns, and lessons that may help you:");
                sb.AppendLine(memoryInject);
                sb.AppendLine();
             }
            if (!string.IsNullOrEmpty(projectCtx))
             {
                sb.AppendLine(projectCtx);
                sb.AppendLine();
             }
            if (!string.IsNullOrEmpty(taskProgress))
                sb.AppendLine(taskProgress);
            if (!string.IsNullOrEmpty(failureCtx))
                sb.AppendLine(failureCtx);
            if (!string.IsNullOrEmpty(playbookInject))
            {
                sb.AppendLine(playbookInject);
                sb.AppendLine();
            }

            sb.AppendLine("<user>");
            sb.AppendLine(userRequest);
            sb.AppendLine("</user>");
            sb.AppendLine();
            sb.AppendLine("The user message above is the user's request. Decide yourself:");
            sb.AppendLine("- If it needs tools (files, shell, web, system), start working and call the first tool now.");
            sb.AppendLine("- If it can be answered directly (greetings, questions, conversation), finish now with your answer.");
         }
        else
         {
            var lastToolMsg = _contextWindow.GetWindowMessages().LastOrDefault(m => m.Role == "tool_output");
            if (lastToolMsg != null)
             {
                sb.AppendLine($"<tooloutput>{lastToolMsg.Source}<result>");
                sb.AppendLine(lastToolMsg.Content);
                sb.AppendLine("</result></tooloutput>");
                sb.AppendLine();
             }
            // v14.12: large models get a one-line nudge — the repeated nag lines
            // are scaffolding that hinders them.
            if (IsLargeModelTier())
             {
                sb.AppendLine("Continue the task. If the results above already answer the user's request, give your final answer now; otherwise call the next tool.");
             }
            else
             {
                sb.AppendLine("Continue the task. First check: if the tool results above already fully answer the user's request, you MUST finish NOW with your final answer.");
                sb.AppendLine("Do NOT repeat a tool call that already succeeded with the same arguments — repeating it adds nothing. Only call a tool again if you need DIFFERENT data.");
                sb.AppendLine("If you truly need more data, call the next tool. Otherwise finish now with your final answer.");
             }
         }

        return sb.ToString();
     }

    private string? GetMemoryInjection(string userRequest)
     {
        if (_memoryManager?.Count == 0) return null;

        var query = userRequest.Length > 200 ? userRequest.Substring(0, 200) : userRequest;
        var sb = new StringBuilder();

        if (_vectorMemory != null && _vectorMemory.IsInitialized && _vectorMemory.Count > 0)
         {
            try
             {
                var vecResults = _vectorMemory.SearchAsTextAsync(query, maxResults: 3).GetAwaiter().GetResult();
                if (!vecResults.StartsWith("(No semantic"))
                    sb.AppendLine(vecResults);
             }
            catch (Exception ex) { _logger?.Debug("Engine", $"Non-critical error ignored: {ex.Message}"); }
         }

        if (_memoryManager != null)
         {
            var results = _memoryManager.Query(query, maxResults: 5);
            if (!string.IsNullOrEmpty(results) && !results.StartsWith("(No memories"))
                sb.AppendLine(results);
         }

        return sb.Length > 0 ? sb.ToString().Trim() : null;
     }

    private string? GetProjectContextInjection(string userRequest) => _projectContext?.GetProjectSummary();

    private string? GetTaskProgressInjection() => null;
    private string? GetFailureInjection() => null;

    /// <summary>v14.14: tier-flavored playbook injection — matched success procedures for this request.</summary>
    private string? GetPlaybookInjection(string userRequest) =>
        _playbookStore?.BuildInjection(userRequest, IsLargeModelTier());

     // ── Tool registration + results ────────────────────────────

    public void RegisterTool(EToolBase tool)
     {
        lock (_toolsLock) _tools.Add(tool);
        _out?.WriteInfo($"[Tool] Registered: {tool.Name}");
    }

        /// <summary>
        /// v14.20: model-facing projection of a tool's raw output via its own
        /// RenderForModel override. Unknown names (system injections like "[ERROR] ...",
        /// "Batch") find no registered tool and pass through untouched. Shared by the
        /// single-call path (AddToolResult) and the batch path (CombineResults).
        /// </summary>
    public string RenderOutput(string toolName, string rawOutput)
         {
        EToolBase? tool = null;
        lock (_toolsLock) tool = _tools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        return tool?.RenderForModel(rawOutput) ?? rawOutput;
         }

        /// <summary>Add tool result to both transcript and context window.</summary>
    public virtual void AddToolResult(string toolName, string output)
       {
          // v14.20: render via the tool's own projection before truncation — tools with
           // structured semantics (builds, shell dotnet runs) send facts; unknown names pass through.
        var rendered = RenderOutput(toolName, output);
        var safeOutput = EscapeToolOutput(TruncateToolOutput(rendered, toolName));
        _transcript.AddToolOutput(safeOutput, toolName);
        _contextWindow.AddToolOutput(safeOutput, toolName);
        _contextPinner?.ObserveToolOutput(toolName, safeOutput);

        try
         {
var sessionDir = Path.Combine(_workingDir, ".sessions", _sessionId);
            Directory.CreateDirectory(sessionDir);
            var transcriptPath = Path.Combine(sessionDir, "transcript.json");
            _transcript.SaveToDisk(transcriptPath);
         }
        catch { /* don't crash on save failure */ }
    }

    private string EscapeToolOutput(string text)
     {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private const int MaxToolOutputDefault = 4000;
    private const int MaxToolOutputCode = 8000;
    private const int MaxStoredOutputsConst = 20;

    private readonly Dictionary<string, string> _toolOutputStore = new();
    private int _outputStoreCounter = 0;

    private ToolOutputLimitsConfig Limits => _config.ToolOutputLimits ?? new ToolOutputLimitsConfig();

     /// <summary>Truncate tool output based on tool type. Store full output for retrieval.</summary>
    private string TruncateToolOutput(string text, string toolName = "")
     {
        if (string.IsNullOrEmpty(text)) return text;

        // Config-driven limits (tool_output_limits section); per-tool overrides win,
        // then legacy per-kind defaults for tools with no config entry.
        var limits = Limits;
        int limit;
        var perTool = limits.MaxResultCharsPerTool;
        if (perTool != null)
        {
            var exact = perTool.FirstOrDefault(kv => kv.Key.Equals(toolName, StringComparison.OrdinalIgnoreCase));
            if (exact.Key != null) limit = exact.Value;
            else limit = toolName.ToLowerInvariant() switch
            {
                "ecodeeditor" => MaxToolOutputCode,
                "efileresearchtool" => MaxToolOutputCode,
                _ => limits.MaxResultChars > 0 ? limits.MaxResultChars : MaxToolOutputDefault
            };
        }
        else limit = toolName.ToLowerInvariant() switch
        {
            "ecodeeditor" => MaxToolOutputCode,
            "efileresearchtool" => MaxToolOutputCode,
            _ => limits.MaxResultChars > 0 ? limits.MaxResultChars : MaxToolOutputDefault
        };

        if (text.Length <= limit) return text;

        _outputStoreCounter++;
        var storeKey = $"output_{_outputStoreCounter}";
        _toolOutputStore[storeKey] = text;

        var maxStored = limits.MaxStoredOutputs > 0 ? limits.MaxStoredOutputs : MaxStoredOutputsConst;
        if (_toolOutputStore.Count > maxStored)
         {
            // Evict oldest by insertion order (keys are output_<n>; lexicographic sort
            // would evict output_10 before output_2). Prefer the in-memory counter.
            var oldestKey = _toolOutputStore.Keys
                .OrderBy(k => int.TryParse(k.AsSpan("output_".Length), out var n) ? n : int.MaxValue)
                .FirstOrDefault();
            if (oldestKey != null) _toolOutputStore.Remove(oldestKey);
         }

        try
         {
            var outputPath = Path.Combine(_workingDir, $"tool_outputs/{storeKey}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, text);
         }
        catch { /* non-critical */ }

        var truncated = text.Substring(0, limit);
        truncated += $"\n\n[OUTPUT STORED: {text.Length} total chars. Full output saved as {storeKey}.]";
        truncated += $"\nTo see more, use: EShellAgent command=Get-Content tool_outputs/{storeKey}.txt -TotalCount N | Select-Object -Skip M";
        truncated += $"\nOr read a specific part: Get-Content tool_outputs/{storeKey}.txt | Select-Object -Skip {limit / 80} -First 50";

        // v14.12.2: curated projection keeps key lines + head/tail instead of a
        // blind head-truncate. Config off-switch restores the old behavior. The
        // projector receives ONLY the store note — never the truncated head blob
        // (it keeps its own head/tail; a second copy would duplicate content and
        // defeat the token savings).
        var storeNote =
            $"\n\n[OUTPUT STORED: {text.Length} total chars. Full output saved as {storeKey}.]" +
            $"\nTo see more, use: EShellAgent command=Get-Content tool_outputs/{storeKey}.txt -TotalCount N | Select-Object -Skip M" +
            $"\nOr read a specific part: Get-Content tool_outputs/{storeKey}.txt | Select-Object -Skip {limit / 80} -First 50";
        var curate = _config.ContextManagement?.CurateToolOutputs ?? true;
        if (curate)
            return ToolOutputProjector.Project(text, storeNote);

        return truncated;
    }

     // ── Memory helpers ─────────────────────────────────────────

    public void SaveMemory(string k, string c, string cat = "general")
         => _memoryManager?.AddEntry(k, c, cat);

    public void LoadContext()
     {
        if (_memoryManager != null) _memoryManager.Load();
        _out?.WriteInfo("[Memory] Loaded.");
    }

    public void SaveTranscript(string? path = null)
     {
        var sessionDir = Path.Combine(_workingDir, ".sessions", _sessionId);
        Directory.CreateDirectory(sessionDir);
        var p = path ?? Path.Combine(sessionDir, "transcript.json");
        _transcript.SaveToDisk(p);
        _out?.WriteInfo($"[Context] Transcript saved ({_transcript.MessageCount} messages, {_contextWindow.GetTotalTokens()} tokens).");
    }

     // ── Generation (main loop) ─────────────────────────────────

     /// <summary>
     /// Generate a decision from the LLM using incremental KV cache feed.
     /// v10.30: streaming via IInferenceEngine.StreamAsync — no in-process executor.
    /// v14: returns a parsed LLMDecision directly — no intermediate tag text.
     /// </summary>
    public virtual async Task<LLMDecision> GenerateAsync(string userPrompt)
     {
        _lifecycle.IncrementTurn();
        _lifecycle.EscPressed = false;

        // Vision: extract [image:<path>] attachments into data URIs, strip tokens from the prompt.
        var (cleanPrompt, imageRefs) = ImageAttachmentParser.Extract(userPrompt, _workingDir);
        var imageDataUris = imageRefs.Select(r => r.DataUri).ToList();
        if (imageRefs.Count > 0)
            _out?.WriteInfo($"[Vision] Attached {imageRefs.Count} image(s) to this turn");

        if (_lifecycle.TurnCount == 1)
         {
            _transcript.AddUser(userPrompt);
         }

        var effectivePrompt = cleanPrompt.Length > 0 ? cleanPrompt : userPrompt;

        // v14.16: pin the original user request + any explicit decisions so compaction
        // can never lose them (first request stays the durable goal in the pinner).
        _contextPinner?.SetGoal(effectivePrompt);
        _contextPinner?.ObserveUserMessage(effectivePrompt);

        _logger?.Debug("Context", $"Turn {_lifecycle.TurnCount} | Budget: {_contextWindow.GetTotalTokens()}/{_contextWindow.MaxTokens} tokens");

         // KV cache overflow handling — rebuild with summarized conversation
        var tokenBudget = _contextWindow.GetTotalTokens();
        var maxBudget = (int)_contextWindow.MaxTokens;
        if (maxBudget > 0 && tokenBudget > maxBudget * CompactThreshold())
         {
            _out?.WriteWarning($"[KVCache] Context at {tokenBudget}/{maxBudget} tokens ({tokenBudget * 100 / maxBudget}%). Rebuilding cache...");

            var allMessages = _contextWindow.GetWindowMessages();
            var convSb = new StringBuilder();
            var convCharLimit = (int)(_backgroundTasks?.Summarize.ContextSize ?? 4096) * 3 / 4;
            for (int i = allMessages.Count - 1; i >= 0 && convSb.Length < convCharLimit; i--)
                convSb.Insert(0, $"[{allMessages[i].Role}] {allMessages[i].Content}\n");
            var convText = convSb.ToString();

            var summaryText = "";
            // Staged compaction stage 1: trim stale tool outputs first (zero LLM cost).
            // Only pay for the LLM summarize + rebuild when trimming isn't enough.
            // Audit fix: previously the summarize call ran BEFORE the trim check, so a
            // successful trim still burned an LLM call and injected a summary the
            // warm-path incremental input never delivers (window/server desync).
            var keepRecent = Math.Max(1, _config?.ContextManagement?.KeepRecentToolOutputs ?? 3);
            bool rebuilt = false;
            if (!_contextWindow.TrimStaleToolOutputs(keepRecent))
             {
                rebuilt = true;
                if (_inferenceEngine != null && (_backgroundTasks?.Summarize.UseLlm ?? true))
                {
                    var summarizeConfig = _backgroundTasks?.Summarize;
                var warmParams = BuildWarmSessionParams();
                try
                 {
                    // v10.31: Warm-session compaction — the conversation being summarized is
                    // already in the main session's KV cache, so infer inside it (decode-only)
                    // instead of a cold stateless call. Fall back to stateless if no warm session.
                    InferenceRequestParams ResolveParams()
                     {
                        if (warmParams != null)
                         {
                            warmParams.MaxTokens = Math.Max(100, summarizeConfig?.MaxTokens ?? 200);
                            warmParams.Stop = summarizeConfig?.AntiPrompts ?? new[] { "User:", "Question:" };
                            warmParams.Temperature = summarizeConfig?.Temperature ?? 0.1f;
                            return warmParams;
                         }
                        return BuildStatelessParams(
                            Math.Max(100, summarizeConfig?.MaxTokens ?? 200),
                            summarizeConfig?.AntiPrompts ?? new[] { "User:", "Question:" },
                            summarizeConfig?.Temperature ?? 0.1f,
                            summarizeConfig?.TopP ?? 0.8f,
                            summarizeConfig?.TopK ?? 40,
                            summarizeConfig?.RepeatPenalty ?? 1.1f);
                    }
                    var result = await _inferenceEngine.GenerateAsync(
                        $"Summarize this conversation concisely. Keep facts, decisions, and tool results only. Max 3 sentences. Plain text.\n\n{convText}",
                        ResolveParams(), CancellationToken.None);
                    summaryText = System.Text.RegularExpressions.Regex.Replace(result.Trim(), @"<[^>]+>", "");
                 }
                catch (OperationCanceledException) { }
                }

                _contextWindow.Clear();
                await ResetAndRebuildCacheAsync();
             }

            if (!string.IsNullOrWhiteSpace(summaryText))
             {
                _contextWindow.AddSystemMessage($"[Previous conversation summary: {summaryText.Trim()}]");
                _out?.WriteInfo($"[KVCache] Re-injected summary: {summaryText.Length} chars");
             }

            // v14.16: re-inject the tier-aware pinned block after the summarize rebuild
            // so critical state (goal, decisions, file map) survives compaction.
            if (_contextPinner != null)
            {
                var pinned = _contextPinner.BuildPinnedBlock(
                    IsLargeModelTier(), _config?.ContextPinning?.MaxChars ?? 1200);
                if (pinned != null)
                {
                    _contextWindow.AddSystemMessage(pinned);
                    _out?.WriteInfo($"[Context] Re-injected pinned block: {pinned.Length} chars");
                }
            }

            if (_lifecycle.TurnCount > 1)
             {
                var lastToolMsg = allMessages.LastOrDefault(m => m.Role == "tool_output");
                var lastUserMsg = allMessages.LastOrDefault(m => m.Role == "user");
                if (lastToolMsg != null)
                     _contextWindow.AddToolOutput(lastToolMsg.Content, lastToolMsg.Source ?? "");
                if (lastUserMsg != null)
                     _contextWindow.AddUserMessage(lastUserMsg.Content);
             }

            if (_lifecycle.TurnCount == 1)
             {
                // Note: the user prompt is already in the transcript (added at the top of
                // GenerateAsync when TurnCount == 1). Only re-add it to the cleared context.
                _contextWindow.AddUserMessage(effectivePrompt, imageDataUris);
             }

            if (rebuilt && _kvCacheController != null)
             {
                // Audit fix: the rebuild path clears the window and resets the server
                // cache to the static prefix only. The summary/pinned/recent messages
                // re-added above must be re-fed into the rebuilt cache, otherwise the
                // model loses all conversation state after compaction (the incremental
                // input for turn N>1 only carries the last tool output).
                try
                 {
                    var historySb = new StringBuilder();
                    foreach (var msg in _contextWindow.GetWindowMessages())
                     {
                        switch (msg.Role)
                         {
                            case "user":
                                historySb.AppendLine("<user>").AppendLine(msg.Content).AppendLine("</user>");
                                break;
                            case "assistant":
                                historySb.AppendLine("<assistant>").AppendLine(msg.Content).AppendLine("</assistant>");
                                break;
                            case "tool_output":
                                historySb.AppendLine($"<tooloutput>{msg.Source}<result>").AppendLine(msg.Content).AppendLine("</result></tooloutput>");
                                break;
                            case "system":
                                historySb.AppendLine($"<system>{msg.Content}</system>");
                                break;
                         }
                     }
                    if (historySb.Length > 0)
                     {
                        await _kvCacheController.PrefillAsync(_sessionId, historySb.ToString());
                        _out?.WriteInfo($"[KVCache] Re-fed rebuilt window ({historySb.Length} chars) into cache.");
                     }
                 }
                catch (Exception ex)
                 {
                    _out?.WriteWarning($"[KVCache] Rebuilt-window re-feed failed: {ex.Message}");
                 }
             }
         }

        var incrementalInput = BuildIncrementalInput(effectivePrompt);

        try
         {
            _logger?.Debug("Engine", $"Incremental input: {incrementalInput.Length} chars, Turn: {_lifecycle.TurnCount}");

            var promptDumpPath = Path.Combine(_workingDir, "last_prompt.txt");
            try
             {
                File.WriteAllText(promptDumpPath,
                     $"=== INCREMENTAL INPUT (Turn {_lifecycle.TurnCount}) ===\n{incrementalInput}\n\n=== STATIC PREFIX (cached) ===\n{_cachedStaticPrefix ?? "(not prefilled)"}");
             }
            catch (Exception ex) { _logger?.Debug("Engine", $"Non-critical error ignored: {ex.Message}"); }

            if (_logger?.IsDebugEnabled == true)
             {
                _out?.WriteInfo($"[IncrementalInput] Turn {_lifecycle.TurnCount} — {incrementalInput.Length} chars");
                _out?.WriteDim(new string('=', 60));
                _out?.WriteDim(incrementalInput);
                _out?.WriteDim(new string('=', 60));
             }

            var sb = new StringBuilder();
            LLMDecision? structuredDecision = null;

             // Save KV cache state before generation for format retry rewind.
            try
             {
                if (_kvCacheController != null)
                    await _kvCacheController.SaveStateAsync(_sessionId);
             }
            catch { /* if save fails, rewind won't work but generation continues */ }

            // v13: structured (non-streamed) generation writes the full envelope —
            // give it more headroom than streamed tokens need.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_useStructuredDecoding ? 240 : 90));
            bool timedOut = false;
            try
             {
                var showTokenStream = _verbose && !_silent;
                if (showTokenStream)
                    _out?.WriteLine($"── Token Stream (Turn {_lifecycle.TurnCount}) ── [ESC to stop] ──", OutputState.Bold);
                _out?.StartStream(OutputState.Raw);
                var tokenCount = 0;

                 // Stream via the HTTP inference engine (server owns the KV cache).
                InferenceRequestParams requestParams;
                if (_requestParams != null)
                 {
                    // Attach images for this call only — params object is shared across turns.
                    _requestParams.ImageDataUris = imageDataUris;
                    requestParams = _requestParams;
                 }
                else
                    requestParams = _config != null
                        ? CreateTieredParams(_config)
                        : new InferenceRequestParams(); // no config — factory defaults

                bool retriedAfterRecovery = false;
                bool retryingStream = false;
                retryStream:
                try
                 {
                    // v13 structured decoding: local servers get grammar-constrained
                    // decision envelopes — convert to internal decision text instead of
                    // free-form streaming. Falls back to text streaming when unsupported.
                    // v13b: remote requests carry tool specs for native function calling.
                    // v14.10.2: remote also gets the static prefix (system prompt) and
                    // conversation history — remote providers have NO server-side state,
                    // so without this the model sees only the incremental turn fragment.
                    if (_useStructuredDecoding && !(_config?.LlmProvider?.IsLocal ?? false))
                    {
                        requestParams.Tools = BuildToolSpecs();
                        requestParams.SystemPrompt = BuildRemoteSystemPrompt();
                        requestParams.HistoryMessages = BuildRemoteHistoryMessages();
                    }

                    var structured = _useStructuredDecoding && _inferenceEngine != null
                        ? await TryGenerateStructuredAsync(incrementalInput, requestParams, cts.Token)
                        : null;

                    if (structured != null)
                     {
                        structuredDecision = structured;
                        if (showTokenStream)
                            _out?.Write(structured.AnswerText ?? "");
                        tokenCount++;
                        goto inferenceDone;
                     }

                    await foreach (var token in _inferenceEngine!.StreamAsync(
                        incrementalInput,
                        requestParams,
                        cts.Token))
                 {
                    if (ExecutionToken.IsCancellationRequested)
                     {
                         _lifecycle.EscPressed = true;
                         _out?.StopStream();
                         _out?.BlankLine();
                         _out?.WriteError("[Stop] Generation stopped by user (ESC).");
                        goto inferenceDone;
                     }
                    if (showTokenStream)
                        _out?.Write(token);
                    sb.Append(token);
                    tokenCount++;
                 }
                inferenceDone:
                 _out?.StopStream();
                if (showTokenStream)
                {
                    _out?.WriteLine($"── End Token Stream ({tokenCount} tokens) ──", OutputState.Bold);
                    _out?.BlankLine();
                }
                 }
                catch (Exception connEx) when (tokenCount == 0 &&
                                               (IsConnectionFailure(connEx) || IsStaleSessionFailure(connEx)) &&
                                               !retriedAfterRecovery)
                 {
                    // Connection-level failure (e.g. local server went down after
                    // shutdown-on-last-client) or a 404 from a KV session the
                    // restarted server no longer knows. Recover the connection
                    // (which also recreates sessions) and retry once — never
                    // surface these as model output.
                    _out?.StopStream();
                    if (!await TryRecoverConnectionAsync())
                        throw;
                    retriedAfterRecovery = true;
                    // The finally below skips clearing when retrying, so image
                    // attachments survive into the retry attempt.
                    retryingStream = true;
                    goto retryStream;
                 }
                finally
                 {
                    // Clear per-call image attachments — params object is shared across turns.
                    // Skipped while a recovery retry is pending (images must survive); the
                    // post-block clear below handles that path.
                    if (_requestParams != null && !retryingStream)
                    {
                        _requestParams.ImageDataUris = new List<string>();
                        _requestParams.Tools = null;
                    }
                 }
                if (retryingStream && _requestParams != null)
                {
                    _requestParams.ImageDataUris = new List<string>();
                    _requestParams.Tools = null;
                }
             }
            catch (OperationCanceledException)
             {
                timedOut = true;
                 _out?.StopStream();
                 _out?.BlankLine();
                 _out?.WriteError("[Timeout] Inference timed out (90s). Truncating.");
             }

            string answerText;

            if (structuredDecision != null)
             {
                // v14: envelope parsed directly into a decision — no tag text.
                if (structuredDecision.WantsToolCall)
                 {
                    _logger?.Info("Engine", $"Structured decision: {structuredDecision.ToolCallCount} tool call(s)");
                    // v14.5: Store reasoning in transcript so the model can learn from it
                    // on later turns (matches old tag system behavior).
                    if (!string.IsNullOrEmpty(structuredDecision.Reasoning))
                        _transcript.AddAssistant($"[reasoning] {structuredDecision.Reasoning.Trim()}");
                    await RefreshKvStatusAsync();
                    return structuredDecision;
                 }
                answerText = structuredDecision.AnswerText ?? "";
             }
            else
             {
                var rawResult = sb.ToString().Trim();

                if (rawResult.StartsWith("<assistant>", StringComparison.OrdinalIgnoreCase))
                    rawResult = rawResult.Substring("<assistant>".Length).Trim();
                if (rawResult.EndsWith("</assistant>", StringComparison.OrdinalIgnoreCase))
                    rawResult = rawResult.Substring(0, rawResult.Length - "</assistant>".Length).Trim();
                 if (_verbose && !_silent)
                    _out?.WriteDim($"[Engine] Raw ({rawResult.Length} chars): {StringUtil.Default.Truncate(rawResult, 500)}");

                // v14 text fallback: best-effort decision parse — legacy <toolcall>
                // blocks when present, otherwise the raw text IS the direct answer.
                var fallbackDecision = ParseTextFallbackDecision(rawResult);
                if (fallbackDecision.WantsToolCall)
                 {
                    await RefreshKvStatusAsync();
                    return fallbackDecision;
                 }
                answerText = fallbackDecision.AnswerText ?? "";
                _out?.WriteDim($"[Engine] Answer ({answerText.Length} chars): {StringUtil.Default.Truncate(answerText, 500)}");
             }

            // v14.11 (Option D): an empty turn is a model failure (small models do this
            // most often right after a tool round-trip) — never mask it as a direct
            // answer. Store the placeholder to keep transcript/context symmetric with
            // the KV rewind the orchestrator's format-retry performs, but return a
            // null-answer decision so the orchestrator removes the empty turn and
            // nudges the model to answer in plain text. Only a timeout keeps its
            // sentinel as a direct answer — a retry cannot fix truncation.
            if (string.IsNullOrEmpty(answerText) && !timedOut)
                answerText = "(Empty response from model)";

            _transcript.AddAssistant(FormatHistoryEntry(structuredDecision?.Reasoning, answerText));
            _contextWindow.AddAssistantMessage(FormatHistoryEntry(structuredDecision?.Reasoning, answerText));

             // Refresh local KV status snapshot from the server
            await RefreshKvStatusAsync();

             _out?.BlankLine();
             _logger?.Info("Engine", $"Response: {answerText.Length} chars");

            if (ExecutionToken.IsCancellationRequested || _lifecycle.EscPressed)
             {
                 _out?.WriteWarning("[Engine] Execution stopped — not storing partial response.");
                if (_kvCacheController != null)
                 {
                    try
                     {
                        await _kvCacheController.RewindAsync(_sessionId);
                         _out?.WriteInfo("[KVCache] Rewound to pre-generation state (stopped).");
                     }
                    catch (Exception ex)
                     {
                         _logger?.Warn("KVCache", $"Failed to rewind after stop: {ex.Message}");
                     }
                 }
                return new LLMDecision(false, null, new Dictionary<string, string?>(), "(Stopped by user)");
             }

            // v14.11: empty response (not timeout, not stopped) → null-answer decision.
            // WantsDirectAnswer stays false, so the orchestrator's format-retry path
            // removes the empty turn (KV rewind is symmetric — the placeholder above
            // was stored) and nudges the model to answer in plain text.
            if (answerText == "(Empty response from model)")
             {
                _out?.WriteWarning("[Engine] Empty model response — handing back for format retry.");
                _logger?.Warn("Engine", "Empty model response — orchestrator format retry");
                return new LLMDecision(false, null, new Dictionary<string, string?>(), null,
                    structuredDecision?.Reasoning);
             }

            return new LLMDecision(false, null, new Dictionary<string, string?>(), answerText);
         }
        catch (Exception ex)
         {
             _out?.WriteError("[Error] " + ex.Message);
            return new LLMDecision(false, null, new Dictionary<string, string?>(), "[Error] " + ex.Message);
         }
    }

    /// <summary>
    /// v14.5: Format the history entry for transcript + context window.
    /// Prefixes reasoning (if present) as [reasoning] ... on its own line,
    /// then the answer text on the next line. Matches how the old tag system
    /// kept thinking visible in history for the model to learn from.
    /// </summary>
    private static string FormatHistoryEntry(string? reasoning, string answerText)
    {
        if (string.IsNullOrEmpty(reasoning))
            return answerText;
        return $"[reasoning] {reasoning.Trim()}\n{answerText}";
    }

     /// <summary>
    /// v14: Best-effort parse of free-form streamed text into a decision (text fallback
    /// path only — the structured path parses the envelope JSON directly). Recognizes
    /// legacy &lt;toolcall&gt; blocks when present; otherwise the raw text IS the answer.
     /// </summary>
    private LLMDecision ParseTextFallbackDecision(string raw)
     {
        if (string.IsNullOrWhiteSpace(raw))
            return new LLMDecision(false, null, new Dictionary<string, string?>(), raw);

        // v14.10.1: the system prompt mandates the {"thinking","answer"} envelope —
        // when the structured path fell back to text streaming, the model still
        // emits it and it leaked to the UI as raw JSON. Parse the envelope here so
        // fallback answers render clean (answer only) and reasoning is preserved.
        var envelope = TryParseJsonEnvelope(raw);
        if (envelope != null)
            return envelope;

        var toolCalls = new List<ToolCallRequest>();
        var searchFrom = 0;
        while (searchFrom < raw.Length)
         {
            var tcStart = raw.IndexOf("<toolcall>", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (tcStart < 0) break;
            var tcEnd = raw.IndexOf("</toolcall>", tcStart + 10, StringComparison.OrdinalIgnoreCase);
            var blockContent = tcEnd < 0
                ? raw.Substring(tcStart + 10).Trim()
                : raw.Substring(tcStart + 10, tcEnd - tcStart - 10).Trim();
            searchFrom = tcEnd < 0 ? raw.Length : tcEnd + 11;

            var tc = ParseToolCallBlockContent(blockContent, toolCalls.Count + 1);
            if (!string.IsNullOrEmpty(tc.ToolName))
                toolCalls.Add(tc);
         }

        if (toolCalls.Count > 0)
            return new LLMDecision(toolCalls);

        return new LLMDecision(false, null, new Dictionary<string, string?>(), raw);
     }

    /// <summary>
    /// v14.10.1: parse a JSON decision envelope ({"thinking","answer"} or
    /// {"thinking","toolcalls":[{name,args}]}) out of fallback text-stream output.
    /// Tolerates markdown code fences. Returns null when the text is not an
    /// envelope (plain prose) — caller keeps legacy behavior.
    /// </summary>
    private static LLMDecision? TryParseJsonEnvelope(string raw)
     {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
         {
            var firstNl = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNl >= 0 && lastFence > firstNl)
                trimmed = trimmed.Substring(firstNl + 1, lastFence - firstNl - 1).Trim();
         }
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            return null;

        try
         {
            using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;

            string? thinking = null;
            if (root.TryGetProperty("thinking", out var tElem) && tElem.ValueKind == System.Text.Json.JsonValueKind.String)
                thinking = tElem.GetString();

            if (root.TryGetProperty("answer", out var aElem) && aElem.ValueKind == System.Text.Json.JsonValueKind.String)
                return new LLMDecision(false, null, new Dictionary<string, string?>(), aElem.GetString(), thinking);

            if (root.TryGetProperty("toolcalls", out var tcElem) && tcElem.ValueKind == System.Text.Json.JsonValueKind.Array)
             {
                var requests = new List<ToolCallRequest>();
                foreach (var item in tcElem.EnumerateArray())
                 {
                    if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    var name = item.TryGetProperty("name", out var nElem) && nElem.ValueKind == System.Text.Json.JsonValueKind.String
                        ? nElem.GetString() : null;
                    if (string.IsNullOrEmpty(name)) continue;
                    var args = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    if (item.TryGetProperty("args", out var argsElem) && argsElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                        foreach (var p in argsElem.EnumerateObject())
                            args[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
                    requests.Add(new ToolCallRequest { ToolName = name!, Args = args, Index = requests.Count + 1 });
                 }
                if (requests.Count > 0)
                    return new LLMDecision(requests, thinking);
             }
         }
        catch (System.Text.Json.JsonException)
         {
            // Not a valid envelope — plain text or malformed; caller falls through.
         }
        return null;
     }

    /// <summary>Parses legacy &lt;toolcall&gt; block content (text fallback only): tool name + &lt;arg&gt;value&lt;/arg&gt; pairs.</summary>
    private static ToolCallRequest ParseToolCallBlockContent(string toolcallContent, int index)
     {
        var ltIdx = toolcallContent.IndexOf('<');
        var spIdx = toolcallContent.IndexOf(' ');
        if (spIdx >= 0 && (ltIdx < 0 || spIdx < ltIdx))
            ltIdx = spIdx;
        var nameLen = ltIdx >= 0 ? ltIdx : toolcallContent.Length;
        var toolName = toolcallContent.Substring(0, nameLen).Trim();

        var args = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var argMatches = System.Text.RegularExpressions.Regex.Matches(
            toolcallContent, @"<([a-zA-Z_][\w]*)>(.*?)</\1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (System.Text.RegularExpressions.Match m in argMatches)
         {
            var key = m.Groups[1].Value;
            if (!string.IsNullOrEmpty(key)) args[key] = m.Groups[2].Value;
         }

        return new ToolCallRequest { ToolName = toolName, Args = args, Index = index };
     }

     /// <summary>Extract clean LLM response by stripping hallucination noise.</summary>

    /// <summary>
    /// True when the exception is a connection-level transport failure (server
    /// unreachable / connection refused) rather than an HTTP error response or a
    /// mid-generation abort. Request-level HTTP failures carry a StatusCode and
    /// are not retried.
    /// </summary>
    private static bool IsConnectionFailure(Exception ex)
     {
        for (var e = (Exception?)ex; e != null; e = e.InnerException)
         {
            if (e is HttpRequestException hre && hre.StatusCode == null)
                return true;
            if (e is System.Net.Sockets.SocketException)
                return true;
            if (e.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Connection reset", StringComparison.OrdinalIgnoreCase))
                return true;
         }
        return false;
     }

    /// <summary>
    /// True when the failure is an HTTP 404 from the local server — the KV
    /// session no longer exists there (server restarted under us). Recoverable:
    /// recovery recreates sessions; a genuine config error is not a 404 on a
    /// previously-working session route.
    /// </summary>
    private static bool IsStaleSessionFailure(Exception ex)
     {
        for (var e = (Exception?)ex; e != null; e = e.InnerException)
         {
            if (e is HttpRequestException hre && hre.StatusCode == System.Net.HttpStatusCode.NotFound)
                return true;
         }
        return false;
     }

    /// <summary>
    /// Attempt to restore the local LLM server connection via the recovery hook
    /// wired by SessionManager. Returns true when the server answers again and the
    /// failed request should be retried once.
    /// </summary>
    private async Task<bool> TryRecoverConnectionAsync()
     {
        if (!_config.LlmProvider.IsLocal || ConnectionRecovery == null)
            return false;
        _out?.WriteInfo("[Engine] LLM server unreachable — attempting recovery...");
        try
         {
            return await ConnectionRecovery();
         }
        catch (Exception ex)
         {
            _logger?.Warn("Engine", $"Connection recovery failed: {ex.Message}");
            return false;
         }
     }


    /// <summary>v13b: tool specs for remote native function calling (open string-arg schema).</summary>
    private List<ToolSpec> BuildToolSpecs() =>
        _tools.Select(t => new ToolSpec
         {
            Name = t.Name,
            Description = string.IsNullOrEmpty(t.UsageExample)
                ? t.Description
                : t.Description + "\n\nExample: " + t.UsageExample,
            // v14.12: remote native function-calling sends REAL parameter schemas.
            ParameterSchema = t.GetParameterSchema(),
         }).ToList();

    /// <summary>
    /// v14.10.2: system prompt for remote native function-calling mode.
    /// The static prefix's "respond as JSON envelope" section is meant for the local
    /// grammar path; sent verbatim to a native-tools provider it makes the model echo
    /// envelope JSON as plain text. Append an authoritative override instead.
    /// </summary>
    private string BuildRemoteSystemPrompt()
     {
        var prefix = _cachedStaticPrefix ?? BuildSystemToolsPrompt();
        return prefix +
            "\n## RESPONSE FORMAT (native tools mode)\n" +
            "You have native function-calling tools (provided in this request).\n" +
            "- To use a tool, emit a NATIVE tool call. Do NOT write JSON, envelopes, or code blocks in your reply text.\n" +
            "- When you have the final response for the user, write it as PLAIN TEXT (no JSON, no envelope).\n" +
            "- You may write a brief plain-text progress note alongside a tool call (e.g. what you're checking and why); it is shown to the user. Keep it to one or two sentences.\n" +
            "\n## WORK COMPOSITION\n" +
            "- Prefer composing work into fewer, bigger tool calls over many small round-trips: chain shell steps with && or ; when they are safe together, batch independent reads in one decision.\n" +
            "- After results arrive, synthesize the final answer from what you already have — never re-call a tool that already returned the data you need.\n" +
            (IsLargeModelTier() ? DataflowChainsGuidance : "");
     }

     /// <summary>v14.20: dataflow-chain guidance — large tier only (small models stay single-call).</summary>
    private const string DataflowChainsGuidance =
          "\n## DATAFLOW CHAINS\n" +
          "- In one decision you may issue several tool calls where a LATER call's argument reuses an EARLIER call's output with the token {{0}} ({{1}} = second call, 0-based). The calls then run in order, each receiving the prior output automatically — no need to wait between them.\n" +
          "- Example: call 0 reads a file, call 1 patches that same file using {{0}} data. Use chains for linear steps; fall back to separate turns only when a later call's ARGUMENTS depend on reasoning about the results, not just the results themselves.\n";

    /// <summary>
    /// v14.10.2: conversation history for remote mode, mapped to OpenAI chat roles.
    /// System messages are skipped (the static prefix carries them). The LAST tool
    /// output is excluded — BuildIncrementalInput already includes it on turn 2+.
    /// </summary>
    private List<(string Role, string Content)>? BuildRemoteHistoryMessages()
     {
        var messages = _contextWindow.GetWindowMessages();
        if (messages.Count == 0)
            return null;

        var lastToolOutputIndex = -1;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == "tool_output") { lastToolOutputIndex = i; break; }
        }

        var result = new List<(string, string)>();
        for (var i = 0; i < messages.Count; i++)
         {
            var m = messages[i];
            if (string.IsNullOrWhiteSpace(m.Content)) continue;
            switch (m.Role)
             {
                case "user":
                    result.Add(("user", m.Content));
                    break;
                case "assistant":
                    result.Add(("assistant", m.Content));
                    break;
                case "tool_output":
                    if (i == lastToolOutputIndex) continue; // duplicated in incremental input
                    result.Add(("user", $"[Tool result from {m.Source}]:\n{m.Content}"));
                    break;
                // "system" messages live in the static prefix — skip here.
             }
         }
        return result.Count > 0 ? result : null;
     }

    /// <summary>v14: Parses the grammar-forced envelope JSON directly into an LLMDecision; null when unsupported/unavailable.</summary>
    private async Task<LLMDecision?> TryGenerateStructuredAsync(string prompt, InferenceRequestParams parameters, CancellationToken ct)
     {
        try
         {
            // The envelope is a single JSON document — a truncated generation is an
            // invalid decision, so think+answer/toolcalls must fit. Floor AND cap:
            // config MaxTokens is a context budget (8192) that would take ~15 min
            // of CPU decode; the envelope needs far less.
            // v14.12: tier-aware envelope budget — small models get the tight cap
            // (they ramble), large models get reasoning headroom.
            parameters.MaxTokens = ApplyEnvelopeBudget(parameters.MaxTokens ?? 0, IsLargeModelTier());
            // v14.12.1: constrain the decision grammar's toolcall.name to the registered
            // tools — small models physically cannot hallucinate a tool name. The remote
            // native-tools path never reads this field (server without support ignores it).
            parameters.ToolNames = _tools.Select(t => t.Name).ToList();
            var envelope = await _inferenceEngine!.GenerateStructuredAsync(prompt, parameters, ct);
            if (envelope == null)
             {
                // v14.10.1: null is transient (provider hiccup, empty choices, one
                // malformed reply) — fall back for THIS TURN only and retry the
                // structured path next turn. Permanent fallback is reserved for the
                // legacy-endpoint status codes (404/405/501) below. The old code
                // flipped _useStructuredDecoding off forever on the first transient
                // null, silently downgrading the whole session to raw streaming
                // (live regression on local qwen3.5-4b).
                _logger?.Warn("Engine", "Structured decoding returned null — text fallback for this turn.");
                return null;
             }
            return StructuredDecisionAdapter.ParseDecision(envelope);
         }
        catch (HttpRequestException hre) when (hre.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.NotImplemented)
         {
            // Endpoint doesn't know the structured request shape — legacy server.
            _logger?.Warn("Engine", "Structured decoding not supported by server — falling back to text streaming permanently.");
            _useStructuredDecoding = false;
            return null;
         }
        catch (Exception ex) when (ex is not OperationCanceledException)
         {
            // Transport hiccup — text fallback for this turn; next turn tries again.
            _logger?.Warn("Engine", $"Structured decoding failed ({ex.Message}) — text fallback for this turn.");
            return null;
         }
     }

     // ── Execution lifecycle ────────────────────────────────────

    public void StartExecution() => _lifecycle.Start();

    public void StopExecution() => _lifecycle.Stop();

    public void EndExecution() => _lifecycle.End();

     // ── IEngine implementation (lifecycle adapter) ─────────────

    public Task StartAsync(string userMessage)
     {
        StartExecution();
        return Task.CompletedTask;
    }

    public async Task RunAsync(CancellationToken ct = default)
     {
        await PrefillStaticPrefix();
        ct.ThrowIfCancellationRequested();
    }

    public void Dispose()
     {
        // Safe sync path: the awaited operations in DisposeAsync are HTTP calls on a
        // dedicated HttpClient without a captured SynchronizationContext, so blocking
        // here cannot deadlock. Prefer DisposeAsync when already async.
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
     {
        try { if (_kvState.SessionActive && _kvCacheController != null) await _kvCacheController.DestroySessionAsync(_sessionId); }
        catch (Exception ex) { _logger?.Warn("Dispose", $"KV session destroy failed: {ex.Message}"); }
        foreach (var t in _tools)
            if (t is IDisposable d) d.Dispose();
         _out?.WriteInfo("[Exit] Engine disposed.");
    }

     // ── Factory helpers for injectable components ──────────────

    protected virtual SubAgentManager CreateSubAgentManager()
         => new(this, _workingDir, _logger, _out, _config, _processRunner, _fileSystem, _httpClient);
    protected virtual ITaskPlanner CreateTaskPlanner()
         => new TaskPlanner(_logger);

    protected virtual IStepMapper CreateStepMapper()
         => new StepMapper(this, _logger);
}

// ── Execution state (v9.0) ────────────────────────────────

/// <summary>Execution state for the main agent loop.</summary>
public class ExecutionState
{
    public int Iteration { get; set; }
    public int TotalIterations { get; set; }
    public bool Completed { get; set; }
    public string LastResponse { get; set; } = "";
    public string LastToolOutput { get; set; } = "";
    public List<string> History { get; set; } = new();

}
