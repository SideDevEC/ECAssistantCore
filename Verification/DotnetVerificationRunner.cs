using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Verification;

/// <summary>
/// v14.13: Real verification runner — executes the configured build/test command via
/// IProcessRunner and returns the tail of the combined output (errors live at the end
/// of build output, so the tail is what matters for feedback).
/// </summary>
public sealed class DotnetVerificationRunner : IVerificationRunner
{
    private const int MaxOutputChars = 4000;
    private readonly IProcessRunner _processRunner;

    public DotnetVerificationRunner(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<VerificationResult> RunAsync(string command, string? workingDir = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new VerificationResult(false, "(no verification command configured)", command, -1);

        var result = await _processRunner.ExecuteAsync(command, workingDir, ct);
        var output = (result.StdOut ?? "") + (string.IsNullOrEmpty(result.StdErr) ? "" : "\n" + result.StdErr);
        if (output.Length > MaxOutputChars) output = output[^MaxOutputChars..];
        var succeeded = result.ExitCode == 0 && !result.TimedOut;
        return new VerificationResult(succeeded, output.Trim(), command, result.ExitCode);
    }
}