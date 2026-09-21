using System.Text.Json;
using ECAssistant.Core.Session;

namespace ECAssistant.Core.Tools.User;

/// <summary>
/// v14.9 ambiguity-triggered checkpoint: lets the MODEL declare uncertainty and ask
/// the user to pick from options mid-task. Surfaces a real interactive checkpoint via
/// ISessionOutput.RequestChoice; the chosen option flows back as the tool result so
/// the conversation continues with the user's answer in context.
///
/// null from RequestChoice (no listener / timeout / cancel) maps to the declared
/// default option when present, else "no answer — proceed with your best judgment",
/// keeping the agent autonomous when nobody is watching.
/// </summary>
public sealed class EUserAskTool : EToolBase
{
    private readonly ISessionOutput? _out;

    public EUserAskTool(ISessionOutput? sessionOutput) => _out = sessionOutput;

    public override string Name => "AskUser";
    public override string Description =>
        "Ask the user a clarifying question with 2-4 options. Use ONLY when you are genuinely " +
        "uncertain how to proceed (multiple valid approaches, ambiguous request, irreversible " +
        "action). Not for trivial choices. The user's selection is returned as the result.";
    public override string UsageExample => "AskUser(question: \"Refactor in place or new module?\", options: \"[\\\"In place\\\", \\\"New module\\\"]\")";

    public override string GetParameterSchema() =>
        """
        {
          "type": "object",
          "required": ["question", "options"],
          "properties": {
            "question": { "type": "string", "description": "The question to ask the user" },
            "options": { "type": "string", "description": "JSON array of 2-4 option strings, e.g. [\"In place\", \"New module\"]" },
            "default": { "type": "string", "description": "Option text to assume if the user is unavailable" }
          }
        }
        """;

    public override async Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        arguments ??= new Dictionary<string, string?>();
        var question = arguments.GetValueOrDefault("question")?.Trim();
        var optionsRaw = arguments.GetValueOrDefault("options")?.Trim();
        var fallback = arguments.GetValueOrDefault("default")?.Trim();

        if (string.IsNullOrEmpty(question))
            return EToolResult.Failure(Name, "Missing 'question' argument.");

        var options = new List<string>();
        if (!string.IsNullOrEmpty(optionsRaw))
        {
            try
            {
                using var doc = JsonDocument.Parse(optionsRaw);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    options.AddRange(doc.RootElement.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString() ?? "")
                        .Where(s => s.Length > 0));
            }
            catch (JsonException) { /* fall through to error below */ }
        }
        if (options.Count < 2 || options.Count > 4)
            return EToolResult.Failure(Name, "Invalid 'options' — provide a JSON array of 2-4 option strings.");

        if (_out == null)
        {
            // No output channel — fall back to declared default or autonomous continuation.
            return EToolResult.Success(Name,
                string.IsNullOrEmpty(fallback)
                    ? "No user available — proceed with your best judgment."
                    : $"No user available — proceeding with default: {fallback}");
        }

        var picked = await Task.Run(() => _out.RequestChoice(question, options), cancellationToken);
        if (picked.HasValue && picked.Value >= 1 && picked.Value <= options.Count)
            return EToolResult.Success(Name, $"User chose option {picked.Value}: {options[picked.Value - 1]}");

        // null = nobody answered (no listener / timeout / cancel)
        return EToolResult.Success(Name,
            string.IsNullOrEmpty(fallback)
                ? "No answer from the user — proceed with your best judgment."
                : $"No answer from the user — proceeding with default: {fallback}");
    }
}
