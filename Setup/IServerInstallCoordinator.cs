namespace ECAssistant.Core.Setup;

/// <summary>
/// Interactive LLM-server install seam. Decides WHEN the server runtime must be
/// present and drives the install: missing → install; version mismatch → hint +
/// ask; foreign install → hint + ask to place alongside.
///
/// Runs exclusively at wizard time (first-run, /reinstall, local-provider start)
/// — never during chat. Implementations only ever write into the shared root's
/// own subpaths (server/, models/, llm-server.json) — never delete anything they
/// do not own.
/// </summary>
public interface IServerInstallCoordinator
{
    /// <summary>
    /// Ensure the server runtime is installed for a local-provider path. Idempotent.
    /// Returns true when the server is usable afterwards; false when the user
    /// declined (wizard continues with a warning).
    /// </summary>
    Task<bool> EnsureServerAsync(CancellationToken cancellationToken = default);

    /// <summary>Inspect the shared root's server directory state (no side effects).</summary>
    ServerInstallState Inspect();
}