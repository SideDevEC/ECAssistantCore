using System.Text.Json;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Default <see cref="IRemoteModelProbe"/>: GET {endpoint}/models with optional bearer
/// auth. Vision detection: OpenRouter-style "architecture.input_modalities" containing
/// "image"; OpenAI-style "modalities" containing "image"; otherwise unknown → false.
/// </summary>
public sealed class RemoteModelProbe : IRemoteModelProbe
{
    private readonly HttpClient? _httpClient;

    /// <param name="httpClient">Optional preconfigured client (tests inject a fake handler); default = per-call client.</param>
    public RemoteModelProbe(HttpClient? httpClient = null)
    {
        _httpClient = httpClient;
    }

    // Stateless utility — no mutable state.
    private static async Task<IReadOnlyList<RemoteModelInfo>> ParseBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseModels(doc.RootElement);
    }

    /// <inheritdoc />
    public async Task<RemoteProbeResult> ProbeAsync(string endpoint, string? apiKey, CancellationToken ct = default)
    {
        try
        {
            HttpClient http;
            if (_httpClient != null) { http = _httpClient; }
            else
            {
                http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            }
            using (http)
            {
                if (!string.IsNullOrEmpty(apiKey))
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                // Endpoints may be typed with or without a trailing "/v1" — try both
                // model-list locations before giving up.
                var baseUrl = endpoint.TrimEnd('/');
                using var resp = await http.GetAsync($"{baseUrl}/models", ct);
                if (resp.IsSuccessStatusCode)
                    return new RemoteProbeResult(true, await ParseBodyAsync(resp, ct));

                // Endpoints may be typed with or without a trailing "/v1" — try the
                // versioned model-list location before giving up.
                if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                {
                    using var resp2 = await http.GetAsync($"{baseUrl}/v1/models", ct);
                    if (resp2.IsSuccessStatusCode)
                        return new RemoteProbeResult(true, await ParseBodyAsync(resp2, ct));
                    return new RemoteProbeResult(false, Array.Empty<RemoteModelInfo>(), $"HTTP {(int)resp.StatusCode}");
                }
                return new RemoteProbeResult(false, Array.Empty<RemoteModelInfo>(), $"HTTP {(int)resp.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            return new RemoteProbeResult(false, Array.Empty<RemoteModelInfo>(), ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new RemoteProbeResult(false, Array.Empty<RemoteModelInfo>(), "timeout");
        }
        catch (JsonException ex)
        {
            return new RemoteProbeResult(false, Array.Empty<RemoteModelInfo>(), $"invalid response: {ex.Message}");
        }
    }

    // Stateless utility — no mutable state.
    private static IReadOnlyList<RemoteModelInfo> ParseModels(JsonElement root)
    {
        var items = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array ? data
            : root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array ? models
            : root;

        var result = new List<RemoteModelInfo>();
        foreach (var item in items.EnumerateArray())
        {
            var id = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString()
                : item.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString()
                : null;
            if (string.IsNullOrEmpty(id)) continue;
            result.Add(new RemoteModelInfo(id, DetectVision(item)));
        }
        return result;
    }

    // Stateless utility — no mutable state.
    private static bool DetectVision(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return false;

        if (item.TryGetProperty("architecture", out var arch) && arch.ValueKind == JsonValueKind.Object &&
            arch.TryGetProperty("input_modalities", out var mods) && mods.ValueKind == JsonValueKind.Array)
            return mods.EnumerateArray().Any(m =>
                m.ValueKind == JsonValueKind.String &&
                m.GetString()?.Equals("image", StringComparison.OrdinalIgnoreCase) == true);

        if (item.TryGetProperty("modalities", out var mods2) && mods2.ValueKind == JsonValueKind.Array)
            return mods2.EnumerateArray().Any(m =>
                m.ValueKind == JsonValueKind.String &&
                m.GetString()?.Contains("image", StringComparison.OrdinalIgnoreCase) == true);

        if (item.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object &&
            caps.TryGetProperty("vision", out var vision) && vision.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return vision.GetBoolean();

        return false;
    }
}
