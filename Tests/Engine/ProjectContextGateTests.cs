using System.Reflection;
using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Engine;

public class ProjectContextGateTests
{
    private static bool Gate(string? request)
    {
        var method = typeof(EAgentEngine).GetMethod("IsProjectRelatedRequest",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, new object?[] { request })!;
    }

    [Theory]
    [InlineData("What day is today?")]
    [InlineData("Hey what's up?")]
    [InlineData("Tell me a joke")]
    [InlineData("How's the weather in Berlin?")]
    [InlineData("Thanks, that helped!")]
    public void Gate_ConversationalRequests_NotProjectRelated(string request)
    {
        Assert.False(Gate(request), $"should NOT inject project context for: {request}");
    }

    [Theory]
    [InlineData("Fix the bug in Program.cs line 42")]
    [InlineData("Build the project")]
    [InlineData("List the files in this directory")]
    [InlineData("What does appsettings.json contain?")]
    [InlineData("Run the tests and report failures")]
    [InlineData("Commit my changes to git")]
    [InlineData("Refactor the EShellAgent class")]
    [InlineData("Read Program.cs and explain the structure")]
    public void Gate_ProjectRequests_InjectContext(string request)
    {
        Assert.True(Gate(request), $"should inject project context for: {request}");
    }

    [Fact]
    public void Gate_NullOrEmpty_False()
    {
        Assert.False(Gate(null));
        Assert.False(Gate(""));
    }
}
