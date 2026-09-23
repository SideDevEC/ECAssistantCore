using System.Text;
using System.Text.RegularExpressions;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Tools;
using ECAssistant.Core.Tools.Handoff;
using ECAssistant.Core.Services;
using ECAssistant.Core.Session;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Playbooks;

namespace ECAssistant.Core.Orchestration;

/// <summary>
/// Orchestrator — the decision-making brain for multi-step agent workflows.
///
/// v14: Tag-free. The engine returns a parsed LLMDecision directly — the legacy
/// <lm>/<output>/<toolcall> tag IR has been removed.
/// We simply act on the decision:
///    1. WantsToolCall → execute the requested tool(s), continue loop
///    2. WantsDirectAnswer → return the answer to the user, stop loop
///    3. Neither → retry once (text fallback), then best-effort delivery
/// </summary>
public sealed class AgentOrchestrator : IAsyncDisposable
{
    private readonly AgentEngine _engine;
    private readonly ISessionOutput? _out;
    private int _turnCount = 0;
    // v15 fix: stale-answer guard — allows exactly one forced continuation per run.
    private bool _staleAnswerContinuationUsed;
    private readonly List<string> _toolCallLog = new();
    private readonly List<string> _completedSteps = new();
    private int _formatRetries = 0;
    private const int MaxFormatRetries = 1; // v14: structured decoding rarely needs retries

     // v10.6: TaskPlanner for chained multi-step tasks
    private List<SubTask>? _subTasks = null;
    private readonly HashSet<string> _failedCallSignatures = new(StringComparer.Ordinal);
    private string? _lastSuccessfulCallSignature;
    // v14.9: long-range loop detection (A→B→A→B, 3+ identical repeats, batch repeats)
    private readonly Engine.ToolRepeatTracker _repeatTracker = new();
    private int _currentSubTask = 0;
     // v10.17: Execution plan from StepMapper
    private ExecutionPlan? _executionPlan = null;

      // v10.23: Config for sub-agent manager injection
    private readonly ECAssistant.Core.Config.AppConfig? _config;

      // ─── Hard Limits ──────────────────────
    private int _maxTurns;   // v10.6: changed from readonly to allow dynamic adjustment
    private readonly int _baseMaxTurns; // original maxTurns — _maxTurns is recomputed from this, never monotonically grown
    private readonly int _maxFailuresBeforeStop;

    // v15 (Emre, 2026-09-23): large tier is TIME-limited, not turn-limited.
    // model_tier.timeout_seconds set → turn cap disabled, wall-clock budget governs.
    private readonly System.Diagnostics.Stopwatch _runClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly int? _timeBudgetSec;

    /// <summary>Turn limit applies only when no time budget is configured (small tier).</summary>
    private bool UseTurnLimit => _timeBudgetSec is null or <= 0;

    private bool TimeBudgetExceeded => _timeBudgetSec is { } budget && _turnCount > 0 && _runStopwatch.Elapsed.TotalSeconds >= budget;

    private readonly System.Diagnostics.Stopwatch _runStopwatch = System.Diagnostics.Stopwatch.StartNew();

      // ─── Whitelist of valid tool names ─────
    private readonly HashSet<string> _toolWhitelist = new(StringComparer.OrdinalIgnoreCase);

      // ─── Tool Policy (permissions + approval) ─────
    private readonly ECAssistant.Core.Tools.ToolPolicy _toolPolicy;
    private readonly Engine.SteeringQueue _steering = new();
    private readonly ECAssistant.Core.Interfaces.ILogger? _logger;

     // v10.18: Sub-agent manager (lazy-init, created when first sub-agent tool is registered)
    private SubAgentManager? _subAgentManager;
    public SubAgentManager? SubAgentManager => _subAgentManager;

      // v15: Handoff executor — created lazily when EHandoff tool is registered
    private HandoffExecutor? _handoffExecutor;
    private string? _handoffWorkingDir;

      // v14.13: tier-aware post-edit verification loop
    private readonly IPostEditVerifier? _postEditVerifier;
    private int _verificationFailRounds = 0;
    private bool _verificationDisabled = false;   // set when max_rounds reached — stop verifying for the rest of the run
    private bool _largeTierVerified = false;      // large tier verifies at most once per run

    // v14.14: persistent success playbooks — capture after GoalAchieved runs with tools
    private readonly IPlaybookStore? _playbookStore;
    private readonly IPlaybookExtractor _playbookExtractor;
    private readonly List<CapturedToolCall> _successfulCalls = new();

      /// <summary>Create orchestrator with config.</summary>
    public AgentOrchestrator(
        AgentEngine engine,
        ISessionOutput? sessionOutput = null,
        int maxTurns = 5,
        int maxFailures = 3,
        ECAssistant.Core.Tools.ToolPolicy? toolPolicy = null,
        ECAssistant.Core.Interfaces.ILogger? logger = null,
        ECAssistant.Core.Config.AppConfig? config = null,
        IPostEditVerifier? postEditVerifier = null,
        IPlaybookStore? playbookStore = null,
        IPlaybookExtractor? playbookExtractor = null)
              {
                  _engine = engine;
                  _out = sessionOutput;
                  _maxTurns = Math.Max(1, maxTurns);
                  _baseMaxTurns = _maxTurns;
                  _timeBudgetSec = config?.ModelTier?.TimeoutSeconds;
                  _maxFailuresBeforeStop = maxFailures;
                  _toolPolicy = toolPolicy ?? new ECAssistant.Core.Tools.ToolPolicy();
                  _logger = logger ?? new Logger();
                  _config = config;
                  // v14.13: mirror the SelfCorrectionManager pattern — create the default
                  // verifier when none is injected and config enables the gate.
                  _postEditVerifier = postEditVerifier ??
                      (config?.Verification.Enabled == true
                          ? new Verification.PostEditVerifier(
                              new Verification.DotnetVerificationRunner(new Services.ProcessRunner()),
                              config.Verification)
                          : null);
              // v14.14: playbook store — explicit injection wins; else reuse the engine's
              // store (created by InitializePlaybooks alongside InitializeSelfCorrection).
              _playbookStore = playbookStore ?? _engine.PlaybookStore;
              _playbookExtractor = playbookExtractor ?? new Playbooks.PlaybookExtractor();

            foreach (var tool in _engine.Tools)
                   _toolWhitelist.Add(tool.Name);
              }

      /// <summary>v14.12: True when the active model runs the large-model (slim) profile.</summary>
     private bool IsLargeModelTier()
      {
         return _config?.ModelTier?.IsLargeRuntime(_config?.LlmProvider?.ModelId) ?? false;
      }

      /// <summary>v14.12.2: mid-run steering seam — hosts queue user input via AgentSession.Steer().</summary>
     public SteeringQueue Steering => _steering;

      /// <summary>v15: preplanning is tier-owned (Emre, 2026-09-23) — large models
      /// skip decomposition (in-loop planning), small models keep it. Config override removed.</summary>
     private bool UsePreplanning() => !IsLargeModelTier();

      /// <summary>v10.18: Initialize sub-agent support. Creates SubAgentManager and registers ESubAgent tool.</summary>
     public void InitializeSubAgents(string defaultWorkingDir)
      {
          // v10.23: Pass config to SubAgentManager (no more hardcoded disk reads)
          _subAgentManager = new SubAgentManager(_engine, defaultWorkingDir, _logger, _out, _config, parentToolPolicy: _toolPolicy);
          _engine.RegisterTool(new Tools.SubAgent.ESubAgentTool(_subAgentManager, defaultWorkingDir));
          _toolWhitelist.Add("ESubAgent");
          _toolPolicy.SetPermission("ESubAgent", approvalRequired: false, "Sub-agent spawning");
         _out?.WriteInfo("Sub-agent system initialized and ESubAgent tool registered.");
      }

      /// <summary>v15: Initialize handoff support. Creates HandoffExecutor and registers EHandoff tool.</summary>
    public void InitializeHandoff(string defaultWorkingDir)
     {
         _handoffWorkingDir = defaultWorkingDir;
         _handoffExecutor = new HandoffExecutor(
             _engine, // ISubAgentEngineHost
             _config ?? new Config.AppConfig(),
             InferenceParamsFactory.Default.Create(_config ?? new Config.AppConfig()),
             defaultWorkingDir,
             _logger,
             _out);

         // The tool's callback delegates to the executor — the orchestrator
         // intercepts EHandoff calls before normal tool execution, so this
         // callback is only reached if interception is bypassed (safety net).
         _engine.RegisterTool(new Tools.Handoff.EHandoffTool(async (req, ct) =>
         {
             if (_handoffExecutor == null)
                 return new OrchestratorResult { FinalOutput = "[Handoff] Executor not initialized.", Status = OrchestratorStatus.Failed };
             return await _handoffExecutor.RunAsync(req, ct);
         }));
         _toolWhitelist.Add("EHandoff");
         _toolPolicy.SetPermission("EHandoff", approvalRequired: false, "Agent handoff");
     }

