namespace ECAssistant.Core.Engine;

public class FileContext
{
    public string Path { get; set; } = "";
    public string Extension { get; set; } = "";
    public int Size { get; set; }
    public int Lines { get; set; }
    public int Classes { get; set; }
    public int Methods { get; set; }
    public List<string> Imports { get; set; } = new();
    public DateTime LastModified { get; set; }
}
