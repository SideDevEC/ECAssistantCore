using ECAssistant.Core.Tools.Build;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.20: BuildOutputRenderer — semantic render of dotnet build/test output.
/// The tool knows its semantics; the render carries facts (verdict, parsed
/// errors with file:line/code/message, warning count, test summary), not raw text.
/// </summary>
public sealed class BuildOutputRendererTests
{
    private const string FailedBuild = """
        /repo/src/App/Program.cs(12,34): error CS1061: 'Order' does not contain a definition for 'Total'
        /repo/src/App/Program.cs(20,5): error CS0103: The name 'customer' does not exist
        /repo/src/App/Helper.cs(3,3): warning CS0219: variable assigned but never used
        Build FAILED.
        """;

    [Fact]
    public void FailedBuild_RendersVerdictAndErrors()
    {
        var rendered = BuildOutputRenderer.Render(FailedBuild);
        Assert.Contains("[BUILD FAILED]", rendered);
        Assert.Contains("2 error(s)", rendered);
        Assert.Contains("CS1061", rendered);
        Assert.Contains("Program.cs(12): CS1061", rendered);   // path shortened to file name
        Assert.Contains("CS0103", rendered);
        Assert.DoesNotContain("/repo/src/App/Program.cs", rendered); // absolute path noise dropped
    }

    [Fact]
    public void SuccessBuild_RendersOkAndWarnings()
    {
        var output = "/repo/src/App/Program.cs(3,3): warning CS0219: unused\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)";
        var rendered = BuildOutputRenderer.Render(output);
        Assert.Contains("[BUILD OK]", rendered);
        Assert.Contains("0 error(s)", rendered);
        Assert.Contains("warning", rendered);
    }

    [Fact]
    public void TestSummary_LineIsCarriedThrough()
    {
        var output = "Build succeeded.\n" +
                     "Passed!  - Failed: 0, Passed: 47, Skipped: 2, Total: 49, Duration: 3 s";
        var rendered = BuildOutputRenderer.Render(output);
        Assert.Contains("Passed!  - Failed: 0, Passed: 47", rendered);
    }

    [Fact]
    public void ErrorCap_SixKept()
    {
        var many = string.Join("\n", Enumerable.Range(1, 10)
            .Select(i => $"/r/P{i}.cs({i},1): error CS100{i}: err{i}"));
        var rendered = BuildOutputRenderer.Render(many + "\nBuild FAILED.");
        Assert.Contains("10 error(s)", rendered);      // verdict counts all
        Assert.DoesNotContain("err9", rendered);        // only the first 6 rendered
        Assert.Contains("err6", rendered);
    }

    [Fact]
    public void IsDotnetOutput_DetectsFamilies()
    {
        Assert.True(BuildOutputRenderer.IsDotnetOutput("Build succeeded."));
        Assert.True(BuildOutputRenderer.IsDotnetOutput("Program.cs(1,1): error CS1002: ; expected"));
        Assert.False(BuildOutputRenderer.IsDotnetOutput("total 48\ndrwxr-xr-x  src"));
    }
}