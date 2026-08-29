using ECAssistant.Core.Orchestration;
using Xunit;

namespace ECAssistant.Core.Tests;

/// <summary>
/// v12.4/v12.5 regression: tool-call signatures must be deterministic and
/// argument-order-independent so the failed-call and back-to-back-repeat
/// guards catch identical calls regardless of how the model orders arguments.
/// </summary>
public class BuildCallSignatureTests
{
    [Fact]
    public void SameTool_SameArgs_SameSignature()
    {
        var a = new Dictionary<string, string?> { ["command"] = "ls ~/Desktop" };
        var b = new Dictionary<string, string?> { ["command"] = "ls ~/Desktop" };
        Assert.Equal(
            AgentOrchestrator.BuildCallSignature("EShellAgent", a),
            AgentOrchestrator.BuildCallSignature("EShellAgent", b));
    }

    [Fact]
    public void ArgumentOrder_DoesNotMatter()
    {
        var a = new Dictionary<string, string?> { ["path"] = "~/Desktop", ["filter"] = "folders" };
        var b = new Dictionary<string, string?> { ["filter"] = "folders", ["path"] = "~/Desktop" };
        Assert.Equal(
            AgentOrchestrator.BuildCallSignature("T", a),
            AgentOrchestrator.BuildCallSignature("T", b));
    }

    [Fact]
    public void DifferentArgs_DifferentSignature()
    {
        var a = new Dictionary<string, string?> { ["command"] = "ls ~/Desktop" };
        var b = new Dictionary<string, string?> { ["command"] = "ls ~/Documents" };
        Assert.NotEqual(
            AgentOrchestrator.BuildCallSignature("EShellAgent", a),
            AgentOrchestrator.BuildCallSignature("EShellAgent", b));
    }

    [Fact]
    public void DifferentTool_DifferentSignature_EvenWithSameArgs()
    {
        var a = new Dictionary<string, string?> { ["query"] = "test" };
        Assert.NotEqual(
            AgentOrchestrator.BuildCallSignature("EWebSearch", a),
            AgentOrchestrator.BuildCallSignature("EWebFetch", a));
    }

    [Fact]
    public void NullArguments_AreHandled()
    {
        var sig = AgentOrchestrator.BuildCallSignature("EWebSearch", null!);
        Assert.Equal("EWebSearch|", sig);
    }
}
