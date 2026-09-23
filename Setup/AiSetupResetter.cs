using System.Text.Json;
using System.Text.Json.Serialization;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Default <see cref="IAiSetupResetter"/>: rewrites appsettings.json back to the
/// default local provider, then deletes the keys/ directory and the shared
/// llm-server.json. Model files and server binary are deliberately preserved.
/// </summary>
public class AiSetupResetter : IAiSetupResetter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc />
    public void Reset(string userConfigDir)
    {
        var appsettingsPath = Path.Combine(userConfigDir, "appsettings.json");

        // Read the current config to get ServerRootPath before we overwrite it
        var llmRoot = ResolveLlmRoot(appsettingsPath);

        ResetAppsettings(appsettingsPath);

        var keysDir = Path.Combine(userConfigDir, "keys");
        if (Directory.Exists(keysDir))
            Directory.Delete(keysDir, recursive: true);

        // Reset the shared LLM server config (keep models + server binary)
        var serverConfigPath = Path.Combine(llmRoot, "llm-server.json");
        if (File.Exists(serverConfigPath))
            File.Delete(serverConfigPath);
    }

    private static string ResolveLlmRoot(string appsettingsPath)
    {
        if (File.Exists(appsettingsPath))
        {
            try
            {
                var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(appsettingsPath), JsonOptions);
                if (config?.LlmProvider?.ServerRootPath != null)
                    return PathExpander.Default.Expand(config.LlmProvider.ServerRootPath);
            }
            catch { /* fall through to default */ }
        }
        return PathExpander.Default.Expand("~/.ECAssistantLLM");
    }

    private static void ResetAppsettings(string appsettingsPath)
    {
        if (!File.Exists(appsettingsPath))
            return;

        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(appsettingsPath), JsonOptions);
        if (config == null)
            return;

        config.LlmProviders = null;
        config.LlmProvider = new LlmProviderConfig(); // defaults = local, port 48217, ~/.ECAssistantLLM
        File.WriteAllText(appsettingsPath, JsonSerializer.Serialize(config, JsonOptions));
    }
}
