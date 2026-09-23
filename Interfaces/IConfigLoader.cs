using ECAssistant.Core.Config;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Interface for loading AppConfig from JSON files.
/// </summary>
public interface IConfigLoader
{
    AppConfig Load(string filePath = "appsettings.json");
}