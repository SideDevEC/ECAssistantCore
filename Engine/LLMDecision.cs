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

      /// <summary>Single tool call (backwards compat).</summary>
    public LLMDecision(
        bool wantsToolCall,
        string? toolName,
        Dictionary<string, string?> args,
        string? answerText = null)
                {
        WantsToolCall = wantsToolCall;
        ToolCalls = wantsToolCall && toolName != null
              ? new List<ToolCallRequest> { new() { ToolName = toolName, Args = args ?? new Dictionary<string, string?>(), Index = 1 } }
              : new List<ToolCallRequest>();
        WantsDirectAnswer = answerText != null;
            AnswerText = answerText;
                }

      /// <summary>Multiple tool calls (v10.13).</summary>
    public LLMDecision(List<ToolCallRequest> toolCalls)
      {
        WantsToolCall = toolCalls.Count > 0;
        ToolCalls = toolCalls;
        WantsDirectAnswer = false;
        AnswerText = null;
      }

      /// <summary>Number of tool calls in this decision.</summary>
    public int ToolCallCount => ToolCalls.Count;

      /// <summary>Is this a multi-call (parallel) decision?</summary>
    public bool IsMultiCall => ToolCalls.Count > 1;

    public LLMDecision ToolCall(string name, Dictionary<string, string?> dict)
                  => new(true, name, dict);

    public LLMDecision DirectAnswer(string answer)
                  => new(false, null, new Dictionary<string, string?>(), answer);

    public LLMDecision Unknown()
                  => new(false, null, new Dictionary<string, string?>(), null);
}
