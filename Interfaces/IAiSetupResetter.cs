using ECAssistant.Core.Setup;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Resets all AI setup state back to first-run defaults: clears configured
/// llm_providers, restores the default local provider, deletes the keys/ folder
/// and the generated llm-server.json. Downloaded GGUF model files are kept.
/// </summary>
public interface IAiSetupResetter
{
    /// <summary>Performs the reset for the given user config directory.</summary>
    /// <param name="userConfigDir">User config directory containing appsettings.json, keys/ and llm/.</param>
    void Reset(string userConfigDir);
}
