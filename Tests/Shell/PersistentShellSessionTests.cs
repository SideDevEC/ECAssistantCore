using ECAssistant.Core.Services.Shell;

namespace ECAssistant.Core.Tests.Shell;

/// <summary>
/// v15: persistent shell session — working directory and exported env survive
/// across RunAsync calls. POSIX-only (skips on Windows). Uses the REAL shell
/// process (zsh/bash) — the protocol is the thing under test.
/// </summary>
public sealed class PersistentShellSessionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-shellsession").FullName;

    [Fact]
    public async Task Cwd_Persists_AcrossCalls()
    {
        if (!PersistentShellSession.IsSupported) return; // POSIX only
        await using var session = await PersistentShellSession.StartAsync(_dir);

        var cd = await session.RunAsync($"mkdir -p nested && cd nested");
        Assert.Equal(0, cd.ExitCode);

        var pwd = await session.RunAsync("pwd");
        Assert.Equal(0, pwd.ExitCode);
        Assert.Contains("nested", pwd.StdOut);
        Assert.EndsWith("nested", session.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task ExportedEnv_Persists_AcrossCalls()
    {
        if (!PersistentShellSession.IsSupported) return; // POSIX only
        await using var session = await PersistentShellSession.StartAsync(_dir);

        await session.RunAsync("export ECA_TEST_TOKEN=hello123");
        var check = await session.RunAsync("echo $ECA_TEST_TOKEN");
        Assert.Contains("hello123", check.StdOut);
    }

    [Fact]
    public async Task ExitCode_And_Stderr_AreCaptured()
    {
        if (!PersistentShellSession.IsSupported) return; // POSIX only
        await using var session = await PersistentShellSession.StartAsync(_dir);

        var fail = await session.RunAsync("echo oops >&2; (exit 3)"); // subshell: rc 3 without killing the session
        Assert.Equal(3, fail.ExitCode);
        Assert.Contains("oops", fail.StdErr);

        var ok = await session.RunAsync("printf 'fine\\n'");
        Assert.Equal(0, ok.ExitCode);
        Assert.Equal("fine", ok.StdOut);
    }

    [Fact]
    public async Task Session_KillsShell_OnDispose()
    {
        if (!PersistentShellSession.IsSupported) return; // POSIX only
        var session = await PersistentShellSession.StartAsync(_dir);
        await session.DisposeAsync();
        Assert.True(session.IsDead);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}

/// <summary>v15: teardown sweep — session artifacts cleaned on dispose.</summary>
public sealed class ShellTeardownSweepTests
{
    [Fact]
    public async Task SessionDispose_RemovesTempErrorFiles()
    {
        var session = await PersistentShellSession.StartAsync(Directory.CreateTempSubdirectory("eca-sweep").FullName);
        // simulate a leaked error file with the session's pid pattern
        var leaked = Path.Combine(Path.GetTempPath(), $"eca_shell_err_{Environment.ProcessId}_9999");
        await File.WriteAllTextAsync(leaked, "leftover");
        Assert.True(File.Exists(leaked));

        await session.DisposeAsync();
        Assert.False(File.Exists(leaked)); // swept
    }
}
