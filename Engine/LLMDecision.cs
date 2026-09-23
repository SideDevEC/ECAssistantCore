using ECAssistant.Core.Engine;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Orchestration;

/// <summary>LLM's structured decision about what to do next.
/// v10.13: Supports multiple tool calls for parallel execution.</summary>
public class LLMDecision
{
            // Tool call fields (v10.13: multiple calls)
    public bool WantsToolCall { get; }
    public List<ToolCallRequest> ToolCalls { get; }

            // Legacy single-call accessors (backwards compat)
    public string? ToolName => ToolCalls.FirstOrDefault()?.ToolName;
    public Dictionary<string, string?> Args => ToolCalls.FirstOrDefault()?.Args ?? new Dictionary<string, string?>();

            // Direct answer field
    public bool WantsDirectAnswer { get; }
    public string? AnswerText { get; }

            // v14.5: Reasoning from the model. Stored in transcript so the model can
            // learn from prior reasoning on later turns (matches old tag system behavior).
    public string? Reasoning { get; }

      /// <summary>v14.12.2: optional plain-text note the remote model wrote alongside its
    /// tool calls (native protocol allows text + calls). Display-only; null on the local
    /// grammar path (envelope stays strict answer-XOR-toolcalls there).</summary>
    public string? Commentary { get; }

      /// <summary>Single tool call (backwards compat).</summary>
    public LLMDecision(
        bool wantsToolCall,
        string? toolName,
        Dictionary<string, string?> args,
        string? answerText = null,
        string? reasoning = null)
                {
        WantsToolCall = wantsToolCall;
        ToolCalls = wantsToolCall && toolName != null
              ? new List<ToolCallRequest> { new() { ToolName = toolName, Args = args ?? new Dictionary<string, string?>(), Index = 1 } }
              : new List<ToolCallRequest>();
        WantsDirectAnswer = answerText != null;
            AnswerText = answerText;
            Reasoning = reasoning;
                }

      /// <summary>Multiple tool calls (v10.13).</summary>
    public LLMDecision(List<ToolCallRequest> toolCalls, string? reasoning = null, string? commentary = null)
      {
        WantsToolCall = toolCalls.Count > 0;
        ToolCalls = toolCalls;
        WantsDirectAnswer = false;
        AnswerText = null;
        Reasoning = reasoning;
        Commentary = commentary;
      }

      /// <summary>Number of tool calls in this decision.</summary>
    public int ToolCallCount => ToolCalls.Count;

      /// <summary>
    /// v14: Build a decision from parsed DecisionEnvelope fields (server JSON shape:
    /// {"thinking", "answer"|"toolcalls":[{name,args}]}). Core cannot reference the
    /// LLM project's DecisionToolCall type, so tool calls arrive as pre-parsed
    /// (name, args) pairs.
    /// </summary>
    public static LLMDecision FromEnvelope(
        string thinking,
        string? answer,
        List<(string Name, Dictionary<string, string> Args)>? toolCalls,
        string? commentary = null)
     {
        if (!string.IsNullOrEmpty(answer))
            return new LLMDecision(false, null, new Dictionary<string, string?>(), answer, thinking);

        if (toolCalls is { Count: > 0 })
         {
            var requests = toolCalls.Select((tc, i) => new ToolCallRequest
             {
                ToolName = tc.Name,
                Args = tc.Args.ToDictionary(kv => kv.Key, kv => (string?)kv.Value),
                Index = i + 1
             }).ToList();
            return new LLMDecision(requests, thinking, commentary);
         }

        // Neither answer nor toolcalls — v14.18: do NOT surface the thinking text
        // as the answer (live qwen3.5-4b E2E showed thinking-only envelopes getting
        // delivered to the user as final output, bypassing the format-retry that
        // gives the model a second chance). Return a null-answer decision — the
        // orchestrator's format-retry path removes the empty turn and nudges the
        // model; thinking stays in Reasoning for the post-retry best-effort path.
        return new LLMDecision(false, null, new Dictionary<string, string?>(), null, thinking);
     }

      /// <summary>Is this a multi-call (parallel) decision?</summary>
    public bool IsMultiCall => ToolCalls.Count > 1;
}
