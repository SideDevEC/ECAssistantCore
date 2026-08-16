namespace ECAssistant.Core.Engine;

public class ProjectContext
{
    public List<FileContext> Files { get; set; } = new();
    public List<FileRelationship> Relationships { get; set; } = new();
    public string ProjectType { get; set; } = "Unknown";
    public string? EntryPoint { get; set; }
    public DateTime? LastScan { get; set; }
}
