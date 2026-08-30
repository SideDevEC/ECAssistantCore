using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Abstracts LLM inference via HTTP (OpenAI-compatible endpoint).
/// Supports both streaming and non-streaming generation.
/// </summary>
public interface IInferenceEngine
{
    /// <summary>Stream tokens one by one via HTTP SSE.</summary>
    IAsyncEnumerable<string> StreamAsync(
        string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default);

    /// <summary>Generate full response (collects all tokens).</summary>
    Task<string> GenerateAsync(
        string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default);

    /// <summary>
    /// v13: grammar-structured decision generation. Returns the raw decision
    /// envelope JSON (see ARCHITECTURE-STRUCTURED-DECODING.md), or null when the
    /// backend does not support structured decoding (caller falls back to text).
    /// </summary>
    Task<string?> GenerateStructuredAsync(
        string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    /// <summary>Server endpoint URL (e.g. http://localhost:8420).</summary>
    string Endpoint { get; }
}

/// <summary>
/// Parameters for an inference request. Maps to OpenAI chat completion fields.
/// </summary>
public sealed class InferenceRequestParams
{
    public string? ModelId { get; set; }
    public string? SessionId { get; set; }
    public int? MaxTokens { get; set; }
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int? TopK { get; set; }
    public float? RepeatPenalty { get; set; }
    public string[]? Stop { get; set; }
    public bool Stream { get; set; } = true;

    /// <summary>
    /// Images attached to this request as base64 data URIs (vision-capable models only).
    /// Sent as OpenAI multimodal content parts on the user message.
    /// </summary>
    public List<string> ImageDataUris { get; set; } = new();
}