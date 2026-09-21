using ECAssistant.Core.Setup;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Tests.Transport;

/// <summary>
/// Tests for base-URL normalization (strip trailing "/v1") and the remote
/// model probe's dual-path fallback. Real-world motivation: OpenRouter-style
/// endpoints ("https://openrouter.ai/api/v1") vs OpenAI-style bases without
/// a version suffix — both must work everywhere.
/// </summary>
public sealed class EndpointNormalizerTests
{
    [Theory]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/V1", "https://openrouter.ai/api")]
    [InlineData("http://localhost:48217", "http://localhost:48217")]
    [InlineData("http://localhost:48217/", "http://localhost:48217")]
    [InlineData("https://api.openai.com", "https://api.openai.com")]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com")]
    public void NormalizeBaseUrl_StripsTrailingV1(string input, string expected)
    {
        Assert.Equal(expected, EndpointNormalizer.NormalizeBaseUrl(input));
    }

    [Fact]
    public void NormalizeBaseUrl_NullOrEmpty_IsSafe()
    {
        Assert.Equal("", EndpointNormalizer.NormalizeBaseUrl(null!));
        Assert.Equal("", EndpointNormalizer.NormalizeBaseUrl(""));
    }
}

public sealed class RemoteModelProbePathTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        private readonly Dictionary<string, HttpResponseMessage> _responses;

        public RecordingHandler(Dictionary<string, HttpResponseMessage> responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(_responses.TryGetValue(request.RequestUri.ToString(), out var r)
                ? r
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task EndpointWithV1_QueriesBaseModels_Directly()
    {
        var handler = new RecordingHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["https://openrouter.ai/api/v1/models"] =
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[]}""", System.Text.Encoding.UTF8, "application/json")
                }
        });
        var probe = new RemoteModelProbe(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) });

        var result = await probe.ProbeAsync("https://openrouter.ai/api/v1", "key");

        Assert.True(result.Reachable);
        Assert.Equal(["https://openrouter.ai/api/v1/models"], handler.Requests);
    }

    [Fact]
    public async Task EndpointWithoutV1_FallsBackToV1Models()
    {
        var handler = new RecordingHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["https://api.example.com/models"] =
                new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
            ["https://api.example.com/v1/models"] =
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[]}""", System.Text.Encoding.UTF8, "application/json")
                }
        });
        var probe = new RemoteModelProbe(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) });

        var result = await probe.ProbeAsync("https://api.example.com", "key");

        Assert.True(result.Reachable);
        Assert.Equal(["https://api.example.com/models", "https://api.example.com/v1/models"], handler.Requests);
    }

    [Fact]
    public async Task UnreachableEndpoint_ReportsFailure()
    {
        var handler = new RecordingHandler(new Dictionary<string, HttpResponseMessage>());
        var probe = new RemoteModelProbe(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) });

        var result = await probe.ProbeAsync("https://api.example.com", "key");

        Assert.False(result.Reachable);
        Assert.Equal(2, handler.Requests.Count);
    }
}