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
    private readonly bool _largeTierRuntime;
    private readonly TextMatchPipeline _matchPipeline = new();

    public override string Name => "ECodeEditor";

    public override string Description =>
        "Surgical code editing: create files, multi-line patch, diff preview, cross-file search & replace, " +
        "line insertion/deletion. Better than shell echo for code changes. " +
        "Use ONLY when the user asks to create or modify actual project files. Do NOT use to write " +
        "example code inside a chat answer — answer coding questions directly. " +
        // v14.15 tier flavor: large = terse hint, small = explicit guidance.
        (_largeTierRuntime
            ? "Approximate matches tolerated."
            : "Copy text exactly first; fuzzy fallback will rescue small mismatches.");

    public override string GetParameterSchema() =>
        """
        {
          "type": "object", "required": ["action"],
          "properties": {
            "action": { "type": "string", "enum": ["create", "diff", "patch", "search", "replace-all", "insert", "delete-lines", "delete"], "description": "create = new file (content/new_text); diff = preview change; patch = replace old_text with new_text; search = find text; replace-all = replace every occurrence; insert = add lines; delete-lines = remove lines; delete = remove file" },
            "file": { "type": "string", "description": "Target file path (relative to workspace)" },
            "content": { "type": "string", "description": "Full file content (action=create)" },
            "old_text": { "type": "string", "description": "Exact text to replace (action=patch)" },
            "new_text": { "type": "string", "description": "Replacement text (action=patch)" },
            "fuzzy": { "type": "boolean", "description": "Optional (default true). Set false to require exact matching only for action=patch." }
          }
        }
        """;
    public override string UsageExample =>
        "ECodeEditor(action:create, file:hello.py, content:print(1))";

    public override bool IsEnabled { get; protected set; } = true;

    public ECodeEditorTool(IFileSystem fileSystem, EAgentConfig config)
    {
        _fileSystem = fileSystem;
        config.Tools.TryGetValue(Name, out var tc);
        _toolConfig = tc.ValueKind == JsonValueKind.Undefined ? null : tc;
        IsEnabled = ReadCfg(_toolConfig, "enabled", true);
        // v14.15: tier resolved like Engine/Orchestrator do (only coupling with tiers).
        var isLocal = config?.LlmProvider?.IsLocal ?? true;
        _largeTierRuntime = config?.ModelTier?.IsLargeRuntime(isLocal) ?? !isLocal;
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
        // Tolerant lookup: models sometimes send content as new_text (schema drift fallback).
        var content = args.GetValueOrDefault("content") ?? args.GetValueOrDefault("new_text") ?? "";

        if (string.IsNullOrEmpty(file))
            return (false, "ECodeEditor: Missing 'file' argument.");
        if (string.IsNullOrWhiteSpace(content))
            return (false, "ECodeEditor: Missing 'content' argument (send the full file content; empty files are not created).");

        var fullPath = ResolvePath(file);

        if (_fileSystem.FileExists(fullPath))
            return (false, $"ECodeEditor: File already exists: {file}. Use action=patch to modify existing files.");

        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !_fileSystem.DirectoryExists(dir))
                _fileSystem.CreateDirectory(dir);

            await Task.Run(() => _fileSystem.WriteFile(fullPath, content));
            // v14.10.2: verify-on-write — models claim success even when the write
            // silently failed; report actual disk bytes so the next turn sees the truth.
            int bytesOnDisk;
            try { bytesOnDisk = _fileSystem.ReadFile(fullPath).Length; }
            catch { bytesOnDisk = 0; }
            if (bytesOnDisk != content.Length)
            {
                return (false, $"ECodeEditor: write NOT verified for {file} — expected {content.Length} chars, found {bytesOnDisk} on disk.");
            }
            return (true, $"✅ Created {file} ({bytesOnDisk} chars verified on disk).\nContent:\n{content}");
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

        // v14.15: fuzzy arg is optional and defaults to true; "false" disables layered matching.
        var fuzzyArg = args.GetValueOrDefault("fuzzy");
        var fuzzyEnabled = fuzzyArg == null || !string.Equals(fuzzyArg.Trim(), "false", StringComparison.OrdinalIgnoreCase);

        if (!fuzzyEnabled)
            return await PatchExact(fullPath, file, content, oldText, newText);

        return await PatchFuzzy(fullPath, file, content, oldText, newText);
    }

    // ─── Patch — strict exact matching (fuzzy=false) ──────────────
    private async Task<(bool Ok, string Message)> PatchExact(string fullPath, string file, string content, string oldText, string newText)
    {
        await Task.CompletedTask;
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

        return (true, $"✅ Patched {file} (match strategy: exact)\n\nDiff:\n{diff}");
    }

    // ─── Patch — layered fuzzy matching (v14.15) ───────────────────
    private async Task<(bool Ok, string Message)> PatchFuzzy(string fullPath, string file, string content, string oldText, string newText)
    {
        var match = _matchPipeline.Find(content, oldText);
        switch (match.Status)
        {
            case TextMatchStatus.NotFound:
            {
                var similar = FindSimilarLines(content, oldText);
                var msg = $"old_text not found in {file} (exact, whitespace-tolerant, and line-anchored matching all failed).";
                if (similar.Count > 0)
                    msg += $"\nSimilar lines found:\n{string.Join("\n", similar.Take(3))}";
                return (false, msg);
            }
            case TextMatchStatus.Ambiguous:
            {
                // Conservative: never guess among candidates — structured error.
                var lines = string.Join(",", match.CandidateLines);
                return (false, $"ambiguous match: {match.CandidateLines.Count} candidates at lines {lines} in {file}. " +
                    $"Add more context to old_text to make it unique.");
            }
        }

        var replacement = RestoreIndentation(match.MatchedText, oldText, newText);
        var newContent = content.Remove(match.StartIndex, match.MatchedText.Length).Insert(match.StartIndex, replacement);
        var patchDiff = GenerateDiff(content, newContent, file);

        await Task.Run(() => _fileSystem.WriteFile(fullPath, newContent));

        return (true, $"✅ Patched {file} (match strategy: {match.StrategyName})\n\nDiff:\n{patchDiff}");
    }

    /// <summary>
    /// v14.15 indentation restoration: when a fuzzy strategy located the original block,
    /// re-indent new_text by the indentation delta between the file's real block and the
    /// model's old_text. Pure function — no mutable state.
    /// </summary>
    private static string RestoreIndentation(string matchedText, string oldText, string newText)
    {
        var matchFirst = matchedText.Replace("\r\n", "\n").Split('\n')[0];
        var oldFirst = oldText.Replace("\r\n", "\n").Split('\n')[0];
        var matchIndent = matchFirst.Length - matchFirst.TrimStart().Length;
        var oldIndent = oldFirst.Length - oldFirst.TrimStart().Length;
        var delta = matchIndent - oldIndent;
        if (delta == 0)
            return newText;

        var nl = newText.Contains("\r\n") ? "\r\n" : "\n";
        var lines = newText.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0)
                continue; // keep blank lines blank
            if (delta > 0)
                lines[i] = new string(' ', delta) + lines[i];
            else
            {
                var leading = lines[i].Length - lines[i].TrimStart().Length;
                var remove = Math.Min(-delta, leading);
                lines[i] = lines[i][remove..];
            }
        }
        return string.Join(nl, lines);
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