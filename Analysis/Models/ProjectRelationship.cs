namespace ECAssistant.Core.Analysis;

public class ProjectRelationship
{
    public string SourceFile { get; init; } = "";
    public string? TargetFile { get; init; }
    public string RelationshipType { get; init; } = "";
    public double Importance { get; init; }
}