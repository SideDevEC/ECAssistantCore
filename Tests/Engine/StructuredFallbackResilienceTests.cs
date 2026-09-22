using ECAssistant.Core.Engine;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Config;
using ECAssistant.Core.Orchestration;

namespace ECAssistant.Core.Tests.Engine;

/// <summary>
/// v14.10.1: a transient null from GenerateStructuredAsync (provider hiccup,
/// empty choices, malformed reply) must fall back to text streaming for THAT
/// TURN only — the session must keep retrying the structured path next turn.
/// Old behavior flipped _useStructuredDecoding off permanently on the first
/// null, silently downgrading the whole session (live regression: local
/// qwen3.5-4b went to raw-JSON streaming after one bad response, no recovery).
/// </summary>
public class StructuredFallbackResilienceTests
{
    private sealed class NullKvController : IKvCacheController
    {
        public Task<bool> CreateSessionAsync(string sessionId, string? modelId = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DestroySessionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PrefillAsync(string sessionId, string text, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SaveStateAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RewindAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ResetAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<KvCacheStatus?> GetStatusAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<KvCacheStatus?>(null);
    }

    private sealed class NullStructuredEngine : IInferenceEngine
    {
        public string Endpoint => "null-mock";
        public Task<string> GenerateAsync(string prompt, InferenceRequestParams parameters, CancellationToken ct = default)
            => Task.FromResult("");
        public IAsyncEnumerable<string> StreamAsync(string prompt, InferenceRequestParams parameters, CancellationToken ct = default)
            => StreamNoop();
        private static async IAsyncEnumerable<string> StreamNoop()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private static EAgentEngine BuildEngine()
        => new("test-session", new NullStructuredEngine(), new NullKvController(),
            inferenceParams: new InferenceRequestParams(), config: new EAgentConfig());

    [Fact]
    public async Task TransientNull_KeepsStructuredDecodingEnabled()
    {
        var engine = BuildEngine();
        var flagField = typeof(EAgentEngine).GetField("_useStructuredDecoding",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(flagField);
        flagField!.SetValue(engine, true);

        var method = typeof(EAgentEngine).GetMethod("TryGenerateStructuredAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<LLMDecision?>)method!.Invoke(engine,
            new object?[] { "test prompt", new InferenceRequestParams(), CancellationToken.None })!;
        var result = await task;

        Assert.Null(result); // fell back for this turn
        // AND the session keeps structured decoding for the next turn
        Assert.True((bool)flagField.GetValue(engine)!);
    }
}
