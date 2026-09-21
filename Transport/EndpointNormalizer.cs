namespace ECAssistant.Core.Transport;

/// <summary>
/// Normalizes OpenAI-compatible base URLs so the rest of the engine can safely
/// append versioned paths like "/v1/chat/completions".
/// Rule: a trailing "/v1" (with optional trailing slashes) is stripped.
/// "https://openrouter.ai/api/v1/" → "https://openrouter.ai/api"
/// "http://localhost:48217" → unchanged (no version suffix).
/// Users type endpoints both with and without "/v1" — normalization makes both work.
/// </summary>
// Stateless utility — no mutable state.
public static class EndpointNormalizer
{
    public static string NormalizeBaseUrl(string baseUrl)
    {
        var url = baseUrl?.Trim() ?? "";
        while (url.EndsWith("/"))
            url = url[..^1];

        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            url = url[..^3];

        return url;
    }
}