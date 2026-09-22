using System.Reflection;
using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Engine;

public class SystemPromptDateSectionTests
{
    [Fact]
    public void AppendCurrentDateSection_AddsDateAndNoToolGuidance()
    {
        var method = typeof(EAgentEngine).GetMethod("AppendCurrentDateSection",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string)method!.Invoke(null, new object[] { "# Prompt" })!;

        Assert.Contains("## CURRENT DATE & TIME", result);
        Assert.Contains("answer directly from this line", result);
        Assert.Contains("do NOT run tools", result);
        Assert.StartsWith("# Prompt", result);
    }

    [Fact]
    public void AppendCurrentDateSection_DateMatchesToday()
    {
        var method = typeof(EAgentEngine).GetMethod("AppendCurrentDateSection",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (string)method!.Invoke(null, new object[] { "" })!;
        var today = DateTime.Now;
        Assert.Contains(today.ToString("dddd"), result);
        Assert.Contains(today.Year.ToString(), result);
    }
}
