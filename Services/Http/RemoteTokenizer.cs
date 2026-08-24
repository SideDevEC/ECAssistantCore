using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// HTTP-based tokenizer. Calls /eca/tokenize on the server for accurate token counting.
/// Falls back to char-based estimation if endpoint unavailable.
/// </summary>
public sealed class RemoteTokenizer
{
    private readonly OpenAIClient _client;
    private readonly string _modelId;

    public RemoteTokenizer(OpenAIClient client, string modelId = "main")
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _modelId = modelId;
    }

    /// <summary>
    /// Count tokens in text. Falls back to ~4 chars/token if server unavailable.
    /// </summary>
    public int Count(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        try
        {
            var body = JsonSerializer.Serialize(new { model = _modelId, text });
            var json = _client.PostJsonAsync("/eca/tokenize", body).GetAwaiter().GetResult();

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tokens", out var tokens))
                return tokens.GetInt32();
        }
        catch
        {
            // Fallback
        }

        return System.Math.Max(1, text.Length / 4);
    }

    /// <summary>
    /// Upper-bound estimate. Uses server if available, else char-based.
    /// </summary>
    public int EstimateUpper(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = Count(text);
        return count + 16;
    }
}