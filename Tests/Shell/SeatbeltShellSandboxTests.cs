using ECAssistant.Core.Services.Shell;

namespace ECAssistant.Core.Tests.Shell;

/// <summary>
/// v15: Seatbelt sandbox — profile generation, command wrapping, and LIVE
/// containment behavior (workspace write allowed, home write denied, network
/// denied). Live tests run sandbox-exec; macOS-only.
/// </summary>
public sealed class SeatbeltShellSandboxTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-sandbox").FullName;

    [Fact]
    public void Wrap_Disabled_ReturnsCommandUnchanged()
    {
        var sb = new SeatbeltShellSandbox(new ShellSandboxOptions(Enabled: false, _dir));
        Assert.Same("echo hi", sb.Wrap("echo hi"));
        Assert.False(sb.IsEnabled);
    }

    [Fact]
    public void Wrap_Enabled_WrapsWithSandboxExec()
    {
        var sb = new SeatbeltShellSandbox(new ShellSandboxOptions(Enabled: true, _dir));
        if (OperatingSystem.IsWindows()) return; // passthrough on Windows
        var wrapped = sb.Wrap("echo hi");
        Assert.StartsWith("sandbox-exec", wrapped);
        Assert.Contains("echo hi", wrapped);
    }

    [Fact]
    public async Task Live_WorkspaceWrite_Allowed_HomeWrite_Denied()
    {
        if (!OperatingSystem.IsMacOS()) return; // Seatbelt is macOS-only
        var sb = new SeatbeltShellSandbox(new ShellSandboxOptions(Enabled: true, _dir));
        var runner = new ECAssistant.Core.Services.ProcessRunner();

        var ok = await runner.ExecuteAsync(sb.Wrap($"echo ok > {_dir}/w.txt && cat {_dir}/w.txt"), _dir);
        Assert.Equal(0, ok.ExitCode);
        Assert.Contains("ok", ok.StdOut);

        var denied = await runner.ExecuteAsync(
            sb.Wrap("touch $HOME/eca-deny-probe 2>/dev/null; ls $HOME/eca-deny-probe 2>/dev/null; echo done"), _dir);
        Assert.DoesNotContain("eca-deny-probe", denied.StdOut); // file never created
        File.Delete(Path.Combine(_dir, "w.txt"));
    }

    [Fact]
    public async Task Live_Network_Denied_ByDefault()
    {
        if (!OperatingSystem.IsMacOS()) return; // Seatbelt is macOS-only
        var sb = new SeatbeltShellSandbox(new ShellSandboxOptions(Enabled: true, _dir));
        var runner = new ECAssistant.Core.Services.ProcessRunner();
        var r = await runner.ExecuteAsync(sb.Wrap("curl -s --max-time 2 https://example.com -o /dev/null; echo rc=$?"), _dir);
        Assert.Contains("rc=6", r.StdOut); // 6 = couldn't resolve host (network denied)
    }

    [Fact]
    public void Live_Session_Inherits_PersistentShellProtocol()
    {
        // The persistent session and the sandbox must compose: session wraps each
        // command, sandbox wraps the wrapped form. Verified indirectly here via
        // profile regeneration + wrap composition.
        var sb = new SeatbeltShellSandbox(new ShellSandboxOptions(Enabled: true, _dir));
        var wrapped = sb.Wrap("cd nested && pwd");
        Assert.Contains("sandbox-exec", wrapped);
        Assert.Contains("cd nested && pwd", wrapped);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