      /// <summary>v15: Initialize handoff AND rebuild KV cache to include EHandoff in system prompt.</summary>
     public async Task InitializeHandoffAsync(string defaultWorkingDir)
      {
         InitializeHandoff(defaultWorkingDir);
         _out?.WriteWarning("Rebuilding KV cache to include EHandoff...");
         await _engine.ResetAndRebuildCacheAsync();
     }

           /// <summary>v10.18: Initialize sub-agents AND rebuild KV cache to include ESubAgent in system prompt.</summary>
     public async Task InitializeSubAgentsAsync(string defaultWorkingDir)
      {
         InitializeSubAgents(defaultWorkingDir);
          // Rebuild KV cache so ESubAgent appears in the tool list the LLM sees
         _out?.WriteWarning("Rebuilding KV cache to include ESubAgent...");
         await _engine.ResetAndRebuildCacheAsync();
      }

      /// <summary>Get the tool policy instance (for runtime modification).</summary>
    public ECAssistant.Core.Tools.ToolPolicy Policy => _toolPolicy;

      /// <summary>Execute multi-step workflow autonomously.</summary>
    public async Task<OrchestratorResult> ExecuteMultiStep(string goal)
              {
         // v10.4.4: Reset turn counters at the start of each new user request.
         // v10.5: Also reset engine for new request (KV cache keeps static prefix).
        Reset();
         _engine.ResetForNewRequest();

         // v10.5: Prefill the static prefix into KV cache if not done yet.
        await _engine.PrefillStaticPrefix();

         // v10.6: Decompose the request into sub-tasks using TaskPlanner
         // v11.4: Gate — skip decomposition for conversational questions
         var planner = _engine.TaskPlanner;
         var preplanning = UsePreplanning();
         if (!preplanning)
         {
            // In-loop planning (default): skip the 2-3 pre-pass LLM calls; the
            // loop's model plans as it goes (Claude Code-style). Turn budget
            // stays at base/max — no sub-task expansion without decomposition.
            _subTasks = new List<SubTask> { new SubTask { Description = goal, Status = SubTaskStatus.Pending } };
            _currentSubTask = 0;
         }
         else if (planner != null)
         {
            List<SubTask>? decomposed = null;

            // v11.4: Fast gate — action verb check (instant, zero cost)
            if (LooksConversational(goal))
            {
                _out?.WriteDim("Conversational question — skipping decomposition.");
                decomposed = new List<SubTask> { new SubTask { Description = goal, Status = SubTaskStatus.Pending } };
            }
            else
            {
             // v10.25: Try LLM-based decomposition via engine (HTTP streaming, stateless mode)
            _out?.WriteInfo("Attempting LLM task decomposition...");
            var steps = await _engine.DecomposeTaskAsync(goal);
            if (steps != null && steps.Count > 0)
             {
                decomposed = steps.Select(s => new SubTask { Description = s, Status = SubTaskStatus.Pending }).ToList();
                _out?.WriteSuccess($"LLM decomposition produced {decomposed.Count} steps.");
             }
            else
             {
                _out?.WriteWarning("LLM decomposition failed or disabled — falling back to keywords.");
             }

             // Fallback: keyword-based decomposition
            if (decomposed == null || decomposed.Count == 0)
             {
                decomposed = planner.Decompose(goal);
             }
            else
             {
                 // Populate planner with LLM-generated steps so GetProgressContext works
                planner.Decompose(string.Join(" then ", decomposed.Select(s => s.Description)));
             }
            }

             _subTasks = decomposed;
             _currentSubTask = 0;

            if (_subTasks.Count > 1)
             {
                 _subTasks[0].Status = SubTaskStatus.InProgress;
                 // v10.6: Dynamic turn limit — allow 2 turns per sub-task + 2 buffer for output/retries
                 // Recompute from the ORIGINAL maxTurns — Math.Max against the field made
                 // the limit grow monotonically across orchestrator runs.
                 var turnsPerSubtask = _config?.Interface.TurnsPerSubtask > 0 ? _config.Interface.TurnsPerSubtask : 2;
                 var turnBuffer = _config?.Interface.SubtaskTurnBuffer > 0 ? _config.Interface.SubtaskTurnBuffer : 2;
                 _maxTurns = Math.Max(_baseMaxTurns, _subTasks.Count * turnsPerSubtask + turnBuffer);
                _out?.WriteInfo($"Decomposed into {_subTasks.Count} steps — max turns adjusted to {_maxTurns}");
                for (int i = 0; i < _subTasks.Count; i++)
                    _out?.WriteDim($"  Step {i+1}: {_subTasks[i].Description}");
                _out?.BlankLine();

                // v14.9: interactive checkpoint — present the plan as choices when the
                // interaction policy allows. null answer (no listener, timeout, cancel,
                // invalid input) = proceed autonomously with the decomposed plan.
                if (_config?.Interaction.ConfirmPlan == true && _out != null)
                {
                    var planLines = _subTasks.Select((t, i) => $"{i + 1}. {t.Description}").ToList();
                    var planText = "Planned steps:\n" + string.Join("\n", planLines);
                    var choice = _out.RequestChoice(
                        $"Plan for: {goal}\nExecute this plan?",
                        new List<string>
                        {
                            "Execute the plan",
                            "Answer directly without tools",
                            "Proceed without the plan (turn-by-turn)",
                        });
                    switch (choice)
                    {
                        case 2: // answer directly
                            _subTasks = new List<SubTask> { new SubTask { Description = goal, Status = SubTaskStatus.Pending } };
                            _currentSubTask = 0;
                            _out.WriteDim("Checkpoint: answering directly — planning skipped.");
                            break;
                        case 3: // no plan
                            _subTasks = new List<SubTask> { new SubTask { Description = goal, Status = SubTaskStatus.Pending } };
                            _currentSubTask = 0;
                            _maxTurns = _baseMaxTurns;
                            _out.WriteDim("Checkpoint: proceeding without the plan.");
                            break;
                        default: // 1 or null — execute plan (autonomous default)
                            _out?.WriteDim(choice == null ? "No checkpoint answer — executing plan autonomously." : "Plan approved.");
                            break;
                    }
                }
             }

             // v10.17: Step Mapping — map sub-tasks to concrete tool calls
            ExecutionPlan? executionPlan = null;
            if (_subTasks != null && _subTasks.Count > 1)
             {
                var mapper = new StepMapper(_engine, _logger);
                _out?.WriteInfo("Mapping steps to tool calls...");
                executionPlan = await mapper.MapAsync(_subTasks, goal);
                 _executionPlan = executionPlan; // Store for advancement logic
                if (executionPlan.IsValid)
                 {
                    _out?.WriteSuccess($"Plan: {executionPlan.Calls.Count} call(s):");
                    for (int i = 0; i < executionPlan.Calls.Count; i++)
                     {
                        var c = executionPlan.Calls[i];
                        _out?.WriteDim($"  Call {i+1}: {c.ToolName} — covers steps {string.Join(",", c.CoversSubTasks.Select(s => s+1))} — {c.Description}");
                     }
                    _out?.BlankLine();
                 }
                else
                 {
                    _out?.WriteWarning($"Mapping failed: {executionPlan.Error} — LLM will plan ad-hoc.");
                 }

                 // Inject the execution plan into context so the LLM follows it
                if (executionPlan != null && executionPlan.IsValid)
                 {
                     _engine.InjectExecutionPlan(executionPlan.ToPromptString());
                 }
             }
         }

        _out?.WriteLine($"[Orchestrator] Starting for: {goal}");
        _out?.WriteLine($"[Orchestrator] Max turns: {_maxTurns}, Failures limit: {_maxFailuresBeforeStop}");

            while (TimeBudgetExceeded == false && (UseTurnLimit == false || _turnCount < _maxTurns))
                   {
                 // v10.9: Check for user cancellation before each turn
                if (_engine.ExecutionToken.IsCancellationRequested)
                 {
                    _out?.WriteWarning("Execution cancelled by user. Stopping.");
                    var cancelSummary = FormatTurnLog();
                    await TryCapturePlaybookAsync(goal);
                    return new OrchestratorResult
                     {
                        FinalOutput = $"Execution cancelled by user.\n\n{cancelSummary}",
                        ToolCallsMade = _turnCount,
                        Status = OrchestratorStatus.GoalAchieved   // not an error — user chose to stop
                     };
                 }
            _logger?.Info("Orchestrator", $"Turn {_turnCount + 1}/{_maxTurns}");

                  // v12.0: chat-classified goals answer directly — no toolcall demanded
              _out?.SetStatus("Thinking\u2026");
                // v14.12.2: steering — drain queued user input before the next decision.
                var steer = _steering.Drain();
                if (!string.IsNullOrEmpty(steer))
                {
                    _out?.WriteInfo($"[Steering] {steer}");
                    _logger?.Info("Orchestrator", "Steering drained: " + steer[..Math.Min(80, steer.Length)]);
                    _engine.InjectFormatRetry(
                        "[USER STEERING] " + steer + "\n" +
                        "Adjust the remaining work accordingly. The original goal still applies unless the steering says otherwise.");
                }
              var decision = await _engine.GenerateAsync(goal);

              // v14.10.2: envelope-echo guard (defense in depth). If a direct answer
              // somehow arrives as raw {"thinking","answer"} JSON (degraded fallback
              // path), unwrap it so the user never sees wire-format JSON.
              if (decision.WantsDirectAnswer && !string.IsNullOrWhiteSpace(decision.AnswerText)
                  && decision.AnswerText.TrimStart().StartsWith('{'))
              {
                  var unwrapped = Engine.StructuredDecisionAdapter.TryExtractAnswer(decision.AnswerText);
                  if (!string.IsNullOrWhiteSpace(unwrapped))
                      decision = LLMDecision.FromEnvelope(decision.Reasoning ?? "", unwrapped, null);
              }

              // v10.11.1: Check if generation was stopped by user (ESC) — bail out immediately,
              // don't attempt format retries on the stopped sentinel.
             if (decision.AnswerText == "(Stopped by user)" || _engine.IsExecutionStopped)
             {
                _out?.WriteWarning("Generation was stopped by user (ESC). Not retrying.");

                 // v10.18.1: Cancel all active sub-agents when main agent is stopped
                 _subAgentManager?.CancelAll();

                 // v10.11.1: Clean up stale context from the stopped attempt so the next
                 // command starts fresh. The KV cache static prefix is preserved.
                 _engine.ClearContextWindowOnly();
                 // v10.11.1: Rebuild KV cache to remove stale user message tokens from the stopped attempt.
                 // The static prefix (system prompt + tools) is re-prefilled fresh.
                await _engine.RebuildCacheAfterStopAsync();
                var stopSummary = FormatTurnLog();
                await TryCapturePlaybookAsync(goal);
                return new OrchestratorResult
                 {
                    FinalOutput = $"Execution stopped by user (ESC).\n\n{stopSummary}",
                    ToolCallsMade = _turnCount,
                    Status = OrchestratorStatus.GoalAchieved   // not an error — user chose to stop
                 };
             }

             _out?.WriteDim($"[Orchestrator] Decision: WantsToolCall={decision.WantsToolCall}, WantsDirectAnswer={decision.WantsDirectAnswer}, ToolCalls={decision.ToolCallCount}, ToolName={decision.ToolName}");

              // v12.12 model-agnostic: transport-level failures are surfaced by the
              // engine as a "[Error] ..." sentinel answer — never treat as model
              // output, never format-retry. Fail the run with the error.
              if (decision.AnswerText?.StartsWith("[Error]", StringComparison.OrdinalIgnoreCase) == true)
                       {
                  // Transport-level failure surfaced by the engine — never treat as
                  // model output, never format-retry. Fail the run with the error.
                  _logger?.Error("Orchestrator", $"Engine error surfaced: {decision.AnswerText}");
                  return new OrchestratorResult
                           {
                          FinalOutput = decision.AnswerText,
                          ToolCallsMade = _turnCount,
                          Status = OrchestratorStatus.Failed
                           };
                       }

                  // Step 2: The decision is already parsed by the engine — no tag parsing here.
              _out?.WriteDim($"[Orchestrator] Parse result: WantsToolCall={decision.WantsToolCall}, WantsDirectAnswer={decision.WantsDirectAnswer}, ToolCalls={decision.ToolCallCount}, ToolName={decision.ToolName}");

              if (decision.WantsToolCall)
                        {
                 _formatRetries = 0; // reset on valid tool call

                 // v10.13: Multi-tool parallel execution
                if (decision.IsMultiCall)
                 {
                    _logger?.Info("Orchestrator", $"Multi-tool call: {decision.ToolCallCount} tools");
                    _out?.WriteInfo($"Multi-tool call: {decision.ToolCallCount} tools — analyzing dependencies...");

                     // v14.9: record batch signatures for long-range loop detection; if any
                     // call in the batch is a 3+ repeat, stop the run (same as single path).
                    foreach (var tc in decision.ToolCalls)
                     {
                        var batchSig = BuildCallSignature(tc.ToolName ?? "(unknown)", tc.Args);
                        var batchRepeat = _repeatTracker.Record(batchSig);
                        if (_repeatTracker.IsStopLevel(batchRepeat))
                         {
                            _out?.WriteWarning($"Loop detected in batch: '{tc.ToolName}' identical repeat #{batchRepeat}. Stopping.");
                            _logger?.Warn("Orchestrator", $"Batch loop stop: {batchSig} x{batchRepeat}");
                            return new OrchestratorResult
                             {
                                FinalOutput = $"Stopped: tool loop detected — '{tc.ToolName}' was called with identical arguments {batchRepeat} times.\n" +
                                              "Steps completed so far:\n" + string.Join("\n", _completedSteps.TakeLast(10)),
                                ToolCallsMade = _turnCount,
                                Status = OrchestratorStatus.TurnsExhausted
                             };
                         }
                     }

                     // Create parallel executor
                    // v14.12.2: show the model's progress narration (remote path).
                    if (!string.IsNullOrEmpty(decision.Commentary))
                        _out?.WriteDim($"[Agent] {decision.Commentary}");

                    var parallelExec = new ParallelToolExecutor(
                         _engine,
                         _toolPolicy,
                        ExecuteTool,
                        msg => _out?.WriteDim(msg),
                        _out);

                     // Execute all tool calls with dependency-aware parallelism
                    _out?.SetStatus($"Running {decision.ToolCalls.Count} tools\u2026");
                    var batchResult = await parallelExec.ExecuteAsync(decision.ToolCalls, _engine.ExecutionToken);

                     // Display summary
                    var consoleSummary = parallelExec.FormatConsoleSummary(batchResult);
                    if (batchResult.AllSucceeded)
                         _out?.WriteSuccess($"Batch: {consoleSummary}");
                    else
                         _out?.WriteWarning($"Batch: {consoleSummary}");
                    _logger?.Info("Orchestrator", $"Batch result: {consoleSummary}");

                    // v14.10.2: a DENIED call was never executed — unrecord it so a user
                    // denial doesn't push the signature toward the loop limit and block
                    // legitimate retries of the same call after approval.
                    foreach (var r in batchResult.Results)
                    {
                        if (!r.Succeeded && r.Error != null && r.Error.Contains("[DENIED]"))
                            _repeatTracker.Unrecord(BuildCallSignature(r.ToolCall.ToolName ?? "(unknown)", r.ToolCall.Args));
                    }

                     // Combine all results into one output block for the LLM
                    var combinedOutput = parallelExec.CombineResults(batchResult);
                    _out?.WriteLine($"[Orchestrator] Batch output:\n{StringUtil.Default.Truncate(combinedOutput, 2000)}");

                     // v10.13.1: Log one summary entry per batch (not per tool) for accurate streak detection
                    var okCount = batchResult.Results.Count(r => r.Succeeded);
                    var failCount = batchResult.Results.Count(r => !r.Succeeded);
                    if (failCount > 0)
                         _toolCallLog.Add($"BATCH FAIL: {failCount}/{batchResult.Results.Count} tools failed");
                    else
                         _toolCallLog.Add($"BATCH OK: {okCount}/{batchResult.Results.Count} tools succeeded");

                     // Track completed steps per tool (for summary)
                    foreach (var r in batchResult.Results)
                     {
                        var stepCmd = r.ToolCall.Args.GetValueOrDefault("command") ?? r.ToolCall.Args.GetValueOrDefault("action") ?? "";
                        var stepDesc = $"{r.ToolCall.ToolName}: {StringUtil.Default.Truncate(stepCmd, 80)}";
                         _completedSteps.Add(stepDesc);
                        // v14.14: record successful batch calls for playbook capture
                        if (r.Succeeded)
                            _successfulCalls.Add(new CapturedToolCall(r.ToolCall.ToolName ?? "(unknown)", SummarizeArgs(r.ToolCall.Args)));
                     }

                     // v10.16.2: Conservative batch sub-task advancement.
                     // Advance one sub-task per successful tool in the batch.
                     // The LLM decides when ALL steps are done via a final answer.
                    if (_subTasks != null && _subTasks.Count > 1)
                     {
                        if (failCount == 0 && okCount > 0)
                         {
                             // All tools succeeded — advance one sub-task per successful tool
                            var toAdvance = Math.Min(okCount, _subTasks.Count - _currentSubTask);
                            for (int i = 0; i < toAdvance; i++)
                                AdvanceSubTask(true, "Batch", $"Batch tool {i + 1}/{toAdvance} succeeded");
                         }
                        else if (okCount == 0 && failCount > 0)
                         {
                            AdvanceSubTask(false, "Batch", $"{failCount} tools failed");
                         }
                        else if (okCount > 0 && failCount > 0)
                         {
                             // Mixed — advance succeeded ones, leave failed one pending
                            var toAdvance = Math.Min(okCount, _subTasks.Count - _currentSubTask);
                            for (int i = 0; i < toAdvance; i++)
                                AdvanceSubTask(true, "Batch", $"Batch tool {i + 1}/{toAdvance} succeeded");
                         }
                     }

                     // Add combined result to conversation history (one block)
                     _engine.AddToolResult("Batch", combinedOutput);

                    // v14.19: verification gate for the BATCH path — the single-call path
                    // gates after every file-modifying edit; batched edits (decomposed
                    // plans) previously bypassed verification entirely. Verify each
                    // successful mutating call (typically one per batch).
                    foreach (var r in batchResult.Results.Where(r => r.Succeeded))
                        await RunPostEditVerificationAsync(r.ToolCall.ToolName!, r.ToolCall.Args, goal);

                     // Check for failure streak — one batch = one turn in streak detection
                    if (failCount > 0)
                     {
                        if (IsFailureStreak(_maxFailuresBeforeStop))
                         {
                            _out?.WriteLine($"[Orchestrator] Too many consecutive failure turns ({_maxFailuresBeforeStop}). Stopping.");
                            return new OrchestratorResult
                             {
                                FinalOutput = $"Stopped after {_maxFailuresBeforeStop} consecutive failure turns during batch execution.\nFailed tools: {string.Join(", ", batchResult.Results.Where(r => !r.Succeeded).Select(r => r.ToolCall.ToolName))}",
                                ToolCallsMade = _turnCount + 1,
                                Status = OrchestratorStatus.TurnsExhausted
                             };
                         }
                     }

                     // Inject step-aware directive
                    var stepDirective = BuildStepDirective();
                     _engine.InjectFormatRetry(stepDirective);

                    _out?.WriteDim($"[Orchestrator] Batch complete, looping back to LLM (turn {_turnCount + 1})...");
                     _turnCount++;
                    continue;
                 }

                 // ── Single tool call (original path) ──
                _logger?.Info("Orchestrator", $"Tool call: {decision.ToolName}");

                var argsDict = decision.Args;

                // v15: Handoff interception — EHandoff is a special tool that
                // delegates to a specialist agent. The specialist's answer
                // becomes the final answer; the parent loop stops.
                if (string.Equals(decision.ToolName, "EHandoff", StringComparison.OrdinalIgnoreCase))
                {
                    _out?.WriteInfo("[Handoff] Model requested specialist handoff.");
                    var handoffResult = await ExecuteHandoffAsync(argsDict, goal);
                    await TryCapturePlaybookAsync(goal);
                    return handoffResult;
                }

                 // ── Tool Policy Check ──
                var policyDecision = _toolPolicy.Check(decision.ToolName!, argsDict);
                if (policyDecision.NeedsApproval)
                 {
                    _out?.WriteLine($"[Policy] {policyDecision.Message}");
                     // In console mode, ask the user directly
                    _out?.WriteLine($"[Policy] Approve execution of {decision.ToolName} with args: {string.Join(", ", argsDict.Select(kvp => kvp.Key + "=" + (kvp.Value ?? "(null)")))}?");
                    var scope = _out?.RequestApprovalScoped($"[Policy] Approve execution of {decision.ToolName} with args: {string.Join(", ", argsDict.Select(kvp => kvp.Key + "=" + (kvp.Value ?? "(null)")))}?") ?? Session.ApprovalScope.Deny;
                    var approved = scope != Session.ApprovalScope.Deny;
                    // v14.10.2: remember-decision — 'a' allows this tool+pattern for the rest of the session.
                    if (scope == Session.ApprovalScope.AllowSession)
                        _toolPolicy.ApproveSessionPattern(decision.ToolName!, Tools.ToolPolicy.BuildSessionPattern(decision.ToolName!, argsDict));
                    if (!approved)
                     {
                        _out?.WriteLine($"[Policy] Tool execution DENIED by user: {decision.ToolName}");
                         _engine.AddToolResult(decision.ToolName!, "[DENIED] User did not approve this tool execution.");
                         _turnCount++;
                        continue;
                     }
                    _out?.WriteLine($"[Policy] Approved by user.");
                 }
                else if (!policyDecision.CanExecute)
                 {
                    _out?.WriteLine($"[Policy] {policyDecision.Message}");
                     _engine.AddToolResult(decision.ToolName!, $"[BLOCKED] {policyDecision.Message}");
                     _turnCount++;
                    continue;
                 }

                var startMs = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

                    try
                          {
                         // v10.9.3: Check cancellation before executing tool
                        if (_engine.ExecutionToken.IsCancellationRequested)
                         {
                            _out?.WriteWarning("Execution cancelled before tool call.");
                            return new OrchestratorResult
                             {
                                FinalOutput = "Execution cancelled by user.",
                                ToolCallsMade = _turnCount,
                                Status = OrchestratorStatus.GoalAchieved
                             };
                         }
                        // v12.4: never re-execute a call that already failed with identical arguments —
                        // force the model to change approach instead of looping on the same error.
                        var callSignature = BuildCallSignature(decision.ToolName!, argsDict);
                        // v14.9: long-range repeat guard — record BEFORE the v12.4/v12.5 checks
                        // so nudges/stop levels are accurate even when those guards fire first.
                        var repeatCount = _repeatTracker.Record(callSignature);
                        // v14.9: stop level — 3+ identical repeats = detected loop. End the run
                        // with a clear report instead of burning the remaining turn budget.
                        if (_repeatTracker.IsStopLevel(repeatCount))
                         {
                            _out?.WriteWarning($"Loop detected: '{decision.ToolName}' called identically {repeatCount} times. Stopping.");
                            _logger?.Warn("Orchestrator", $"Loop stop: {callSignature} x{repeatCount}");
                            return new OrchestratorResult
                             {
                                FinalOutput = $"Stopped: tool loop detected — '{decision.ToolName}' was called with identical arguments {repeatCount} times.\n" +
                                              "Steps completed so far:\n" + string.Join("\n", _completedSteps.TakeLast(10)),
                                ToolCallsMade = _turnCount,
                                Status = OrchestratorStatus.TurnsExhausted
                             };
                         }
                        if (_failedCallSignatures.Contains(callSignature))
                         {
                            _out?.WriteWarning("Identical tool call already failed — blocked. Forcing a different approach.");
                            _logger?.Warn("Orchestrator", $"Blocked repeat of failed call: {callSignature}");
                            _engine.InjectFormatRetry(
                                $"The user's original request was: \"{goal}\"\n" +
                                "That exact tool call already failed (see the error above). Repeating it gives the same error.\n" +
                                "Do NOT start a new or unrelated task. Change your approach: use DIFFERENT arguments or a different tool, or if the task cannot proceed, provide your final answer with what you found and what blocked you.");
                            _turnCount++;
                            continue;
                         }
                        // v12.5: an identical call that JUST succeeded adds nothing — the results are
                        // already in context. Force the model to use them instead of looping.
                        if (callSignature == _lastSuccessfulCallSignature)
                         {
                            _out?.WriteWarning("Identical call just succeeded — results are above. Blocking repeat.");
                            _logger?.Warn("Orchestrator", $"Blocked repeat of just-successful call: {callSignature}");
                            _engine.InjectFormatRetry(
                                $"The user's original request was: \"{goal}\"\n" +
                                "You already executed exactly this call and its results are ABOVE in the conversation.\n" +
                                "Do NOT repeat it, do NOT start a new or unrelated task. Use those results to answer the ORIGINAL request directly. " +
                                "Only call a tool again with CHANGED arguments if you genuinely need different data for it.");
                            _turnCount++;
                            continue;
                         }
                        // v14.9: nudge level — 2nd identical repeat that the v12.4/v12.5 guards
                        // don't cover (first one succeeded but is no longer the last call),
                        // or an A→B→A→B alternation. Redirect without executing.
                        var alternationLoop = _repeatTracker.IsAlternationLoop();
                        if (_repeatTracker.IsNudgeLevel(repeatCount) || alternationLoop)
                         {
                            var reason = alternationLoop
                                 ? "You are alternating between the same calls without progress (A→B→A→B pattern)."
                                 : $"You already executed '{decision.ToolName}' with exactly these arguments {repeatCount - 1} time(s) before.";
                            _out?.WriteWarning("Tool repeat nudge injected (no re-execution).");
                            _logger?.Warn("Orchestrator", $"Repeat nudge: {callSignature} x{repeatCount} alt={alternationLoop}");
                            _engine.InjectFormatRetry(
                                $"The user's original request was: \"{goal}\"\n" +
                                reason + " Repeating identical calls wastes turns and gives the same result.\n" +
                                "Change your approach: use DIFFERENT arguments, a DIFFERENT tool, or answer directly with what you already have.");
                            _turnCount++;
                            continue;
                         }
                        _out?.SetStatus($"Running {decision.ToolName}\u2026");
                        // v14.12.2: show the model's progress narration (remote path).
                        if (!string.IsNullOrEmpty(decision.Commentary))
                            _out?.WriteDim($"[Agent] {decision.Commentary}");
                        var result = await ExecuteTool(decision.ToolName!, argsDict);
                        var elapsedMs = (long)((DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) - startMs);

                            if (!result.Succeeded)
                                 _failedCallSignatures.Add(callSignature);
                            else
                                 _lastSuccessfulCallSignature = callSignature;
                            if (result.Succeeded)
                                 _out?.WriteSuccess($"[Tool] {decision.ToolName}: OK ({elapsedMs}ms)");
                            else
                                 _out?.WriteError($"[Tool] {decision.ToolName}: FAIL ({elapsedMs}ms)");
                            _logger?.Info("Orchestrator", $"Tool: {decision.ToolName} = {(result.Succeeded ? "SUCCESS" : "FAILURE")} ({elapsedMs}ms)");

                        if (result.Succeeded)
                                 {
                                _out?.WriteLine($"[Orchestrator] Output:\n{(result.Output != null ? StringUtil.Default.Truncate(result.Output, 2000) : "(no output)")}");

                                     // Log for LLM context
                                    var logEntry = $"Tool:{decision.ToolName} \u2192 OK\nOutput: {(result.Output != null ? StringUtil.Default.Truncate(result.Output, 1000) : "(no output)")}";
                                        _toolCallLog.Add(logEntry);

                                     // Add tool result to conversation history
                                            var toolOutput = result.Output!;

                                     // Track completed step
                                    var stepCmd = argsDict.GetValueOrDefault("command") ?? "";
                                    var stepDesc = $"{decision.ToolName}: {StringUtil.Default.Truncate(stepCmd, 80)}";
                                     _completedSteps.Add(stepDesc);

                                     // v14.14: record the successful call for playbook capture
                                     _successfulCalls.Add(new CapturedToolCall(decision.ToolName!, SummarizeArgs(argsDict)));

                                     // v10.17: Sub-task advancement based on execution plan.
                                     // If the plan says this call covers multiple steps, advance all of them.
                                     // If no plan or call not in plan, advance one (conservative default).
                                    if (_subTasks != null && _subTasks.Count > 1)
                                     {
                                        var plannedCall = _executionPlan?.Calls.FirstOrDefault(c => c.ToolName.Equals(decision.ToolName!, StringComparison.OrdinalIgnoreCase));
                                        if (plannedCall != null && plannedCall.CoversSubTasks.Count > 1)
                                         {
                                             // Advance all steps the plan says this call covers
                                            foreach (var stepIdx in plannedCall.CoversSubTasks)
                                             {
                                                if (_currentSubTask < _subTasks.Count)
                                                    AdvanceSubTask(true, decision.ToolName!, stepDesc);
                                             }
                                         }
                                        else
                                         {
                                             // No plan mapping — advance one conservatively
                                            AdvanceSubTask(true, decision.ToolName!, stepDesc);
                                         }
                                     }

                                     _engine.AddToolResult(decision.ToolName!, toolOutput);

                                     // v14.13: tier-aware post-edit verification gate
                                     await RunPostEditVerificationAsync(decision.ToolName!, argsDict, goal);

                                     // v10.6: Inject step-aware directive with sub-task context
                                    var stepDirective = BuildStepDirective();
                                     _engine.InjectFormatRetry(stepDirective);

                                _out?.WriteDim($"[Orchestrator] Tool succeeded, looping back to LLM (turn {_turnCount + 1})...");
                                 }
                        else
                                 {
                                 // v10.6: Advance sub-task tracking on failure
                                AdvanceSubTask(false, decision.ToolName!, $"Tool failed: {result.Error}");

                                 // v10.15.1: Log the failure for streak detection
                                 _toolCallLog.Add($"Tool:{decision.ToolName} \u2192 FAIL: {result.Error}");

                                 // v10.15.1: Feed the error back to the LLM so it knows the tool failed.
                                 // v14.20: AddToolResult renders via the tool's own projection — for
                                 // build failures the embedded raw log compacts to parsed errors here.
                                 // Render exactly ONCE (double render re-parses compacted text).
                                 _engine.AddToolResult(decision.ToolName!, $"[ERROR] Tool failed: {result.Error}");

                                if (IsFailureStreak(_maxFailuresBeforeStop))
                                          {
                                        _out?.WriteLine($"[Orchestrator] Too many failures ({_maxFailuresBeforeStop} in a row). Stopping.");
                                            return new OrchestratorResult
                                                     {
                                                    FinalOutput = $"Stopped after {_maxFailuresBeforeStop} consecutive failures on tool: {decision.ToolName}",
                                                    ToolCallsMade = _turnCount + 1,
                                                    Status = OrchestratorStatus.TurnsExhausted
                                                     };
                                          }

                                 // v10.15.1: Inject directive so LLM knows to retry or report
                                var failDirective = BuildStepDirective();
                                 _engine.InjectFormatRetry(failDirective);
                                 }
                          }
                     catch (Exception ex)
                             {
                            _out?.WriteLine($"[Orchestrator] Tool exception: {ex.Message}");
                                     _toolCallLog.Add($"Tool:{decision.ToolName} \u2192 EXCEPTION: {ex.Message}");
                            // Feed the exception back to the LLM — without this the model
                            // never learns the tool call failed and repeats it blindly.
                            if (!string.IsNullOrEmpty(decision.ToolName))
                                _engine.AddToolResult(decision.ToolName, $"[EXCEPTION] Tool threw: {ex.GetType().Name}: {ex.Message}");
                            // Count the turn — otherwise a persistently throwing tool loops forever.
                            _turnCount++;
                            continue;
                             }

                                 // Always increment turn and loop back — let LLM decide next action
                                 _turnCount++;
                        continue;
                      }
            else if (decision.WantsDirectAnswer)
                       {
                // v15 fix: stale-answer guard — see IsStaleFinalAnswer.
                if (IsStaleFinalAnswer(decision.AnswerText))
                 {
                    _staleAnswerContinuationUsed = true;
                    _out?.WriteWarning("[Orchestrator] Final answer is a stale pre-tool reply — forcing one synthesis turn.");
                    _engine.InjectFormatRetry(
                        "You called a tool, and its result has arrived. Do not stop without reporting it: " +
                        "give your final answer now, using the tool result above.");
                    _turnCount++;
                    continue;
                 }
                _out?.WriteLine("[Orchestrator] LLM gave direct answer. Stopping.");
                    await TryCapturePlaybookAsync(goal);
                    return new OrchestratorResult
                             {
                            FinalOutput = decision.AnswerText!,
                            ToolCallsMade = _turnCount + 1,
                            Status = OrchestratorStatus.GoalAchieved
                             };
                       }
            else
                       {
                   // v14: text-fallback path only — the structured envelope always yields a
                   // valid decision, so this branch is reached when free-form streaming
                   // produced neither an answer nor a tool call. Retry once, then deliver best-effort.
                  _formatRetries++;
                if (_formatRetries <= MaxFormatRetries)
                  {
                     _logger?.Warn("Orchestrator", $"No decision (attempt {_formatRetries}/{MaxFormatRetries}). Removing bad response, retrying.");

                      // Remove the bad assistant response from history so model doesn't learn from it
                    await _engine.RemoveLastAssistantResponseAsync();

                      // Inject as a user-level message (not tool result) for stronger signal
                      _engine.InjectFormatRetry(
                           "Your last response did not produce an answer or a tool call.\n" +
                           "If you have enough information, provide your final answer. If you need more data, call a tool.");
                       _turnCount++;
                    continue;
                  }
                else
                  {
                     // v12.12 model-agnostic: after format retries, don't error out —
                     // deliver the model's last response (best effort) so ANY model can
                     // complete a conversation.
                     _logger?.Warn("Orchestrator", $"No decision after {MaxFormatRetries} retries — delivering best-effort response.");
                    if (IsStaleFinalAnswer(decision.AnswerText))
                     {
                        _staleAnswerContinuationUsed = true;
                        _out?.WriteWarning("[Orchestrator] Best-effort answer is stale (pre-tool) — forcing one synthesis turn.");
                        _engine.InjectFormatRetry(
                            "You called a tool, and its result has arrived. Do not stop without reporting it: " +
                            "give your final answer now, using the tool result above.");
                        _turnCount++;
                        continue;
                     }
                    await TryCapturePlaybookAsync(goal);
                    return new OrchestratorResult
                             {
                            // v14.18: after retries fail, thinking text is the last
                            // resort (never surfaced on the first pass — see LLMDecision).
                            FinalOutput = decision.AnswerText ??
                                (string.IsNullOrWhiteSpace(decision.Reasoning)
                                    ? "(No response content)"
                                    : decision.Reasoning),
                            ToolCallsMade = _turnCount + 1,
                            Status = OrchestratorStatus.GoalAchieved
                             };
                 }
                       }
                  }

                    // v15: exit reason depends on the active limiter — time budget
                    // (large tier) or turn cap (small tier).
        if (TimeBudgetExceeded)
         {
            var minutes = _runStopwatch.Elapsed.TotalMinutes.ToString("F1");
            _out?.WriteLine($"[Orchestrator] Time budget ({_timeBudgetSec}s) exhausted. Stopping.");
            await TryCapturePlaybookAsync(goal);
            var timeSummary = FormatTurnLog();
            if (string.IsNullOrEmpty(timeSummary)) timeSummary = "(No useful output in the last turn.)";
            return new OrchestratorResult
                     {
                    FinalOutput = $"Reached the time budget ({_timeBudgetSec}s). Partial progress:\n\n{timeSummary}",
                    ToolCallsMade = _turnCount,
                    Status = OrchestratorStatus.TurnsExhausted // reuse: budget exhausted
                     };
         }
        _out?.WriteLine("[Orchestrator] Max turns reached. Stopping.");
        // v14.19: capture partial-progress playbooks on the max-turns exit too —
        // successful tool calls still happened; they'd otherwise never persist.
        await TryCapturePlaybookAsync(goal);
        var summaryText = FormatTurnLog();
        if (string.IsNullOrEmpty(summaryText)) summaryText = "(No useful output in the last turn.)";
            return new OrchestratorResult
                     {
                    FinalOutput = $"Reached max turns ({_maxTurns}). Last response was empty or unhelpful.\n\n{summaryText}",
                    ToolCallsMade = _turnCount,
                    Status = OrchestratorStatus.TurnsExhausted
                     };
              }



