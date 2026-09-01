using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// Code Editor Tool — surgical code edits with diff preview, multi-line replacement,
/// cross-file search & replace, and syntax-aware editing.
/// </summary>
public class ECodeEditorTool : EToolBase
{
    private readonly IFileSystem _fileSystem;
    private readonly JsonElement? _toolConfig;
    private readonly string _workingDir;

    public override string Name => "ECodeEditor";

    public override string Description =>
        "Surgical code editing: create files, multi-line patch, diff preview, cross-file search & replace, " +
        "line insertion/deletion. Better than shell echo for code changes.";

    public override string UsageExample =>
        "<toolcall>ECodeEditor<action>patch</action><file>Program.cs</file><old_text>bug</old_text><new_text>fix</new_text></toolcall>";

    public override bool IsEnabled { get; protected set; } = true;

    public ECodeEditorTool(IFileSystem fileSystem, EAgentConfig config)
    {
        _fileSystem = fileSystem;
        config.Tools.TryGetValue(Name, out var tc);
        _toolConfig = tc.ValueKind == JsonValueKind.Undefined ? null : tc;
        IsEnabled = ReadCfg(_toolConfig, "enabled", true);
        _workingDir = config.AgentSettings.WorkingDirectory;
    }

    public override object GetConfigSection() => new { enabled = true };

    public override async Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        var action = arguments.GetValueOrDefault("action")?.ToLower().Trim();
        if (string.IsNullOrEmpty(action))
            return EToolResult.Failure(Name, "Missing 'action' argument.");

        // (success, message) tuples — result classification must not be guessed from
        // message text (that marked real failures like "old_text found 5 times" as Success).
        var (ok, result) = action switch
        {
            "create" => await DoCreate(arguments, cancellationToken),
            "diff" => await DoDiff(arguments, cancellationToken),
            "patch" => await DoPatch(arguments, cancellationToken),
            "search" => await DoSearch(arguments, cancellationToken),
            "replace-all" => await DoReplaceAll(arguments, cancellationToken),
            "insert" => await DoInsert(arguments, cancellationToken),
            "delete-lines" => await DoDeleteLines(arguments, cancellationToken),
            _ => (false, $"ECodeEditor: Unknown action: {action}")
        };

