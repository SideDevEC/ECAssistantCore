namespace ECAssistant.Core.Setup;

/// <summary>Default <see cref="ISetupUi"/> backed by System.Console.</summary>
public sealed class ConsoleSetupUi : ISetupUi
{
    /// <inheritdoc />
    public void WriteLine(string text = "") => Console.WriteLine(text);

    /// <inheritdoc />
    public void Write(string text) => Console.Write(text);

    /// <inheritdoc />
    public string? ReadLine() => Console.ReadLine();
}
