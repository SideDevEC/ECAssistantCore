using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Session;

namespace ECAssistant.Core.Engine;

// ── Mock engine for testing ──────────────────────────────────

/// <summary>
/// Mock engine for testing — no real model loaded. Returns pre-queued responses.
/// Uses no-op HTTP transport so tests don't require a running ECAssistantLLM server.
/// </summary>
public class MockEngine : EAgentEngine
{
    private readonly Queue<string> _responses = new();
    private readonly Queue<LLMDecision> _decisions = new();
    private readonly bool _stopAfterFirstTool;
    private readonly bool _cycleResponses;
    private readonly ISessionOutput? _mockOut;
    private readonly List<string> _allResponses = new();
    private string? _defaultResponse;

    public List<(string toolName, string output)> ToolResults { get; } = new();
    public int GenerateCallCount { get; private set; }
    public IReadOnlyList<string> ConsumedResponses => _allResponses;
    public Action<string>? OnResponseConsumed { get; set; }
    public Action<string>? OnGenerateCalled { get; set; }

    public MockEngine(Queue<string> responses, int maxIterations = 5,
        bool stopAfterFirstTool = false, string? workingDir = null, ISessionOutput? sessionOutput = null)
        : base("mock-" + Guid.NewGuid().ToString("N")[..8],
            InferenceEngineNoop.Instance, KvCacheNoop.Instance,
            tokenizer: null,
            inferenceParams: new InferenceRequestParams(),
            contextSize: 8192,
            modelPath: "mock",
            workingDir: workingDir)
    {
        _responses = responses;
        SetMockMode(true);
        _stopAfterFirstTool = stopAfterFirstTool;
        _mockOut = sessionOutput;
        SetMaxIterations(maxIterations);
        SetSessionOutput(sessionOutput);
    }

    public MockEngine(string? workingDir = null, ISessionOutput? sessionOutput = null, bool cycleResponses = false)
        : base("mock-" + Guid.NewGuid().ToString("N")[..8],
            InferenceEngineNoop.Instance, KvCacheNoop.Instance,
            tokenizer: null,
            inferenceParams: new InferenceRequestParams(),
            contextSize: 8192,
            modelPath: "mock",
            workingDir: workingDir)
    {
        SetMockMode(true);
        _stopAfterFirstTool = false;
        _cycleResponses = cycleResponses;
        _mockOut = sessionOutput;
        SetMaxIterations(5);
        SetSessionOutput(sessionOutput);
    }

    public void EnqueueResponse(string response) => _responses.Enqueue(response);
    public void AddResponse(string response) => _responses.Enqueue(response);
    public void AddResponses(params string[] responses) { foreach (var r in responses) _responses.Enqueue(r); }

    // ── v14: structured decision helpers (native JSON pipeline) ──

    /// <summary>Queue a direct-answer LLMDecision.</summary>
    public void EnqueueDirectAnswer(string answer)
        => _responses.Enqueue($"__DIRECT__:{answer}");

    /// <summary>Queue a single tool-call LLMDecision.</summary>
    public void EnqueueToolCall(string toolName, Dictionary<string, string?> args)
        => _responses.Enqueue($"__TOOLCALL__:{toolName}:{System.Text.Json.JsonSerializer.Serialize(args)}");

    /// <summary>Queue an LLMDecision with multiple tool calls (parallel execution).</summary>
    public void EnqueueMultiToolCall(params (string Name, Dictionary<string, string?> Args)[] calls)
        => _responses.Enqueue($"__MULTITOOL__:{System.Text.Json.JsonSerializer.Serialize(calls.Select(c => new { c.Name, c.Args }))}");

    /// <summary>Queue a pre-built LLMDecision directly.</summary>
    public void EnqueueDecision(LLMDecision decision) => _decisions.Enqueue(decision);
    public void SetDefaultResponse(string response) => _defaultResponse = response;

    /// <summary>v14: set the default response to a tool-call decision (for never-ending loops).</summary>
    public void SetDefaultToolCall(string toolName, Dictionary<string, string?> args)
        => _defaultResponse = $"__TOOLCALL__:{toolName}:{System.Text.Json.JsonSerializer.Serialize(args)}";
    public void ClearResponses() { _responses.Clear(); _allResponses.Clear(); GenerateCallCount = 0; }
    public int QueuedCount => _responses.Count;

    public override void AddToolResult(string toolName, string output)
    {
        ToolResults.Add((toolName, output));
        base.AddToolResult(toolName, output);
    }

    public override Task PrefillStaticPrefix()
    {
        _mockOut?.WriteInfo("[MockEngine] PrefillStaticPrefix (no-op)");
        return Task.CompletedTask;
    }

    public override Task ResetAndRebuildCacheAsync()
    {
        _mockOut?.WriteInfo("[MockEngine] ResetAndRebuildCacheAsync (no-op)");
        return Task.CompletedTask;
    }

    public override Task RebuildCacheAfterStopAsync()
    {
        _mockOut?.WriteInfo("[MockEngine] RebuildCacheAfterStopAsync (no-op)");
        return Task.CompletedTask;
    }

    public override Task RemoveLastAssistantResponseAsync()
    {
        _contextWindow.RemoveLastAssistantMessage();
        _mockOut?.WriteInfo("[MockEngine] RemoveLastAssistantResponseAsync (context window only)");
        return Task.CompletedTask;
    }

