using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.20: dataflow toolchains — {{N}} reference substitution over prior call
/// outputs. Pure logic; orchestrator wiring covered by executor chain tests.
/// </summary>
public sealed class ToolCallChainSubstitutionTests
{
    [Fact]
    public void HasReferences_DetectsToken()
    {
        Assert.True(ToolCallChainSubstitution.HasReferences(
            new Dictionary<string, string?> { ["file"] = "{{0}}" }));
        Assert.True(ToolCallChainSubstitution.HasReferences(
            new Dictionary<string, string?> { ["text"] = "prefix {{ 1 }} suffix" }));
        Assert.False(ToolCallChainSubstitution.HasReferences(
            new Dictionary<string, string?> { ["text"] = "no refs here" }));
        Assert.False(ToolCallChainSubstitution.HasReferences(
            new Dictionary<string, string?> { ["text"] = "braces {{ but not a ref" }));
    }

    [Fact]
    public void Substitute_ReplacesWithPriorOutput()
    {
        var result = ToolCallChainSubstitution.Substitute(
            new Dictionary<string, string?> { ["text"] = "first={{0}} second={{1}}" },
            new List<string?> { "OUTPUT-A", "OUTPUT-B" });
        Assert.Equal("first=OUTPUT-A second=OUTPUT-B", result["text"]);
    }

    [Fact]
    public void Substitute_FailedPriorCall_GetsExplicitMarker()
    {
        var result = ToolCallChainSubstitution.Substitute(
            new Dictionary<string, string?> { ["text"] = "{{2}}" },
            new List<string?> { "A", null }); // call 2 never happened
        Assert.Equal("[reference {{2}} unavailable: call failed or produced no output]", result["text"]);
    }

    [Fact]
    public void Substitute_TruncatesHugeOutput_ToCap()
    {
        var huge = new string('x', ToolCallChainSubstitution.MaxSubstitutionChars + 5000);
        var result = ToolCallChainSubstitution.Substitute(
            new Dictionary<string, string?> { ["text"] = "{{0}}" },
            new List<string?> { huge });
        Assert.True(result["text"]!.Length <= ToolCallChainSubstitution.MaxSubstitutionChars + 100);
        Assert.Contains("truncated to", result["text"]);
    }

    [Fact]
    public void AnyCallHasReferences_AnyCallTriggersSequential()
    {
        var calls = new List<ToolCallRequest>
        {
            new() { ToolName = "A", Args = new() },
            new() { ToolName = "B", Args = new() { ["file"] = "{{0}}" } },
        };
        Assert.True(ToolCallChainSubstitution.AnyCallHasReferences(calls));
    }
}