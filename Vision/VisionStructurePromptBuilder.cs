using System.Text;

namespace ECAssistant.Core.Vision;

/// <summary>
/// Builds the analysis prompt sent with an image to the vision model.
/// Single source of truth for the EVisionStructure prompt — the parser's
/// schema and this prompt must stay in sync (schemaVersion "1.0").
/// </summary>
// Stateless utility — no mutable state, no external dependencies.
public static class VisionStructurePromptBuilder
{
    private const string SchemaLiteral =
        """
        {
          "schemaVersion": "1.0",
          "source": { "kind": "screenshot|pdf-page", "page": 1, "width": 0, "height": 0 },
          "elements": [
            {
              "id": "e1",
              "type": "header|label|button|input|checkbox|radio|select|table|image|text|other",
              "text": "visible text, else empty string",
              "bbox": { "x": 0, "y": 0, "width": 0, "height": 0 },
              "confidence": 0.0,
              "associatedWith": []
            }
          ],
          "groups": [
            { "id": "g1", "role": "form|section|toolbar|list|table|other", "memberIds": [] }
          ],
          "warnings": []
        }
        """;

    /// <summary>
    /// Build the user prompt for a vision structure analysis.
    /// </summary>
    /// <param name="sourceKind">"screenshot" or "pdf-page" — pins the source.kind value.</param>
    /// <param name="page">Page number to report for pdf-page sources (ignored for screenshots).</param>
    public static string Build(string sourceKind, int page)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyze this image and extract every visible UI element and its layout.");
        sb.AppendLine("Respond ONLY with one JSON object exactly matching this schema:");
        sb.AppendLine(SchemaLiteral);
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Output raw JSON only — no markdown, no commentary.");
        sb.AppendLine($"- Set source.kind to \"{sourceKind}\".");
        sb.AppendLine(sourceKind == "pdf-page"
            ? $"- Set source.page to {page}."
            : "- Set source.page to 1.");
        sb.AppendLine("- Set source.width/height to the image dimensions in pixels (best estimate).");
        sb.AppendLine("- List EVERY visible element with an approximate pixel bbox (integers).");
        sb.AppendLine("- type must be exactly one of the listed values; use \"other\" if nothing fits.");
        sb.AppendLine("- Set text to the element's visible text, or \"\" if it has none.");
        sb.AppendLine("- confidence is a 0.0-1.0 float reflecting how sure you are.");
        sb.AppendLine("- For each label, set associatedWith to the ids of the controls it labels");
        sb.AppendLine("  (inputs, selects, checkboxes). Controls point back at their labels.");
        sb.AppendLine("- Group spatially/semantically related elements into groups (forms, sections,");
        sb.AppendLine("  toolbars, lists, tables). Every id in memberIds must exist in elements.");
        sb.AppendLine("- Add a warning string for anything uncertain (cut-off regions, illegible text,");
        sb.AppendLine("  overlapping elements). If nothing is uncertain, use an empty array.");
        return sb.ToString();
    }
}
