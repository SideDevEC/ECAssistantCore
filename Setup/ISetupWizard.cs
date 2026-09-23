namespace ECAssistant.Core.Setup;

/// <summary>
/// Staged first-run installation wizard seam. Each stage only shows what it needs:
/// 1. LLM — local or remote; remote asks endpoint → key → model; local asks
///    vision, then lists catalog models.
/// 2. Memory — embeddings enabled? If yes: remote or local, configured and verified.
/// </summary>
public interface ISetupWizard
{
    /// <summary>Runs the full staged installation flow.</summary>
    Task RunAsync(WizardContext ctx);
}