      /// <summary>v15: run teardown — dispose persistent shell sessions + sandbox
      /// profiles; kill tracked background processes so nothing outlives the run.</summary>
     public async Task DisposeRunShellSessionsAsync()
      {
        foreach (var shell in _engine.Tools.OfType<ECAssistant.Core.Tools.Shell.EShellAgent>())
            await shell.DisposeSessionAsync();
        foreach (var bg in _engine.Tools.OfType<ECAssistant.Core.Tools.Background.EBackgroundExecTool>())
            bg.DisposeProcesses();
      }

      // ─── Multi-Tool Parsing (v10.13) ────────────

     // v11.4: Fast conversational gate — action verb + step indicator heuristic
    private static readonly string[] ActionVerbs = new[]
    {
        "build", "create", "add", "remove", "update", "fix", "replace", "refactor",
        "test", "delete", "move", "copy", "run", "search", "find", "read", "write",
        "install", "deploy", "configure", "check", "analyze", "scan", "show",
        "list", "open", "edit", "generate", "execute", "start", "stop", "restart"
    };

    private static readonly string[] StepIndicators = new[]
    {
        " and then ", " then ", " after that ", " also ", " finally ", " next ", " and "
    };

    private static bool LooksConversational(string request)
    {
        // v12.1: the verb/step lists are English-only — for non-English input the check
        // can never match reliably. Defer to the multilingual LLM classifier instead.
        if (request.Any(c => c > 127))
            return false;

        var hasActions = ActionVerbs.Any(v => request.Contains(v, StringComparison.OrdinalIgnoreCase));
        var hasSteps = StepIndicators.Any(s => request.Contains(s, StringComparison.OrdinalIgnoreCase));
        return !hasActions && !hasSteps;
    }

