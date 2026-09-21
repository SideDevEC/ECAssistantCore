using System.Text.Json;
using ECAssistant.Core.Services;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Project Context Manager — maintains persistent knowledge about the project structure,
/// file relationships, and architecture. Injected into every prompt so the LLM has context.
/// 
/// Capabilities:
/// - Auto-scan project on startup and on file changes
/// - Build dependency graph (from EContextAnalyzer)
/// - Detect file relationships (using statements, references)
/// - Track recent changes
/// - Inject project summary into every prompt
/// - Persist project context to disk for future sessions
/// </summary>
public class ProjectContextManager : IDisposable
{
    private readonly string _workingDir;
    private readonly string _contextFile;
    private ProjectContext _context = new();
    private DateTime _lastScan = DateTime.MinValue;
    private readonly ILogger _logger;

    public ProjectContextManager(string workingDir, ILogger? logger = null)
    {
        _logger = logger ?? new Logger();
        _workingDir = workingDir;
        _contextFile = Path.Combine(workingDir, ".project_context.json");
    }

    /// <summary>Load persisted context and scan if stale.</summary>
    public async Task InitializeAsync()
    {
        await LoadAsync();
        if (_context.LastScan == null || (DateTime.UtcNow - _context.LastScan.Value).TotalMinutes > 30)
        {
            await ScanProjectAsync();
        }
        _logger.Info("ProjectCtx", $"Initialized: {_context.Files.Count} files, {_context.Relationships.Count} relationships, last scan: {_context.LastScan}");
    }

