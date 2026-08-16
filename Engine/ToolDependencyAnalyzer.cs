using System.Text.RegularExpressions;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Analyzes a batch of toolcalls and determines which can run in parallel
/// vs which depend on results of earlier calls.
/// OS-aware: detects file targets in both PowerShell (Windows) and bash/zsh (macOS/Linux) commands.
/// </summary>
public class ToolDependencyAnalyzer
{
    private readonly HashSet<string> _postModifyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "EDotnetBuild", "EGitTool"
    };

    private readonly HashSet<string> _alwaysIndependentTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "EBackgroundExec"
    };

    // ── Write patterns for each OS ──────────────────

    private static readonly string[] _powershellWritePatterns = {
        "set-content", "add-content", "out-file", "tee-object",
        "remove-item", "copy-item", "move-item", "new-item",
        "invoke-webrequest", "start-process",
        "-replace", "mkdir", "rmdir", "del ", "rm ",
        "git commit", "git push", "git checkout", "git merge",
        "dotnet build", "dotnet test", "dotnet format", "dotnet run",
        "dotnet publish", "dotnet pack"
    };

    private static readonly string[] _unixWritePatterns = {
        "sed -i", "echo ", "cat >", "cat >>", "tee ",
        "rm ", "rm -", "rmdir", "mkdir -p", "mkdir ",
        "mv ", "cp ", "cp -", "touch ",
        "chmod", "chown",
        "git commit", "git push", "git checkout", "git merge",
        "dotnet build", "dotnet test", "dotnet format", "dotnet run",
        "dotnet publish", "dotnet pack",
        ">", ">>"
    };

    public List<DependencyGroup> Analyze(List<ToolCallRequest> toolCalls)
    {
        if (toolCalls.Count <= 1)
        {
            return new List<DependencyGroup>
            {
                new() { ToolCalls = toolCalls, GroupIndex = 0 }
            };
        }

        var targets = new List<HashSet<string>>();
        foreach (var tc in toolCalls)
        {
            targets.Add(ExtractTargets(tc));
        }

        var modifies = new bool[toolCalls.Count];
        for (int i = 0; i < toolCalls.Count; i++)
        {
            modifies[i] = IsModifyingTool(toolCalls[i]);
        }

        var deps = new List<int>[toolCalls.Count];
        for (int i = 0; i < toolCalls.Count; i++)
            deps[i] = new List<int>();

        for (int i = 0; i < toolCalls.Count; i++)
        {
            for (int j = 0; j < toolCalls.Count; j++)
            {
                if (i == j) continue;

                bool depends = false;

                if (_alwaysIndependentTools.Contains(toolCalls[j].ToolName ?? ""))
                {
                    depends = false;
                }
                else
                {
                    if (modifies[i] && targets[i].Overlaps(targets[j]))
                        depends = true;

                    if (_postModifyTools.Contains(toolCalls[j].ToolName ?? "") && modifies[i])
                        depends = true;

                    if (toolCalls[i].ToolName?.Equals("ECodeEditor", StringComparison.OrdinalIgnoreCase) == true &&
                        toolCalls[j].ToolName?.Equals("ECodeEditor", StringComparison.OrdinalIgnoreCase) == true &&
                        targets[i].Overlaps(targets[j]))
                        depends = true;
                }

                if (depends && !deps[j].Contains(i))
                    deps[j].Add(i);
            }
        }

        var groups = new List<DependencyGroup>();
        var processed = new bool[toolCalls.Count];
        int groupIndex = 0;

        while (processed.Count(p => p) < toolCalls.Count)
        {
            var currentBatch = new List<ToolCallRequest>();

            for (int i = 0; i < toolCalls.Count; i++)
            {
                if (!processed[i] && deps[i].All(d => processed[d]))
                {
                    currentBatch.Add(toolCalls[i]);
                    processed[i] = true;
                }
            }

            if (currentBatch.Count == 0)
            {
                for (int i = 0; i < toolCalls.Count; i++)
                {
                    if (!processed[i])
                    {
                        currentBatch.Add(toolCalls[i]);
                        processed[i] = true;
                    }
                }
            }

            if (currentBatch.Count > 0)
                groups.Add(new DependencyGroup { ToolCalls = currentBatch, GroupIndex = groupIndex++ });
        }

        return groups;
    }

    private HashSet<string> ExtractTargets(ToolCallRequest tc)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tc.Args == null) return targets;

        foreach (var kvp in tc.Args)
        {
            if (string.IsNullOrEmpty(kvp.Value)) continue;

            var key = kvp.Key?.ToLowerInvariant() ?? "";
            var value = kvp.Value;

            if (key == "file" || key == "path" || key == "filename" || key == "filepath")
            {
                foreach (var f in SplitPaths(value))
                    targets.Add(f);
            }

            if (key == "file" && tc.ToolName?.Equals("ECodeEditor", StringComparison.OrdinalIgnoreCase) == true)
                targets.Add(value.Trim().ToLowerInvariant());

            if (tc.ToolName?.Equals("EShellAgent", StringComparison.OrdinalIgnoreCase) == true &&
                (key == "command" || key == "script"))
            {
                // Detect both PowerShell and Unix shell file targets
                targets.UnionWith(ExtractPathsFromPowerShell(value));
                targets.UnionWith(ExtractPathsFromUnixShell(value));
            }

            if (tc.ToolName?.Equals("EGitTool", StringComparison.OrdinalIgnoreCase) == true && key == "file")
                targets.Add(value.Trim().ToLowerInvariant());
        }

        return targets;
    }

    private IEnumerable<string> SplitPaths(string value)
    {
        foreach (var part in value.Split(';', ',', '|'))
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                yield return trimmed.ToLowerInvariant();
        }
    }

    /// <summary>Extract file paths from PowerShell commands (Windows).</summary>
    private HashSet<string> ExtractPathsFromPowerShell(string command)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(command)) return paths;

        var patterns = new[]
        {
            @"(?:Get-Content|Set-Content|Get-ChildItem|Remove-Item|Copy-Item|Move-Item|New-Item|Add-Content|Out-File|Tee-Object)\s+(?:-Path\s+)?[`'\""]?([^`'\"";|&\s]+)[`'\""]?",
            @"(?:Get-Content|Set-Content|Get-ChildItem|Remove-Item|Copy-Item|Move-Item|New-Item|Add-Content|Out-File|Tee-Object)\s+[`'\""]?([A-Za-z0-9_\\/.\-]+)[`'\""]?"
        };

        foreach (var pattern in patterns)
        {
            foreach (Match m in Regex.Matches(command, pattern, RegexOptions.IgnoreCase))
            {
                var p = m.Groups[1].Value.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(p) && p.Length > 2)
                    paths.Add(p);
            }
        }

        // Also catch bare filenames in PowerShell commands
        foreach (Match m in Regex.Matches(command, @"\b([A-Za-z0-9_\-]+\.(?:cs|md|json|ps1|txt|csproj|sln|xaml))\b", RegexOptions.IgnoreCase))
            paths.Add(m.Groups[1].Value.Trim().ToLowerInvariant());

        return paths;
    }

    /// <summary>Extract file paths from Unix shell commands (macOS/Linux).</summary>
    private HashSet<string> ExtractPathsFromUnixShell(string command)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(command)) return paths;

        // Match: cat/sed/head/tail/less/more/tee/cp/mv/rm/touch/chmod/chown followed by file paths
        // Handles: cat File.cs, sed -i 's/...' File.cs, head -n 5 File.cs, etc.
        var patterns = new[]
        {
            // cat/sed/head/tail/less/more/tee/uniq/sort/diff/wc with file arguments
            @"(?:cat|sed|head|tail|less|more|tee|uniq|sort|diff|wc|file|stat|cp|mv|rm|touch|chmod|chown|ln|grep|rg|ag|find)\s+(?:[^\s]*\s+)*([A-Za-z0-9_./~\-]+\.(?:[a-zA-Z]{1,5}))",
            // Redirect targets: cat > File.cs, echo "..." >> File.cs
            @">\s*([A-Za-z0-9_./~\-]+)",
            @">>\s*([A-Za-z0-9_./~\-]+)",
            // Bare filenames anywhere in the command
            @"\b([A-Za-z0-9_\-]+\.(?:cs|md|json|sh|txt|csproj|sln|xaml|py|js|ts|go|rs|java|cpp|c|h|hpp))\b"
        };

        foreach (var pattern in patterns)
        {
            foreach (Match m in Regex.Matches(command, pattern, RegexOptions.IgnoreCase))
            {
                var p = m.Groups[1].Value.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(p) && p.Length > 2)
                    paths.Add(p);
            }
        }

        return paths;
    }

    private bool IsModifyingTool(ToolCallRequest tc)
    {
        var name = tc.ToolName ?? "";

        if (name.Equals("ECodeEditor", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Equals("EShellAgent", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = tc.Args?.GetValueOrDefault("command") ?? "";
            return IsShellWriteCommand(cmd);
        }

        if (name.Equals("EGitTool", StringComparison.OrdinalIgnoreCase))
        {
            var action = tc.Args?.GetValueOrDefault("action") ?? "";
            return action.Equals("commit", StringComparison.OrdinalIgnoreCase) ||
                   action.Equals("push", StringComparison.OrdinalIgnoreCase) ||
                   action.Equals("checkout", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>Detect if a shell command modifies files — checks both PowerShell and Unix patterns.</summary>
    private bool IsShellWriteCommand(string cmd)
    {
        if (string.IsNullOrEmpty(cmd)) return false;
        var lower = cmd.ToLowerInvariant();

        // Check both PowerShell and Unix write patterns
        foreach (var p in _powershellWritePatterns)
            if (lower.Contains(p)) return true;

        foreach (var p in _unixWritePatterns)
            if (lower.Contains(p)) return true;

        return false;
    }
}