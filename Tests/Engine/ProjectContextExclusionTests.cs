using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Engine;

/// <summary>
/// Project-context scan must exclude host runtime/config files — the model should
/// not see (or narrate) the app's own internals as "project files" (2026-09-21 fix).
/// </summary>
public sealed class ProjectContextExclusionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-projctx").FullName;

    [Fact]
    public async Task ScanProjectAsync_ExcludesHostRuntimeFiles()
    {
        File.WriteAllText(Path.Combine(_dir, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "model-catalog.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, ".project_context.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "ECAssistant.log"), "log");
        File.WriteAllText(Path.Combine(_dir, "Program.cs"), "class P { }");

        var manager = new ProjectContextManager(_dir);
        await manager.ScanProjectAsync();

        var paths = manager.GetProjectSummary();
        // Note: the summary's nudge line mentions appsettings.json — assert the
        // Files section only (anything before the "Note:" line).
        var filesSection = paths.Substring(0, paths.IndexOf("Note:"));
        Assert.DoesNotContain("appsettings.json", filesSection);
        Assert.DoesNotContain("model-catalog.json", filesSection);
        Assert.DoesNotContain(".project_context.json", filesSection);
        Assert.DoesNotContain("ECAssistant.log", filesSection);
        Assert.Contains("Program.cs", paths);
    }

    [Fact]
    public async Task ScanProjectAsync_ExcludesRuntimeDirectories()
    {
        foreach (var sub in new[] { ".sessions", "Workspace", "tool_outputs" })
        {
            Directory.CreateDirectory(Path.Combine(_dir, sub));
            File.WriteAllText(Path.Combine(_dir, sub, "session.cs"), "class X { }");
        }
        File.WriteAllText(Path.Combine(_dir, "real.cs"), "class R { }");

        var manager = new ProjectContextManager(_dir);
        await manager.ScanProjectAsync();

        var paths = manager.GetProjectSummary();
        Assert.DoesNotContain(".sessions", paths);
        Assert.DoesNotContain("Workspace", paths);
        Assert.DoesNotContain("tool_outputs", paths);
        Assert.Contains("real.cs", paths);
    }

    [Fact]
    public async Task GetProjectSummary_IncludesHostConfigNudge()
    {
        File.WriteAllText(Path.Combine(_dir, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "Program.cs"), "class P { }");

        var manager = new ProjectContextManager(_dir);
        await manager.ScanProjectAsync();

        var summary = manager.GetProjectSummary();
        Assert.Contains("Do not proactively mention", summary);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}