    public override async Task<LLMDecision> GenerateAsync(string userPrompt)
    {
        _lifecycle.TurnCount = 0;
        GenerateCallCount++;
        OnGenerateCalled?.Invoke(userPrompt);

        LLMDecision decision;
        if (_decisions.Count > 0)
        {
            decision = _decisions.Dequeue();
            if (_cycleResponses) _decisions.Enqueue(decision);
        }
        else
        {
            string response;
            if (_responses.Count > 0)
            {
                response = _responses.Dequeue();
                if (_cycleResponses) _responses.Enqueue(response);
            }
            else if (_defaultResponse != null)
                response = _defaultResponse;
            else
                response = "(No more queued responses)";

            _allResponses.Add(response);
            OnResponseConsumed?.Invoke(response);

            _mockOut?.WriteInfo($"[MockEngine] Returning queued response ({response.Length} chars)");

            if (response.StartsWith("<assistant>", StringComparison.OrdinalIgnoreCase))
                response = response.Substring("<assistant>".Length).Trim();

            const int maxResponseLength = 2000;
            if (response.Length > maxResponseLength)
            {
                response = response.Substring(0, maxResponseLength) + "\n[response truncated for testing]";
                _mockOut?.WriteWarning($"[MockEngine] Response truncated to {maxResponseLength} chars");
            }

            // v14: structured prefixes build typed decisions; plain strings are direct answers.
            if (response.StartsWith("__DIRECT__:", StringComparison.Ordinal))
            {
                decision = new LLMDecision(false, null, new Dictionary<string, string?>(), response.Substring("__DIRECT__:".Length));
            }
            else if (response.StartsWith("__TOOLCALL__:", StringComparison.Ordinal))
            {
                var rest = response.Substring("__TOOLCALL__:".Length);
                var sep = rest.IndexOf(':');
                var toolName = sep >= 0 ? rest[..sep] : rest;
                var args = new Dictionary<string, string?>();
                if (sep >= 0 && sep + 1 < rest.Length)
                {
                    using var argsDoc = System.Text.Json.JsonDocument.Parse(rest[(sep + 1)..]);
                    foreach (var p in argsDoc.RootElement.EnumerateObject())
                        args[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                            ? p.Value.GetString()
                            : p.Value.GetRawText();
                }
                decision = new LLMDecision(true, toolName, args);
            }
            else if (response.StartsWith("__MULTITOOL__:", StringComparison.Ordinal))
            {
                var requests = new List<ToolCallRequest>();
                using (var doc = System.Text.Json.JsonDocument.Parse(response.Substring("__MULTITOOL__:".Length)))
                {
                    var i = 0;
                    foreach (var c in doc.RootElement.EnumerateArray())
                    {
                        i++;
                        var callArgs = new Dictionary<string, string?>();
                        if (c.TryGetProperty("Args", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Object)
                            foreach (var p in a.EnumerateObject())
                                callArgs[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                                    ? p.Value.GetString()
                                    : p.Value.GetRawText();
                        requests.Add(new ToolCallRequest
                        {
                            ToolName = c.GetProperty("Name").GetString() ?? "",
                            Args = callArgs,
                            Index = i
                        });
                    }
                }
                decision = new LLMDecision(requests);
            }
            else
            {
                // Backwards compat: plain string = direct answer.
                decision = new LLMDecision(false, null, new Dictionary<string, string?>(), response);
            }
        }

        var decisionText = decision.AnswerText
            ?? string.Join("\n", decision.ToolCalls.Select(tc => $"[toolcall {tc.ToolName}]"));
        _transcript.AddAssistant(decisionText);
        _contextWindow.AddAssistantMessage(decisionText);

        if (_stopAfterFirstTool)
        {
            foreach (var t in Tools)
            {
                if (t.Name.Equals("eshellagent", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals("ecodeeditor", StringComparison.OrdinalIgnoreCase))
                {
                    _lifecycle.EscPressed = true;
                    StopExecution();
                    break;
                }
            }
        }

        await Task.CompletedTask;
        // v14: return the queued response as an LLMDecision (direct answer by default).
        return decision;
    }

    protected override SubAgentManager CreateSubAgentManager()
        => new(this, _workingDir, _logger, _mockOut, _config, _processRunner, _fileSystem, _httpClient);

    /// <summary>No-op inference engine for mock mode.</summary>
    internal sealed class InferenceEngineNoop : IInferenceEngine
    {
        public static readonly InferenceEngineNoop Instance = new();
        public string Endpoint => "mock";
        public Task<string> GenerateAsync(string prompt, InferenceRequestParams parameters, CancellationToken ct = default)
            => Task.FromResult("");
        public IAsyncEnumerable<string> StreamAsync(string prompt, InferenceRequestParams parameters, CancellationToken ct = default)
        {
            return StreamNoop(ct);
        }

        private static async IAsyncEnumerable<string> StreamNoop([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>No-op KV cache controller for mock mode.</summary>
    internal sealed class KvCacheNoop : IKvCacheController
    {
        public static readonly KvCacheNoop Instance = new();
        public Task<bool> CreateSessionAsync(string sessionId, string? modelId = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DestroySessionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PrefillAsync(string sessionId, string text, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SaveStateAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RewindAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ResetAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<KvCacheStatus?> GetStatusAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<KvCacheStatus?>(null);
    }
}
