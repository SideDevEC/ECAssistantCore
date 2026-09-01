using System;
using System.IO;
using System.Text.Json;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Config;

namespace ECAssistant.Core.Services;

/// <summary>
/// JSON configuration loader.
/// </summary>
public class ConfigProvider : IConfigProvider
{
    private readonly IFileSystem _fileSystem;
    private readonly string _configPath;
    private readonly EAgentConfig? _preloadedConfig;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Parsed JSON root — parsing per-lookup was wasteful and re-read the file.
    private JsonDocument? _cachedRoot;
    private readonly object _cacheLock = new();

    /// <summary>Load config from a JSON file on disk.</summary>
    public ConfigProvider(IFileSystem fileSystem, string configPath)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
    }

    /// <summary>v10.23: Use a preloaded EAgentConfig directly — no file I/O. For library consumers.</summary>
    public ConfigProvider(IFileSystem fileSystem, EAgentConfig config)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _preloadedConfig = config ?? throw new ArgumentNullException(nameof(config));
        _configPath = "";
    }

    /// <summary>Parse the config once and cache the document root for all subsequent lookups.</summary>
    private JsonDocument GetRoot()
    {
        lock (_cacheLock)
        {
            if (_cachedRoot != null) return _cachedRoot;
            var raw = _preloadedConfig != null
                ? JsonSerializer.Serialize(_preloadedConfig, _jsonOptions)
                : _fileSystem.ReadFile(_configPath);
            _cachedRoot = JsonDocument.Parse(raw);
            return _cachedRoot;
        }
    }

    public T GetSection<T>(string section) where T : class, new()
    {
        var root = GetRoot();
        // Missing section must not throw — return a fresh default instead.
        return root.RootElement.ValueKind == JsonValueKind.Object
               && root.RootElement.TryGetProperty(section, out var sectionElement)
            ? JsonSerializer.Deserialize<T>(sectionElement.GetRawText(), _jsonOptions) ?? new T()
            : new T();
    }

    public string GetValue(string key, string defaultValue = "")
    {
        var root = GetRoot();
        return root.RootElement.ValueKind == JsonValueKind.Object
               && root.RootElement.TryGetProperty(key, out var prop) ? prop.GetString() ?? defaultValue : defaultValue;
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        var root = GetRoot();
        return root.RootElement.ValueKind == JsonValueKind.Object
               && root.RootElement.TryGetProperty(key, out var prop) ? prop.GetInt32() : defaultValue;
    }

    public float GetFloat(string key, float defaultValue = 0f)
    {
        var root = GetRoot();
        return root.RootElement.ValueKind == JsonValueKind.Object
               && root.RootElement.TryGetProperty(key, out var prop) ? prop.GetSingle() : defaultValue;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        var root = GetRoot();
        return root.RootElement.ValueKind == JsonValueKind.Object
               && root.RootElement.TryGetProperty(key, out var prop) ? prop.GetBoolean() : defaultValue;
    }
}