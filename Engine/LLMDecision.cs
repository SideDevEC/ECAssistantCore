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
    public LLMDecision(List<ToolCallRequest> toolCalls, string? reasoning = null)
      {
        WantsToolCall = toolCalls.Count > 0;
        ToolCalls = toolCalls;
        WantsDirectAnswer = false;
        AnswerText = null;
        Reasoning = reasoning;
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
        List<(string Name, Dictionary<string, string> Args)>? toolCalls)
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
            return new LLMDecision(requests, thinking);
         }

        // Neither answer nor toolcalls — fall back to thinking text as the answer.
        return new LLMDecision(false, null, new Dictionary<string, string?>(), thinking, thinking);
     }

      /// <summary>Is this a multi-call (parallel) decision?</summary>
    public bool IsMultiCall => ToolCalls.Count > 1;

    public LLMDecision ToolCall(string name, Dictionary<string, string?> dict)
                  => new(true, name, dict);

    public LLMDecision DirectAnswer(string answer)
                  => new(false, null, new Dictionary<string, string?>(), answer);

    public LLMDecision Unknown()
                  => new(false, null, new Dictionary<string, string?>(), null);
}
