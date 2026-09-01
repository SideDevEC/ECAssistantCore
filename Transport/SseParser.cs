using System.IO;
using System.Text.Json;

namespace ECAssistant.Core.Transport;

/// <summary>
/// Parses SSE (Server-Sent Events) stream from OpenAI-compatible chat completions.
/// Extracts token content from "data:" lines.
/// </summary>
public static class SseParser
{
    /// <summary>
    /// Parse an SSE stream and yield content tokens one by one.
    /// Stops at "data: [DONE]".
    /// </summary>
    public static async IAsyncEnumerable<string> ParseTokenStreamAsync(
        Stream responseStream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(responseStream);
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SseParser] Stream read aborted: {ex.Message}");
                break;
            }

            if (line == null)
                break; // end of stream

            // Skip empty lines (event boundaries)
            if (string.IsNullOrEmpty(line))
                continue;

            // Check for end of stream
            if (line == "data: [DONE]" || line == "data:[DONE]")
                break;

            // Parse "data: {json}" lines — accept with OR without the space after the colon
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var json = line["data:".Length..].Trim();

            string? contentText = null;
            try
            {
                using var doc = JsonDocument.Parse(json);

                // Navigate: choices[0].delta.content
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String)
                    {
                        contentText = content.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed JSON — skip this line
            }

            if (!string.IsNullOrEmpty(contentText))
                yield return contentText;
        }
    }
}