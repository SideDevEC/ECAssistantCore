namespace ECAssistant.Core.Interfaces;

/// <summary>A fully-resolved remote provider ready for engine construction.</summary>
public sealed record RemoteProvider(
    string Name,
    string Endpoint,
    string? ApiKey,
    string ModelId,
    string? EmbeddingModelId);

/// <summary>
/// Resolves multi-provider configuration into concrete providers.
/// Keys are resolved eagerly (literal vs file reference) so callers never
/// deal with key storage mechanics.
/// </summary>
public interface ILlmProviderRegistry
{
    /// <summary>Providers that failed validation (name/endpoint/key errors) — surfaced for logging.</summary>
    IReadOnlyList<string> ValidationErrors { get; }

    /// <summary>All valid providers in configured order.</summary>
    IReadOnlyList<RemoteProvider> Providers { get; }

    /// <summary>The default provider (default_provider name, else is_default, else first). Null if none configured/valid.</summary>
    RemoteProvider? Default { get; }

    /// <summary>Resolve by exact name (case-insensitive). Null if unknown.</summary>
    RemoteProvider? GetByName(string? name);

    /// <summary>
    /// Ordered candidates to try. Fallback disabled (or one entry): just the preferred/default.
    /// Fallback enabled: preferred first, then all remaining in configured order.
    /// Entries with no providers configured yield empty list.
    /// </summary>
    IReadOnlyList<RemoteProvider> OrderedCandidates(string? preferredName = null);
}
