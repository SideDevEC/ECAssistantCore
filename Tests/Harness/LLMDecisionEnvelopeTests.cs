using ECAssistant.Core.Orchestration;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.18: thinking-only decision envelopes (answer empty, no tool calls) must
/// NOT surface the thinking text as a direct answer — live qwen3.5-4b E2E showed
/// the model emitting {"thinking": "...plan...", "answer": ""} and the harness
/// delivering the plan to the user instead of format-retrying. The null-answer
/// decision routes the turn into the orchestrator's format-retry path; thinking
/// remains in Reasoning for the post-retry best-effort fallback.
/// </summary>
public sealed class LLMDecisionEnvelopeTests
{
    [Fact]
    public void FromEnvelope_ThinkingOnly_ReturnsNullAnswer_ForFormatRetry()
    {
        var d = LLMDecision.FromEnvelope("I need to use a tool now", "", null);
        Assert.False(d.WantsDirectAnswer);
        Assert.Null(d.AnswerText);
        Assert.False(d.WantsToolCall);
        Assert.Equal("I need to use a tool now", d.Reasoning);
    }

    [Fact]
    public void FromEnvelope_ThinkingOnlyNoAnswerProperty_SameBehavior()
    {
        var d = LLMDecision.FromEnvelope("plan only", null, null);
        Assert.False(d.WantsDirectAnswer);
        Assert.Null(d.AnswerText);
        Assert.Equal("plan only", d.Reasoning);
    }

    [Fact]
    public void FromEnvelope_AnswerPresent_IsDirectAnswer()
    {
        var d = LLMDecision.FromEnvelope("thinking", "the answer", null);
        Assert.True(d.WantsDirectAnswer);
        Assert.Equal("the answer", d.AnswerText);
    }
}