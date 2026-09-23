using System.Text.RegularExpressions;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Verification;

/// <summary>
/// v14.13: Default post-edit verifier. Pure classification/gating logic plus an
/// async pass that delegates command execution to IVerificationRunner (real dotnet
/// build lives behind that seam — tests inject a stub).
/// </summary>
public sealed class PostEditVerifier : IPostEditVerifier
{
    // Mutating ECodeEditor actions (diff/search are read-only).
    private static readonly HashSet<string> MutatingCodeActions = new(StringComparer.Ordinal)
    { "create", "patch", "replace-all", "insert", "delete-lines", "delete" };

    // Write/delete/move indicators for EShellAgent commands. Heuristic by design —
    // a shell command is free-form text; we prefer over-triggering (extra verification)
    // over missing a real edit. Pure lookup tables — no mutable state.
    private static readonly Regex ShellWritePattern = new(
        @"(^|\s|;|&&|\|)(rm|mv|cp|mkdir|touch|tee|ln|dd|chmod|sed)\b" +
        @"|\b(Set-Content|Add-Content|Out-File|New-Item|Remove-Item|Move-Item|Copy-Item|Clear-Content|Copy-ItemProperty)\b" +
        @"|(^|\s)(>>|>)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IVerificationRunner _runner;
    private readonly VerificationConfig _config;

    public PostEditVerifier(IVerificationRunner runner, VerificationConfig config)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc/>
    public bool IsFileModifyingCall(string toolName, IReadOnlyDictionary<string, string?> args)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return false;
        args ??= new Dictionary<string, string?>();

        if (toolName.Equals("ECodeEditor", StringComparison.OrdinalIgnoreCase))
        {
            var action = args.GetValueOrDefault("action")?.Trim().ToLowerInvariant();
            return action != null && MutatingCodeActions.Contains(action);
        }

        if (toolName.Equals("EShellAgent", StringComparison.OrdinalIgnoreCase))
        {
            var command = args.GetValueOrDefault("command") ?? "";
            return IsShellWriteCommand(command);
        }

        return false;
    }

    /// <summary>Heuristic: does this shell command likely write/move/delete files? Pure.</summary>
    // Stateless regex classification — no mutable state.
    private static bool IsShellWriteCommand(string command) =>
        !string.IsNullOrWhiteSpace(command) && ShellWritePattern.IsMatch(command);

    /// <summary>Large-tier trivial-edit check: single-line payload under the char threshold. Pure.</summary>
    // Stateless comparison — no mutable state.
    private bool IsTrivialEdit(IReadOnlyDictionary<string, string?> args)
    {
        var payload = args.GetValueOrDefault("new_text") ?? args.GetValueOrDefault("content") ?? "";
        if (payload.Length == 0) return false;
        return !payload.Contains('\n') && payload.Length <= _config.TrivialEditMaxChars;
    }

    /// <inheritdoc/>
    public bool ShouldVerify(string toolName, IReadOnlyDictionary<string, string?> args, bool isLargeTier)
    {
        if (!IsFileModifyingCall(toolName, args)) return false;
        // Large tier keeps scaffolding slim: skip trivially-small edits.
        if (isLargeTier && IsTrivialEdit(args)) return false;
        return true;
    }

    /// <inheritdoc/>
    public int MaxRounds(bool isLargeTier) =>
        isLargeTier ? 1 : Math.Max(1, _config.MaxRounds);

    /// <inheritdoc/>
    public async Task<VerificationResult> VerifyAsync(string? workingDir, CancellationToken ct = default)
    {
        var build = await _runner.RunAsync(_config.BuildCommand, workingDir, ct);
        if (!build.Succeeded) return build;

        if (string.IsNullOrWhiteSpace(_config.TestCommand)) return build;

        var tests = await _runner.RunAsync(_config.TestCommand, workingDir, ct);
        if (tests.Succeeded) return tests;

        // Build passed, tests failed — report the test failure.
        return new VerificationResult(false, tests.Output, tests.Command, tests.ExitCode);
    }

    /// <inheritdoc/>
    public string BuildFailureFeedback(int round, int maxRounds, VerificationResult result)
    {
        var tail = result.Output;
        if (tail.Length > 1500) tail = tail[^1500..];
        var lines = "[VERIFY FAIL] " +
                    $"Your file edit broke the build verification (round {round}/{maxRounds}):\n" +
                    $"Command: {result.Command}\n" + tail + "\n" +
                    "Fix the reported errors with a file edit. Do NOT start unrelated work.";
        if (round < maxRounds)
            lines += "\nAfter your fix, verification will run again.";
        return lines;
    }

    /// <inheritdoc/>
    public string BuildSuccessNote() =>
        "[VERIFY] Post-edit build verification passed — the project compiles.";
}