using ECAssistant.Core.Services.Http;
using Xunit;

namespace ECAssistant.Core.Tests;

/// <summary>
/// v12.9 runtime path contract: the LLM server executable resolves against the app ROOT
/// only — never against dev trees (bin/Debug siblings, repo layout). Regression tests for
/// the stale-Release and CWD-dependent launch failures found on 2026-08-29.
///
/// Layout per test (single disposable root, matching a realistic deployment):
///   root/
///     ecassistant/            ← app root passed to the resolver
///     console/                ← dev CWD (dotnet run from a project folder)
///     ECAssistantLLM/bin/…    ← server binary location (root-relative or dev sibling)
/// </summary>
public class ServerLauncherResolveTests : IDisposable
{
    private readonly string _root;      // the integrating app's root
    private readonly string _appRoot;   // ECAssistant root (may be a subfolder of the host root)
    private readonly string _baseDir;   // publish layout (app binary folder)
    private readonly string _cwd;       // dev CWD (dotnet run)

    public ServerLauncherResolveTests()
    {
        _root = CreateDir();
        _appRoot = Path.Combine(_root, "ecassistant");
        Directory.CreateDirectory(_appRoot);
        _baseDir = Path.Combine(_root, "console", "bin");
        Directory.CreateDirectory(_baseDir);
        _cwd = Path.Combine(_root, "console");
        Directory.CreateDirectory(_cwd);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static string CreateDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "sltest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static string WriteExe(string dir)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "ECAssistant.LLM");
        File.WriteAllText(p, "stub");
        return p;
    }

    private const string ConfiguredRel = "../ECAssistantLLM/bin/Release/net8.0/ECAssistant.LLM";

    [Fact]
    public void AbsolutePath_Wins()
    {
        var exe = WriteExe(Path.Combine(_root, "anywhere"));
        var got = ServerLauncher.ResolveExecutablePath(exe, _appRoot, _baseDir, _cwd);
        Assert.Equal(exe, got);
    }

    [Fact]
    public void RootRelative_Found()
    {
        // configured "../ECAssistantLLM/…" resolves against the app root → root/ECAssistantLLM/…
        var exe = WriteExe(Path.Combine(_appRoot, "ECAssistantLLM", "bin", "Release", "net8.0"));
        var got = ServerLauncher.ResolveExecutablePath(ConfiguredRel, _appRoot, _baseDir, _cwd);
        Assert.Equal(exe, got);
    }

    [Fact]
    public void RootScan_FindsExecutable_TwoLevelsDeep()
    {
        var exe = WriteExe(Path.Combine(_appRoot, "server", "bin", "Release", "net8.0"));
        Assert.True(File.Exists(exe), $"exe missing on disk: {exe}");
        var got = ServerLauncher.ResolveExecutablePath("some/missing/dir/ECAssistant.LLM", _appRoot, _baseDir, _cwd);
        Assert.True(got != null, $"resolver returned null; exe={exe}; appRoot={_appRoot}; exists={File.Exists(exe)}");
        Assert.Equal(exe, got);
    }

    [Fact]
    public void PublishLayout_BaseDirectory_Found()
    {
        var exe = WriteExe(_baseDir);
        var got = ServerLauncher.ResolveExecutablePath("ECAssistant.LLM", _appRoot, _baseDir, _cwd);
        Assert.Equal(exe, got);
    }

    [Fact]
    public void DevCwd_Found_WhenNothingInRoot()
    {
        // dev run: dotnet run from console/, configured Release path relative to CWD
        var exe = WriteExe(Path.Combine(_root, "ECAssistantLLM", "bin", "Release", "net8.0"));
        var got = ServerLauncher.ResolveExecutablePath(ConfiguredRel, _appRoot, _baseDir, _cwd);
        Assert.Equal(exe, got);
    }

    [Fact]
    public void FresherDebug_Build_Preferred_Over_StaleRelease()
    {
        var release = WriteExe(Path.Combine(_root, "ECAssistantLLM", "bin", "Release", "net8.0"));
        Thread.Sleep(50); // ensure distinct timestamps
        var debug = WriteExe(Path.Combine(_root, "ECAssistantLLM", "bin", "Debug", "net8.0"));
        File.SetLastWriteTimeUtc(release, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(debug, DateTime.UtcNow);

        var got = ServerLauncher.ResolveExecutablePath(ConfiguredRel, _appRoot, _baseDir, _cwd);
        Assert.Equal(debug, got);
    }

    [Fact]
    public void StaleDebug_DoesNotBeat_FreshRelease()
    {
        var release = WriteExe(Path.Combine(_root, "ECAssistantLLM", "bin", "Release", "net8.0"));
        var debug = WriteExe(Path.Combine(_root, "ECAssistantLLM", "bin", "Debug", "net8.0"));
        File.SetLastWriteTimeUtc(release, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(debug, DateTime.UtcNow.AddHours(-2));

        var got = ServerLauncher.ResolveExecutablePath(ConfiguredRel, _appRoot, _baseDir, _cwd);
        Assert.Equal(release, got);
    }

    [Fact]
    public void MissingEverywhere_ReturnsNull()
    {
        var got = ServerLauncher.ResolveExecutablePath("does/not/exist/ECAssistant.LLM", _appRoot, _baseDir, _cwd);
        Assert.Null(got);
    }

    [Fact]
    public void RootScan_DoesNotEscape_Root()
    {
        // an executable OUTSIDE the root must not be found via root-relative candidates
        var outside = WriteExe(Path.Combine(_cwd, "ECAssistantLLM", "bin"));
        var got = ServerLauncher.ResolveExecutablePath("nope/ECAssistant.LLM", _appRoot, _baseDir, _cwd);
        Assert.NotEqual(outside, got);
    }
}
