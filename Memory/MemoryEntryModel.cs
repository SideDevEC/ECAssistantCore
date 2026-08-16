namespace ECAssistant.Core.Memory;

public class MemoryEntry
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Content { get; set; } = "";
    public string Category { get; set; } = "general";
    public string RelatedProject { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public float Confidence { get; set; } = 0.8f;

    public override string ToString()
    {
        return $"[{Category}] {Key}: {Content.Substring(0, Math.Min(Content.Length, 100))}...";
    }
}
