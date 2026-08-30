using System.Runtime.CompilerServices;
using ECAssistant.Core.Orchestration;
using Xunit;

namespace ECAssistant.Core.Tests;

/// <summary>
/// v12.12 model-agnostic decision parsing — ANY model must produce a usable
/// decision: tool calls, &lt;output&gt; answers, and thinking-only responses
/// (models that ignore the tag protocol). ParseLLMDecision is null-logger-safe,
/// so tests run it on an uninitialized orchestrator (no engine wiring needed).
/// </summary>
public class DecisionParserTests
{
    private static ECAssistant.Core.Orchestration.AgentOrchestrator BareOrchestrator() =>
        (ECAssistant.Core.Orchestration.AgentOrchestrator)RuntimeHelpers.GetUninitializedObject(typeof(ECAssistant.Core.Orchestration.AgentOrchestrator));

    private static ECAssistant.Core.Orchestration.LLMDecision Parse(string response)
    {
        var orchestrator = BareOrchestrator();
        var method = typeof(ECAssistant.Core.Orchestration.AgentOrchestrator)
            .GetMethod("ParseLLMDecision", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("ParseLLMDecision not found");
        return (LLMDecision)method.Invoke(orchestrator, new object?[] { response })!;
    }

    [Fact]
    public void ThinkingOnlyResponse_BecomesDirectAnswer()
    {
        var decision = Parse("<thinking>Welcome! How can I assist you today?</thinking>");
        Assert.True(decision.WantsDirectAnswer);
        Assert.Equal("Welcome! How can I assist you today?", decision.AnswerText);
        Assert.False(decision.WantsToolCall);
    }

    [Fact]
    public void UnclosedThinkingResponse_BecomesDirectAnswer()
    {
        var decision = Parse("<thinking>Let me help with that.");
        Assert.True(decision.WantsDirectAnswer);
        Assert.Contains("help", decision.AnswerText);
    }

    [Fact]
    public void PlainTextWithoutTags_IsUnknown_FirstThenFormatRetryDeliversBestEffort()
    {
        // Plain text gets 2 format-retry chances (models usually adapt), then the
        // orchestrator's best-effort fallback delivers the response as the answer.
        var decision = Parse("Hello! How can I help you today?");
        Assert.False(decision.WantsDirectAnswer);
        Assert.False(decision.WantsToolCall);
    }

    [Fact]
    public void OutputBlock_StillWinsOverThinking()
    {
        var decision = Parse("<lm><thinking>reason</thinking><output>The answer</output></lm>");
        Assert.True(decision.WantsDirectAnswer);
        Assert.Equal("The answer", decision.AnswerText);
    }

    [Fact]
    public void ToolCallBlock_TakesPriority()
    {
        var decision = Parse("<thinking>need file</thinking><toolcall>read_file<argpath>x.txt</argpath></toolcall>");
        Assert.True(decision.WantsToolCall);
        Assert.Equal("read_file", decision.ToolName);
        Assert.False(decision.WantsDirectAnswer);
    }

    [Fact]
    public void EmptyResponse_StaysUnknown()
    {
        var decision = Parse("");
        Assert.False(decision.WantsDirectAnswer);
        Assert.False(decision.WantsToolCall);
    }
}
