using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services.Shell;

/// <summary>
/// Factory for per-run persistent shell sessions — picks the platform
/// implementation: zsh/bash on macOS/Linux, pwsh on Windows. Falls back to
/// null when the platform cannot host a session (caller then runs isolated).
/// </summary>
public sealed class ShellSessionFactory : IShellSessionFactory
{
    private readonly ILogger? _logger;

    public ShellSessionFactory(ILogger? logger = null) => _logger = logger;

    public async Task<IShellSession> CreateAsync(string initialWorkingDirectory, CancellationToken ct = default)
    {
        if (PersistentShellSession.IsSupported)
            return await PersistentShellSession.StartAsync(initialWorkingDirectory, _logger);
        if (PersistentPowerShellSession.IsSupported)
            return await PersistentPowerShellSession.StartAsync(initialWorkingDirectory, _logger);
        throw new PlatformNotSupportedException("No persistent shell implementation for this platform.");
    }
}
