using System.Diagnostics;
using System.Text;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Tools;
using ECAssistant.Core.Session;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Executes dependency-ordered tool call groups in parallel.
///
/// - Independent toolcalls within a group run via Task.WhenAll()
/// - Groups execute sequentially (group N waits for group N-1)
/// - All results are combined into a single output block
/// - Failures in one tool don't block other parallel tools
/// - Per-tool policy checks and approval gates
/// </summary>
public class ParallelToolExecutor : IParallelToolExecutor
{
    private readonly EAgentEngine _engine;
    private readonly ECAssistant.Core.Tools.ToolPolicy _toolPolicy;
    private readonly Func<string, Dictionary<string, string?>, Task<EToolResult>> _executeToolFn;
    private readonly Action<string> _log;
    private readonly ISessionOutput? _out;

    /// <summary>
    /// Create the parallel executor.
    /// </summary>
    /// <param name="engine">Engine for tool lookup</param>
    /// <param name="toolPolicy">Policy checker for approvals</param>
    /// <param name="executeToolFn">Function that executes a single tool by name+args</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="sessionOutput">Optional session output for approval requests</param>
    public ParallelToolExecutor(
        EAgentEngine engine,
        ECAssistant.Core.Tools.ToolPolicy toolPolicy,
        Func<string, Dictionary<string, string?>, Task<EToolResult>> executeToolFn,
        Action<string>? log = null,
        ISessionOutput? sessionOutput = null)
    {
        _engine = engine;
        _toolPolicy = toolPolicy;
        _executeToolFn = executeToolFn;
        _log = log ?? (_ => { });
        _out = sessionOutput;
    }

    /// <summary>
    /// Execute a batch of tool calls with dependency-aware parallelism.
    /// Returns a combined BatchToolResult with all individual results.
    /// </summary>
    public async Task<BatchToolResult> ExecuteAsync(List<ToolCallRequest> toolCalls, CancellationToken ct = default)
    {
        // v14.20: dataflow chains — {{N}} references in args force sequential
        // execution in call order, each later call receiving earlier outputs.
        // Approvals still apply per call (ExecuteSingleWithPolicy).
        if (ToolCallChainSubstitution.AnyCallHasReferences(toolCalls))
            return await ExecuteSequentialChain(toolCalls, ct);

        // Analyze dependencies
        var analyzer = new ToolDependencyAnalyzer(); var groups = analyzer.Analyze(toolCalls);
        var allResults = new List<SingleToolResult>();

        if (groups.Count == 1 && groups[0].ToolCalls.Count == 1)
        {
            // Single tool — no parallelism overhead
            _log($"[Parallel] Single tool call — executing directly: {groups[0].ToolCalls[0]}");
            var tc = groups[0].ToolCalls[0];
            var result = await ExecuteSingleWithPolicy(tc, ct);
            allResults.Add(result);
        }
        else
        {
            // Multiple groups or parallel group
            _log($"[Parallel] Analyzed {toolCalls.Count} tool calls → {groups.Count} dependency group(s):");
            foreach (var g in groups)
                _log($"  {g}");

            for (int gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];

                if (ct.IsCancellationRequested)
                {
                    _log($"[Parallel] Cancelled before group {gi}.");
                    break;
                }

                if (group.IsParallel)
                {
                    // v10.13.1: Pre-check approvals sequentially before launching parallel tasks
                    // to avoid concurrent PromptRaw calls racing on the same console.
                    var approved = new List<ToolCallRequest>();
                    var denied = new List<ToolCallRequest>();
                    foreach (var tc in group.ToolCalls)
                    {
                        if (string.IsNullOrEmpty(tc.ToolName))
                        {
                            denied.Add(tc);
                            continue;
                        }
                        var policy = _toolPolicy.Check(tc.ToolName!, tc.Args);
                        if (policy.NeedsApproval)
                        {
                            _log($"[Policy] {tc}: {policy.Message}");
                            var scope = _out?.RequestApprovalScoped($"[Policy] Approve {tc.ToolName}#{tc.Index} ({string.Join(", ", tc.Args.Select(kvp => kvp.Key + "=" + StringUtil.Default.Truncate(kvp.Value ?? "", 60)))})?") ?? Session.ApprovalScope.Deny;
                            // v14.10.2: remember-decision — 'a' approves this tool+pattern for the session.
                            if (scope == Session.ApprovalScope.AllowSession)
                                _toolPolicy.ApproveSessionPattern(tc.ToolName!, Tools.ToolPolicy.BuildSessionPattern(tc.ToolName!, tc.Args));
                            if (scope != Session.ApprovalScope.Deny)
                            {
                                _log($"[Policy] Approved{(scope == Session.ApprovalScope.AllowSession ? " (session)" : "")}: {tc}");
                                approved.Add(tc);
                            }
                            else
                            {
                                _log($"[Policy] DENIED by user: {tc}");
                                denied.Add(tc);
                            }
                        }
                        else if (!policy.CanExecute)
                        {
                            _log($"[Policy] BLOCKED: {tc}: {policy.Message}");
                            denied.Add(tc);
                        }
                        else
                        {
                            approved.Add(tc);
                        }
                    }

                    // Add denied/blocked results immediately
                    foreach (var tc in denied)
                    {
                        allResults.Add(new SingleToolResult
                        {
                            ToolCall = tc,
                            Succeeded = false,
                            Output = "",
                            Error = "[DENIED] User did not approve this tool execution.",
                            ElapsedMs = 0
                        });
                    }

                    // Execute approved tasks in parallel
                    if (approved.Count > 0)
                    {
                        if (approved.Count == 1)
                        {
                            _log($"[Parallel] Group {gi}: 1 call approved, executing...");
                            allResults.Add(await ExecuteSingleNoPolicy(approved[0], ct));
                        }
                        else
                        {
                            _log($"[Parallel] Group {gi}: executing {approved.Count} calls in parallel...");
                            var tasks = approved.Select(tc => ExecuteSingleNoPolicy(tc, ct)).ToArray();
                            var results = await Task.WhenAll(tasks);
                            allResults.AddRange(results);
                        }
                    }
                }
                else
                {
                    _log($"[Parallel] Group {gi}: executing 1 call...");
                    var result = await ExecuteSingleWithPolicy(group.ToolCalls[0], ct);
                    allResults.Add(result);
                }
            }
        }

