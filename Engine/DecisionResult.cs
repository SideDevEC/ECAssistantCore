namespace ECAssistant.Core.Engine;

public class DecisionResult
{
    public bool Success { get; set; }
    public string? OptionChosen { get; set; }
    public string Outcome { get; set; } = "";
}
