using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Engine;

/// <summary>
/// v14.10.1: when the structured path falls back to text streaming, the model
/// still emits the prompt-mandated {"thinking","answer"} envelope — the fallback
/// parser must decode it instead of surfacing raw JSON (live regression: user
/// saw {"thinking": ...} with literal escapes in the terminal).
/// </summary>
public class JsonEnvelopeFallbackTests
{
    private static object? InvokeEnvelope(string raw)
    {
        var method = typeof(AgentEngine).GetMethod("TryParseJsonEnvelope",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return method!.Invoke(null, new object?[] { raw });
    }

    private static ECAssistant.Core.Orchestration.LLMDecision? Parse(string raw)
        => InvokeEnvelope(raw) as ECAssistant.Core.Orchestration.LLMDecision;

    [Fact]
    public void Envelope_WithAnswer_ParsedClean()
    {
        var d = Parse("{\"thinking\": \"Just a joke request\", \"answer\": \"Why don't scientists trust atoms?\\n\\n😄\"}");
        Assert.NotNull(d);
        Assert.False(d!.WantsToolCall);
        Assert.Equal("Why don't scientists trust atoms?\n\n😄", d.AnswerText);
        Assert.Equal("Just a joke request", d.Reasoning);
    }

    [Fact]
    public void Envelope_WithToolcalls_Parsed()
    {
        var d = Parse("{\"thinking\": \"Need to list files\", \"toolcalls\": [{\"name\": \"EShellAgent\", \"args\": {\"command\": \"ls -la\"}}]}");
        Assert.NotNull(d);
        Assert.True(d!.WantsToolCall);
        Assert.Equal("EShellAgent", d.ToolName);
        Assert.Equal("ls -la", d.Args["command"]);
    }

    [Fact]
    public void FencedEnvelope_Parsed()
    {
        var d = Parse("```json\n{\"thinking\": \"hi\", \"answer\": \"hello\"}\n```");
        Assert.NotNull(d);
        Assert.Equal("hello", d!.AnswerText);
    }

    [Fact]
    public void PlainProse_NotTouched()
    {
        Assert.Null(Parse("Once upon a time, in the heart of Italy, stood Rome."));
        Assert.Null(Parse("{\"oops malformed"));
    }

    [Fact]
    public void Envelope_WithoutAnswerOrCalls_ReturnsNull()
    {
        Assert.Null(Parse("{\"thinking\": \"only thinking\"}"));
    }
}
