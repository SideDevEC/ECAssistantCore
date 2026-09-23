using ECAssistant.Core.Engine;
using ECAssistant.Core.Session;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.20: chain execution through the real ParallelToolExecutor — {{N}}
/// references force sequential order, substitution feeds later args, approvals
/// still run per call. Stub executeToolFn captures received args.
/// </summary>
public sealed class ChainExecutionTests
{
    /// <summary>Minimal ISessionOutput double — records approval requests.</summary>
    private sealed class RecordingSessionOutput : ISessionOutput
    {
        public int ApprovalRequests;
        public bool Approve = true;

        public bool RequestApproval(string message) { ApprovalRequests++; return Approve; }

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
        public int? RequestChoice(string prompt, IReadOnlyList<string> options) => null;
    }

    [Fact]
    public async Task Chain_RunsSequentially_AndSubstitutesPriorOutput()
    {
        var received = new List<Dictionary<string, string?>>();
        var executor = new ParallelToolExecutor(
            engine: null!,
            toolPolicy: new ToolPolicy(),
            executeToolFn: (name, args) =>
            {
                received.Add(new Dictionary<string, string?>(args));
                return Task.FromResult(EToolResult.Success(name, $"OUT-{received.Count}"));
            });

        var calls = new List<ToolCallRequest>
        {
            new() { ToolName = "ProbeTool", Index = 0, Args = new() { ["action"] = "probe" } },
            new() { ToolName = "ProbeTool", Index = 1, Args = new() { ["text"] = "prior was {{0}}" } },
        };

        var result = await executor.ExecuteAsync(calls);

        Assert.Equal(2, result.Results.Count);
        Assert.True(result.AllSucceeded);
        Assert.Equal("prior was OUT-1", received[1]["text"]); // call 0's output substituted
    }

    [Fact]
    public async Task Chain_FailedCall_LaterRefSeesMarker()
    {
        var received = new List<Dictionary<string, string?>>();
        var executor = new ParallelToolExecutor(
            engine: null!,
            toolPolicy: new ToolPolicy(),
            executeToolFn: (name, args) =>
            {
                received.Add(new Dictionary<string, string?>(args));
                return Task.FromResult(received.Count == 1
                    ? EToolResult.Failure(name, "boom")
                    : EToolResult.Success(name, "ok"));
            });

        var calls = new List<ToolCallRequest>
        {
            new() { ToolName = "A", Index = 0, Args = new() },
            new() { ToolName = "B", Index = 1, Args = new() { ["text"] = "{{0}}" } },
        };

        var result = await executor.ExecuteAsync(calls);

        Assert.False(result.Results[0].Succeeded);
        Assert.Contains("unavailable", received[1]["text"]);
    }

    [Fact]
    public async Task Chain_ApprovalStillRuns_PerCall()
    {
        var sessionOutput = new RecordingSessionOutput { Approve = false };
        var policy = new ToolPolicy();
        policy.SetPermission("SomeTool", approvalRequired: true, "test");
        var gatedExecutor = new ParallelToolExecutor(
            engine: null!,
            toolPolicy: policy,
            executeToolFn: (name, args) => Task.FromResult(EToolResult.Success(name, "x")),
            sessionOutput: sessionOutput);

        var calls = new List<ToolCallRequest>
        {
            new() { ToolName = "SomeTool", Index = 0, Args = new() },
        };

        var result = await gatedExecutor.ExecuteAsync(calls);

        Assert.Equal(1, sessionOutput.ApprovalRequests);
        Assert.False(result.Results[0].Succeeded); // denied — gate preserved in chain mode
    }
}