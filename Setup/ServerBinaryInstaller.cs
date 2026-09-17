namespace ECAssistant.Core.Setup;

/// <summary>
/// Copies the LLM server runtime from the app's NuGet-populated content directory
/// to the shared standalone location (~/.ECAssistantLLM/server/).
///
/// This is the ONLY place that references the app's content directory (AppContext.BaseDirectory).
/// ServerLauncher never uses it — this class runs at wizard time only.
///
/// The NuGet package (ECAssistant.LLM.Server) places the server runtime as content files
/// in the consuming project's output directory under server/. The wizard copies this to
/// the shared location once. Subsequent projects detect the binary is already installed
/// and skip the copy.
/// </summary>
public sealed class ServerBinaryInstaller
{
    private readonly string _sourceServerDir;
    private readonly string _targetServerDir;

    /// <summary>
    /// Create the installer.
    /// </summary>
    /// <param name="sourceServerDir">Source: the app's content directory containing the
    /// server runtime (populated by the NuGet package at build time). Typically
    /// {AppContext.BaseDirectory}/server/.</param>
    /// <param name="targetServerDir">Destination: the shared server directory
    /// (typically ~/.ECAssistantLLM/server/).</param>
    public ServerBinaryInstaller(string sourceServerDir, string targetServerDir)
    {
        _sourceServerDir = sourceServerDir ?? throw new ArgumentNullException(nameof(sourceServerDir));
        _targetServerDir = targetServerDir ?? throw new ArgumentNullException(nameof(targetServerDir));
    }

    /// <summary>True when the server binary is already installed at the target.</summary>
    public bool IsInstalled()
    {
        return File.Exists(Path.Combine(_targetServerDir, "ECAssistant.LLM.dll"));
    }

    /// <summary>True when the source server runtime is available in the app's content.</summary>
    public bool IsSourceAvailable()
    {
        return File.Exists(Path.Combine(_sourceServerDir, "ECAssistant.LLM.dll"));
    }

    /// <summary>
    /// Copy server runtime to the shared location. Overwrites if source is newer.
    /// Creates the target directory if it doesn't exist.
    /// </summary>
    public void Install()
    {
        if (!IsSourceAvailable())
            throw new InvalidOperationException(
                $"Server binary not found in app content: {_sourceServerDir}. " +
                "Ensure the ECAssistant.LLM.Server NuGet package is referenced.");

        Directory.CreateDirectory(_targetServerDir);

        var sourceMarker = Path.Combine(_sourceServerDir, "ECAssistant.LLM.dll");
        var targetMarker = Path.Combine(_targetServerDir, "ECAssistant.LLM.dll");

        // Skip copy if target is up-to-date
        if (File.Exists(targetMarker) &&
            File.GetLastWriteTimeUtc(targetMarker) >= File.GetLastWriteTimeUtc(sourceMarker))
            return;

        CopyDirectory(_sourceServerDir, _targetServerDir);

        // Stamp the target marker so the staleness comparison compares against copy time
        File.SetLastWriteTimeUtc(targetMarker, DateTime.UtcNow);
    }

    /// <summary>
    /// Version of the installed server binary, if a VERSION file exists.
    /// Returns null if no version file is present.
    /// </summary>
    public string? GetInstalledVersion()
    {
        var versionFile = Path.Combine(_targetServerDir, "VERSION");
        return File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            // Skip log files — they're runtime artifacts, not part of the server distribution
            if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            // Skip logs directory
            if (dirName.Equals("logs", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectory(dir, Path.Combine(targetDir, dirName));
        }
    }
}