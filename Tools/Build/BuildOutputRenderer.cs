using System.Text;

namespace ECAssistant.Core.Tools.Build;

/// <summary>
/// v14.20: semantic render of .NET build/test output for the model context.
/// One renderer shared by EDotnetBuildTool and EShellAgent (when it runs dotnet
/// commands). Turns a full build log into the facts the next decision needs:
/// verdict, parsed errors (file:line, code, message — capped), warning count,
/// test summary when present. Deterministic — parses the output again with the
/// same BuildErrorParser the tool used, so oddly-phrased failure lines can't
/// slip past keyword heuristics.
/// Stateless utility — no mutable state.
/// </summary>
public static class BuildOutputRenderer
{
    /// <summary>Maximum error lines kept in the model-facing render.</summary>
    public const int MaxErrors = 6;

    private static readonly BuildErrorParser Parser = new();

    /// <summary>True when the text looks like dotnet build/test output.</summary>
    public static bool IsDotnetOutput(string output)
    {
        if (string.IsNullOrEmpty(output)) return false;
        return output.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
            || output.Contains("error CS", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Passed!", StringComparison.Ordinal)
            || output.Contains("Failed!", StringComparison.Ordinal)
            || output.Contains("MSBuild", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Render dotnet build/test output as a compact, decision-ready projection.
    /// Pure function — deterministic, no side effects.
    /// </summary>
    // Stateless utility — no mutable state
    public static string Render(string output)
    {
        var errors = Parser.ParseErrors(output);
        var warnings = Parser.ParseWarnings(output);

        var failed = output.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
                     || errors.Count > 0
                     || (output.Contains("Failed!", StringComparison.Ordinal)
                         && !output.Contains("Passed!", StringComparison.Ordinal));

        var sb = new StringBuilder();
        sb.Append(failed ? "[BUILD FAILED]" : "[BUILD OK]");
        sb.Append($" {errors.Count} error(s), {warnings.Count} warning(s).");

        // Test-run summary line (dotnet test), when present.
        var testSummary = ExtractTestSummary(output);
        if (testSummary != null)
            sb.Append(' ').Append(testSummary);

        if (errors.Count > 0)
        {
            sb.AppendLine();
            sb.Append("ERRORS:");
            foreach (var e in errors.Take(MaxErrors))
            {
                sb.Append($"\n  {ShortenPath(e.File)}({e.Line}): {e.Code}: {e.Message}");
            }
        }

        var warnLines = ExtractWarningLines(output, warnings.Count);
        if (warnLines.Count > 0)
        {
            sb.AppendLine();
            sb.Append("Warnings:");
            foreach (var w in warnLines)
                sb.Append($"\n  {w}");
        }

        return sb.ToString();
    }

    private static string ShortenPath(string file)
    {
        // Absolute repo paths burn tokens for zero decision value — keep the tail.
        if (string.IsNullOrEmpty(file)) return file;
        var idx = file.LastIndexOf('/');
        return idx >= 0 && idx < file.Length - 1 ? file[(idx + 1)..] : file;
    }

    private static string? ExtractTestSummary(string output)
    {
        // dotnet test tail: "Passed!  - Failed: 0, Passed: 12, Skipped: 0, ..."
        var idx = output.LastIndexOf("Passed!", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var tail = output[idx..];
            var nl = tail.IndexOf('\n');
            return (nl > 0 ? tail[..nl] : tail).Trim();
        }
        idx = output.LastIndexOf("Failed!", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var tail = output[idx..];
            var nl = tail.IndexOf('\n');
            return (nl > 0 ? tail[..nl] : tail).Trim();
        }
        return null;
    }

    private static List<string> ExtractWarningLines(string output, int warningCount)
    {
        var result = new List<string>();
        if (warningCount == 0) return result;
        foreach (var line in output.Split('\n'))
        {
            if (result.Count >= 3) break;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("warning ", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains(") : warning ", StringComparison.OrdinalIgnoreCase))
                result.Add(trimmed.TrimEnd());
        }
        return result;
    }
}