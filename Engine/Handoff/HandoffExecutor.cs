using System.Text;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Services;
using ECAssistant.Core.Services.Http;
using ECAssistant.Core.Session;  // ISessionOutput
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Executes a handoff: creates an isolated specialist engine with the request's
/// system prompt and tool subset, runs it to completion, and returns the result.
///
/// The specialist is ephemeral — its engine, KV cache, and session are disposed
/// when <see cref="RunAsync"/> returns. Nothing is persisted.
///
/// Architecture mirrors <see cref="SubAgentManager.RunSingleAsync"/> but simpler:
/// no retry loop, no resource-limit tracking, no file snapshots. The specialist
/// either produces an answer or it doesn't — that answer becomes the parent
/// orchestrator's final output.
/// </summary>
public sealed class HandoffExecutor : IAsyncDisposable
{
    private readonly ISubAgentEngineHost _mainEngine;
    private readonly AppConfig _config;
    private readonly InferenceRequestParams _inferenceParams;
    private readonly ILogger _logger;
    private readonly ISessionOutput? _out;
    private readonly string _workingDir;
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly Services.BackgroundProcessManager _bgManager;

    private AgentEngine? _specialistEngine;

    /// <summary>Create a handoff executor bound to the main engine's host surface.</summary>
    public HandoffExecutor(
        ISubAgentEngineHost mainEngine,
        AppConfig config,
        InferenceRequestParams inferenceParams,
        string workingDir,
        ILogger? logger,
        ISessionOutput? sessionOutput = null,
        IProcessRunner? processRunner = null,
        IFileSystem? fileSystem = null,
        Services.BackgroundProcessManager? bgManager = null)
    {
        _mainEngine = mainEngine;
        _config = config;
        _inferenceParams = inferenceParams;
        _workingDir = workingDir;
        _logger = logger;
        _out = sessionOutput;
        _processRunner = processRunner ?? new ProcessRunner();
        _fileSystem = fileSystem ?? new FileSystemAdapter();
        _bgManager = bgManager ?? new Services.BackgroundProcessManager();
    }

