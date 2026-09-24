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
    private int _windowSummarized = 0;

    /// <summary>Flat token estimate per attached image for budgeting.</summary>
    public const int TokensPerImage = 800;
    private readonly TokenCounter _tokenCounter;
    private readonly uint _maxTokens;
    private SummaryService? _summaryService;
    private uint _autoSummarizeThreshold;

    public ContextWindow(uint maxTokens, TokenCounter? tokenCounter = null,
        double autoSummarizeThresholdFraction = 0.50)
    {
        _maxTokens = maxTokens;
        _autoSummarizeThreshold = (uint)(maxTokens * autoSummarizeThresholdFraction);
        _tokenCounter = tokenCounter ?? new TokenCounter();
    }

    public ContextWindow(uint maxTokens, SummaryService? summaryService, TokenCounter? tokenCounter = null,
        double autoSummarizeThresholdFraction = 0.50)
    {
        _maxTokens = maxTokens;
        _autoSummarizeThreshold = (uint)(maxTokens * autoSummarizeThresholdFraction);
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
            (totalEst > (int)_autoSummarizeThreshold && MessageCount > 4))
        {
            SummarizeOldest(totalEst);
        }

        List<TranscriptMessage> snapshot;
        lock (_messagesLock)
            snapshot = _messages.ToList();
        return snapshot;
    }

    public void SetSummaryService(SummaryService service) => _summaryService = service;

    /// <summary>
    /// v15: returns true exactly once after SummarizeOldest replaced in-window
    /// messages with a summary — the server KV cache needs a reset + re-feed.
    /// </summary>
    public bool ConsumeWindowSummarized() =>
        Interlocked.Exchange(ref _windowSummarized, 0) == 1;

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

    /// <summary>
    /// Staged compaction stage 1 (2026-09-21): drop stale tool outputs (all but the
    /// most recent) WITHOUT an LLM summarize call — the cheapest way to reclaim
    /// budget. Returns true if trimming brought the window back under budget.
    /// </summary>
    /// <summary>
    /// v14.9 tool-aware trim: keeps the <paramref name="keepRecent"/> most recent tool
    /// outputs verbatim; older ones are replaced with a one-line stub (positions stay
    /// valid, context keeps a trace that the call happened). Returns true when the
    /// window is back within budget after trimming.
    /// </summary>
    public bool TrimStaleToolOutputs(int keepRecent = 1)
    {
        lock (_messagesLock)
        {
            var toolIndices = new List<int>();
            for (int i = 0; i < _messages.Count; i++)
                if (_messages[i].Role == "tool_output") toolIndices.Add(i);

            // Replace older tool outputs (descending keeps positions valid). Replacement
            // preserves the marker that a tool ran without retaining the bulky output.
            for (int k = toolIndices.Count - keepRecent - 1; k >= 0; k--)
            {
                var msg = _messages[toolIndices[k]];
                msg.Content = "[earlier tool output trimmed by compaction — full result in transcript]";
                msg.EstimatedTokens = 12;
            }
        }
        return GetTotalTokens() <= (int)_maxTokens;
    }

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

    /// <summary>v15: true when a compaction summary message is present (window
    /// shrinkage is then legitimate, not corruption).</summary>
    public bool ContainsSummaryMarker
     {
        get
         {
            lock (_messagesLock)
                return _messages.Any(m => m.Role == "system" &&
                    (m.Content.Contains("[Summary", StringComparison.Ordinal) ||
                     m.Content.Contains("[Previous conversation summary", StringComparison.Ordinal)));
         }
     }

    /// <summary>v15: true when a user message contains the given task text.</summary>
    public bool HasUserMessageContaining(string text)
     {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var probe = text.Trim();
        if (probe.Length > 64) probe = probe[..64];
        lock (_messagesLock)
            return _messages.Any(m => m.Role == "user" && m.Content.Contains(probe, StringComparison.Ordinal));
     }

    /// <summary>
    /// v15: window-integrity self-heal — replace the window with the full transcript.
    /// Used when the window lost content it must logically contain (empty window,
    /// or the user task vanished without a compaction summary). The window is
    /// derived state; the transcript is the source of truth. Over-budget windows
    /// re-compact normally on the next GetWindowMessages call.
    /// </summary>
    public void RestoreFrom(IEnumerable<TranscriptMessage> messages)
     {
        lock (_messagesLock)
         {
            _messages.Clear();
            _messages.AddRange(messages.Select(m => new TranscriptMessage
             {
                Role = m.Role,
                Source = m.Source,
                Content = m.Content,
                EstimatedTokens = m.EstimatedTokens,
                ImageDataUris = m.ImageDataUris
             }));
         }
     }
    public uint MaxTokens => _maxTokens;

    /// <summary>
    /// Trims old messages when over budget and replaces them with a summary.
    /// Messages are snapshotted first and only deleted AFTER the summary succeeds
    /// (memory control lags one summarize call — correctness beats immediacy).
    /// A guard prevents overlapping summarize runs.
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
            List<TranscriptMessage> oldMessages;

            // Snapshot the messages to summarize — do NOT remove them yet.
            lock (_messagesLock)
            {
                oldMessages = _messages.Take(Math.Max(0, count - keepCount)).ToList();
            }

            if (_summaryService == null || oldMessages.Count <= 3) return;

            // Keep the guard held until the background insert completes so a
            // concurrent GetWindowMessages cannot start a second trim race.
            backgroundScheduled = true;

            // Summarize FIRST, then delete. Deleting before the summary succeeds meant
            // a failed LLM call permanently lost the old messages.
            _ = Task.Run(async () =>
            {
                try
                {
                    var summary = await _summaryService.SummarizeAsync(oldMessages, preferWarmSession: false);
                    var summaryMsg = TranscriptMessage.System(summary);
                    summaryMsg.EstimatedTokens = _tokenCounter.Count(summary);
                    lock (_messagesLock)
                    {
                        // Summary succeeded — only NOW remove the summarized messages.
                        // Identity-based removal: safe even if new messages were prepended.
                        foreach (var old in oldMessages)
                        {
                            var idx = _messages.IndexOf(old);
                            if (idx >= 0) _messages.RemoveAt(idx);
                        }

                        // Insert after any leading system message(s) so the real system prompt stays first
                        var insertAt = 0;
                        while (insertAt < _messages.Count && _messages[insertAt].Role == "system")
                            insertAt++;
                        _messages.Insert(insertAt, summaryMsg);
                        // v15 fix: the server-side KV cache still holds the pre-summary
                        // conversation. Flag it so the engine can reset + re-feed the
                        // server cache (KV/window desync otherwise overflows the server
                        // context window even though this window stays compact).
                        Interlocked.Exchange(ref _windowSummarized, 1);
                    }
                }
                catch { /* summarization is best-effort — messages stay in place on failure */ }
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