    /// <summary>Canonical signature for a tool call (tool + ordered args) — used by the repeat/failure guards. Pure.</summary>
    // Stateless utility — no mutable state.
    internal static string BuildCallSignature(string toolName, Dictionary<string, string?> args) =>
        toolName + "|" + string.Join("&",
            (args ?? new Dictionary<string, string?>()).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

       // ─── Tool Execution ──────────────────────

       /// <summary>Check if recent tool calls have all failed.</summary>
    private bool IsFailureStreak(int threshold)
                {
        if (_toolCallLog.Count < threshold) return false;
        var lastN = _toolCallLog.TakeLast(threshold);
              // v10.13.1: Match FAIL, ERR, EXCEPTION, BATCH FAIL, and DENIED
            return lastN.All(log => log.Contains("ERR") || log.Contains("EXCEPTION") || log.Contains("FAIL") || log.Contains("DENIED"));
                }

       /// <summary>Execute a tool call by name with args dictionary.</summary>
    private async Task<EToolResult> ExecuteTool(string toolName, Dictionary<string, string?> args)
               {
        var tool = _engine.Tools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        if (tool == null)
            throw new InvalidOperationException($"Unknown tool: {toolName}");

        if (!tool.IsEnabled)
         {
            _logger?.Info("Orchestrator", $"Tool blocked (disabled): {tool.Name}");
            return EToolResult.Failure(toolName, "[BLOCKED] Tool is disabled by configuration.");
         }
         _logger?.Debug("Orchestrator", $"Executing: {tool.Name}");
          // v10.9.3: Pass execution cancellation token to tool
        return await tool.ExecuteAsync(args, _engine.ExecutionToken);
               }

       /// <summary>v14.13: Tier-aware post-edit verification gate. Runs after a successful file-modifying tool call.</summary>
    private async Task RunPostEditVerificationAsync(string toolName, Dictionary<string, string?> args, string goal)
          {
        var verifier = _postEditVerifier;
        if (verifier == null || _verificationDisabled) return;

        var isLarge = IsLargeModelTier();
        // Large tier: verify only once per run (slim scaffolding).
        if (isLarge && _largeTierVerified) return;
        if (!verifier.ShouldVerify(toolName, args, isLarge)) return;

        // v15 fix: verification must run where the agent edits files. GetRootPath()
        // alone made the gate build the wrong directory whenever WorkingDirectory
        // differs from RootPath (MSB1003 "no project" on a valid workspace).
        var workingDir = !string.IsNullOrWhiteSpace(_config?.AgentSettings?.WorkingDirectory)
            ? _config.AgentSettings.WorkingDirectory
            : _config?.GetRootPath();
        VerificationResult result;
        try
         {
            result = await verifier.VerifyAsync(workingDir, _engine.ExecutionToken);
         }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
         {
            _logger?.Warn("Orchestrator", $"Verification runner failed: {ex.Message}");
            return; // verification infra failure must never kill the agent loop
         }
        _largeTierVerified = true;

        if (result.Succeeded)
         {
            _verificationFailRounds = 0;
            _out?.WriteSuccess("[Verify] build OK");
            // Success note only for the small tier — large tier stays slim.
            if (!isLarge)
                _engine.AddToolResult(toolName, verifier.BuildSuccessNote());
            return;
         }

        _verificationFailRounds++;
        var maxRounds = verifier.MaxRounds(isLarge);
        _out?.WriteError($"[Verify] FAILED (round {_verificationFailRounds}/{maxRounds})");
        _logger?.Warn("Orchestrator", $"Verification failed: {result.Command} exit={result.ExitCode}");
        _engine.AddToolResult(toolName, verifier.BuildFailureFeedback(_verificationFailRounds, maxRounds, result));

        if (_verificationFailRounds >= maxRounds)
         {
            // Cap reached — stop verifying for this run and make the model report.
            _verificationDisabled = true;
            _engine.InjectFormatRetry(
                "Post-edit verification failed " + maxRounds + " time(s) without a fix.\n" +
                "Do NOT keep editing blindly. Report the remaining build errors in your final answer.");
            return;
         }

        // Failure fed back via AddToolResult above — direct the model to fix it now.
        _engine.InjectFormatRetry(
            "The user's original request was: \"" + goal + "\"\n" +
            "Your last edit broke the build. Fix the [VERIFY FAIL] errors above with a file edit before doing anything else.");
          }

       /// <summary>v14.14: Summarize tool args for a playbook step line (key=value pairs, char-capped). Pure.</summary>
     internal static string SummarizeArgs(Dictionary<string, string?> args)
          {
        var summary = string.Join(", ",
            (args ?? new Dictionary<string, string?>())
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => $"{kv.Key}={kv.Value!.Trim()}"));
        summary = summary.Replace("\r", " ").Replace("\n", " ");
        return summary.Length <= 120 ? summary : summary[..120] + "…";
          }

