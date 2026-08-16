namespace ECAssistant.Core.Analysis;

public class ProjectRelationship
{
    public string SourceFile { get; set; } = "";
    public string? TargetFile { get; set; }
    public string RelationshipType { get; set; } = "";
    public double Importance { get; set; }
}