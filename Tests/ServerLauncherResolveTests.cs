using ECAssistant.Core.Services.Http;
using Xunit;

namespace ECAssistant.Core.Tests;

/// <summary>
/// v12.10 runtime contract tests: the integrating app gives Core a root folder; Core
/// copies the LLM server runtime INTO the root (root/server/) and only ever executes
/// from there. No runtime dependency on dev trees (bin/Debug siblings, repo layout).
/// </summary>
public class ServerLauncherResolveTests : IDisposable
{
    private readonly string _root;      // the root folder given to the application
    private readonly string _baseDir;   // the app binary's own folder (publish layout)
    private readonly string _devDebug;  // dev build (ECAssistantLLM/bin/Debug/net8.0)
    private readonly string _devRelease;// dev build (ECAssistantLLM/bin/Release/net8.0)

    public ServerLauncherResolveTests()
    {
        _root = CreateDir();
        _baseDir = Path.Combine(_root, "publish");
        Directory.CreateDirectory(_baseDir);
        _devDebug = Path.Combine(_root, "ECAssistantLLM", "bin", "Debug", "net8.0");
        _devRelease = Path.Combine(_root, "ECAssistantLLM", "bin", "Release", "net8.0");
        Directory.CreateDirectory(_devDebug);
        Directory.CreateDirectory(_devRelease);
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

    private static string WriteMarker(string dir)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "ECAssistant.LLM.dll");
        File.WriteAllText(p, "stub");
        return p;
    }

    private static string WriteExe(string dir)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "ECAssistant.LLM");
        File.WriteAllText(p, "stub");
        return p;
    }

    // ── ResolveServerSourceDirectory ──

    [Fact]
    public void SourceDir_PublishLayout_Preferred()
    {
        WriteMarker(_baseDir);
        WriteMarker(_devDebug);
        File.SetLastWriteTimeUtc(Path.Combine(_devDebug, "ECAssistant.LLM.dll"), DateTime.UtcNow.AddHours(1));

        var got = ServerLauncher.ResolveServerSourceDirectory(_baseDir, _root);
        Assert.Equal(_baseDir, got);
    }

    [Fact]
    public void SourceDir_DevBuild_NewestWins()
    {
        var release = WriteMarker(_devRelease);
        var debug = WriteMarker(_devDebug);
        File.SetLastWriteTimeUtc(release, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(debug, DateTime.UtcNow);

        // dev walk requires the CWD to sit in a tree whose ancestor contains ECAssistantLLM/bin
        var cwd = Path.Combine(_root, "console");
        Directory.CreateDirectory(cwd);

        var got = ServerLauncher.ResolveServerSourceDirectory(_baseDir, cwd);
        Assert.Equal(_devDebug, got);
    }

    [Fact]
    public void SourceDir_None_ReturnsNull()
    {
        Assert.Null(ServerLauncher.ResolveServerSourceDirectory(_baseDir, _root));
    }

    // ── EnsureServerBinaryCopied ──

    [Fact]
    public void Copy_CreatesRootServer_WithFilesAndSubdirs()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "ECAssistant.LLM.dll"), "stub");
        File.WriteAllText(Path.Combine(source, "ECAssistant.LLM"), "exe");
        File.WriteAllText(Path.Combine(source, "runtimes.log"), "skip me");
        Directory.CreateDirectory(Path.Combine(source, "runtimes", "osx"));
        File.WriteAllText(Path.Combine(source, "runtimes", "osx", "libllama.dylib"), "native");

        ServerLauncher.EnsureServerBinaryCopied(source, _root);

        var target = Path.Combine(_root, "server");
        Assert.True(File.Exists(Path.Combine(target, "ECAssistant.LLM.dll")));
        Assert.True(File.Exists(Path.Combine(target, "ECAssistant.LLM")));
        Assert.True(File.Exists(Path.Combine(target, "runtimes", "osx", "libllama.dylib")));
        Assert.False(File.Exists(Path.Combine(target, "runtimes.log"))); // logs skipped
    }

    [Fact]
    public void Copy_Skips_WhenUpToDate()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        WriteMarker(source);

        ServerLauncher.EnsureServerBinaryCopied(source, _root);
        var target = Path.Combine(_root, "server", "ECAssistant.LLM.dll");
        Assert.Equal("stub", File.ReadAllText(target));

        // unchanged source (mtime not newer than the copy stamp) → no re-copy work
        ServerLauncher.EnsureServerBinaryCopied(source, _root);
        Assert.Equal("stub", File.ReadAllText(target));

        // newer source build → re-copied
        Thread.Sleep(50);
        File.WriteAllText(Path.Combine(source, "ECAssistant.LLM.dll"), "NEW BUILD");
        File.SetLastWriteTimeUtc(Path.Combine(source, "ECAssistant.LLM.dll"), DateTime.UtcNow.AddMinutes(1));
        ServerLauncher.EnsureServerBinaryCopied(source, _root);
        Assert.Equal("NEW BUILD", File.ReadAllText(target));
    }

    [Fact]
    public void Copy_NullOrInvalidSource_IsNoOp()
    {
        ServerLauncher.EnsureServerBinaryCopied(null, _root);
        ServerLauncher.EnsureServerBinaryCopied(Path.Combine(_root, "nope"), _root);
        Assert.False(Directory.Exists(Path.Combine(_root, "server")));
    }

    // ── ResolveExecutablePath ──

    [Fact]
    public void Resolve_AbsolutePath_Wins()
    {
        var exe = WriteExe(Path.Combine(_root, "anywhere"));
        var got = ServerLauncher.ResolveExecutablePath(exe, _root, _baseDir, _root);
        Assert.Equal(exe, got);
    }

    [Fact]
    public void Resolve_PrimaryLocation_IsRootServer()
    {
        var exe = WriteExe(Path.Combine(_root, "server"));
        var got = ServerLauncher.ResolveExecutablePath("../ECAssistantLLM/bin/Release/net8.0/ECAssistant.LLM", _root, _baseDir, _root);
        Assert.Equal(exe, got);
    }

    [Fact]
    public void Resolve_RootScan_FindsTwoLevelsDeep()
    {
        var exe = WriteExe(Path.Combine(_root, "server", "bin", "Release", "net8.0"));
        var got = ServerLauncher.ResolveExecutablePath("missing/dir/ECAssistant.LLM", _root, _baseDir, _root);
        Assert.Equal(exe, got);
    }

    [Fact]
    public void Resolve_PublishLayout_Found()
    {
        var exe = WriteExe(_baseDir);
        var got = ServerLauncher.ResolveExecutablePath("ECAssistant.LLM", _root, _baseDir, _root);
        Assert.Equal(exe, got);
    }

    [Fact]
    public void Resolve_Missing_ReturnsNull()
    {
        Assert.Null(ServerLauncher.ResolveExecutablePath("nope/ECAssistant.LLM", _root, _baseDir, _root));
    }
}