       /// <summary>v14.14: capture a success playbook after a goal achieved with at least one successful tool call. Never kills the agent loop.
     /// v14.18: the store resolves LAZILY — the session ctor creates the orchestrator before
     /// InitializePlaybooks, so a ctor-time snapshot captured a null store forever.</summary>
     /// <summary>
    /// v15: stale-answer guard. True when the answer about to be delivered was
    /// effectively written BEFORE the last tool result arrived:
    /// (a) a tool_output is newer than the newest assistant message, (b) that tool
    /// result was a success (a failed/denied tool makes the pre-tool answer the
    /// legitimate final word), and (c) the new answer is empty or a verbatim repeat
    /// of the pre-tool narration. Fires at most once per run (cap prevents loops
    /// against a stubborn model). A genuinely fresh synthesis never trips this —
    /// its text differs from anything the model said before the tool ran.
    /// </summary>
    private bool IsStaleFinalAnswer(string? answerText)
     {
        if (_staleAnswerContinuationUsed) return false;
        try
         {
            var msgs = _engine.ContextWindow.GetWindowMessages();
            var lastToolIdx = -1;
            var lastAsstIdx = -1;
            for (var i = 0; i < msgs.Count; i++)
             {
                if (msgs[i].Role == "tool_output") lastToolIdx = i;
                else if (msgs[i].Role == "assistant") lastAsstIdx = i;
             }
            if (lastToolIdx < 0 || lastToolIdx <= lastAsstIdx) return false; // no pending tool result

            var toolContent = msgs[lastToolIdx].Content ?? "";
            // Failed/errored/denied tool → the pre-tool answer may be the honest final word.
            if (toolContent.Contains("[EXCEPTION]", StringComparison.Ordinal) ||
                toolContent.Contains("[VERIFY FAIL]", StringComparison.Ordinal) ||
                toolContent.Contains("[DENIED]", StringComparison.Ordinal) ||
                toolContent.Contains("(FAIL", StringComparison.Ordinal))
                return false;

            if (string.IsNullOrWhiteSpace(answerText)) return true; // nothing new to deliver
            var priorNarration = lastAsstIdx >= 0 ? (msgs[lastAsstIdx].Content ?? "").Trim() : "";
            if (priorNarration.Length == 0) return false;

            var candidate = answerText.Trim();
            return candidate.Equals(priorNarration, StringComparison.Ordinal) ||
                   priorNarration.Contains(candidate, StringComparison.Ordinal);
         }
        catch (Exception ex)
         {
            _logger?.Debug("Orchestrator", $"Stale-answer check failed: {ex.Message}");
            return false;
         }
     }

