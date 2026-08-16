using System;
using System.IO;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// Concrete file system implementation.
/// </summary>
public class FileSystemAdapter : IFileSystem
{
    public string ReadFile(string path)
    {
        return File.ReadAllText(path);
    }

    public string[] ListFiles(string directory, string pattern = "*")
    {
        return Directory.GetFiles(directory, pattern);
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public void WriteFile(string path, string content)
    {
        File.WriteAllText(path, content);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }
}