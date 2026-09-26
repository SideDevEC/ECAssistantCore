using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Services.Http;

/// <summary>
/// HTTP-based inference engine. Talks to ECAssistantLLM (or any OpenAI-compatible endpoint).
/// Supports streaming (SSE) and non-streaming generation.
/// </summary>
public sealed class HttpStreamingEngine : IInferenceEngine
{
    private readonly OpenAIClient _client;
    private readonly string _defaultModelId;

    /// <summary>Legacy default session id. v15: REMOVED from request routing —
    /// SessionId=null on the params object is now a REAL stateless signal (server
    /// runs a fresh executor, no KV writes). Main turns pass their session id
    /// explicitly via request params, so helper prompts (decompose, planner,
    /// stateless summary) can never leak into a session's KV cache again.</summary>
    public string Endpoint => _client.BaseUrl;

    /// <summary>
    /// Create with an existing OpenAIClient (shared with other services).
    /// <paramref name="defaultSessionId"/> is accepted for ctor compatibility but
    /// no longer used (v15): request routing is driven solely by params.SessionId.
    /// </summary>
    public HttpStreamingEngine(OpenAIClient client, string defaultModelId = "main", string? defaultSessionId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _defaultModelId = defaultModelId;
        // v15: defaultSessionId intentionally unused — see class comment above.
    }

