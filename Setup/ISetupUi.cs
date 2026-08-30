namespace ECAssistant.Core.Setup;

/// <summary>Console I/O abstraction for the setup wizard — enables unit testing of flow logic.</summary>
public interface ISetupUi
{
    /// <summary>Writes a line of output.</summary>
    void WriteLine(string text = "");

    /// <summary>Writes text without a trailing newline.</summary>
    void Write(string text);

    /// <summary>Reads a line of input (may be null on EOF).</summary>
    string? ReadLine();

    /// <summary>Writes a line highlighted in green (installed models). Falls back to plain output.</summary>
    void WriteLineGreen(string text) => WriteLine(text);
}
