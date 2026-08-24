using System;
using ECAssistant.Core.Services.Http;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Token counting via RemoteTokenizer (HTTP /eca/tokenize endpoint).
/// Falls back to char-based estimation if server unavailable.
/// </summary>
public class TokenCounter
{
    private RemoteTokenizer? _tokenizer;

    public TokenCounter(RemoteTokenizer? tokenizer = null)
    {
        _tokenizer = tokenizer;
    }

    public void Initialize(RemoteTokenizer tokenizer) => _tokenizer = tokenizer;

    public int Count(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        if (_tokenizer != null)
        {
            return _tokenizer.Count(text);
        }

        // Char-based fallback
        return Math.Max(1, text.Length / 4);
    }

    public int EstimateUpper(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        if (_tokenizer != null)
        {
            return _tokenizer.EstimateUpper(text);
        }

        return (int)(text.Length / 3.5f);
    }
}