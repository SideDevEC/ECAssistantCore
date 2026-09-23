using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services.Shell;

/// <summary>
/// Factory for per-run persistent shell sessions (injected; one store per agent run).
/// Sessions live for the duration of an agent run and are disposed with it.
/// </summary>
public sealed class ShellSessionFactory : IShellSessionFactory
{
    private readonly ILogger? _logger;

    public ShellSessionFactory(ILogger? logger = null) => _logger = logger;

    public async Task<IShellSession> CreateAsync(string initialWorkingDirectory, CancellationToken ct = default)
        => await PersistentShellSession.StartAsync(initialWorkingDirectory, _logger);
}