    /// <summary>Scan the project directory and build context.</summary>
    public async Task ScanProjectAsync()
    {
        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", ".snapshots", "vecmem", "Memory", ".sessions", "Workspace", "tool_outputs" };
        // Host runtime/config files are NOT project files — including them makes the
        // model narrate the app's own config in every conversation. They remain
        // accessible via the HOST ENVIRONMENT section of the system prompt.
        var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "appsettings.json", "model-catalog.json", ".project_context.json", "ECAssistant.log" };
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".csproj", ".sln", ".md", ".json", ".ps1", ".sql", ".html", ".css", ".js" };

        _context.Files.Clear();
        _context.Relationships.Clear();

        var allFiles = Directory.GetFiles(_workingDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !exclude.Any(ex => f.Contains(Path.DirectorySeparatorChar + ex + Path.DirectorySeparatorChar)))
            .Where(f => !excludedFiles.Contains(Path.GetFileName(f)))
            .Where(f => exts.Contains(Path.GetExtension(f)))
            .ToList();

        foreach (var file in allFiles)
        {
            try
            {
                var info = new FileInfo(file);
                var relPath = Path.GetRelativePath(_workingDir, file);
                var ext = Path.GetExtension(file).ToLower();
                var content = await File.ReadAllTextAsync(file);
                var lineCount = content.Split('\n').Length;

                var fileCtx = new FileContext
                {
                    Path = relPath,
                    Extension = ext,
                    Size = (int)info.Length,
                    Lines = lineCount,
                    LastModified = info.LastWriteTimeUtc,
                    Imports = ParseImports(content, ext),
                    Classes = ext == ".cs" ? CountClasses(content) : 0,
                    Methods = ext == ".cs" ? CountMethods(content) : 0
                };
                _context.Files.Add(fileCtx);

                // Build relationships from imports
                foreach (var import in fileCtx.Imports)
                {
                    var target = FindFileByImport(import, allFiles, _workingDir);
                    if (target != null)
                    {
                        _context.Relationships.Add(new FileRelationship
                        {
                            Source = relPath,
                            Target = Path.GetRelativePath(_workingDir, target),
                            Type = "import"
                        });
                    }
                }
            }
            catch (Exception ex) { _logger.Debug("ProjectContext", $"Non-critical error ignored: {ex.Message}"); }
        }

        _context.ProjectType = DetectProjectType();
        _context.LastScan = DateTime.UtcNow;
        _context.EntryPoint = FindEntryPoint();

        await SaveAsync();
        _logger.Info("ProjectCtx", $"Scan complete: {_context.Files.Count} files, {_context.Relationships.Count} relationships");
    }

    /// <summary>Get a compact project summary for prompt injection.</summary>
    public string GetProjectSummary()
    {
        if (_context.Files.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("### PROJECT CONTEXT:");
        sb.AppendLine($"Type: {_context.ProjectType}");
        sb.AppendLine($"Files: {_context.Files.Count}");
        sb.AppendLine($"Entry point: {_context.EntryPoint ?? "(unknown)"}");
        sb.AppendLine();

        // File list (compact)
        sb.AppendLine("Files:");
        foreach (var f in _context.Files.OrderBy(f => f.Path).Take(30))
        {
            var info = $"{f.Path} ({f.Lines}L";
            if (f.Classes > 0) info += $", {f.Classes}C";
            if (f.Methods > 0) info += $", {f.Methods}M";
            info += ")";
            sb.AppendLine($"  {info}");
        }
        if (_context.Files.Count > 30)
            sb.AppendLine($"  ... and {_context.Files.Count - 30} more");

        // Key relationships
        if (_context.Relationships.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Dependencies ({_context.Relationships.Count}):");
            foreach (var r in _context.Relationships.Take(15))
                sb.AppendLine($"  {r.Source} → {r.Target}");
        }

        // Runtime/config files that remain in the list are the host app's internals —
        // the model should not proactively comment on them.
        sb.AppendLine("Note: appsettings.json and model-catalog.json (if listed) are the host application's runtime files, not part of your user's project. Do not proactively mention or analyze them — only touch them when the user explicitly asks about the app's own configuration.");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>Find files related to a given file (via dependency graph).</summary>
    public List<string> GetRelatedFiles(string filePath)
    {
        var relPath = Path.GetRelativePath(_workingDir, ResolvePath(filePath));
        var related = new List<string>();

        // Files that this file imports
        foreach (var r in _context.Relationships.Where(r => r.Source == relPath))
            related.Add(r.Target);

        // Files that import this file
        foreach (var r in _context.Relationships.Where(r => r.Target == relPath))
            related.Add(r.Source);

        return related.Distinct().ToList();
    }

    /// <summary>Get impact analysis — what files might be affected if this file changes.</summary>
    public string GetImpactAnalysis(string filePath)
    {
        var relPath = Path.GetRelativePath(_workingDir, ResolvePath(filePath));
        var impacted = GetRelatedFiles(filePath);

        if (impacted.Count == 0) return $"No files directly depend on {relPath}.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[IMPACT ANALYSIS] Changing {relPath} may affect:");
        foreach (var f in impacted)
            sb.AppendLine($"  {f}");
        return sb.ToString();
    }

    public string ProjectType => _context.ProjectType;
    public int FileCount => _context.Files.Count;
    public DateTime? LastScan => _context.LastScan;

    // ─── Persistence ──────────────────────────────────────────────

    private async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_context, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_contextFile, json);
        }
        catch (Exception ex) { _logger.Error("ProjectCtx", $"Save failed: {ex.Message}"); }
    }

    private async Task LoadAsync()
    {
        if (!File.Exists(_contextFile)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_contextFile);
            _context = JsonSerializer.Deserialize<ProjectContext>(json) ?? new ProjectContext();
        }
        catch (Exception ex) { _logger.Error("ProjectCtx", $"Load failed: {ex.Message}"); }
    }

    // ─── Helpers ──────────────────────────────────────────────────

    private string ResolvePath(string file)
    {
        if (Path.IsPathRooted(file)) return file;
        return Path.Combine(_workingDir, file);
    }

    private List<string> ParseImports(string content, string ext)
    {
        var imports = new List<string>();
        foreach (var line in content.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("using ") && t.EndsWith(";"))
                imports.Add(t.Substring(6).TrimEnd(';'));
            else if (t.StartsWith("import ") || t.StartsWith("require("))
                imports.Add(t);
            else if (t.StartsWith("#include"))
                imports.Add(t);
        }
        return imports;
    }

    private int CountClasses(string content)
        => System.Text.RegularExpressions.Regex.Matches(content, @"(?i)\b(class|interface|struct|enum|record)\s+\w+").Count;

    private int CountMethods(string content)
        => System.Text.RegularExpressions.Regex.Matches(content, @"(?i)\b(public|private|protected|internal)\s+(static\s+)?(async\s+)?\w+\s+\w+\s*\(").Count;

    private string? FindFileByImport(string import, List<string> allFiles, string workingDir)
    {
        var parts = import.Split('.');
        if (parts.Length == 0) return null;
        var lastPart = parts[^1];
        return allFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == lastPart);
    }

    private string DetectProjectType()
    {
        var hasCsproj = _context.Files.Any(f => f.Extension == ".csproj");
        var hasSln = _context.Files.Any(f => f.Extension == ".sln");
        var hasController = _context.Files.Any(f => f.Path.Contains("Controller"));
        var hasTest = _context.Files.Any(f => f.Path.Contains("Test"));

        if (hasController) return "ASP.NET Web App";
        if (hasSln && hasCsproj) return ".NET Solution (multi-project)";
        if (hasCsproj && hasTest) return ".NET Project with Tests";
        if (hasCsproj) return ".NET Console Application";
        return "Mixed/Unknown";
    }

    private string? FindEntryPoint()
    {
        return _context.Files.FirstOrDefault(f => f.Path.EndsWith("Program.cs"))?.Path
            ?? _context.Files.FirstOrDefault(f => f.Path.EndsWith("Main.cs"))?.Path;
    }

    public void Dispose() { }
}
