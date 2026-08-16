namespace ECAssistant.Core.Session;

public class OutputEntry
{
    /// <summary>"stream" (accumulated tokens) or "line" (a discrete line)</summary>
    public string Type { get; set; } = "line";

    /// <summary>The output text content</summary>
    public string Text { get; set; } = "";

    /// <summary>Output state (Info, Success, Warning, etc.)</summary>
    public OutputState State { get; set; } = OutputState.Raw;

    /// <summary>ISO-8601 timestamp</summary>
    public string Ts { get; set; } = DateTime.UtcNow.ToString("O");
}