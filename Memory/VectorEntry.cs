namespace ECAssistant.Core.Memory;

public class VectorEntry
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Content { get; set; } = "";
    public string Category { get; set; } = "general";
    public Dictionary<string, string> Tags { get; set; } = new();
    public float[]? Vector { get; set; }
    public string Timestamp { get; set; } = "";
}