    /// <summary>
    /// Stream tokens via SSE.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        InferenceRequestParams parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(prompt, parameters, stream: true);
        var response = await _client.PostStreamAsync("/v1/chat/completions", body, ct);
        // Audit fix: dispose the response even if ReadAsStreamAsync throws
        // (previously the gap between PostStreamAsync and the stream acquisition
        // leaked the HttpResponseMessage/connection on failure).
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await foreach (var token in SseParser.ParseTokenStreamAsync(stream, ct))
                yield return token;
        }
        finally
        {
            response.Dispose(); // release the connection even on mid-stream cancellation
        }
    }

    /// <summary>
    /// Generate full response (non-streaming, collects all tokens).
    /// </summary>
    public async Task<string> GenerateAsync(
        string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default)
    {
        var body = BuildRequestBody(prompt, parameters, stream: false);
        var json = await _client.PostJsonAsync("/v1/chat/completions", body, ct);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                return content ?? "";
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HttpStreamingEngine] Non-critical error ignored: {ex.Message}"); }

        return "";
    }

    /// <summary>
    /// v13 structured decision generation: requests grammar-constrained decoding
    /// from the server (DecisionGrammar) and returns the raw decision envelope
    /// JSON. Null when the server doesn't support it (caller falls back to text).
    /// </summary>
    public async Task<string?> GenerateStructuredAsync(
        string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default)
    {
        // v13b remote path: native OpenAI function calling — provider-enforced
        // tool_calls, no tags. Synthesizes the same decision envelope the local
        // grammar path produces so downstream handling is identical.
        if (parameters.Tools is { Count: > 0 })
            return await GenerateNativeToolsDecisionAsync(prompt, parameters, ct);

        // v13 local path: server-side grammar-constrained envelope.
        var body = BuildStructuredRequestBody(prompt, parameters);
        var json = await _client.PostJsonAsync("/v1/chat/completions", body, ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("decision", out var decision))
            return null;
        return decision.GetRawText();
    }

    /// <summary>v13b: native function-calling decision via the OpenAI `tools` parameter.</summary>
    private async Task<string?> GenerateNativeToolsDecisionAsync(
        string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct)
    {
        // Typed schemas: use the tool's real parameter schema when it provides one;
        // fall back to the permissive string-args stub for tools without a schema.
        var tools = parameters.Tools!.Select(t =>
        {
            JsonDocument? schemaDoc = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(t.ParameterSchema))
                    schemaDoc = JsonDocument.Parse(t.ParameterSchema);
            }
            catch (JsonException) { /* malformed schema → permissive fallback */ }

            object parameters = schemaDoc is null
                ? new { type = "object", properties = new { }, additionalProperties = new { type = "string" } }
                : (object)schemaDoc.RootElement.Clone();

            return new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters
                }
            };
        }).ToList();

        // v14.10.2: carry the full conversation — system prompt (static prefix) and
        // prior turns — instead of a context-free single user message. The local path
        // gets this from the server-side KV prefix/session; remote has no server-side
        // state, so without this the model is a goldfish (no rules, no history).
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(parameters.SystemPrompt))
            messages.Add(new { role = "system", content = parameters.SystemPrompt });
        if (parameters.HistoryMessages is { Count: > 0 })
            foreach (var (role, msgContent) in parameters.HistoryMessages)
                messages.Add(new { role, content = msgContent });
        messages.Add(new { role = "user", content = prompt });

        var body = JsonSerializer.Serialize(new
        {
            model = parameters.ModelId ?? _defaultModelId,
            messages,
            stream = false,
            tools,
            tool_choice = "auto",
            temperature = parameters.Temperature,
            thinking = parameters.Thinking,
            top_p = parameters.TopP,
            max_tokens = parameters.MaxTokens,
        }, JsonOptions);

        var json = await _client.PostJsonAsync("/v1/chat/completions", body, ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        var message = choices[0].GetProperty("message");
        var thinking = message.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String
            ? rc.GetString() ?? ""
            : "";

        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
        {
            var calls = toolCalls.EnumerateArray()
                .Select(tc =>
                {
                    var fn = tc.GetProperty("function");
                    var args = new Dictionary<string, string>();
                    if (fn.TryGetProperty("arguments", out var raw))
                    {
                        if (raw.ValueKind == JsonValueKind.String)
                        {
                            // Audit fix: models emit malformed JSON arguments more often
                            // than anyone would like — one bad call must not poison the
                            // whole decision (it previously threw out of the parser and
                            // forced a full text fallback for the turn).
                            try
                            {
                                using var argsDoc = JsonDocument.Parse(raw.GetString() ?? "{}");
                                foreach (var p in argsDoc.RootElement.EnumerateObject())
                                    args[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
                            }
                            catch (JsonException)
                            {
                                args["_raw_arguments"] = raw.GetString() ?? "";
                            }
                        }
                        else if (raw.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var p in raw.EnumerateObject())
                                args[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
                        }
                    }
                    return new { name = fn.GetProperty("name").GetString() ?? "", args };
                })
                .ToList();

            // v14.12.2: remote models may write a plain-text progress note alongside
            // tool calls (native protocol allows text + calls). Carry it as commentary
            // instead of discarding — the orchestrator shows it to the user.
            var commentary = message.TryGetProperty("content", out var tc) && tc.ValueKind == JsonValueKind.String
                ? tc.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(commentary)) commentary = null;

            return JsonSerializer.Serialize(new { thinking, commentary, toolcalls = calls });
        }

        // No tool calls — content is the answer, reasoning is the thinking.
        // v14.10.2: envelope-trained models sometimes echo the {"thinking","answer"}
        // envelope as plain content. Unwrap it so raw JSON never leaks to the UI
        // (previously thinking ALSO duplicated content when no reasoning_content was
        // returned, doubling the leak).
        var content = message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String
            ? contentEl.GetString() ?? ""
            : thinking;
        if (content.TrimStart().StartsWith('{'))
        {
            var unwrapped = Engine.StructuredDecisionAdapter.TryExtractAnswer(content);
            if (unwrapped != null)
                content = unwrapped;
        }
        return JsonSerializer.Serialize(new { thinking, answer = content });
    }

    /// <summary>
    /// v15: SessionId=null on the params object means STATELESS — the caller
    /// explicitly chose no KV session (BuildStatelessParams contract). Resolve to
    /// null so `session_id` is omitted from the JSON (WhenWritingNull) and the
    /// server routes the request to its stateless executor instead of appending
    /// this prompt to a session's KV cache.
    /// </summary>
    private static string? ResolveSessionId(InferenceRequestParams parameters)
        => parameters.SessionId; // null stays null — no fallback

    private string BuildStructuredRequestBody(string prompt, InferenceRequestParams parameters)
    {
        // v15 fix: SessionId=null means STATELESS — never fall back to the engine's
        // default session. The old `?? _defaultSessionId` silently routed decompose,
        // planner, and stateless-summary calls into the MAIN session's KV cache,
        // inflating it toward the context wall (16k overflow on shift-incapable
        // models, J2 failure). Null (omitted in JSON) → server's stateless executor.
        var req = new
        {
            model = parameters.ModelId ?? _defaultModelId,
            messages = new[] { new { role = "user", content = prompt } },
            stream = false,
            structured = true,
            temperature = parameters.Temperature,
            thinking = parameters.Thinking,
            top_p = parameters.TopP,
            top_k = parameters.TopK,
            max_tokens = parameters.MaxTokens,
            repeat_penalty = parameters.RepeatPenalty,
            session_id = ResolveSessionId(parameters),
            tool_names = parameters.ToolNames,
            reasoning_effort = parameters.ReasoningEffort,
        };
        return JsonSerializer.Serialize(req, JsonOptions);
    }

    private string BuildRequestBody(string prompt, InferenceRequestParams parameters, bool stream)
    {
        // Multimodal: when images are attached, send OpenAI content-parts array
        // (text part + one image_url data-URI part per image).
        object content = parameters.ImageDataUris is { Count: > 0 }
            ? BuildMultimodalContent(prompt, parameters.ImageDataUris)
            : prompt;

        var req = new
        {
            model = parameters.ModelId ?? _defaultModelId,
            messages = new[]
            {
                new { role = "user", content }
            },
            stream,
            temperature = parameters.Temperature,
            thinking = parameters.Thinking,
            top_p = parameters.TopP,
            top_k = parameters.TopK,
            max_tokens = parameters.MaxTokens,
            repeat_penalty = parameters.RepeatPenalty,
            // v15 fix: SessionId=null means STATELESS — never fall back to the
            // engine's default session (see BuildStructuredRequestBody).
            session_id = ResolveSessionId(parameters),
            stop = parameters.Stop,
            grammar = parameters.Grammar,
            reasoning_effort = parameters.ReasoningEffort,
        };

        return JsonSerializer.Serialize(req, JsonOptions);
    }

    /// <summary>
    /// Build OpenAI multimodal content parts. Images become data-URI image_url parts.
    /// </summary>
    private static System.Text.Json.JsonElement BuildMultimodalContent(string text, IReadOnlyList<string> imageDataUris)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", text);
            writer.WriteEndObject();
            foreach (var uri in imageDataUris)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "image_url");
                writer.WriteStartObject("image_url");
                writer.WriteString("url", uri);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return System.Text.Json.JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}