        return new BatchToolResult { Results = allResults, Groups = groups };
    }

    /// <summary>
    /// Execute a single tool call WITHOUT policy check (already pre-approved).
    /// Used inside Task.WhenAll for parallel execution after pre-approval.
    /// </summary>
    private async Task<SingleToolResult> ExecuteSingleNoPolicy(ToolCallRequest tc, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrEmpty(tc.ToolName))
            {
                return new SingleToolResult
                {
                    ToolCall = tc,
                    Succeeded = false,
                    Output = "",
                    Error = "Tool name was empty",
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }

            if (ct.IsCancellationRequested)
            {
                return new SingleToolResult
                {
                    ToolCall = tc,
                    Succeeded = false,
                    Output = "",
                    Error = "Cancelled before execution",
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }

            var result = await _executeToolFn(tc.ToolName!, tc.Args);
            sw.Stop();

            return new SingleToolResult
            {
                ToolCall = tc,
                Succeeded = result.Succeeded,
                Output = result.Succeeded ? result.Output : "",
                Error = result.Succeeded ? "" : result.Error,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log($"[Parallel] Exception in {tc}: {ex.Message}");
            return new SingleToolResult
            {
                ToolCall = tc,
                Succeeded = false,
                Output = "",
                Error = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Execute a single tool call with policy check and approval gate.
    /// Used for single-tool path and sequential groups.
    /// Returns a SingleToolResult with success/failure info.
    /// </summary>
    private async Task<SingleToolResult> ExecuteSingleWithPolicy(ToolCallRequest tc, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrEmpty(tc.ToolName))
            {
                return new SingleToolResult
                {
                    ToolCall = tc,
                    Succeeded = false,
                    Output = "",
                    Error = "Tool name was empty",
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }

            // Policy check
            var policyDecision = _toolPolicy.Check(tc.ToolName!, tc.Args);
            if (policyDecision.NeedsApproval)
            {
                _log($"[Policy] {tc}: {policyDecision.Message}");
                var isApproved = _out?.RequestApproval($"[Policy] Approve {tc.ToolName}#{tc.Index} ({string.Join(", ", tc.Args.Select(kvp => kvp.Key + "=" + StringUtil.Default.Truncate(kvp.Value ?? "", 60)))})?") ?? false;

                if (!isApproved)
                {
                    _log($"[Policy] DENIED by user: {tc}");
                    return new SingleToolResult
                    {
                        ToolCall = tc,
                        Succeeded = false,
                        Output = "",
                        Error = "[DENIED] User did not approve this tool execution.",
                        ElapsedMs = sw.ElapsedMilliseconds
                    };
                }
                _log($"[Policy] Approved: {tc}");
            }
            else if (!policyDecision.CanExecute)
            {
                _log($"[Policy] BLOCKED: {tc}: {policyDecision.Message}");
                return new SingleToolResult
                {
                    ToolCall = tc,
                    Succeeded = false,
                    Output = "",
                    Error = $"[BLOCKED] {policyDecision.Message}",
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }

            if (ct.IsCancellationRequested)
            {
                return new SingleToolResult
                {
                    ToolCall = tc,
                    Succeeded = false,
                    Output = "",
                    Error = "Cancelled before execution",
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }

            // Execute
            var result = await _executeToolFn(tc.ToolName!, tc.Args);
            sw.Stop();

            return new SingleToolResult
            {
                ToolCall = tc,
                Succeeded = result.Succeeded,
                Output = result.Succeeded ? result.Output : "",
                Error = result.Succeeded ? "" : result.Error,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log($"[Parallel] Exception in {tc}: {ex.Message}");
            return new SingleToolResult
            {
                ToolCall = tc,
                Succeeded = false,
                Output = "",
                Error = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// v14.20: dataflow chain execution — calls run strictly in decision order
    /// and each call's args have {{N}} tokens substituted with call N's output
    /// before execution. Failed calls yield null (later references see the
    /// explicit unavailable marker from the substitution utility).
    /// Policy/approval gates run per call, exactly like the normal paths.
    /// </summary>
    private async Task<BatchToolResult> ExecuteSequentialChain(List<ToolCallRequest> toolCalls, CancellationToken ct)
    {
        _log($"[Chain] Dataflow references detected — executing {toolCalls.Count} call(s) sequentially.");
        var allResults = new List<SingleToolResult>();
        var priorOutputs = new List<string?>();

        foreach (var tc in toolCalls)
        {
            if (ct.IsCancellationRequested)
            {
                _log("[Chain] Cancelled before remaining calls.");
                break;
            }

            var effectiveArgs = ToolCallChainSubstitution.Substitute(tc.Args, priorOutputs);
            var effective = new ToolCallRequest { ToolName = tc.ToolName, Args = effectiveArgs, Index = tc.Index };
            var result = await ExecuteSingleWithPolicy(effective, ct);
            priorOutputs.Add(result.Succeeded ? result.Output : null);
            allResults.Add(result);
        }

        return new BatchToolResult
        {
            Results = allResults,
            Groups = new List<DependencyGroup> { new() { ToolCalls = toolCalls, GroupIndex = 0 } }
        };
    }

    /// <summary>
    /// Combine all results from a batch into a single output string for the LLM.
    /// Format:
    /// <tooloutput>Batch<result>
    /// [Tool 1: ToolName] Output: ...
    /// [Tool 2: ToolName] Output: ...
    /// </result></tooloutput>
    /// </summary>
    public string CombineResults(BatchToolResult batch)
    {
        if (batch.Results.Count == 1)
        {
            var r = batch.Results[0];
            if (r.Succeeded)
                return _engine?.RenderOutput(r.ToolCall.ToolName ?? "", r.Output) ?? r.Output;
            return $"[FAILED] {r.ToolCall.ToolName}: {r.Error}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[BATCH: {batch.Results.Count} tool calls executed in {batch.Groups.Count} group(s)]");

        foreach (var r in batch.Results)
        {
            sb.AppendLine();
            sb.AppendLine($"--- Tool {r.ToolCall.Index}: {r.ToolCall.ToolName} ({(r.Succeeded ? "OK" : "FAIL")}, {r.ElapsedMs}ms) ---");

            if (r.Succeeded)
                sb.AppendLine(_engine?.RenderOutput(r.ToolCall.ToolName ?? "", r.Output) ?? r.Output);
            else
                sb.AppendLine($"ERROR: {r.Error}");
        }

        sb.AppendLine();
        sb.AppendLine($"[END BATCH — {batch.Results.Count(r => r.Succeeded)}/{batch.Results.Count} succeeded]");
        return sb.ToString();
    }

    /// <summary>
    /// Format a short summary for console display (not for LLM).
    /// </summary>
    public string FormatConsoleSummary(BatchToolResult batch)
    {
        if (batch.Results.Count == 1)
        {
            var r = batch.Results[0];
            return $"{r.ToolCall.ToolName}: {(r.Succeeded ? "OK" : "FAIL")} ({r.ElapsedMs}ms)";
        }

        var parts = batch.Results.Select(r => $"{r.ToolCall.ToolName}#{r.ToolCall.Index}:{(r.Succeeded ? "OK" : "FAIL")}");
        var ok = batch.Results.Count(r => r.Succeeded);
        return $"[{ok}/{batch.Results.Count} OK] {string.Join(" | ", parts)} ({batch.Groups.Count} groups)";
    }
}
