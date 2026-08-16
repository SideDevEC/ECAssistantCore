namespace ECAssistant.Core.Engine;

public class SubTask
{
    public string Description { get; set; } = "";
    public SubTaskStatus Status { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
}
