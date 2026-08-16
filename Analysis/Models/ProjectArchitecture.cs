namespace ECAssistant.Core.Analysis;

public class ProjectArchitecture
{
    public string ProjectRoot { get; init; } = "";
    public int Files { get; init; }
    public int Relationships { get; init; }
    public string ProjectType { get; init; } = "";
    public Dictionary<string, List<string>> DependencyGraph { get; init; } = new();
    public List<string> PotentialIssues { get; init; } = new();
}