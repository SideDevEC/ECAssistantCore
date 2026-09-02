using ECAssistant.Core.Engine;
using Xunit;

namespace ECAssistant.Core.Tests;

/// <summary>v14: grammar-forced decision envelope → LLMDecision (native JSON pipeline).</summary>
public class StructuredDecisionAdapterTests
{
    [Fact]
    public void AnswerEnvelope_ParsesToDirectAnswer()
    {
        var result = StructuredDecisionAdapter.ParseDecision(
            """{"thinking":"Simple greeting","answer":"Hey! What can I do for you?"}""");
        Assert.True(result.WantsDirectAnswer);
        Assert.Equal("Hey! What can I do for you?", result.AnswerText);
        Assert.False(result.WantsToolCall);
    }

    [Fact]
    public void ToolCallEnvelope_ParsesToToolCall()
    {
        var result = StructuredDecisionAdapter.ParseDecision(
            """{"thinking":"need file","toolcalls":[{"name":"read_file","args":{"path":"x.txt","limit":"10"}}]}""");
        Assert.True(result.WantsToolCall);
        Assert.False(result.WantsDirectAnswer);
        Assert.Single(result.ToolCalls);
        Assert.Equal("read_file", result.ToolCalls[0].ToolName);
        Assert.Equal("x.txt", result.ToolCalls[0].Args["path"]);
        Assert.Equal("10", result.ToolCalls[0].Args["limit"]);
    }

    [Fact]
    public void MultipleToolCalls_ParseInOrder()
    {
        var result = StructuredDecisionAdapter.ParseDecision(
            """{"thinking":"parallel","toolcalls":[{"name":"a","args":{}},{"name":"b","args":{"k":"v"}}]}""");
        Assert.True(result.WantsToolCall);
        Assert.Equal(2, result.ToolCallCount);
        Assert.Equal("a", result.ToolCalls[0].ToolName);
        Assert.Equal("b", result.ToolCalls[1].ToolName);
        Assert.Equal("v", result.ToolCalls[1].Args["k"]);
        Assert.True(result.IsMultiCall);
    }

    [Fact]
    public void EmptyEnvelope_FallsBackToThinkingAsAnswer()
    {
        var result = StructuredDecisionAdapter.ParseDecision("""{"thinking":"hmm"}""");
        Assert.True(result.WantsDirectAnswer);
        Assert.Equal("hmm", result.AnswerText);
    }

    [Fact]
    public void InvalidJson_Throws()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => StructuredDecisionAdapter.ParseDecision("junk"));
    }
}