    private async Task TryCapturePlaybookAsync(string goal)
          {
        var store = _playbookStore ?? _engine.PlaybookStore;
        if (store == null || _successfulCalls.Count == 0) return;
        try
         {
            var candidate = _playbookExtractor.Extract(goal, _successfulCalls);
            if (candidate == null) return;
            var saved = await store.CaptureAsync(candidate);
            _out?.WriteDim($"[Playbook] {saved.Title} (x{saved.UseCount})");
            _logger?.Info("Orchestrator", $"Playbook captured: {saved.Id} (use_count={saved.UseCount})");
         }
        catch (Exception ex)
         {
            _logger?.Warn("Orchestrator", $"Playbook capture failed (non-critical): {ex.Message}");
         }
          }

       /// <summary>v10.18.1: Get tool call log for sub-agent partial results.</summary>
     public IReadOnlyList<string> GetToolCallLog() => _toolCallLog.AsReadOnly();

       /// <summary>Format tool call log for final summary output.</summary>
    private string FormatTurnLog()
                {
        if (_toolCallLog.Count == 0) return "";
        var sb = new StringBuilder();
            sb.AppendLine("--- Tool Call History ---");
        foreach (var logEntry in _toolCallLog)
            sb.AppendLine($"             - {logEntry}");
        sb.AppendLine("--- End ---");
            return sb.ToString();
                }