        return ok ? EToolResult.Success(Name, result) : EToolResult.Failure(Name, result);
    }

    // ─── Create: create a new file with content ───────────────
    private async Task<(bool Ok, string Message)> DoCreate(Dictionary<string, string?> args, CancellationToken ct)
    {
        var file = args.GetValueOrDefault("file")?.Trim();
        var content = args.GetValueOrDefault("content") ?? "";

        if (string.IsNullOrEmpty(file))
            return (false, "ECodeEditor: Missing 'file' argument.");

        var fullPath = ResolvePath(file);

        if (_fileSystem.FileExists(fullPath))
            return (false, $"ECodeEditor: File already exists: {file}. Use action=patch to modify existing files.");

        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !_fileSystem.DirectoryExists(dir))
                _fileSystem.CreateDirectory(dir);

            await Task.Run(() => _fileSystem.WriteFile(fullPath, content));
            return (true, $"✅ Created {file} ({content.Length} chars).\nContent:\n{content}");
        }
        catch (Exception ex)
        {
            return (false, $"ECodeEditor: Failed to create {file}: {ex.Message}");
        }
    }

    // ─── Patch: replace old_text with new_text in a file ─────────
    private async Task<(bool Ok, string Message)> DoPatch(Dictionary<string, string?> args, CancellationToken ct)
    {
        var file = args.GetValueOrDefault("file");
        var oldText = args.GetValueOrDefault("old_text");
        var newText = args.GetValueOrDefault("new_text");

        if (string.IsNullOrEmpty(file) || oldText == null || newText == null)
            return (false, "ECodeEditor: Missing file, old_text, or new_text.");

        var fullPath = ResolvePath(file);
        if (!_fileSystem.FileExists(fullPath))
            return (false, $"ECodeEditor: File not found: {file}");

        if (ct.IsCancellationRequested)
            return (false, "ECodeEditor: [CANCELLED] Operation cancelled by user.");

        var content = await Task.Run(() => _fileSystem.ReadFile(fullPath));

        if (oldText.Length == 0)
            return (false, "ECodeEditor: old_text must not be empty.");

        if (!content.Contains(oldText))
        {
            var similar = FindSimilarLines(content, oldText);
            var msg = $"old_text not found in {file}.";
            if (similar.Count > 0)
                msg += $"\nSimilar lines found:\n{string.Join("\n", similar.Take(3))}";
            return (false, msg);
        }

        var count = CountOccurrences(content, oldText);
        if (count > 1)
        {
            return (false, $"old_text found {count} times in {file}. Add more context to make it unique, " +
                $"or use action=replace-all for intentional multi-replacement.");
        }

        var newContent = content.Replace(oldText, newText);
        var diff = GenerateDiff(content, newContent, file);

        await Task.Run(() => _fileSystem.WriteFile(fullPath, newContent));

        return (true, $"✅ Patched {file}\n\nDiff:\n{diff}");
    }

    // ─── Diff: show what would change ─────────────────────────────
    private async Task<(bool Ok, string Message)> DoDiff(Dictionary<string, string?> args, CancellationToken ct)
    {
        var file = args.GetValueOrDefault("file");
        var newText = args.GetValueOrDefault("new_text");

        if (string.IsNullOrEmpty(file) || newText == null)
            return (false, "ECodeEditor: Missing file or new_text.");

        var fullPath = ResolvePath(file);
        if (!_fileSystem.FileExists(fullPath))
            return (false, $"ECodeEditor: File not found: {file}");

        var oldContent = await Task.Run(() => _fileSystem.ReadFile(fullPath));
        var diff = GenerateDiff(oldContent, newText, file);

        return (true, $"Diff for {file}:\n\n{diff}");
    }

    // ─── Search: find pattern across files ────────────────────────
    private async Task<(bool Ok, string Message)> DoSearch(Dictionary<string, string?> args, CancellationToken ct)
    {
        var pattern = args.GetValueOrDefault("pattern");
        var filter = args.GetValueOrDefault("file_filter") ?? "*.*";

        if (string.IsNullOrEmpty(pattern))
            return (false, "ECodeEditor: Missing 'pattern'.");

        var files = GetFiles(filter);
        var sb = new StringBuilder();
        var totalMatches = 0;

        // Warn instead of silently capping — the LLM otherwise thinks the search
        // was exhaustive over the whole tree when only 50 files were scanned.
        var scanned = files.Take(50).ToList();
        if (files.Count > scanned.Count)
            sb.AppendLine($"[Warning: {files.Count} files match '{filter}' — only the first {scanned.Count} were searched. Use a narrower file_filter to cover the rest.]");

        foreach (var filePath in scanned)
        {
            try
            {
                var content = await Task.Run(() => _fileSystem.ReadFile(filePath));
                var lines = SplitLines(content);
                var relPath = Path.GetRelativePath(_workingDir, filePath);

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"  {relPath}:{i + 1}: {lines[i].Trim()}");
                        totalMatches++;
                        if (totalMatches >= 30) break;
                    }
                }
                if (totalMatches >= 30) break;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ECodeEditorTool] Non-critical error ignored: {ex.Message}"); }
        }

        if (totalMatches == 0)
            return (true, $"No matches found for '{pattern}' in {filter}.");

        sb.Insert(0, $"Found {totalMatches} match(es) for '{pattern}':\n\n");
        return (true, sb.ToString());
    }

    // ─── Replace-All: replace pattern across files ────────────────
    private async Task<(bool Ok, string Message)> DoReplaceAll(Dictionary<string, string?> args, CancellationToken ct)
    {
        var pattern = args.GetValueOrDefault("pattern");
        var replacement = args.GetValueOrDefault("replacement") ?? "";
        var filter = args.GetValueOrDefault("file_filter") ?? "*.*";

        if (string.IsNullOrEmpty(pattern))
            return (false, "ECodeEditor: Missing 'pattern'.");
        if (pattern.Length == 0)
            return (false, "ECodeEditor: pattern must not be empty.");

        var files = GetFiles(filter);
        var modifiedFiles = new List<string>();
        var totalReplacements = 0;

        // Warn instead of silently capping — files beyond the first 50 are NOT
        // modified and the caller must know that.
        var scanned = files.Take(50).ToList();
        var capWarning = files.Count > scanned.Count
            ? $"[Warning: {files.Count} files match '{filter}' — only the first {scanned.Count} were processed. Use a narrower file_filter to cover the rest.]\n"
            : "";

        foreach (var filePath in scanned)
        {
            try
            {
                var content = await Task.Run(() => _fileSystem.ReadFile(filePath));
                if (!content.Contains(pattern)) continue;

                var count = CountOccurrences(content, pattern);
                var newContent = content.Replace(pattern, replacement);
                await Task.Run(() => _fileSystem.WriteFile(filePath, newContent));
                modifiedFiles.Add(Path.GetRelativePath(_workingDir, filePath));
                totalReplacements += count;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ECodeEditorTool] Non-critical error ignored: {ex.Message}"); }
        }

        if (modifiedFiles.Count == 0)
            return (true, $"No files contained '{pattern}'.");

        return (true, capWarning +
            $"✅ Replaced {totalReplacements} occurrence(s) of '{pattern}' → '{replacement}' in {modifiedFiles.Count} file(s):\n" +
            string.Join("\n", modifiedFiles.Select(f => $"  {f}")));
    }

    // ─── Insert: insert text at specific line ─────────────────────
    private async Task<(bool Ok, string Message)> DoInsert(Dictionary<string, string?> args, CancellationToken ct)
    {
        var file = args.GetValueOrDefault("file");
        var lineStr = args.GetValueOrDefault("line");
        var text = args.GetValueOrDefault("text");

        if (string.IsNullOrEmpty(file) || !int.TryParse(lineStr, out var line) || text == null)
            return (false, "ECodeEditor: Missing file, line (number), or text.");

        var fullPath = ResolvePath(file);
        if (!_fileSystem.FileExists(fullPath))
            return (false, $"ECodeEditor: File not found: {file}");

        if (ct.IsCancellationRequested)
            return (false, "ECodeEditor: [CANCELLED] Operation cancelled by user.");

        var content = await Task.Run(() => _fileSystem.ReadFile(fullPath));
        var lines = SplitLines(content).ToList();
        var insertAt = Math.Clamp(line - 1, 0, lines.Count);
        lines.Insert(insertAt, text);
        var newContent = string.Join('\n', lines);
        await Task.Run(() => _fileSystem.WriteFile(fullPath, newContent));

        return (true, $"✅ Inserted text at line {line} in {file}");
    }

    // ─── Delete-Lines: remove a range of lines ──────────────────────
    private async Task<(bool Ok, string Message)> DoDeleteLines(Dictionary<string, string?> args, CancellationToken ct)
    {
        var file = args.GetValueOrDefault("file");
        var startStr = args.GetValueOrDefault("start_line");
        var endStr = args.GetValueOrDefault("end_line");

        if (string.IsNullOrEmpty(file) || !int.TryParse(startStr, out var start) || !int.TryParse(endStr, out var end))
            return (false, "ECodeEditor: Missing file, start_line, or end_line (must be numbers).");

        var fullPath = ResolvePath(file);
        if (!_fileSystem.FileExists(fullPath))
            return (false, $"ECodeEditor: File not found: {file}");

        var content = await Task.Run(() => _fileSystem.ReadFile(fullPath));
        var lines = SplitLines(content).ToList();
        var delStart = Math.Clamp(start - 1, 0, lines.Count - 1);
        var delEnd = Math.Clamp(end, delStart + 1, lines.Count);
        var count = delEnd - delStart;
        lines.RemoveRange(delStart, count);
        var newContent = string.Join('\n', lines);
        await Task.Run(() => _fileSystem.WriteFile(fullPath, newContent));

        return (true, $"✅ Deleted {count} line(s) ({start}-{end}) from {file}");
    }

    // ─── Helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Normalize CRLF/CR line endings to LF and split. Dropping a single trailing
    /// empty entry keeps line numbers stable (a file ending in a newline does not
    /// have an extra phantom line).
    /// </summary>
    private static string[] SplitLines(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];
        return lines;
    }

    private string ResolvePath(string file)
    {
        if (Path.IsPathRooted(file)) return file;
        return Path.Combine(_workingDir, file);
    }

    private List<string> GetFiles(string filter)
    {
        var exclude = new[] { Path.DirectorySeparatorChar + "bin", Path.DirectorySeparatorChar + "obj", Path.DirectorySeparatorChar + ".git" };
        return ListFilesRecursive(_workingDir)
            .Where(f => !exclude.Any(ex => f.Contains(ex)))
            .Where(f => MatchesFilter(Path.GetFileName(f), filter))
            .ToList();
    }

    /// <summary>Wildcard match against the file_filter argument (supports comma-separated patterns, e.g. "*.cs,*.md").</summary>
    private static bool MatchesFilter(string fileName, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || filter == "*.*" || filter == "*") return true;
        return filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p =>
            {
                var regex = "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase);
            });
    }

    private List<string> ListFilesRecursive(string directory)
    {
        var result = new List<string>();
        var files = _fileSystem.ListFiles(directory, "*");
        result.AddRange(files);

        var subDirs = Directory.GetDirectories(directory);
        foreach (var subDir in subDirs)
            result.AddRange(ListFilesRecursive(subDir));

        return result;
    }

    private int CountOccurrences(string content, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 0; // empty pattern would loop forever
        int count = 0, pos = 0;
        while ((pos = content.IndexOf(pattern, pos, StringComparison.Ordinal)) >= 0)
        {
            count++;
            pos += pattern.Length;
        }
        return count;
    }

    private List<string> FindSimilarLines(string content, string searchText)
    {
        var lines = SplitLines(content);
        var result = new List<string>();
        var searchWords = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3).Take(3).ToHashSet();

        foreach (var line in lines)
        {
            var lineWords = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchCount = lineWords.Count(w => searchWords.Any(s => w.Contains(s, StringComparison.OrdinalIgnoreCase)));
            if (matchCount >= 2)
                result.Add($"  {line.Trim()}");
        }
        return result;
    }

    private string GenerateDiff(string old, string newText, string file)
    {
        var oldLines = SplitLines(old).Where(l => l.Length > 0).ToArray();
        var newLines = SplitLines(newText).Where(l => l.Length > 0).ToArray();
        var sb = new StringBuilder();

        var maxLines = Math.Max(oldLines.Length, newLines.Length);
        var changes = 0;

        for (int i = 0; i < maxLines; i++)
        {
            var oldLine = i < oldLines.Length ? oldLines[i].TrimEnd() : null;
            var newLine = i < newLines.Length ? newLines[i].TrimEnd() : null;

            if (oldLine != newLine)
            {
                changes++;
                if (oldLine != null)
                    sb.AppendLine($"- {i + 1}: {oldLine}");
                if (newLine != null)
                    sb.AppendLine($"+ {i + 1}: {newLine}");
            }
        }

        if (changes == 0)
            sb.AppendLine("(no changes)");

        return sb.ToString();
    }
}