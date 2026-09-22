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

    // ── v14.10.2 TryExtractAnswer ──

    [Fact]
    public void TryExtractAnswer_EnvelopeWithAnswer_ReturnsAnswer()
    {
        const string raw = """{"thinking":"greeting","answer":"Hello there!"}""";
        Assert.Equal("Hello there!", StructuredDecisionAdapter.TryExtractAnswer(raw));
    }

    [Fact]
    public void TryExtractAnswer_EnvelopeWithToolcalls_ReturnsNull()
    {
        const string raw = """{"thinking":"need data","toolcalls":[{"name":"EShellAgent","args":{"command":"ls"}}]}""";
        Assert.Null(StructuredDecisionAdapter.TryExtractAnswer(raw));
    }

    [Fact]
    public void TryExtractAnswer_PlainText_ReturnsNull()
    {
        Assert.Null(StructuredDecisionAdapter.TryExtractAnswer("1. Read file\n2. Fix bug"));
    }

    [Fact]
    public void TryExtractAnswer_Empty_ReturnsNull()
    {
        Assert.Null(StructuredDecisionAdapter.TryExtractAnswer(""));
        Assert.Null(StructuredDecisionAdapter.TryExtractAnswer("   "));
    }

    [Fact]
    public void TryExtractAnswer_BracesInsideStrings_AreNotCounted()
    {
        const string raw = """{"thinking":"json { inside","answer":"done } ok"}""";
        Assert.Equal("done } ok", StructuredDecisionAdapter.TryExtractAnswer(raw));
    }

    [Fact]
    public void TryExtractAnswer_InvalidJsonObject_ReturnsNull()
    {
        Assert.Null(StructuredDecisionAdapter.TryExtractAnswer("{"));
        Assert.Null(StructuredDecisionAdapter.TryExtractAnswer("{\"not\":\"json\"}"));
    }
}
