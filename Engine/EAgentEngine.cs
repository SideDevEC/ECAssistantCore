using System.Text;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Memory;
using ECAssistant.Core.Engine;
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
public class EAgentEngine : IEngine, IEngineToolContext, ISubAgentEngineHost
{
    protected readonly string _modelPath;
    protected readonly uint _contextSize;
    protected readonly EAgentConfig _config;
    protected readonly string _workingDir;
    protected readonly ILogger _logger = new Logger();
    protected readonly ISessionOutput? _out;
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

     // ── Core components ──
    protected TokenCounter _tokenCounter;
    protected EMemoryManager _memoryManager;
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
    private ECAssistant.Core.Engine.ProjectContextManager? _projectContext;
    private ITaskPlanner? _taskPlanner;
    private IStepMapper? _sharedStepMapper;

     // ── Public API ─────────────────────────────────────────────

    public ISessionOutput? SessionOutput { get; private set; }

    /// <summary>Attach or replace the session output renderer.</summary>
    public void SetSessionOutput(ISessionOutput? output) => SessionOutput = output;

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

    /// <summary>
    /// Update HTTP client and client ID after server reconnection.
    /// Replaces the inference engine and KV cache controller with new instances
    /// bound to the new client ID.
    /// </summary>
    public void UpdateHttpClient(OpenAIClient newClient, string newClientId)
    {
        _clientId = newClientId;
        // Recreate KV cache controller with new client
        if (_kvCacheController is RemoteKvCacheController)
        {
            _kvCacheController = new RemoteKvCacheController(newClient);
        }
        // Recreate inference engine with new client
        var modelId = _requestParams?.ModelId ?? "main";
        _inferenceEngine = new HttpStreamingEngine(newClient, modelId, _sessionId);
        // Reset KV session state so PrefillStaticPrefix recreates it
        _kvState.SessionActive = false;
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
    public EMemoryManager Memory => _memoryManager;
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
        _kvState.ContextSize > 0 && _kvState.ApproxTokens > _kvState.ContextSize * 0.8;

     // ── Constructor (HTTP transport) ───────────────────────────

    /// <summary>
     /// Primary constructor. Takes the HTTP transport (inference engine, KV cache
     /// controller, tokenizer) plus a session id. No in-process LLamaSharp model.
     /// </summary>
    public EAgentEngine(
        string sessionId,
        IInferenceEngine inferenceEngine,
        IKvCacheController kvCacheController,
        RemoteTokenizer? tokenizer = null,
        InferenceRequestParams? inferenceParams = null,
        uint contextSize = 8192,
        string modelPath = "",
        EAgentConfig? config = null,
        string? workingDir = null,
        ILogger? logger = null,
        EMemoryManager? memoryManager = null,
        ECAssistant.Core.Engine.SelfCorrectionManager? selfCorrection = null,
        ECAssistant.Core.Engine.ProjectContextManager? projectContext = null,
        ITaskPlanner? taskPlanner = null)
     {
        if (logger != null) _logger = logger;
        _injectedSelfCorrection = selfCorrection;
        _injectedProjectContext = projectContext;
        _injectedTaskPlanner = taskPlanner;

        _inferenceEngine = inferenceEngine;
        _kvCacheController = kvCacheController;
        _tokenizer = tokenizer;
        _sessionId = sessionId;
        _requestParams = inferenceParams ?? InferenceParamsFactory.Default.Create(config ?? new EAgentConfig());
        _requestParams.SessionId = sessionId;

        _modelPath = modelPath;
        _contextSize = contextSize;
        _kvState.ContextSize = contextSize;
        _config = config ?? new EAgentConfig();
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

        _memoryManager = memoryManager ?? new EMemoryManager();
        var summarySvc = _inferenceEngine != null
             ? new SummaryService(p => _inferenceEngine.GenerateAsync(p, BuildStatelessParams(), CancellationToken.None))
             : null;
        _contextWindow = new ContextWindow(contextSize, summarySvc);
        _transcript = new ConversationTranscript();

        // Load any existing transcript from disk for session resumption
        var transcriptPath = Path.Combine(_workingDir, "transcript.json");
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
            var planParams = BuildStatelessParams(300, new[] { "</lm>", "User:", "Question:" });
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

        var prompt = @"You decompose tasks. Wrap your answer in <lm></lm> tags. Inside the tags, output ONLY numbered steps. Nothing else.

Count the distinct actions the user asked for. Output exactly that many steps. Stop. Do not add any more.

FORBIDDEN:
- Extra steps not requested (verify, check, clean up, create project, setup)
- Explanations, reasoning, or text outside numbered steps
- Steps the user did not explicitly ask for

User: read Program.cs then fix line 42 then rebuild
<lm>
1. Read Program.cs
2. Fix the bug at line 42
3. Rebuild the project
</lm>

User: what day is today
<lm>
1. Get the current date
</lm>

User: " + userRequest + "\n<lm>\n";

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
        if (!string.IsNullOrEmpty(_systemPromptText))
            sb.AppendLine(_systemPromptText);

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
        sb.AppendLine($"- App root (user config directory): {_workingDir}");
        sb.AppendLine($"- appsettings.json (provider, embedding, vector memory settings): {Path.Combine(_workingDir, "appsettings.json")}");
        sb.AppendLine($"- llm-server.json (registered models, gpu_layers, context sizes): {Path.Combine(_workingDir, "llm", "llm-server.json")}");
        sb.AppendLine($"- Model files (GGUF + mmproj): {Path.Combine(_workingDir, "llm", "models")}");
        sb.AppendLine($"- Session transcripts: {Path.Combine(_workingDir, ".sessions")}");
        sb.AppendLine($"- Vector memory index: {Path.Combine(_workingDir, "vecmem")}");
        sb.AppendLine($"- Application log: {Path.Combine(_workingDir, "ECAssistant.log")}");
        sb.AppendLine();
        sb.AppendLine("When the user asks about the app itself — its configuration, models, memory or history — these are the paths to inspect. Do not modify config files unless the user explicitly asks for it.");
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
                    await _kvCacheController.CreateSessionAsync(_sessionId);
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
                            historySb.AppendLine("<assistant><lm>");
                            historySb.AppendLine(msg.Content);
                            historySb.AppendLine("</lm></assistant>");
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

     /// <summary>Reset the turn counter for a new user request.</summary>
    public virtual void ResetTurnCount()
     {
        _lifecycle.TurnCount = 0;
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

            sb.AppendLine("<user>");
            sb.AppendLine(userRequest);
            sb.AppendLine("</user>");
            sb.AppendLine();
            sb.AppendLine("The user message above is the user's request. Decide yourself:");
            sb.AppendLine("- If it needs tools (files, shell, web, system), start working and output your first <toolcall> now.");
            sb.AppendLine("- If it can be answered directly (greetings, questions, conversation), respond with <output>your answer</output>.");
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
            sb.AppendLine("Continue the task. First check: if the tool results above already fully answer the user's request, you MUST finish NOW with <output>the final answer</output>.");
            sb.AppendLine("Do NOT repeat a tool call that already succeeded with the same arguments — repeating it adds nothing. Only call a tool again if you need DIFFERENT data.");
            sb.AppendLine("If you truly need more data, output the next <toolcall>. Otherwise output <output>the final answer</output>.");
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

     // ── Tool registration + results ────────────────────────────

    public void RegisterTool(EToolBase tool)
     {
        lock (_toolsLock) _tools.Add(tool);
        _out?.WriteInfo($"[Tool] Registered: {tool.Name}");
    }

     /// <summary>Add tool result to both transcript and context window.</summary>
    public virtual void AddToolResult(string toolName, string output)
     {
        var safeOutput = EscapeToolOutput(TruncateToolOutput(output, toolName));
        _transcript.AddToolOutput(safeOutput, toolName);
        _contextWindow.AddToolOutput(safeOutput, toolName);

        try
         {
            var transcriptPath = Path.Combine(_workingDir, "transcript.json");
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
    private const int MaxToolOutputSearch = 6000;

    private readonly Dictionary<string, string> _toolOutputStore = new();
    private int _outputStoreCounter = 0;
    private const int MaxStoredOutputs = 20;

     /// <summary>Truncate tool output based on tool type. Store full output for retrieval.</summary>
    private string TruncateToolOutput(string text, string toolName = "")
     {
        if (string.IsNullOrEmpty(text)) return text;

        var limit = toolName.ToLowerInvariant() switch
         {
             "ecodeeditor" => MaxToolOutputCode,
             "efileresearchtool" => MaxToolOutputCode,
             "ewebsearch" => MaxToolOutputSearch,
             _ => MaxToolOutputDefault
         };

        if (text.Length <= limit) return text;

        _outputStoreCounter++;
        var storeKey = $"output_{_outputStoreCounter}";
        _toolOutputStore[storeKey] = text;

        if (_toolOutputStore.Count > MaxStoredOutputs)
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
        return truncated;
    }

    public string? GetStoredOutput(string key, int offset = 0, int maxChars = 4000)
     {
        if (!_toolOutputStore.TryGetValue(key, out var full)) return null;
        if (offset >= full.Length) return "(Offset beyond output length)";
        var available = full.Length - offset;
        var take = Math.Min(maxChars, available);
        var result = full.Substring(offset, take);
        if (take < available)
            result += $"\n[Showing {take}/{available} chars from offset {offset}. Use higher offset to see more.]";
        return result;
    }

     // ── Memory helpers ─────────────────────────────────────────

    public string QueryMemory(string s, int maxResults = 5)
         => (_memoryManager == null) ? "(Not initialized)" : _memoryManager.Query(s, maxResults: maxResults);

    public void SaveMemory(string k, string c, string cat = "general")
         => _memoryManager?.AddEntry(k, c, cat);

    public void LoadContext()
     {
        if (_memoryManager != null) _memoryManager.Load();
        _out?.WriteInfo("[Memory] Loaded.");
    }

    public void SaveContext()
         => _memoryManager?.Save();

    public void SaveTranscript(string? path = null)
     {
        var p = path ?? Path.Combine(_workingDir, "transcript.json");
        _transcript.SaveToDisk(p);
        _out?.WriteInfo($"[Context] Transcript saved ({_transcript.MessageCount} messages, {_contextWindow.GetTotalTokens()} tokens).");
    }

     // ── Generation (main loop) ─────────────────────────────────

     /// <summary>
     /// Generate text from the LLM using incremental KV cache feed.
     /// v10.30: streaming via IInferenceEngine.StreamAsync — no in-process executor.
     /// </summary>
    public virtual async Task<string> GenerateAsync(string userPrompt)
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

        _logger?.Debug("Context", $"Turn {_lifecycle.TurnCount} | Budget: {_contextWindow.GetTotalTokens()}/{_contextWindow.MaxTokens} tokens");

         // KV cache overflow handling — rebuild with summarized conversation
        var tokenBudget = _contextWindow.GetTotalTokens();
        var maxBudget = (int)_contextWindow.MaxTokens;
        if (maxBudget > 0 && tokenBudget > maxBudget * 0.8)
         {
            _out?.WriteWarning($"[KVCache] Context at {tokenBudget}/{maxBudget} tokens ({tokenBudget * 100 / maxBudget}%). Rebuilding cache...");

            var allMessages = _contextWindow.GetWindowMessages();
            var convSb = new StringBuilder();
            var convCharLimit = (int)(_backgroundTasks?.Summarize.ContextSize ?? 4096) * 3 / 4;
            for (int i = allMessages.Count - 1; i >= 0 && convSb.Length < convCharLimit; i--)
                convSb.Insert(0, $"[{allMessages[i].Role}] {allMessages[i].Content}\n");
            var convText = convSb.ToString();

            var summaryText = "";
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
                            warmParams.Stop = summarizeConfig?.AntiPrompts ?? new[] { "User:", "Question:", "</lm>" };
                            warmParams.Temperature = summarizeConfig?.Temperature ?? 0.1f;
                            return warmParams;
                         }
                        return BuildStatelessParams(
                            Math.Max(100, summarizeConfig?.MaxTokens ?? 200),
                            summarizeConfig?.AntiPrompts ?? new[] { "User:", "Question:", "</lm>" },
                            summarizeConfig?.Temperature ?? 0.1f,
                            summarizeConfig?.TopP ?? 0.8f,
                            summarizeConfig?.TopK ?? 40,
                            summarizeConfig?.RepeatPenalty ?? 1.1f);
                    }
                    var result = await _inferenceEngine.GenerateAsync(
                        $"You are a summarization assistant. Wrap your summary in <lm></lm> tags.\nSummarize this conversation concisely. Keep facts, decisions, and tool results only. Max 3 sentences. Plain text inside the tags.\n\n{convText}\n\n<lm>",
                        ResolveParams(), CancellationToken.None);
                    summaryText = System.Text.RegularExpressions.Regex.Replace(result.Trim(), @"<[^>]+>", "");
                 }
                catch (OperationCanceledException) { }
             }

            _contextWindow.Clear();
            await ResetAndRebuildCacheAsync();

            if (!string.IsNullOrWhiteSpace(summaryText))
             {
                _contextWindow.AddSystemMessage($"[Previous conversation summary: {summaryText.Trim()}]");
                _out?.WriteInfo($"[KVCache] Re-injected summary: {summaryText.Length} chars");
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

             // Save KV cache state before generation for format retry rewind.
            try
             {
                if (_kvCacheController != null)
                    await _kvCacheController.SaveStateAsync(_sessionId);
             }
            catch { /* if save fails, rewind won't work but generation continues */ }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            bool timedOut = false;
            try
             {
                var stopTags = new[] { "</lm>" };
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
                    requestParams = InferenceParamsFactory.Default.Create(_config);

                bool retriedAfterRecovery = false;
                bool retryingStream = false;
                retryStream:
                try
                 {
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
                    var soFar = sb.ToString();
                    foreach (var stopTag in stopTags)
                     {
                        if (soFar.Contains(stopTag, StringComparison.OrdinalIgnoreCase))
                         {
                            if (showTokenStream)
                            {
                                _out?.BlankLine();
                                _out?.WriteInfo($"[Stop] Manual anti-prompt hit: {stopTag} (after {tokenCount} tokens)");
                            }
                            goto inferenceDone;
                         }
                     }
                 }
                inferenceDone:
                 _out?.StopStream();
                if (showTokenStream)
                {
                    _out?.WriteLine($"── End Token Stream ({tokenCount} tokens) ──", OutputState.Bold);
                    _out?.BlankLine();
                }
                 }
                catch (Exception connEx) when (tokenCount == 0 && IsConnectionFailure(connEx) && !retriedAfterRecovery)
                 {
                    // Connection-level failure (e.g. local server went down after
                    // shutdown-on-last-client). Recover the connection and retry once —
                    // never surface a connection error as model output.
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
                        _requestParams.ImageDataUris = new List<string>();
                 }
                if (retryingStream && _requestParams != null)
                    _requestParams.ImageDataUris = new List<string>();
             }
            catch (OperationCanceledException)
             {
                timedOut = true;
                 _out?.StopStream();
                 _out?.BlankLine();
                 _out?.WriteError("[Timeout] Inference timed out (90s). Truncating.");
             }

            var rawResult = sb.ToString().Trim();
            string cleanResponse;

            if (rawResult.StartsWith("<assistant>", StringComparison.OrdinalIgnoreCase))
                rawResult = rawResult.Substring("<assistant>".Length).Trim();
            if (rawResult.EndsWith("</assistant>", StringComparison.OrdinalIgnoreCase))
                rawResult = rawResult.Substring(0, rawResult.Length - "</assistant>".Length).Trim();
             if (_verbose && !_silent)
                _out?.WriteDim($"[Engine] Raw ({rawResult.Length} chars): {StringUtil.Default.Truncate(rawResult, 500)}");

            cleanResponse = ExtractCleanResponse(rawResult);
             _out?.WriteDim($"[Engine] Clean ({cleanResponse.Length} chars): {StringUtil.Default.Truncate(cleanResponse, 500)}");

            if (string.IsNullOrEmpty(cleanResponse))
                cleanResponse = timedOut ? "(Response truncated — model timed out)" : "(Empty response from model)";

            if (!string.IsNullOrEmpty(cleanResponse) && cleanResponse.Contains("<"))
             {
                 _transcript.AddAssistant(cleanResponse);
                 _contextWindow.AddAssistantMessage(cleanResponse);
             }

             // Refresh local KV status snapshot from the server
            await RefreshKvStatusAsync();

             _out?.BlankLine();
             _logger?.Info("Engine", $"Response: {cleanResponse.Length} chars");

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
                return "(Stopped by user)";
             }

            return cleanResponse;
         }
        catch (Exception ex)
         {
             _out?.WriteError("[Error] " + ex.Message);
            return "[Error] " + ex.Message;
         }
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


