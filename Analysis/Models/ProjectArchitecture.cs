namespace ECAssistant.Core.Analysis;

public class ProjectArchitecture
{
    public string ProjectRoot { get; set; } = "";
    public int Files { get; set; }
    public int Relationships { get; set; }
    public string ProjectType { get; set; } = "";
    public Dictionary<string, List<string>> DependencyGraph { get; set; } = new();
    public List<string> PotentialIssues { get; set; } = new();
}