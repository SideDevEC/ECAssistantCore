using ECAssistant.Core.Engine;
using Xunit;

namespace ECAssistant.Core.Tests;

/// <summary>v13: grammar-forced decision envelope → internal decision text.</summary>
public class StructuredDecisionAdapterTests
{
    [Fact]
    public void AnswerEnvelope_ConvertsToOutputTag()
    {
        var result = StructuredDecisionAdapter.Convert(
            """{"thinking":"Simple greeting","answer":"Hey! What can I do for you?"}""");
        Assert.Equal("<lm><thinking>Simple greeting</thinking><output>Hey! What can I do for you?</output></lm>", result);
    }

    [Fact]
    public void ToolCallEnvelope_ConvertsToToolcallTag()
    {
        var result = StructuredDecisionAdapter.Convert(
            """{"thinking":"need file","toolcalls":[{"name":"read_file","args":{"path":"x.txt","limit":"10"}}]}""");
        Assert.Equal(
            "<lm><thinking>need file</thinking><toolcall>read_file<path>x.txt</path><limit>10</limit></toolcall></lm>",
            result);
    }

    [Fact]
    public void MultipleToolCalls_ConvertInOrder()
    {
        var result = StructuredDecisionAdapter.Convert(
            """{"thinking":"parallel","toolcalls":[{"name":"a","args":{}},{"name":"b","args":{"k":"v"}}]}""");
        Assert.Contains("<toolcall>a</toolcall>", result);
        Assert.Contains("<toolcall>b<k>v</k></toolcall>", result);
        Assert.EndsWith("</lm>", result);
    }

    [Fact]
    public void EmptyEnvelope_FallsBackToThinkingAsAnswer()
    {
        var result = StructuredDecisionAdapter.Convert("""{"thinking":"hmm"}""");
        Assert.Equal("<lm><thinking>hmm</thinking><output>hmm</output></lm>", result);
    }

    [Fact]
    public void InvalidJson_Throws()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => StructuredDecisionAdapter.Convert("junk"));
    }
}
