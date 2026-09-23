namespace ECAssistant.Core.Interfaces;

/// <summary>
/// v14.13: Immutable outcome of one verification run (build + optional test).
/// </summary>
public sealed class VerificationResult
{
    public bool Succeeded { get; }
    public string Output { get; }
    public string Command { get; }
    public int ExitCode { get; }

    public VerificationResult(bool succeeded, string output, string command, int exitCode)
    {
        Succeeded = succeeded;
        Output = output ?? "";
        Command = command ?? "";
        ExitCode = exitCode;
    }
}