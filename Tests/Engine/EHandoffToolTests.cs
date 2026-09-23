using ECAssistant.Core.Engine;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Tools;
using ECAssistant.Core.Tools.Handoff;

namespace ECAssistant.Core.Tests.Engine;

/// <summary>
/// Unit tests for EHandoffTool — the model-facing handoff tool.
/// The tool is callback-shaped, so the executor boundary is a captured lambda:
/// no engine, no model, no HTTP needed.
/// </summary>
public class EHandoffToolTests
{
    private static Dictionary<string, string?> Args(params (string K, string? V)[] pairs)
        => pairs.ToDictionary(p => p.K, p => p.V);

    [Fact]
    public void ToolMetadata_NameAndSchemaPresent()
    {
        var tool = new EHandoffTool((_, _) => Task.FromResult(new OrchestratorResult()));

        Assert.Equal("EHandoff", tool.Name);
        Assert.DoesNotContain("\n", tool.Description); // single-line description block
        Assert.Contains("prompt", tool.GetParameterSchema());
        Assert.Contains("tools", tool.GetParameterSchema());
        Assert.Contains("context", tool.GetParameterSchema());
        // Rules must steer the model away from trivial use and from recursion:
        Assert.Contains("ESubAgent", tool.GetToolRules());
        Assert.Contains("Do NOT use", tool.GetToolRules());
    }

    [Fact]
    public async Task ExecuteAsync_MissingPrompt_ReturnsFailure()
    {
        var callbackRan = false;
        var tool = new EHandoffTool((_, _) => { callbackRan = true; return Task.FromResult(new OrchestratorResult()); });

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>());

        Assert.False(result.Succeeded);
        Assert.Contains("prompt", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(callbackRan); // never reached the executor
    }

    [Fact]
    public async Task ExecuteAsync_EmptyPromptString_ReturnsFailure()
    {
        var tool = new EHandoffTool((_, _) => Task.FromResult(new OrchestratorResult()));

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["prompt"] = "   " });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteAsync_ValidArgs_CallbackReceivesParsedRequest()
    {
        HandoffRequest? captured = null;
        var tool = new EHandoffTool((req, _) => { captured = req; return Task.FromResult(new OrchestratorResult { Status = OrchestratorStatus.GoalAchieved, FinalOutput = "done" }); });

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["prompt"] = "You are a SQL specialist.",
            ["tools"] = "EShellAgent,EFileReader",
            ["reason"] = "focus",
            ["context"] = "optimize join",
            ["name"] = "sql-expert",
            ["max_turns"] = "4",
            ["timeout"] = "60",
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(captured);
        Assert.Equal("You are a SQL specialist.", captured!.SystemPrompt);
        Assert.Equal("EShellAgent,EFileReader", captured.AllowedTools);
        Assert.Equal("focus", captured.Reason);
        Assert.Equal("optimize join", captured.ContextSummary);
        Assert.Equal("sql-expert", captured.Name);
        Assert.Equal(4, captured.MaxTurns);    // with-override applied
        Assert.Equal(60, captured.TimeoutSeconds); // with-override applied
    }

    [Fact]
    public async Task ExecuteAsync_Defaults_WhenNumericArgsAbsent()
    {
        HandoffRequest? captured = null;
        var tool = new EHandoffTool((req, _) => { captured = req; return Task.FromResult(new OrchestratorResult()); });

        await tool.ExecuteAsync(new Dictionary<string, string?> { ["prompt"] = "p" });

        Assert.Equal(8, captured!.MaxTurns);
        Assert.Equal(180, captured.TimeoutSeconds);
    }

    [Fact]
    public async Task ExecuteAsync_GarbageNumericArgs_FallBackToDefaults()
    {
        HandoffRequest? captured = null;
        var tool = new EHandoffTool((req, _) => { captured = req; return Task.FromResult(new OrchestratorResult()); });

        await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["prompt"] = "p", ["max_turns"] = "abc", ["timeout"] = "xyz",
        });

        Assert.Equal(8, captured!.MaxTurns);
        Assert.Equal(180, captured.TimeoutSeconds);
    }

    [Fact]
    public async Task ExecuteAsync_CallbackReturnsGoalAchieved_MapsToSuccessWithHandoffMetadata()
    {
        var tool = new EHandoffTool((_, _) => Task.FromResult(new OrchestratorResult
        {
            Status = OrchestratorStatus.GoalAchieved,
            FinalOutput = "specialist answer",
            ToolCallsMade = 3,
        }));

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["prompt"] = "p" });

        Assert.True(result.Succeeded);
        Assert.Equal("specialist answer", result.Output);
        Assert.NotNull(result.Metadata);
        Assert.Equal("true", result.Metadata!["handoff"]);
        Assert.Equal("GoalAchieved", result.Metadata["status"]);
        Assert.Equal("3", result.Metadata["tool_calls"]);
    }

    [Fact]
    public async Task ExecuteAsync_CallbackReturnsFailure_MapsToFailedResult()
    {
        var tool = new EHandoffTool((_, _) => Task.FromResult(new OrchestratorResult
        {
            Status = OrchestratorStatus.Failed,
            FinalOutput = "specialist blew up",
        }));

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["prompt"] = "p" });

        Assert.False(result.Succeeded);
        Assert.Contains("blew up", result.Error);
        Assert.Equal("Failed", result.Metadata!["status"]);
    }

    [Fact]
    public async Task ExecuteAsync_CallbackThrows_ReturnsFailureNotThrow()
    {
        var tool = new EHandoffTool((_, _) => throw new InvalidOperationException("boom"));

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["prompt"] = "p" });

        Assert.False(result.Succeeded);
        Assert.Contains("boom", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_HonoursCancellationToken_PassedThroughToCallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        CancellationToken observed = default;
        var tool = new EHandoffTool((_, ct) => { observed = ct; return Task.FromResult(new OrchestratorResult()); });

        await tool.ExecuteAsync(new Dictionary<string, string?> { ["prompt"] = "p" }, cts.Token);

        Assert.True(observed.IsCancellationRequested);
    }
}