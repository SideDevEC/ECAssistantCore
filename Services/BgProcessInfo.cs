namespace ECAssistant.Core.Services;

public class BgProcessInfo
{
    public string Id { get; set; } = "";
    public string Command { get; set; } = "";
    public BgStatus Status { get; set; }
    public int ExitCode { get; set; }
    public double ElapsedSeconds { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public override string ToString()
    {
        var elapsed = ElapsedSeconds > 60
            ? $"{(int)ElapsedSeconds / 60}m {(int)ElapsedSeconds % 60}s"
            : $"{ElapsedSeconds:F1}s";
        return $"[{Id}] {Status} | {elapsed} | exit={ExitCode} | {Command.Substring(0, Math.Min(Command.Length, 60))}";
    }
}