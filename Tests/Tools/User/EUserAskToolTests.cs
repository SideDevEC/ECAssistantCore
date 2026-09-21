using ECAssistant.Core.Session;
using ECAssistant.Core.Tools.User;
using Xunit;

namespace ECAssistant.Core.Tests.Tools.User;

/// <summary>v14.9 AskUser tool — model-driven ambiguity checkpoint: parses options,
/// surfaces RequestChoice, maps the pick / null-answer to tool results.</summary>
public class EUserAskToolTests
{
    private sealed class FakeOutput : ISessionOutput
    {
        public int? Answer { get; set; }
        public string? LastPrompt { get; private set; }
        public System.Collections.Generic.IReadOnlyList<string>? LastOptions { get; private set; }

        public void StartStream(OutputState state) { }
        public void Write(string token) { }
        public void StopStream() { }
        public void WriteLine(string text, OutputState state = OutputState.Info) { }
        public void BlankLine() { }
        public void WriteInfo(string text) { }
        public void WriteSuccess(string text) { }
        public void WriteWarning(string text) { }
        public void WriteError(string text) { }
        public void WriteDim(string text) { }
        public void WriteTag(string tag, string message, OutputState state = OutputState.Info) { }
        public string GetStreamBuffer() => "";
        public OutputState GetStreamState() => OutputState.Info;
        public bool RequestApproval(string message) => false;
        public int? RequestChoice(string prompt, System.Collections.Generic.IReadOnlyList<string> options)
        {
            LastPrompt = prompt; LastOptions = options; return Answer;
        }
    }

    private static Dictionary<string, string?> Args(string? q = "How?", string? o = null, string? d = null) =>
        new() { ["question"] = q, ["options"] = o, ["default"] = d };

    [Fact]
    public async Task ExecuteAsync_UserPicks_ReturnsChoice()
    {
        var output = new FakeOutput { Answer = 2 };
        var tool = new EUserAskTool(output);

        var result = await tool.ExecuteAsync(Args(o: """["In place", "New module"]"""));

        Assert.True(result.Succeeded);
        Assert.Contains("New module", result.Output);
        Assert.Equal("How?", output.LastPrompt);
        Assert.Equal(2, output.LastOptions!.Count);
    }

    [Fact]
    public async Task ExecuteAsync_NoAnswer_UsesDefault()
    {
        var tool = new EUserAskTool(new FakeOutput { Answer = null });

        var result = await tool.ExecuteAsync(Args(o: """["A", "B"]""", d: "A"));

        Assert.True(result.Succeeded);
        Assert.Contains("default: A", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_NoAnswer_NoDefault_Autonomous()
    {
        var tool = new EUserAskTool(new FakeOutput { Answer = null });

        var result = await tool.ExecuteAsync(Args(o: """["A", "B"]"""));

        Assert.True(result.Succeeded);
        Assert.Contains("best judgment", result.Output);
    }

    [Theory]
    [InlineData(null, """["A","B"]""")]        // missing question
    [InlineData("q", "[\"only-one\"]")]        // <2 options
    [InlineData("q", "not-json")]              // malformed
    public async Task ExecuteAsync_InvalidArgs_Fails(string? q, string o)
    {
        var tool = new EUserAskTool(new FakeOutput());
        var result = await tool.ExecuteAsync(Args(q: q, o: o));
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteAsync_NoOutputChannel_FallsBackGracefully()
    {
        var tool = new EUserAskTool(null);
        var result = await tool.ExecuteAsync(Args(o: """["A", "B"]"""));
        Assert.True(result.Succeeded);
        Assert.Contains("best judgment", result.Output);
    }
}
