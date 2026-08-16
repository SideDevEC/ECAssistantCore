namespace ECAssistant.Core.Interfaces;

public record ProcessResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut
);