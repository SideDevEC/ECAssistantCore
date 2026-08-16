using System.Diagnostics;
namespace ECAssistant.Core.Services;

internal class BgProcess
{
    public string Id { get; set; } = "";
    public Process Process { get; set; } = null!;
    public string Command { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TimeoutSeconds { get; set; }
    public int ExitCode { get; set; } = -1;
    public bool IsFinished { get; set; }
    public bool TimedOut { get; set; }
    public string TempScriptPath { get; set; } = "";
    public Task<string>? OutputTask { get; set; }
    public Task<string>? ErrorTask { get; set; }
}
