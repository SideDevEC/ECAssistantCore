using ECAssistant.Core.Engine;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Tools;
using System.Text.Json;
using Xunit;

namespace ECAssistant.Core.Tests.Integration;

/// <summary>
/// v14.7: Integration tests for the remote provider path (native OpenAI function calling).
/// Verifies that HttpStreamingEngine.GenerateNativeToolsDecisionAsync correctly:
/// - Sends the tools parameter to an OpenAI-compatible API
/// - Parses tool_calls from the response into a DecisionEnvelope JSON
/// - ParseDecision converts it to LLMDecision with WantsToolCall=true
/// - Handles direct answers (no tool_calls) correctly
/// - Handles multi-tool-call responses
/// - Handles reasoning_content from thinking-enabled models (GLM-5.3, DeepSeek, etc.)
/// </summary>
public class RemoteProviderIntegrationTests
{
    // ── Helper: simulate the OpenAI response shape that OpenRouter/DeepSeek/etc return ──

    /// <summary>Build a simulated OpenAI chat completion response with tool_calls.</summary>
    private static string SimulateToolCallResponse(string reasoning, params (string Name, string ArgsJson)[] calls)
    {
        var toolCalls = calls.Select((c, i) => new
        {
            type = "function",
            index = i,
            id = $"chatcmpl-tool-{i}",
            function = new
            {
                name = c.Name,
                arguments = c.ArgsJson
            }
        }).ToArray();

        var response = new
        {
            id = "gen-test",
            choices = new[]
            {
                new
                {
                    index = 0,
                    finish_reason = "tool_calls",
                    message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        reasoning = reasoning,
                        tool_calls = toolCalls
                    }
                }
            }
        };
        return JsonSerializer.Serialize(response);
    }

    /// <summary>Build a simulated OpenAI response with a direct text answer (no tool_calls).</summary>
    private static string SimulateDirectAnswerResponse(string reasoning, string answer)
    {
        var response = new
        {
            id = "gen-test",
            choices = new[]
            {
                new
                {
                    index = 0,
                    finish_reason = "stop",
                    message = new
                    {
                        role = "assistant",
                        content = answer,
                        reasoning = reasoning
                    }
                }
            }
        };
        return JsonSerializer.Serialize(response);
    }

    /// <summary>
    /// Mirrors HttpStreamingEngine.GenerateNativeToolsDecisionAsync: takes the OpenAI response JSON,
    /// extracts reasoning + tool_calls (or content), synthesizes the decision envelope JSON.
    /// </summary>
    private static string SynthesizeEnvelope(string openAiResponseJson)
    {
        using var doc = JsonDocument.Parse(openAiResponseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return JsonSerializer.Serialize(new { thinking = "", answer = "" });

        var message = choices[0].GetProperty("message");

        // Extract reasoning (GLM-5.3 uses "reasoning", some providers use "reasoning_content")
        string thinking = "";
        if (message.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String)
            thinking = r.GetString() ?? "";
        else if (message.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
            thinking = rc.GetString() ?? "";
        else if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            thinking = c.GetString() ?? "";

        // Check for tool_calls
        if (message.TryGetProperty("tool_calls", out var toolCalls) &&
            toolCalls.ValueKind == JsonValueKind.Array &&
            toolCalls.GetArrayLength() > 0)
        {
            var calls = new List<object>();
            foreach (var tc in toolCalls.EnumerateArray())
            {
                var fn = tc.GetProperty("function");
                var name = fn.GetProperty("name").GetString() ?? "";
                var args = new Dictionary<string, string>();

                if (fn.TryGetProperty("arguments", out var raw))
                {
                    if (raw.ValueKind == JsonValueKind.String)
                    {
                        using var argsDoc = JsonDocument.Parse(raw.GetString() ?? "{}");
                        foreach (var p in argsDoc.RootElement.EnumerateObject())
                            args[p.Name] = p.Value.ValueKind == JsonValueKind.String
                                ? p.Value.GetString() ?? ""
                                : p.Value.GetRawText();
                    }
                    else if (raw.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in raw.EnumerateObject())
                            args[p.Name] = p.Value.ValueKind == JsonValueKind.String
                                ? p.Value.GetString() ?? ""
                                : p.Value.GetRawText();
                    }
                }
                calls.Add(new { name, args });
            }
            return JsonSerializer.Serialize(new { thinking, toolcalls = calls });
        }

        // No tool calls — content is the answer, reasoning is the thinking
        var content = message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String
            ? contentEl.GetString() ?? ""
            : thinking;
        return JsonSerializer.Serialize(new { thinking, answer = content });
    }

    // ── Tests ──

    [Fact]
    public void Remote_ToolCall_Response_Parses_To_LLMDecision_With_Tools()
    {
        // Simulate OpenRouter GLM-5.3-flash returning a tool_call for EShellAgent
        var openAiResponse = SimulateToolCallResponse(
            "User wants to list files. I'll use a shell command.",
            ("EShellAgent", "{\"command\": \"ls -la\"}"));

        // Synthesize envelope (this is what GenerateNativeToolsDecisionAsync does)
        var envelopeJson = SynthesizeEnvelope(openAiResponse);

        // Parse into LLMDecision (this is what StructuredDecisionAdapter.ParseDecision does)
        var decision = StructuredDecisionAdapter.ParseDecision(envelopeJson);

        Assert.True(decision.WantsToolCall);
        Assert.False(decision.WantsDirectAnswer);
        Assert.Single(decision.ToolCalls);
        Assert.Equal("EShellAgent", decision.ToolCalls[0].ToolName);
        Assert.Equal("ls -la", decision.ToolCalls[0].Args["command"]);
        Assert.NotNull(decision.Reasoning);
        Assert.Contains("list files", decision.Reasoning!);
    }

    [Fact]
    public void Remote_MultiToolCall_Response_Parses_All_Calls()
    {
        var openAiResponse = SimulateToolCallResponse(
            "Need to read two files in parallel.",
            ("EFileReader", "{\"file\": \"FileA.cs\"}"),
            ("EFileReader", "{\"file\": \"FileB.cs\"}"));

        var envelopeJson = SynthesizeEnvelope(openAiResponse);
        var decision = StructuredDecisionAdapter.ParseDecision(envelopeJson);

        Assert.True(decision.WantsToolCall);
        Assert.Equal(2, decision.ToolCallCount);
        Assert.Equal("EFileReader", decision.ToolCalls[0].ToolName);
        Assert.Equal("FileA.cs", decision.ToolCalls[0].Args["file"]);
        Assert.Equal("EFileReader", decision.ToolCalls[1].ToolName);
        Assert.Equal("FileB.cs", decision.ToolCalls[1].Args["file"]);
    }

    [Fact]
    public void Remote_DirectAnswer_Response_Parses_To_LLMDecision_With_Answer()
    {
        var openAiResponse = SimulateDirectAnswerResponse(
            "Simple greeting, no tools needed.",
            "Hey! What can I help you with?");

        var envelopeJson = SynthesizeEnvelope(openAiResponse);
        var decision = StructuredDecisionAdapter.ParseDecision(envelopeJson);

        Assert.True(decision.WantsDirectAnswer);
        Assert.False(decision.WantsToolCall);
        Assert.Equal("Hey! What can I help you with?", decision.AnswerText);
        Assert.Contains("greeting", decision.Reasoning!);
    }

    [Fact]
    public void Remote_ReasoningContent_Field_Supported()
    {
        // Some providers (DeepSeek) use "reasoning_content" instead of "reasoning"
        var responseJson = """
        {
            "choices": [{
                "index": 0,
                "finish_reason": "tool_calls",
                "message": {
                    "role": "assistant",
                    "content": null,
                    "reasoning_content": "Need to check git status",
                    "tool_calls": [{
                        "type": "function",
                        "index": 0,
                        "id": "call-1",
                        "function": {
                            "name": "EGitTool",
                            "arguments": "{\"action\": \"status\"}"
                        }
                    }]
                }
            }]
        }
        """;

        var envelopeJson = SynthesizeEnvelope(responseJson);
        var decision = StructuredDecisionAdapter.ParseDecision(envelopeJson);

        Assert.True(decision.WantsToolCall);
        Assert.Equal("EGitTool", decision.ToolCalls[0].ToolName);
        Assert.Equal("status", decision.ToolCalls[0].Args["action"]);
        Assert.Contains("git status", decision.Reasoning!);
    }

    [Fact]
    public void Remote_EmptyToolCallArgs_Handled_Gracefully()
    {
        var openAiResponse = SimulateToolCallResponse(
            "Running a command with no args.",
            ("EShellAgent", "{}"));

        var envelopeJson = SynthesizeEnvelope(openAiResponse);
        var decision = StructuredDecisionAdapter.ParseDecision(envelopeJson);

        Assert.True(decision.WantsToolCall);
        Assert.Equal("EShellAgent", decision.ToolCalls[0].ToolName);
        Assert.Empty(decision.ToolCalls[0].Args);
    }

    [Fact]
    public void Remote_NoToolCalls_NoContent_FallsBack_To_Thinking()
    {
        // Edge case: model returns neither tool_calls nor content, only reasoning
        var responseJson = """
        {
            "choices": [{
                "index": 0,
                "finish_reason": "stop",
                "message": {
                    "role": "assistant",
                    "content": null,
                    "reasoning": "I am thinking about the answer."
                }
            }]
        }
        """;

        var envelopeJson = SynthesizeEnvelope(responseJson);
        var decision = StructuredDecisionAdapter.ParseDecision(envelopeJson);

        // Fallback: thinking text becomes the answer
        Assert.True(decision.WantsDirectAnswer);
        Assert.Equal("I am thinking about the answer.", decision.AnswerText);
    }
}