    /// <summary>Removes &lt;think&gt;…&lt;/think&gt; reasoning blocks (streaming models like Qwen3.5). Unclosed blocks removed entirely.</summary>
    // Stateless utility — no mutable state.
    internal static string StripThinkBlocks(string text)
     {
        if (string.IsNullOrEmpty(text)) return text;
        var result = System.Text.RegularExpressions.Regex.Replace(
            text, "<think>[\\s\\S]*?(?:</think>|$)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return result;
     }

    private string ExtractCleanResponse(string raw)
     {
        if (string.IsNullOrEmpty(raw)) return "";

        // Reasoning models: strip <think>…</think> blocks before tag parsing.
        raw = StripThinkBlocks(raw);

        var llmStart = raw.IndexOf("<lm>", StringComparison.OrdinalIgnoreCase);
        var llmEnd = raw.IndexOf("</lm>", StringComparison.OrdinalIgnoreCase);

        string content;
        if (llmStart >= 0 && llmEnd >= 0 && llmEnd > llmStart)
         {
            content = raw.Substring(llmStart + 4, llmEnd - llmStart - 4).Trim();
             _logger?.Debug("Extract", $"Extracted from <lm> container: {content.Length} chars (noise stripped: {raw.Length - content.Length - 9} chars)");
         }
        else if (llmStart >= 0 && llmEnd < 0)
         {
            content = raw.Substring(llmStart + 4).Trim();
             _logger?.Debug("Extract", $"<lm> opened but not closed — taking rest: {content.Length} chars");
         }
        else
         {
            var lmRegex = new System.Text.RegularExpressions.Regex(@"<l?m[^>]*>?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var lmMatch = lmRegex.Match(raw);
            if (lmMatch.Success)
             {
                content = raw.Substring(lmMatch.Index + lmMatch.Length).Trim();
                var closeRegex = new System.Text.RegularExpressions.Regex(@"</?l?m[^>]*>?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                content = closeRegex.Replace(content, "").Trim();
                 _logger?.Debug("Extract", $"Fallback regex found malformed <lm> tag at {lmMatch.Index}: extracted {content.Length} chars");
             }
            else
             {
                content = raw.Trim();
                 _logger?.Debug("Extract", $"No <lm> container found — using raw: {content.Length} chars");
             }
         }

         _logger?.Debug("Extract", $"Content length: {content.Length}");

        var thinkStart = content.IndexOf("<thinking>", StringComparison.OrdinalIgnoreCase);
        var thinkEnd = thinkStart >= 0
             ? content.IndexOf("</thinking>", thinkStart + 10, StringComparison.OrdinalIgnoreCase)
             : -1;

        var toolcallBlocks = new List<(int start, int end)>();
        var searchFrom = thinkEnd >= 0 ? thinkEnd + 11 : 0;
        while (searchFrom < content.Length)
         {
            var tcStart = content.IndexOf("<toolcall>", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (tcStart < 0) break;
            var tcEnd = content.IndexOf("</toolcall>", tcStart + 10, StringComparison.OrdinalIgnoreCase);
            if (tcEnd < 0)
             {
                toolcallBlocks.Add((tcStart, content.Length));
                break;
             }
            toolcallBlocks.Add((tcStart, tcEnd + 11));
            searchFrom = tcEnd + 11;
         }

        var outputSearchFrom = thinkEnd >= 0 ? thinkEnd + 11 : 0;
        var outputStart = content.IndexOf("<output>", outputSearchFrom, StringComparison.OrdinalIgnoreCase);
        int? outputEnd = null;
        if (outputStart >= 0)
         {
            var oc = content.IndexOf("</output>", outputStart + 8, StringComparison.OrdinalIgnoreCase);
            outputEnd = oc >= 0 ? oc + 9 : content.Length;

            var firstToolcallStart = toolcallBlocks.Count > 0 ? toolcallBlocks[0].start : int.MaxValue;
            bool hasOutputFirst = outputStart >= 0 && outputStart < firstToolcallStart;

            var outSb = new StringBuilder();

            if (thinkStart >= 0 && thinkEnd >= 0)
             {
                var thinkContent = content.Substring(thinkStart, thinkEnd + 11 - thinkStart).Trim();
                outSb.AppendLine(thinkContent);
             }

            if (hasOutputFirst)
             {
                var outputLen = outputEnd!.Value - outputStart;
                outSb.Append(content.Substring(outputStart, outputLen).Trim());
             }
            else if (toolcallBlocks.Count > 0)
             {
                foreach (var (tcS, tcE) in toolcallBlocks)
                 {
                    var blockContent = content.Substring(tcS, tcE - tcS).Trim();
                    outSb.AppendLine(blockContent);
                 }
                 _logger?.Debug("Extract", $"Extracted {toolcallBlocks.Count} <toolcall> blocks");
             }
            else if (outputStart >= 0)
             {
                var outputLen = outputEnd!.Value - outputStart;
                outSb.Append(content.Substring(outputStart, outputLen).Trim());
             }
            else
             {
                return content.Trim();
             }

            var result = outSb.ToString().Trim();
             _logger?.Debug("Extract", $"Output: {result.Length} chars, starts with: {StringUtil.Default.Truncate(result, 80)}");
            return result;
         }

        var sb = new StringBuilder();
        if (thinkStart >= 0 && thinkEnd >= 0)
         {
            var thinkContent = content.Substring(thinkStart, thinkEnd + 11 - thinkStart).Trim();
            sb.AppendLine(thinkContent);
         }

        if (toolcallBlocks.Count > 0)
         {
            foreach (var (tcS, tcE) in toolcallBlocks)
             {
                var blockContent = content.Substring(tcS, tcE - tcS).Trim();
                sb.AppendLine(blockContent);
             }
             _logger?.Debug("Extract", $"Extracted {toolcallBlocks.Count} <toolcall> blocks");
         }
        else if (thinkStart < 0)
         {
            return content.Trim();
         }

        var finalResult = sb.ToString().Trim();
         _logger?.Debug("Extract", $"Output: {finalResult.Length} chars, starts with: {StringUtil.Default.Truncate(finalResult, 80)}");
        return finalResult;
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

