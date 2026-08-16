namespace ECAssistant.Core.Memory;

public class VectorSearchResult
{
    public string Key { get; set; } = "";
    public string Content { get; set; } = "";
    public string Category { get; set; } = "";
    public float Score { get; set; }
    public string Timestamp { get; set; } = "";

    public override string ToString() => $"[{Category}] {Key} (score: {Score:F3})";
}