namespace ECAssistant.Core.Services;

public class FileChange
{
    public string Path { get; set; } = "";
    public FileChangeType Type { get; set; }
    public DateTime Timestamp { get; set; }
}