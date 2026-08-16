using System;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// Concrete terminal I/O implementation.
/// </summary>
public class TerminalAdapter : ITerminal
{
    public void Write(string text)
    {
        Console.Write(text);
    }

    public string ReadLine()
    {
        return Console.ReadLine() ?? string.Empty;
    }

    public int Width => Console.WindowWidth;
    public int Height => Console.WindowHeight;

    public void Clear()
    {
        Console.Clear();
    }
}