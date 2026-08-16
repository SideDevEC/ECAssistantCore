namespace ECAssistant.Core.Testing;

public class TestResult
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string Description { get; set; } = "";
    public string FailureReason { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public string FinalOutput { get; set; } = "";
    public int ToolCallsMade { get; set; }
    public string CapturedLog { get; set; } = "";
}
