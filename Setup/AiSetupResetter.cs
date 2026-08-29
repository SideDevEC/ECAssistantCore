using System.Text.Json;
using System.Text.Json.Serialization;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Default <see cref="IAiSetupResetter"/>: rewrites appsettings.json back to the
/// default local provider, then deletes the keys/ directory and llm/llm-server.json.
/// Model files are deliberately preserved.
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
        ResetAppsettings(Path.Combine(userConfigDir, "appsettings.json"));

        var keysDir = Path.Combine(userConfigDir, "keys");
        if (Directory.Exists(keysDir))
            Directory.Delete(keysDir, recursive: true);

        var serverConfigPath = Path.Combine(userConfigDir, "llm", "llm-server.json");
        if (File.Exists(serverConfigPath))
            File.Delete(serverConfigPath);
    }

    private static void ResetAppsettings(string appsettingsPath)
    {
        // Stateless helper — no mutable state.
        if (!File.Exists(appsettingsPath))
            return;

        var config = JsonSerializer.Deserialize<EAgentConfig>(File.ReadAllText(appsettingsPath), JsonOptions);
        if (config == null)
            return;

        config.LlmProviders = null;
        config.LlmProvider = new LlmProviderConfig(); // defaults = local, port 58777
        File.WriteAllText(appsettingsPath, JsonSerializer.Serialize(config, JsonOptions));
    }
}
