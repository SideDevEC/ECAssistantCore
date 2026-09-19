using System.Text.Json;

namespace ECAssistant.Core.Setup;

/// <summary>
/// Fetches the model catalog from GitHub at wizard time so model links/availability
/// stay current without shipping a new app release. Falls back to the embedded
/// default catalog on ANY failure (offline, timeout, invalid payload) — setup must
/// never brick because GitHub is unreachable. Stateless utility — no mutable state.
/// </summary>
public sealed class CatalogFetcher
{
    // Stateless utility — no mutable state

    /// <summary>Canonical remote catalog location (main branch, this repo).</summary>
    public const string RemoteUrl =
        "https://raw.githubusercontent.com/SideDevEC/ECAssistantCore/main/catalog/model-catalog.json";

    private readonly HttpClient _http;
    private readonly string _url;

    public CatalogFetcher(HttpClient http) : this(http, RemoteUrl) { }

    /// <summary>Test/override constructor — custom catalog URL.</summary>
    public CatalogFetcher(HttpClient http, string url)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    /// <summary>
    /// Try to fetch + validate the remote catalog. Returns null on any failure —
    /// callers fall back to the embedded default.
    /// </summary>
    public async Task<ModelCatalogDocument?> TryFetchAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var resp = await _http.GetAsync(_url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var doc = await JsonSerializer.DeserializeAsync<ModelCatalogDocument>(stream, ModelCatalogDocument.Options, cts.Token);
            if (doc is null || doc.Validate() is not null) return null;

            return doc;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null; // offline / timeout / invalid — caller falls back to embedded
        }
    }
}
