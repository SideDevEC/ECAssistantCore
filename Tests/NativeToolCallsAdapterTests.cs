using System.Text.Json;
using ECAssistant.Core.Engine;
using Xunit;

namespace ECAssistant.Core.Tests;

/// <summary>v14: remote native tool_calls (OpenAI function calling) → decision envelope → LLMDecision.</summary>
public class NativeToolCallsAdapterTests
{
    private static string Synthesize(string responseJson)
    {
        // Mirrors HttpStreamingEngine.GenerateNativeToolsDecisionAsync logic.
        using var doc = JsonDocument.Parse(responseJson);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        var thinking = message.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String
            ? rc.GetString() ?? ""
            : message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";

        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
        {
            var calls = toolCalls.EnumerateArray().Select(tc =>
            {
                var fn = tc.GetProperty("function");
                var args = new Dictionary<string, string>();
                var rawArgs = fn.GetProperty("arguments");
                using var argsDoc = JsonDocument.Parse(rawArgs.ValueKind == JsonValueKind.String ? rawArgs.GetString() ?? "{}" : rawArgs.GetRawText());
                foreach (var p in argsDoc.RootElement.EnumerateObject())
                    args[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
                return new { name = fn.GetProperty("name").GetString() ?? "", args };
            }).ToList();
            return JsonSerializer.Serialize(new { thinking, toolcalls = calls });
        }
        return JsonSerializer.Serialize(new { thinking, answer = thinking });
    }

    private static string Response(string messageJson) =>
        $$"""{"choices":[{"message":{{messageJson}}}]}""";

    [Fact]
    public void NativeToolCall_ParsesToToolCallDecision()
    {
        var message = JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[]
            {
                new { type = "function", function = new { name = "read_file", arguments = """{"path":"x.txt"}""" } }
            }
        });
        var decision = StructuredDecisionAdapter.ParseDecision(Synthesize(Response(message)));
        Assert.True(decision.WantsToolCall);
        Assert.False(decision.WantsDirectAnswer);
        Assert.Single(decision.ToolCalls);
        Assert.Equal("read_file", decision.ToolCalls[0].ToolName);
        Assert.Equal("x.txt", decision.ToolCalls[0].Args["path"]);
    }

    [Fact]
    public void ReasoningContent_UsedAsThinking()
    {
        var message = JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = (string?)null,
            reasoning_content = "need to look",
            tool_calls = new[]
            {
                new { type = "function", function = new { name = "list_dir", arguments = "{}" } }
            }
        });
        var decision = StructuredDecisionAdapter.ParseDecision(Synthesize(Response(message)));
        Assert.True(decision.WantsToolCall);
        Assert.Equal("need to look", decision.Reasoning);
    }

    [Fact]
    public void NoToolCalls_AnswerFromContent()
    {
        var message = JsonSerializer.Serialize(new { role = "assistant", content = "Hello!" });
        var decision = StructuredDecisionAdapter.ParseDecision(Synthesize(Response(message)));
        Assert.True(decision.WantsDirectAnswer);
        Assert.Equal("Hello!", decision.AnswerText);
        Assert.False(decision.WantsToolCall);
    }
}
