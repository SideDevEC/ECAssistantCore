using ECAssistant.Core.Vision;

namespace ECAssistant.Core.Tests.Vision;

public class VisionStructureJsonParserTests
{
    private const string ValidJson = """
    {
      "schemaVersion": "1.0",
      "source": { "kind": "screenshot", "page": 1, "width": 800, "height": 600 },
      "elements": [
        { "id": "e1", "type": "header", "text": "Title", "bbox": { "x": 10, "y": 20, "width": 300, "height": 40 }, "confidence": 0.9, "associatedWith": [] },
        { "id": "e2", "type": "input", "text": "", "bbox": { "x": 10, "y": 80, "width": 200, "height": 24 }, "confidence": 0.8, "associatedWith": ["e1"] }
      ],
      "groups": [
        { "id": "g1", "role": "form", "memberIds": ["e1", "e2"] }
      ],
      "warnings": ["right edge cut off"]
    }
    """;

    [Fact]
    public void TryParse_ValidJson_ReturnsFullyPopulatedResult()
    {
        var ok = VisionStructureJsonParser.TryParse(ValidJson, out var result, out var errors);

        Assert.True(ok);
        Assert.Empty(errors);
        Assert.NotNull(result);
        Assert.Equal("1.0", result!.SchemaVersion);
        Assert.Equal(VisionSourceKind.Screenshot, result.Source.Kind);
        Assert.Equal(800, result.Source.Width);
        Assert.Equal(2, result.Elements.Count);
        Assert.Equal(VisionElementType.Header, result.Elements[0].Type);
        Assert.Equal(0.9, result.Elements[0].Confidence);
        Assert.Equal(new VisionBoundingBox(10, 20, 300, 40), result.Elements[0].BoundingBox);
        Assert.Single(result.Groups);
        Assert.Equal(VisionGroupRole.Form, result.Groups[0].Role);
        Assert.Contains("right edge cut off", result.Warnings);
    }

    [Fact]
    public void TryParse_MarkdownFencedJson_Parses()
    {
        var raw = "```json\n" + ValidJson + "\n```";
        var ok = VisionStructureJsonParser.TryParse(raw, out var result, out _);

        Assert.True(ok);
        Assert.Equal(2, result!.Elements.Count);
    }

    [Fact]
    public void TryParse_EmptyInput_Fails()
    {
        var ok = VisionStructureJsonParser.TryParse("", out var result, out var errors);

        Assert.False(ok);
        Assert.Null(result);
        Assert.Contains(errors, e => e.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryParse_MalformedJson_Fails()
    {
        var ok = VisionStructureJsonParser.TryParse("{ not json }", out var result, out var errors);

        Assert.False(ok);
        Assert.Null(result);
        Assert.Contains(errors, e => e.Contains("Malformed JSON"));
    }

    [Fact]
    public void TryParse_UnknownEnumValues_MapToOther_WithErrors()
    {
        var raw = """
        {
          "source": { "kind": "weird-kind", "page": 1, "width": 0, "height": 0 },
          "elements": [
            { "id": "e1", "type": "spaceship", "text": "x", "bbox": { "x": 0, "y": 0, "width": 1, "height": 1 }, "confidence": 1, "associatedWith": [] }
          ],
          "groups": []
        }
        """;

        var ok = VisionStructureJsonParser.TryParse(raw, out var result, out var errors);

        Assert.True(ok);
        Assert.Equal(VisionElementType.Other, result!.Elements[0].Type);
        Assert.Equal(VisionSourceKind.Unknown, result.Source.Kind);
        Assert.Contains(errors, e => e.Contains("spaceship"));
        Assert.Contains(errors, e => e.Contains("weird-kind"));
    }

    [Fact]
    public void TryParse_DanglingReferences_RemovedWithWarnings()
    {
        var raw = """
        {
          "source": { "kind": "screenshot", "page": 1, "width": 0, "height": 0 },
          "elements": [
            { "id": "e1", "type": "label", "text": "Name", "bbox": { "x": 0, "y": 0, "width": 10, "height": 10 }, "confidence": 1, "associatedWith": ["ghost"] }
          ],
          "groups": [
            { "id": "g1", "role": "form", "memberIds": ["e1", "ghost2"] }
          ]
        }
        """;

        var ok = VisionStructureJsonParser.TryParse(raw, out var result, out var errors);

        Assert.True(ok);
        Assert.Empty(result!.Elements[0].AssociatedWith); // dangling refs stripped
        Assert.Single(result.Groups[0].MemberIds);
        Assert.Equal("e1", result.Groups[0].MemberIds[0]);
        Assert.Contains(result.Warnings, w => w.Contains("ghost"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("ghost2") && !w.Contains("g1"));
    }

    [Fact]
    public void TryParse_MissingFields_GetNeverNullDefaults()
    {
        var raw = """
        {
          "elements": [
            { "id": "e1" }
          ]
        }
        """;

        var ok = VisionStructureJsonParser.TryParse(raw, out var result, out var errors);

        Assert.True(ok);
        Assert.NotNull(result!.Source);
        Assert.Equal(VisionSourceKind.Unknown, result.Source.Kind);
        var e = result.Elements[0];
        Assert.Equal(VisionElementType.Other, e.Type);
        Assert.Equal("", e.Text);
        Assert.Equal(VisionBoundingBox.Empty, e.BoundingBox);
        Assert.Equal(0.0, e.Confidence);
        Assert.Empty(e.AssociatedWith);
        Assert.Empty(result.Groups);
        Assert.Empty(result.Warnings);
        Assert.NotEmpty(errors); // problems reported, structure still complete
    }

    [Fact]
    public void TryParse_ConfidenceOutOfRange_Clamps()
    {
        var raw = """
        {
          "source": { "kind": "screenshot", "page": 1, "width": 0, "height": 0 },
          "elements": [
            { "id": "e1", "type": "text", "text": "a", "bbox": { "x": 0, "y": 0, "width": 1, "height": 1 }, "confidence": 7, "associatedWith": [] }
          ]
        }
        """;

        var ok = VisionStructureJsonParser.TryParse(raw, out var result, out _);

        Assert.True(ok);
        Assert.Equal(1.0, result!.Elements[0].Confidence);
    }

    [Fact]
    public void TryParse_DuplicateElementIds_SecondIgnored()
    {
        var raw = """
        {
          "source": { "kind": "screenshot", "page": 1, "width": 0, "height": 0 },
          "elements": [
            { "id": "e1", "type": "text", "text": "a", "bbox": { "x": 0, "y": 0, "width": 1, "height": 1 }, "confidence": 1, "associatedWith": [] },
            { "id": "e1", "type": "text", "text": "b", "bbox": { "x": 0, "y": 0, "width": 1, "height": 1 }, "confidence": 1, "associatedWith": [] }
          ]
        }
        """;

        var ok = VisionStructureJsonParser.TryParse(raw, out var result, out var errors);

        Assert.True(ok);
        Assert.Single(result!.Elements);
        Assert.Equal("a", result.Elements[0].Text);
        Assert.Contains(errors, e => e.Contains("Duplicate element id"));
    }
}
