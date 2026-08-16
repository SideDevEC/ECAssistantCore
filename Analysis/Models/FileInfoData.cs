namespace ECAssistant.Core.Analysis;

public class FileInfoData
{
    public string FilePath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int FileSize { get; set; }
    public int LineCount { get; set; }
    public int ClassCount { get; set; }
    public int MethodCount { get; set; }
    public int TodoCount { get; set; }
    public List<string>? ImportList { get; set; }
    public DateTime LastModified { get; set; }
}