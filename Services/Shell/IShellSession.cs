using System.Text;

namespace ECAssistant.Core.Services.Shell;

/// <summary>
/// A persistent interactive shell session: working directory, environment variables,
/// and exported state survive across tool calls within a run — instead of every
/// command starting in a fresh process (v15, Emre 2026-09-23).
/// Implementation detail: drives ONE long-lived shell process (zsh/bash) via stdin,
/// demarking command output with unique sentinel markers. Windows uses pwsh.
/// </summary>
public interface IShellSession : IAsyncDisposable
{
    /// <summary>Execute a command inside the session (inherits cwd/env of prior calls).</summary>
    Task<ShellCommandResult> RunAsync(string command, CancellationToken ct = default);

    /// <summary>Current working directory of the session (tracked via sentinel after each cd).</summary>
    string CurrentWorkingDirectory { get; }

    /// <summary>True once the underlying shell process has exited (crash/kill).</summary>
    bool IsDead { get; }
}

/// <summary>Result of one command inside a persistent session.</summary>
public sealed record ShellCommandResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Factory: creates session shells (injected; one instance per agent run).</summary>
public interface IShellSessionFactory
{
    Task<IShellSession> CreateAsync(string initialWorkingDirectory, CancellationToken ct = default);
}
