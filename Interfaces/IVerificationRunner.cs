namespace ECAssistant.Core.Interfaces;

/// <summary>
/// v14.13: Executes one verification command (build / filtered test) in a working
/// directory. Behind an interface so the verification loop is testable without a
/// real dotnet build.
/// </summary>
public interface IVerificationRunner
{
    Task<VerificationResult> RunAsync(string command, string? workingDir = null, CancellationToken ct = default);
}