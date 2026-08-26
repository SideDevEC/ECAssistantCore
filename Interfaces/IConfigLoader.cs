using ECAssistant.Core.Config;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Interface for loading EAgentConfig from JSON files.
/// </summary>
public interface IConfigLoader
{
    EAgentConfig Load(string filePath = "appsettings.json");
}