       /// <summary>Reset the orchestrator state.</summary>
    public void Reset()
                {
                    _turnCount = 0;
                    _runStopwatch.Restart(); // v15: time budget restarts per user request
                    _toolCallLog.Clear();
                    _formatRetries = 0;
                    // Reset failure-loop detection — stale signatures would suppress retries
                    // of legitimately-different calls on the next run.
                    _failedCallSignatures.Clear();
                    _lastSuccessfulCallSignature = null;
                    // v14.10.2: reset the repeat tracker per goal — denied/aborted calls
                    // from a previous goal must not block legitimate retries in the next.
                    _repeatTracker.Reset();
                    // v14.13: reset verification gate state per goal
                    _verificationFailRounds = 0;
                    _verificationDisabled = false;
                    _largeTierVerified = false;
                    _successfulCalls.Clear(); // v14.14: playbook capture log is per-goal
                    _maxTurns = _baseMaxTurns; // recompute on next decomposition
                    // v10.6: Reset sub-task state
                    _subTasks = null;
                    _currentSubTask = 0;
                    _executionPlan = null; // v10.17: Reset execution plan
                }

      // v10.6: Build step-aware directive that tells the LLM which sub-task to focus on.
      // This is injected after each tool result to guide the LLM through chained tasks.
    private string BuildStepDirective()
      {
        // v14.12: large models get a one-line directive — the full checklist +
        // mechanical progress tracker is scaffolding that hinders them.
        if (IsLargeModelTier())
        {
            return "Tool result above. If it fully answers the user's request, give your final answer now. " +
                   "Otherwise call the next tool with different arguments if you need different data.";
        }

        var sb = new StringBuilder();

        sb.AppendLine("The tool has returned its result above. Now respond to the user.");
        sb.AppendLine("If you have enough information, provide your final answer. If you need more data, call a tool.");
        sb.AppendLine("IMPORTANT: Check [TASK PROGRESS] below. If you completed multiple steps in a single tool call (e.g. batch shell command), the progress tracker may only show one as completed. Check the tool output above — if you covered all remaining steps, finish with your final answer. If steps genuinely remain, call another tool.");
        sb.AppendLine("Do NOT retry steps that already succeeded — check the tool output above to see what was already done.");

          // v10.6: If we have sub-tasks, inject step context
        if (_subTasks != null && _subTasks.Count > 1)
          {
            sb.AppendLine();
            sb.AppendLine($"[TASK PROGRESS] You are on step {_currentSubTask + 1} of {_subTasks.Count}:");

            for (int i = 0; i < _subTasks.Count; i++)
              {
                var status = _subTasks[i].Status switch
                  {
                    SubTaskStatus.Completed => "[OK]",
                    SubTaskStatus.Failed => "[FAIL]",
                    SubTaskStatus.InProgress => "[...]",
                      _ => "[ ]"
                  };
                var marker = i == _currentSubTask ? " >> " : "      ";
                  // v10.7.4: Escape angle brackets in step descriptions to prevent fake XML tags
            var safeDesc = _subTasks[i].Description.Replace("<", "&lt;").Replace(">", "&gt;");
            sb.AppendLine($"{marker}{status} {safeDesc}");
              }

              // Give explicit instruction for the current step
            if (_currentSubTask < _subTasks.Count)
              {
                var current = _subTasks[_currentSubTask];
                if (current.Status == SubTaskStatus.Pending || current.Status == SubTaskStatus.InProgress)
                  {
                    sb.AppendLine();
                    var safeCurrent = _subTasks[_currentSubTask].Description.Replace("<", "&lt;").Replace(">", "&gt;");
                    sb.AppendLine($"> CURRENT STEP: {safeCurrent}");
                    sb.AppendLine("Focus on completing THIS step. If the previous tool result gives you what you need, proceed to this step.");
                  }
              }

              // Check if all steps are done
            var allDone = _subTasks.All(s => s.Status == SubTaskStatus.Completed || s.Status == SubTaskStatus.Failed);
            var anyFailed = _subTasks.Any(s => s.Status == SubTaskStatus.Failed);
            if (allDone)
              {
                sb.AppendLine();
                if (anyFailed)
                  {
                    sb.AppendLine("All steps have been attempted. Some FAILED. Check the tool results above.");
                    sb.AppendLine("If you can fix the failed steps, call a tool. If not, provide your final answer reporting what happened.");
                  }
                else
                  {
                    sb.AppendLine("All steps are complete! Provide your final answer summarizing what was done.");
                  }
              }
          }

        return sb.ToString();
      }

