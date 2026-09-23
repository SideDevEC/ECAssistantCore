using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Engine;

/// <summary>
/// Unit tests for HandoffRequest — the ephemeral handoff data record.
/// Pure data-class tests: defaults, with-init copies, immutability.
/// </summary>
public class HandoffRequestTests
{
    [Fact]
    public void Defaults_MaxTurns8_Timeout180()
    {
        var request = new HandoffRequest();

        Assert.Equal(8, request.MaxTurns);
        Assert.Equal(180, request.TimeoutSeconds);
        Assert.Equal("", request.Name);
        Assert.Equal("", request.SystemPrompt);
        Assert.Equal("", request.AllowedTools);
        Assert.Equal("", request.Reason);
        Assert.Equal("", request.ContextSummary);
        Assert.Null(request.ModelOverride);
    }

    [Fact]
    public void Init_SetsAllFields()
    {
        var request = new HandoffRequest
        {
            Name = "sql-expert",
            SystemPrompt = "You are a SQL specialist.",
            AllowedTools = "EShellAgent,EFileReader",
            Reason = "deep SQL focus",
            ContextSummary = "Optimize a join",
            ModelOverride = "phi-4:14b",
            MaxTurns = 4,
            TimeoutSeconds = 60,
        };

        Assert.Equal("sql-expert", request.Name);
        Assert.Equal("You are a SQL specialist.", request.SystemPrompt);
        Assert.Equal("EShellAgent,EFileReader", request.AllowedTools);
        Assert.Equal("deep SQL focus", request.Reason);
        Assert.Equal("Optimize a join", request.ContextSummary);
        Assert.Equal("phi-4:14b", request.ModelOverride);
        Assert.Equal(4, request.MaxTurns);
        Assert.Equal(60, request.TimeoutSeconds);
    }

    [Fact]
    public void WithExpression_CopiesAndOverrides()
    {
        var request = new HandoffRequest { Name = "a", SystemPrompt = "p", MaxTurns = 8 };
        var adjusted = request with { MaxTurns = 12 };

        Assert.Equal(12, adjusted.MaxTurns);
        Assert.Equal("a", adjusted.Name);        // unchanged
        Assert.Equal("p", adjusted.SystemPrompt); // unchanged
        Assert.Equal(8, request.MaxTurns);        // original untouched — record immutability
    }

    [Fact]
    public void Records_WithSameValues_AreEqual()
    {
        var a = new HandoffRequest { Name = "x", SystemPrompt = "p" };
        var b = new HandoffRequest { Name = "x", SystemPrompt = "p" };

        Assert.Equal(a, b); // record value equality
    }
}