namespace ECAssistant.Core.Composition;

using ECAssistant.Core.Config;
using ECAssistant.Core.Services;
using ECAssistant.Core.Interfaces;

/// <summary>
/// Central composition root for ECAssistant services.
/// Single place where all services are created and wired.
/// Replaces scattered 'new' calls across Program.cs, AppController, AgentSession.
/// </summary>
public class EcaCompositionRoot
{
    private readonly string _userConfigDir;
    private readonly string[] _args;

    /// <summary>
    /// Creates the composition root for the default ~/ECAssistant/ config directory.
    /// </summary>
    public EcaCompositionRoot() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ECAssistant"),
        Array.Empty<string>())
    { }

    /// <summary>
    /// Creates the composition root with a custom config directory and CLI args.
    /// </summary>
    public EcaCompositionRoot(string userConfigDir, string[] args)
    {
        _userConfigDir = userConfigDir;
        _args = args;
    }

    /// <summary>
    /// Wires all services and returns a bundle ready for use.
    /// Handles: config loading, model resolution, directory creation,
    /// logger, background processes, file watcher, session builder.
    /// </summary>
    public EcaServiceBundle Build()
    {
        // ── Logger ──
        var logPath = Path.Combine(_userConfigDir, "ECAssistant.log");
        var logger = new Logger(logPath, LogLevel.Info);

        // ── Config ──
        var builder = AgentConfigBuilder.Create().WorkingDirectory(_userConfigDir);
        ApplyCommandLineArgs(ref builder, _args);
        var config = builder.Build();

        // ── Resolve model path ──
        var modelPath = ResolveModelPath(config, _userConfigDir);

        // ── Create directories ──
        Directory.CreateDirectory(_userConfigDir);
        if (config.Memory?.DataPath != null)
            Directory.CreateDirectory(Path.Combine(_userConfigDir, config.Memory.DataPath));
        if (config.Workspace?.Path != null)
            Directory.CreateDirectory(Path.Combine(_userConfigDir, config.Workspace.Path));

        // ── Services ──
        var bgManager = new BackgroundProcessManager();
        var fileWatcher = new FileWatcherService(_userConfigDir, logger: logger);
        var sessionBuilder = new SessionBuilder(config, _userConfigDir, _userConfigDir, logger, bgManager);

        return new EcaServiceBundle(
            config,
            modelPath,
            _userConfigDir,
            _userConfigDir,
            logger,
            bgManager,
            fileWatcher,
            sessionBuilder);
    }

    private string ResolveModelPath(EAgentConfig config, string userConfigDir)
    {
        var modelPath = config.Llm.ModelPath;
        if (Path.IsPathRooted(modelPath))
            return modelPath;

        var inWorkDir = Path.Combine(userConfigDir, modelPath);
        var inBuildDir = Path.Combine(AppContext.BaseDirectory, modelPath);
        if (File.Exists(inWorkDir))
            return inWorkDir;
        if (File.Exists(inBuildDir))
            return inBuildDir;
        return inWorkDir;
    }

    private void ApplyCommandLineArgs(ref AgentConfigBuilder builder, string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLower().TrimStart('-');
            switch (arg)
            {
                case "model":
                    if (i + 1 < args.Length) builder.WithModel(args[++i]); break;
                case "ctx":
                case "contextsize":
                    if (i + 1 < args.Length && uint.TryParse(args[++i], out uint ctx)) builder.ContextSize(ctx); break;
                case "gpu":
                case "gpulayers":
                case "gpu_layers":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int layers)) builder.GpuLayers(Math.Clamp(layers, 0, 100)); break;
                case "threads":
                case "threadcount":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int thr)) builder.Threads(thr); break;
                case "temp":
                case "temperature":
                    if (i + 1 < args.Length && float.TryParse(args[++i], out float t)) builder.Temperature(Math.Clamp(t, 0.0f, 2.0f)); break;
            }
        }
    }
}