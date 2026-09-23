namespace ECAssistant.Core.Interfaces;

/// <summary>
/// v14.13: Tier-aware post-edit verification gate. Classifies file-modifying tool
/// calls, decides whether verification applies (tier + trivial-edit rules), runs
/// the build/test pass, and formats the feedback injected into the loop.
/// </summary>
public interface IPostEditVerifier
{
    /// <summary>True when the tool call modifies files (by tool name + args only).</summary>
    bool IsFileModifyingCall(string toolName, IReadOnlyDictionary<string, string?> args);

    /// <summary>Tier-gated decision: large tier skips trivially-small edits.</summary>
    bool ShouldVerify(string toolName, IReadOnlyDictionary<string, string?> args, bool isLargeTier);

    /// <summary>Small tier = config max_rounds; large tier = 1.</summary>
    int MaxRounds(bool isLargeTier);

    /// <summary>Feedback block injected into the loop after a failed verification.</summary>
    string BuildFailureFeedback(int round, int maxRounds, VerificationResult result);

    /// <summary>Short success note injected into the loop (small tier only).</summary>
    string BuildSuccessNote();

    /// <summary>Run build (and optional configured test) and return the outcome.</summary>
    Task<VerificationResult> VerifyAsync(string? workingDir, CancellationToken ct = default);
}