using System.Text.RegularExpressions;

namespace ECAssistant.Core.Tools.Build;

/// <summary>
/// Parser for .NET build output — extracts errors and warnings.
/// Stateless, no dependencies. Instance class for OOP compliance.
/// </summary>
public class BuildErrorParser
{
    /// <summary>Parse build errors from dotnet build output.</summary>
    public List<BuildError> ParseErrors(string output)
    {
        var errors = new List<BuildError>();
        var pattern = @"(.+?)\((\d+),(\d+)\):\s+(error|fatal error)\s+(\w+):\s+(.+)$";
        foreach (Match m in Regex.Matches(output, pattern, RegexOptions.Multiline))
            errors.Add(new BuildError(m.Groups[1].Value, int.Parse(m.Groups[2].Value), m.Groups[5].Value, m.Groups[6].Value));
        return errors;
    }

    /// <summary>Parse build warnings from dotnet build output.</summary>
    public List<BuildError> ParseWarnings(string output)
    {
        var warnings = new List<BuildError>();
        var pattern = @"(.+?)\((\d+),(\d+)\):\s+warning\s+(\w+):\s+(.+)$";
        foreach (Match m in Regex.Matches(output, pattern, RegexOptions.Multiline))
            warnings.Add(new BuildError(m.Groups[1].Value, int.Parse(m.Groups[2].Value), m.Groups[4].Value, m.Groups[5].Value));
        return warnings;
    }
}

public record BuildError(string File, int Line, string Code, string Message);