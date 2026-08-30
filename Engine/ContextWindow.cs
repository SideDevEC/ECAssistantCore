using System.Text;
using ECAssistant.Core.Config;
using ECAssistant.Core.Services;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Manages the LLM conversation context window.
/// </summary>
public class ContextWindow
{
    private readonly List<TranscriptMessage> _messages = new();
    private readonly object _messagesLock = new();
    private int _summarizeInProgress = 0;

    /// <summary>Flat token estimate per attached image for budgeting.</summary>
    public const int TokensPerImage = 800;
    private readonly TokenCounter _tokenCounter;
    private readonly uint _maxTokens;
    private SummaryService? _summaryService;
    private uint _autoSummarizeThreshold;

    public ContextWindow(uint maxTokens, TokenCounter? tokenCounter = null)
    {
        _maxTokens = maxTokens;
        _autoSummarizeThreshold = (uint)(maxTokens * 0.50f);
        _tokenCounter = tokenCounter ?? new TokenCounter();
    }

    public ContextWindow(uint maxTokens, SummaryService? summaryService, TokenCounter? tokenCounter = null)
    {
        _maxTokens = maxTokens;
        _autoSummarizeThreshold = (uint)(maxTokens * 0.50f);
        _summaryService = summaryService;
        _tokenCounter = tokenCounter ?? new TokenCounter();
    }

    public int AddUserMessage(string content)
        => AddUserMessage(content, null);

    /// <summary>
    /// Add a user message with attached images (data URIs) for vision-capable models.
    /// Each image adds a flat token estimate (~800 tokens).
    /// </summary>
    public int AddUserMessage(string content, IReadOnlyList<string>? imageDataUris)
    {
        var tokens = _tokenCounter.Count(content) + Math.Max(0, imageDataUris?.Count ?? 0) * TokensPerImage;
        var msg = TranscriptMessage.User(content);
        msg.EstimatedTokens = tokens;
        if (imageDataUris is { Count: > 0 })
            msg.ImageDataUris.AddRange(imageDataUris);
        lock (_messagesLock)
            _messages.Add(msg);
        return tokens;
    }

    public int AddAssistantMessage(string content)
    {
        var tokens = _tokenCounter.Count(content);
        var msg = TranscriptMessage.Assistant(content);
        msg.EstimatedTokens = tokens;
        lock (_messagesLock)
            _messages.Add(msg);
        return tokens;
    }

    public int AddToolOutput(string content, string toolName = "")
    {
        var tokens = _tokenCounter.Count(content);
        var msg = TranscriptMessage.ToolOutput(content, toolName);
        msg.EstimatedTokens = tokens;
        lock (_messagesLock)
            _messages.Add(msg);
        return tokens;
    }

    public int AddSystemMessage(string content)
    {
        var tokens = _tokenCounter.Count(content);
        lock (_messagesLock)
        {
            if (_messages.Count == 0 || _messages[0].Role != "system")
            {
                var msg = TranscriptMessage.System(content);
                msg.EstimatedTokens = tokens;
                _messages.Insert(0, msg);
            }
            else
            {
                _messages[0].Content = content;
                _messages[0].EstimatedTokens = tokens;
            }
        }
        return tokens;
    }

    public List<TranscriptMessage> GetWindowMessages()
    {
        int totalEst = 0;
        lock (_messagesLock)
        {
            foreach (var m in _messages) totalEst += m.EstimatedTokens;
        }

        if (totalEst > (int)_maxTokens ||
            (totalEst > (int)_autoSummarizeThreshold && MessageCount > 10))
        {
            SummarizeOldest(totalEst);
        }

        List<TranscriptMessage> snapshot;
        lock (_messagesLock)
            snapshot = _messages.ToList();
        return snapshot;
    }

    public void SetSummaryService(SummaryService service) => _summaryService = service;

    public bool RemoveLastAssistantMessage()
    {
        lock (_messagesLock)
        {
            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].Role == "assistant")
                {
                    _messages.RemoveAt(i);
                    return true;
                }
            }
        }
        return false;
    }

    public void Clear() { lock (_messagesLock) _messages.Clear(); }

    public bool IsWithinBudget()
    {
        lock (_messagesLock)
        {
            var total = 0;
            foreach (var m in _messages) total += m.EstimatedTokens;
            return total <= _maxTokens;
        }
    }

    public int GetTotalTokens()
    {
        lock (_messagesLock)
        {
            var total = 0;
            foreach (var m in _messages) total += m.EstimatedTokens;
            return total;
        }
    }

    public int MessageCount { get { lock (_messagesLock) return _messages.Count; } }
    public uint MaxTokens => _maxTokens;

    /// <summary>
    /// Trims old messages when over budget and replaces them with a summary.
    /// The trim is synchronous (memory control must be immediate); the LLM summary
    /// completes in the background and is then inserted after the leading system
    /// message(s). A guard prevents overlapping summarize runs.
    /// </summary>
    private void SummarizeOldest(int currentTotal)
    {
        // Trigger when the estimate crosses the auto-summarize threshold
        // (not just when it exceeds the hard max-token budget).
        if (currentTotal <= (int)_autoSummarizeThreshold) return;
        if (Interlocked.Exchange(ref _summarizeInProgress, 1) == 1) return;

        bool backgroundScheduled = false;
        try
        {
            int count;
            lock (_messagesLock)
                count = _messages.Count;
            var keepCount = Math.Max(5, (int)(count * 0.3f));
            var oldMessages = new List<TranscriptMessage>();

            lock (_messagesLock)
            {
                while (_messages.Count > keepCount && currentTotal > (int)_autoSummarizeThreshold)
                {
                    var removed = _messages[0];
                    oldMessages.Add(removed);
                    currentTotal -= removed.EstimatedTokens;
                    _messages.RemoveAt(0);
                }
            }

            if (_summaryService == null || oldMessages.Count <= 3) return;

            // Keep the guard held until the background insert completes so a
            // concurrent GetWindowMessages cannot start a second trim race.
            backgroundScheduled = true;

            // Fire-and-forget but NOT async void: exceptions are contained here.
            _ = Task.Run(async () =>
            {
                try
                {
                    var summary = await _summaryService.SummarizeAsync(oldMessages, preferWarmSession: true);
                    var summaryMsg = TranscriptMessage.System(summary);
                    lock (_messagesLock)
                    {
                        // Insert after any leading system message(s) so the real system prompt stays first
                        var insertAt = 0;
                        while (insertAt < _messages.Count && _messages[insertAt].Role == "system" && insertAt < oldMessages.Count && oldMessages[insertAt].Role == "system")
                            insertAt++;
                        while (insertAt < _messages.Count && _messages[insertAt].Role == "system")
                            insertAt++;
                        _messages.Insert(insertAt, summaryMsg);
                    }
                }
                catch { /* summarization is best-effort — old messages are already trimmed */ }
                finally { Interlocked.Exchange(ref _summarizeInProgress, 0); }
            });
        }
        finally
        {
            if (!backgroundScheduled)
                Interlocked.Exchange(ref _summarizeInProgress, 0);
        }
    }

}