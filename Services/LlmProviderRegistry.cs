using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// Resolves the "llm_providers" config section into concrete RemoteProvider records.
/// API keys: literal values pass through; "file:&lt;path&gt;" references are read from disk
/// at construction (~/ expanded, whitespace trimmed). Invalid entries are skipped with
/// reasons retrievable via ValidationErrors so misconfiguration is visible in logs.
/// </summary>
public sealed class LlmProviderRegistry : ILlmProviderRegistry
{
    private readonly List<RemoteProvider> _providers = new();
    private readonly string? _defaultName;
    private readonly string? _flagDefaultName;
    private readonly bool _fallbackEnabled;
    public IReadOnlyList<string> ValidationErrors { get; }

    public LlmProviderRegistry(MultiLlmProvidersConfig? section, ILogger? logger = null)
    {
        var errors = new List<string>();
        if (section == null || section.Providers.Count == 0)
        {
            ValidationErrors = errors;
            return;
        }

        _defaultName = section.DefaultProvider;
        _fallbackEnabled = section.FallbackEnabled;
        var flagDefault = section.Providers.FirstOrDefault(p => p.IsDefault)?.Name;

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in section.Providers)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                errors.Add("provider entry missing 'name' — skipped");
                continue;
            }
            if (!seenNames.Add(entry.Name))
            {
                errors.Add($"duplicate provider name '{entry.Name}' — skipped");
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.Endpoint))
            {
                errors.Add($"provider '{entry.Name}' missing 'endpoint' — skipped");
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.ModelId))
            {
                errors.Add($"provider '{entry.Name}' missing 'model_id' — skipped");
                continue;
            }

            string? apiKey;
            try { apiKey = ResolveApiKey(entry.ApiKey); }
            catch (Exception ex)
            {
                errors.Add($"provider '{entry.Name}' api_key reference failed: {ex.Message} — skipped");
                continue;
            }

            _providers.Add(new RemoteProvider(
                entry.Name.Trim(),
                entry.Endpoint.TrimEnd('/'),
                apiKey,
                entry.ModelId,
                entry.EmbeddingModelId));
        }

        if (_defaultName is { Length: > 0 } && Resolve(_defaultName) == null)
            errors.Add($"default_provider '{_defaultName}' not found among valid providers");

        foreach (var e in errors)
            logger?.Warn("LlmProviderRegistry", e);

        ValidationErrors = errors;
        _flagDefaultName = flagDefault;
    }

    /// <summary>Resolve literal vs "file:<path>" API key reference.</summary>
    public static string? ResolveApiKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        const string prefix = "file:";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return value;

        var rawPath = value[prefix.Length..].Trim();
        if (rawPath.StartsWith("~/"))
            rawPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), rawPath[2..]);

        var full = Path.GetFullPath(rawPath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"api_key file not found: {full}");

        return File.ReadAllText(full).Trim();
    }

    public IReadOnlyList<RemoteProvider> Providers => _providers;

    public RemoteProvider? Default =>
        Resolve(_defaultName)
        ?? Resolve(_flagDefaultName)
        ?? _providers.FirstOrDefault();

    public RemoteProvider? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _providers.FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<RemoteProvider> OrderedCandidates(string? preferredName = null)
    {
        if (_providers.Count == 0) return Array.Empty<RemoteProvider>();

        var preferred = Resolve(preferredName);
        preferred ??= Default;
        if (preferred == null) return Array.Empty<RemoteProvider>();

        if (!_fallbackEnabled)
            return new[] { preferred };

        var result = new List<RemoteProvider> { preferred };
        result.AddRange(_providers.Where(p => !p.Name.Equals(preferred.Name, StringComparison.OrdinalIgnoreCase)));
        return result;
    }
}