    /// <summary>
    /// Run the specialist to completion. The specialist's
    /// <see cref="OrchestratorResult"/> is returned directly — the parent
    /// orchestrator should adopt it as its own final result.
    /// </summary>
    public async Task<OrchestratorResult> RunAsync(HandoffRequest request, CancellationToken parentToken)
    {
        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
            return new OrchestratorResult
            {
                FinalOutput = "[Handoff] No system prompt provided — cannot create specialist.",
                Status = OrchestratorStatus.Failed
            };

        var endpoint = _mainEngine.InferenceEngine?.Endpoint
            ?? throw new InvalidOperationException("Main engine has no inference engine — cannot create handoff specialist.");

        var modelId = request.ModelOverride ?? _config.LlmProvider.ModelId;
        var sessionId = $"handoff-{Guid.NewGuid():N}";

        _out?.WriteInfo($"[Handoff] Starting specialist '{request.Name}' (model: {modelId})...");
        if (!string.IsNullOrEmpty(request.Reason))
            _out?.WriteDim($"[Handoff] Reason: {request.Reason}");

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // ── Create specialist engine ──
            // v15 fix: reuse the host's shared client (Bearer key in remote mode;
            // registered client-id locally) and honor local/remote mode for KV sessions.
            var isLocal = _mainEngine.IsLocalMode;
            var client = _mainEngine.SharedHttpClient
                ?? new OpenAIClient(endpoint);
            var inference = new HttpStreamingEngine(client, modelId, isLocal ? sessionId : null);
            var kvCache = isLocal
                ? (IKvCacheController)new RemoteKvCacheController(client)
                : new NopKvCacheController();

            _specialistEngine = new AgentEngine(
                sessionId: sessionId,
                inferenceEngine: inference,
                kvCacheController: kvCache,
                inferenceParams: _inferenceParams,
                config: _config,
                workingDir: _workingDir,
                logger: _logger,
                sharedHttpClient: client,
                isLocalMode: isLocal);

            // ── Inject the specialist's system prompt ──
            _specialistEngine.SystemPromptText = request.SystemPrompt;

            _specialistEngine.LoadContext();
            _specialistEngine.WireSummaryService();

            // ── Register tools (filtered subset or all) ──
            var allowedSet = string.IsNullOrEmpty(request.AllowedTools)
                ? null
                : new HashSet<string>(
                    request.AllowedTools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase);

            RegisterSpecialistTools(_specialistEngine, allowedSet);

            await _specialistEngine.PrefillStaticPrefix();

            // ── Cancellation: parent ESC + timeout ──
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 180));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken, timeoutCts.Token);

            using var cancelReg = linkedCts.Token.Register(() =>
            {
                try { _specialistEngine.StopExecution(); }
                catch (Exception ex) { _logger.Debug("Handoff", $"StopExecution after cancel failed: {ex.Message}"); }
            });

            // ── Run the specialist orchestrator ──
            var specialistOrchestrator = new AgentOrchestrator(
                _specialistEngine,
                sessionOutput: _out,
                maxTurns: request.MaxTurns > 0 ? request.MaxTurns : 8,
                maxFailures: 3,
                logger: _logger,
                config: _config);

            _specialistEngine.StartExecution();

            // The context summary is the opening user message — it tells the
            // specialist what the user wants and what the parent already knows.
            var openingMessage = string.IsNullOrEmpty(request.ContextSummary)
                ? "Complete the task you were created for."
                : request.ContextSummary;

            var result = await specialistOrchestrator.ExecuteMultiStep(openingMessage);
            _specialistEngine.EndExecution();

            sw.Stop();
            var icon = result.Status == OrchestratorStatus.GoalAchieved ? "✅" : "❌";
            _out?.WriteInfo($"[Handoff] {icon} Specialist '{request.Name}' finished ({sw.Elapsed.TotalSeconds:F1}s, {result.ToolCallsMade} tool calls)");

            return result;
        }
        catch (OperationCanceledException)
        {
            _specialistEngine?.EndExecution();
            var isParentCancel = parentToken.IsCancellationRequested;
            _out?.WriteWarning($"[Handoff] Specialist '{request.Name}' cancelled: {(isParentCancel ? "parent stopped" : "timeout")}");
            return new OrchestratorResult
            {
                FinalOutput = isParentCancel
                    ? "Handoff specialist was cancelled by user."
                    : $"Handoff specialist '{request.Name}' timed out after {request.TimeoutSeconds}s.",
                Status = OrchestratorStatus.Failed
            };
        }
        catch (Exception ex)
        {
            _specialistEngine?.EndExecution();
            _logger.Error("Handoff", $"Specialist '{request.Name}' failed: {ex.Message}");
            return new OrchestratorResult
            {
                FinalOutput = $"[Handoff] Specialist '{request.Name}' failed: {ex.Message}",
                Status = OrchestratorStatus.Failed
            };
        }
    }

    /// <summary>
    /// Register the built-in tools on the specialist engine, filtered by the
    /// allowed-tools set if provided. EHandoff itself is never registered on
    /// a specialist — no recursive handoffs.
    /// </summary>
    private void RegisterSpecialistTools(AgentEngine engine, HashSet<string>? allowedSet)
    {
        static bool IsAllowed(string toolName, HashSet<string>? allowed) =>
            allowed == null || allowed.Contains(toolName);

        if (IsAllowed("EShellAgent", allowedSet))
            engine.RegisterTool(new Tools.Shell.EShellAgent(_processRunner, _config, _workingDir));
        if (IsAllowed("EBackgroundExec", allowedSet))
            engine.RegisterTool(new Tools.Background.EBackgroundExecTool(_bgManager, _processRunner, _fileSystem, _config));
        if (IsAllowed("EDotnetBuild", allowedSet))
            engine.RegisterTool(new Tools.Build.EDotnetBuildTool(_processRunner, _config));
        if (IsAllowed("EGitTool", allowedSet))
            engine.RegisterTool(new Tools.Git.EGitTool(_processRunner, _fileSystem, _config));
        if (IsAllowed("ECodeEditor", allowedSet))
            engine.RegisterTool(new Tools.Code.ECodeEditorTool(_fileSystem, _config));
        if (IsAllowed("EFileReader", allowedSet))
            engine.RegisterTool(new Tools.Reader.EFileReaderTool(_fileSystem, _config));
        if (IsAllowed("EFileResearchTool", allowedSet))
            engine.RegisterTool(new Tools.Research.EFileResearchTool(_fileSystem, _config));
    }

    public async ValueTask DisposeAsync()
    {
        if (_specialistEngine != null)
        {
            try { await _specialistEngine.DisposeAsync(); }
            catch (Exception ex) { _logger.Debug("Handoff", $"Non-critical dispose error: {ex.Message}"); }
        }
    }
}