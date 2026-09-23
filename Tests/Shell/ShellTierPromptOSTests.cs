using ECAssistant.Core.Services.Shell;
using ECAssistant.Core.Tools.Shell;

namespace ECAssistant.Core.Tests.Shell;

/// <summary>
/// v15: EShellAgent prompt matrix — 3 OSes × 2 tiers. The tool's rules are
/// runtime-OS-sensitive (OperatingSystem.* checks), so these tests assert the
/// CURRENT platform's dialect AND invariants that must hold on ALL platforms.
/// CI runs the suite on windows/macos/linux runners — every run checks its own
/// OS-specific branch plus the cross-platform invariants.
/// </summary>
public sealed class ShellTierPromptOSTests
{
    private static EShellAgent CreateTool(bool isLargeTier) => new(
        new ECAssistant.Core.Services.ProcessRunner(),
        new ECAssistant.Core.Config.AppConfig(),
        Directory.GetCurrentDirectory(),
        sessionFactory: null,
        isLargeTier: isLargeTier);

    private static string Rules(bool isLargeTier) =>
        CreateTool(isLargeTier).GetToolRulesForTier(isLargeTier);

    // ── cross-platform invariants (must hold on EVERY OS) ──

    [Fact]
    public void AllOS_PersistenceMentioned_BothTiers()
    {
        Assert.Contains("PERSISTENT shell session", Rules(isLargeTier: true));
        Assert.Contains("PERSISTENT shell session", Rules(isLargeTier: false));
    }

    [Fact]
    public void AllOS_SandboxMentionedOnlyForLarge()
    {
        var large = Rules(isLargeTier: true);
        var small = Rules(isLargeTier: false);
        if (OperatingSystem.IsMacOS())
        {
            Assert.Contains("sandbox", large, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sandbox", small, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // Non-macOS: no sandbox at all — must not appear in either tier
            Assert.DoesNotContain("sandbox", large, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sandbox", small, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AllOS_LargeTier_HasNoSmallTierNags()
    {
        var large = Rules(isLargeTier: true);
        Assert.DoesNotContain("ONE command per call", large);
        Assert.DoesNotContain("NEVER use interactive commands", large);
    }

    // ── Windows dialect (only assertable on Windows runners) ──

    [Fact]
    public void Windows_PowerShellVocabulary_WhenOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return; // asserted on the Windows CI leg
        foreach (var large in new[] { true, false })
        {
            var rules = Rules(large);
            Assert.Contains("PowerShell", rules);
            Assert.Contains("$env:", rules);
            Assert.Contains("NEVER bash commands", rules);
            Assert.DoesNotContain("(cd)", rules);           // POSIX vocab
            Assert.DoesNotContain("re-export", rules);       // POSIX vocab
            Assert.DoesNotContain("&& or ;", rules);         // bash composition
        }
    }

    // ── POSIX dialect (macOS + Linux CI legs) ──

    [Fact]
    public void POSIX_CdExportVocabulary_WhenOnPosix()
    {
        if (OperatingSystem.IsWindows()) return; // asserted on POSIX legs
        foreach (var large in new[] { true, false })
        {
            var rules = Rules(large);
            Assert.Contains("(cd)", rules);
            Assert.Contains("re-export", rules);
            Assert.DoesNotContain("$env:", rules);
            Assert.DoesNotContain("Set-Location", rules);
        }
    }

    [Fact]
    public void POSIX_SmallTier_KeepsDoNots()
    {
        if (OperatingSystem.IsWindows()) return;
        var small = Rules(isLargeTier: false);
        Assert.Contains("ONE command per call", small);
        Assert.Contains("NEVER chain with && or ;", small);
        Assert.Contains("head, tail, or grep", small);
    }

    [Fact]
    public void POSIX_LargeTier_CompositionAllowed()
    {
        if (OperatingSystem.IsWindows()) return;
        var large = Rules(isLargeTier: true);
        Assert.Contains("&& or ;", large); // batching allowed on POSIX
    }

    // ── macOS-only: Seatbelt awareness ──

    [Fact]
    public void MacOS_LargeTier_SeatbeltExplained()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var large = Rules(isLargeTier: true);
        Assert.Contains("Seatbelt", large);
        Assert.Contains("Operation not permitted", large); // sandbox failure signature
        Assert.Contains("Network access is denied", large);
    }

    // ── session implementation selection per OS ──

    [Fact]
    public void SessionFactory_PicksPlatformImplementation()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            Assert.True(PersistentShellSession.IsSupported);
            Assert.False(PersistentPowerShellSession.IsSupported);
        }
        else if (OperatingSystem.IsWindows())
        {
            Assert.False(PersistentShellSession.IsSupported);
            Assert.True(PersistentPowerShellSession.IsSupported);
        }
    }

    [Fact]
    public async Task SessionFactory_CreatesLiveSession_OnThisPlatform()
    {
        // Requires a session factory (hosts opt in) — direct creation per OS.
        var dir = Directory.CreateTempSubdirectory("eca-os-session").FullName;
        try
        {
            var factory = new ShellSessionFactory();
            await using var session = await factory.CreateAsync(dir);
            Assert.False(session.IsDead);

            var r = await session.RunAsync(
                OperatingSystem.IsWindows() ? "Write-Output probe-ok" : "echo probe-ok");
            Assert.Equal(0, r.ExitCode);
            Assert.Contains("probe-ok", r.StdOut);
        }
        catch (PlatformNotSupportedException)
        {
            // CI without pwsh on Windows — acceptable, session falls back to isolated
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