      // v10.6: Advance sub-task tracking based on tool result
     // v10.22: Post-hoc effect matching — check actual tool effects against remaining sub-tasks
    private void AdvanceSubTask(bool success, string toolName, string description)
      {
        if (_subTasks == null || _subTasks.Count <= 1) return;
        if (_currentSubTask >= _subTasks.Count) return;

        var current = _subTasks[_currentSubTask];
        if (success)
          {
            current.Status = SubTaskStatus.Completed;
            current.CompletedAt = DateTime.UtcNow;
             _out?.WriteSuccess($"Step {_currentSubTask + 1}/{_subTasks.Count} completed: {current.Description}");
              _currentSubTask++;

              // v10.22: Post-hoc effect matching — check if subsequent sub-tasks were also completed
              // by this single tool call (e.g., one shell command created 3 files covering 3 steps)
             MatchEffectsToSubTasks(toolName, description);

              // Mark next sub-task as in-progress
            if (_currentSubTask < _subTasks.Count)
              {
                  _subTasks[_currentSubTask].Status = SubTaskStatus.InProgress;
                 _out?.WriteInfo($"-> Next step: {_subTasks[_currentSubTask].Description}");
              }
          }
        else
          {
            current.Status = SubTaskStatus.Failed;
            current.FailureReason = $"Tool {toolName} failed";
             _out?.WriteError($"Step {_currentSubTask + 1}/{_subTasks.Count} failed: {current.Description}");
              _currentSubTask++;

            if (_currentSubTask < _subTasks.Count)
              {
                  _subTasks[_currentSubTask].Status = SubTaskStatus.InProgress;
                 _out?.WriteWarning($"-> Skipping to next step: {_subTasks[_currentSubTask].Description}");
              }
          }
      }

     /// <summary>
     /// v10.22: Post-hoc effect matching — check if a completed tool call's actual effects
     /// also satisfy subsequent pending sub-tasks. For example, if one shell command creates
     /// 3 files and the plan had 3 steps for creating each file, this detects that all 3 are done.
     ///
     /// Uses keyword matching between the tool description/output and sub-task descriptions.
     /// Conservative: only advances if there's a clear match (keyword overlap > 60%).
     /// </summary>
    private void MatchEffectsToSubTasks(string toolName, string toolDescription)
      {
        if (_subTasks == null || _currentSubTask >= _subTasks.Count) return;

        var descWords = toolDescription.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries)
             .Where(w => w.Length > 2)
             .ToHashSet();

         // Also get the last tool output from context for richer matching
        var windowMsgs = _engine.ContextWindow.GetWindowMessages();
        var lastTool = windowMsgs.LastOrDefault(m => m.Role == "tool_output");
        if (lastTool != null)
         {
            var outputWords = lastTool.Content.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                 .Where(w => w.Length > 2);
            foreach (var w in outputWords) descWords.Add(w);
         }

        int matched = 0;
        while (_currentSubTask < _subTasks.Count)
          {
            var task = _subTasks[_currentSubTask];
            if (task.Status != SubTaskStatus.Pending && task.Status != SubTaskStatus.InProgress) break;

            var taskWords = task.Description.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                 .Where(w => w.Length > 2)
                 .ToList();
            if (taskWords.Count == 0) break;

            var overlap = taskWords.Count(w => descWords.Contains(w));
            var matchRatio = (double)overlap / taskWords.Count;

            if (matchRatio >= 0.6)
             {
                task.Status = SubTaskStatus.Completed;
                task.CompletedAt = DateTime.UtcNow;
                 _out?.WriteSuccess($"Step {_currentSubTask + 1}/{_subTasks.Count} auto-detected as done: {task.Description} (effect match {matchRatio:P0})");
                 _currentSubTask++;
                matched++;
             }
            else break;
          }

        if (matched > 0)
             _out?.WriteInfo($"Post-hoc matching: {matched} additional sub-task(s) completed by effect overlap.");
      }

      // ─── v15: Handoff ──────────────────────

    /// <summary>
    /// Build a HandoffRequest from the model's tool-call arguments and run
    /// the specialist executor. The specialist's result becomes the parent
    /// orchestrator's final result — the parent loop stops.
    /// </summary>
    private async Task<OrchestratorResult> ExecuteHandoffAsync(Dictionary<string, string?> args, string originalGoal)
    {
        if (_handoffExecutor == null)
        {
            _out?.WriteError("[Handoff] Executor not initialized — cannot hand off.");
            return new OrchestratorResult
            {
                FinalOutput = "[Handoff] Executor not initialized.",
                Status = OrchestratorStatus.Failed
            };
        }

        var prompt = args.GetValueOrDefault("prompt")?.Trim();
        if (string.IsNullOrEmpty(prompt))
        {
            _out?.WriteError("[Handoff] Model called EHandoff without a prompt.");
            return new OrchestratorResult
            {
                FinalOutput = "[Handoff] No specialist prompt provided.",
                Status = OrchestratorStatus.Failed
            };
        }

        // Build context summary: if the model provided one, use it; otherwise
        // synthesize from the original goal + completed steps.
        var contextSummary = args.GetValueOrDefault("context") ?? "";
        if (string.IsNullOrEmpty(contextSummary) && _completedSteps.Count > 0)
        {
            contextSummary = $"Original task: {originalGoal}\nSteps completed so far:\n" +
                             string.Join("\n", _completedSteps.TakeLast(5));
        }
        else if (string.IsNullOrEmpty(contextSummary))
        {
            contextSummary = $"Original task: {originalGoal}";
        }

        var request = new HandoffRequest
        {
            SystemPrompt = prompt,
            AllowedTools = args.GetValueOrDefault("tools") ?? "",
            Reason = args.GetValueOrDefault("reason") ?? "",
            ContextSummary = contextSummary,
            Name = args.GetValueOrDefault("name") ?? "specialist",
            ModelOverride = args.GetValueOrDefault("model_override"),
        };

        if (int.TryParse(args.GetValueOrDefault("max_turns"), out var mt))
            request = request with { MaxTurns = mt };
        if (int.TryParse(args.GetValueOrDefault("timeout"), out var ts))
            request = request with { TimeoutSeconds = ts };

        try
        {
            _logger?.Info("Handoff", $"Handing off to specialist '{request.Name}' (prompt {request.SystemPrompt.Length} chars, tools: {request.AllowedTools})");
            var result = await _handoffExecutor.RunAsync(request, _engine.ExecutionToken);

            // The specialist's result IS the final answer.
            _out?.WriteLine($"[Handoff] Specialist returned: {result.Status}");
            return result;
        }
        catch (Exception ex)
        {
            _logger?.Error("Handoff", $"Specialist failed: {ex.Message}");
            return new OrchestratorResult
            {
                FinalOutput = $"[Handoff] Specialist failed: {ex.Message}",
                Status = OrchestratorStatus.Failed
            };
        }
    }

    public async ValueTask DisposeAsync()
      {
        await DisposeRunShellSessionsAsync();
        try { _subAgentManager?.Dispose(); } catch (Exception ex) { _logger?.Debug("Orchestrator", $"Non-critical error ignored: {ex.Message}"); }
        try { if (_handoffExecutor != null) await _handoffExecutor.DisposeAsync(); } catch (Exception ex) { _logger?.Debug("Orchestrator", $"Non-critical error ignored: {ex.Message}"); }
        await Task.CompletedTask;
      }
}
