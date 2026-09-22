using System.Text.Json;

namespace ECAssistant.Core.Vision;

/// <summary>
/// Parses and validates a model response into a VisionStructureResult.
/// Deterministic and defensive: unknown enum values map to Other, dangling
/// references are removed, missing fields get defaults — every successful
/// parse that finds a JSON object yields a fully-populated, never-null result,
/// and any corrections are reported as non-fatal issues. Only unparseable
/// output returns false (caller may retry the inference).
/// </summary>
// Stateless utility — no mutable state, no external dependencies.
public static class VisionStructureJsonParser
{
    public static bool TryParse(
        string raw,
        out VisionStructureResult? result,
        out IReadOnlyList<string> issues)
    {
        result = null;
        var errorList = new List<string>();
        issues = errorList;

        if (string.IsNullOrWhiteSpace(raw))
        {
            errorList.Add("Model returned empty output.");
            return false;
        }

        // Strip optional markdown fences / prose around the JSON object.
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            errorList.Add("No JSON object found in model output.");
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw[start..(end + 1)]);
        }
        catch (JsonException ex)
        {
            errorList.Add($"Malformed JSON: {ex.Message}");
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errorList.Add("Root of model output is not a JSON object.");
                return false;
            }

            var schemaVersion = GetString(root, "schemaVersion") ?? VisionStructureResult.CurrentSchemaVersion;
            if (schemaVersion != VisionStructureResult.CurrentSchemaVersion)
                errorList.Add($"Unexpected schemaVersion '{schemaVersion}' (expected '{VisionStructureResult.CurrentSchemaVersion}').");

            var source = ParseSource(root, errorList);

            var elementIds = new HashSet<string>(StringComparer.Ordinal);
            var rawElements = new List<JsonElement>();
            if (root.TryGetProperty("elements", out var elementsProp) && elementsProp.ValueKind == JsonValueKind.Array)
                rawElements.AddRange(elementsProp.EnumerateArray());
            else
                errorList.Add("Missing 'elements' array.");

            var elements = new List<VisionElement>();
            foreach (var item in rawElements)
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var element = ParseElement(item, errorList);
                if (element is null) continue;
                if (!elementIds.Add(element.Id))
                {
                    errorList.Add($"Duplicate element id '{element.Id}' ignored.");
                    continue;
                }
                elements.Add(element);
            }

            // Strip dangling element-level associations now that all ids are known.
            for (var i = 0; i < elements.Count; i++)
            {
                var dangling = elements[i].AssociatedWith.Where(a => !elementIds.Contains(a)).ToList();
                if (dangling.Count == 0) continue;
                elements[i] = elements[i] with { AssociatedWith = elements[i].AssociatedWith.Where(elementIds.Contains).ToList() };
                errorList.Add($"Element '{elements[i].Id}' references unknown ids '{string.Join(", ", dangling)}' — removed.");
            }

            var warnings = new List<string>();
            var groups = new List<VisionElementGroup>();
            if (root.TryGetProperty("groups", out var groupsProp) && groupsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in groupsProp.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var group = ParseGroup(item, elementIds, warnings, errorList);
                    if (group is not null) groups.Add(group);
                }
            }

            if (root.TryGetProperty("warnings", out var warningsProp) && warningsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in warningsProp.EnumerateArray())
                {
                    if (w.ValueKind == JsonValueKind.String && w.GetString() is { Length: > 0 } text)
                        warnings.Add(text);
                }
            }

            result = new VisionStructureResult(
                SchemaVersion: schemaVersion,
                Source: source,
                Elements: elements,
                Groups: groups,
                Warnings: warnings);
            return true;
        }
    }

    private static VisionSourceInfo ParseSource(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("source", out var src) || src.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Missing 'source' object.");
            return new VisionSourceInfo(VisionSourceKind.Unknown, 1, 0, 0);
        }

        var kindText = GetString(src, "kind");
        var kind = kindText switch
        {
            "screenshot" => VisionSourceKind.Screenshot,
            "pdf-page" => VisionSourceKind.PdfPage,
            null => VisionSourceKind.Unknown,
            _ => LogUnknown(errors, $"Unknown source kind '{kindText}'.")
        };

        return new VisionSourceInfo(
            Kind: kind,
            Page: GetNonNegativeInt(src, "page", 1, errors, "source.page"),
            Width: GetNonNegativeInt(src, "width", 0, errors, "source.width"),
            Height: GetNonNegativeInt(src, "height", 0, errors, "source.height"));
    }

    private static VisionSourceKind LogUnknown(List<string> errors, string message)
    {
        errors.Add(message);
        return VisionSourceKind.Unknown;
    }

    private static VisionElement? ParseElement(JsonElement item, List<string> errors)
    {
        var id = GetString(item, "id");
        if (string.IsNullOrEmpty(id))
        {
            errors.Add("Element without id skipped.");
            return null;
        }

        var typeText = GetString(item, "type");
        var type = ParseEnum<VisionElementType>(typeText, VisionElementType.Other,
            typeText is null ? null : $"Unknown element type '{typeText}' on '{id}' → Other.", errors);

        var bbox = VisionBoundingBox.Empty;
        if (item.TryGetProperty("bbox", out var bboxProp) && bboxProp.ValueKind == JsonValueKind.Object)
        {
            bbox = new VisionBoundingBox(
                X: GetNonNegativeInt(bboxProp, "x", 0, errors, $"bbox.x on '{id}'"),
                Y: GetNonNegativeInt(bboxProp, "y", 0, errors, $"bbox.y on '{id}'"),
                Width: GetNonNegativeInt(bboxProp, "width", 0, errors, $"bbox.width on '{id}'"),
                Height: GetNonNegativeInt(bboxProp, "height", 0, errors, $"bbox.height on '{id}'"));
        }
        else
        {
            errors.Add($"Missing bbox on element '{id}'.");
        }

        var confidence = 0.0;
        if (item.TryGetProperty("confidence", out var confProp))
        {
            if (confProp.ValueKind == JsonValueKind.Number && confProp.TryGetDouble(out var raw))
                confidence = Math.Clamp(raw, 0.0, 1.0);
            else
                errors.Add($"Non-numeric confidence on element '{id}' → 0.");
        }
        else
        {
            errors.Add($"Missing confidence on element '{id}' → 0.");
        }

        var associated = new List<string>();
        if (item.TryGetProperty("associatedWith", out var assocProp) && assocProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assocProp.EnumerateArray())
            {
                if (a.ValueKind == JsonValueKind.String && a.GetString() is { Length: > 0 } refId)
                    associated.Add(refId);
            }
        }

        return new VisionElement(
            Id: id,
            Type: type,
            Text: GetString(item, "text") ?? "",
            BoundingBox: bbox,
            Confidence: confidence,
            AssociatedWith: associated);
    }

    private static VisionElementGroup? ParseGroup(
        JsonElement item,
        HashSet<string> elementIds,
        List<string> warnings,
        List<string> errors)
    {
        var id = GetString(item, "id");
        if (string.IsNullOrEmpty(id))
        {
            errors.Add("Group without id skipped.");
            return null;
        }

        var roleText = GetString(item, "role");
        var role = ParseEnum<VisionGroupRole>(roleText, VisionGroupRole.Other,
            roleText is null ? null : $"Unknown group role '{roleText}' on '{id}' → Other.", errors);

        var memberIds = new List<string>();
        if (item.TryGetProperty("memberIds", out var membersProp) && membersProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in membersProp.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.String || m.GetString() is not { Length: > 0 } memberId)
                    continue;
                if (elementIds.Contains(memberId))
                    memberIds.Add(memberId);
                else
                    warnings.Add($"Group '{id}' references unknown element '{memberId}' — removed.");
            }
        }

        return new VisionElementGroup(Id: id, Role: role, MemberIds: memberIds);
    }

    private static TEnum ParseEnum<TEnum>(
        string? text,
        TEnum fallback,
        string? warning,
        List<string> errors) where TEnum : struct, Enum
    {
        if (text is null)
        {
            errors.Add($"Missing {typeof(TEnum).Name} value → {fallback}.");
            return fallback;
        }
        if (Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(typeof(TEnum), parsed))
            return parsed;
        if (warning is not null) errors.Add(warning);
        return fallback;
    }

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int GetNonNegativeInt(JsonElement obj, string name, int fallback, List<string> errors, string context)
    {
        if (!obj.TryGetProperty(name, out var prop))
        {
            errors.Add($"Missing {context} → {fallback}.");
            return fallback;
        }
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value) && value >= 0)
            return value;
        errors.Add($"Invalid {context} → {fallback}.");
        return fallback;
    }
}
