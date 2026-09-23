namespace ECAssistant.Core.Composition;

using ECAssistant.Core.Config;
using ECAssistant.Core.Services;
using ECAssistant.Core.Interfaces;

/// <summary>
/// Bundle of all wired services returned by EcaCompositionRoot.Build().
/// Consumers (Program.cs, AppController, ECSQL) receive this bundle
/// instead of creating services individually.
/// </summary>
public record EcaServiceBundle(
    AppConfig Config,
    string ModelPath,
    string WorkingDirectory,
    string UserConfigDirectory,
    ILogger Logger,
    BackgroundProcessManager BackgroundProcesses,
    FileWatcherService FileWatcher,
    ISessionBuilder SessionBuilder);