using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.12.2: curated tool-output projection — key lines + head/tail instead of a
/// blind head-truncate. Pure function tests.
/// </summary>
public sealed class ToolOutputProjectorTests
{
    private const string StoreNote = "[OUTPUT STORED: full output saved as output_1.]";

    [Fact]
    public void OversizedText_KeepsKeyLinesHeadTailAndNote()
    {
        var filler = new string('x', 100);
        var sb = new System.Text.StringBuilder();
        sb.Append(filler); // head region
        var middle = new System.Text.StringBuilder();
        for (var i = 0; i < 60; i++) middle.Append(filler).Append('\n');
        middle.AppendLine("ERROR: build failed at line 42");
        middle.AppendLine("warning: deprecated API");
        for (var i = 0; i < 60; i++) middle.Append(filler).Append('\n');
        sb.Append(middle);
        var text = sb.ToString();
        System.Diagnostics.Debug.Assert(text.Length > 4000);

        var projected = ToolOutputProjector.Project(text, StoreNote);

        Assert.Contains("ERROR: build failed at line 42", projected);
        Assert.Contains("warning: deprecated API", projected);
        Assert.Contains(text[..100], projected);
        Assert.Contains(text[^100..], projected);
        Assert.Contains(StoreNote, projected);
    }

    [Fact]
    public void KeyLineInHead_NotDuplicated()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("error: early failure line");
        while (sb.Length < 6000) sb.AppendLine(new string('x', 100));
        var text = sb.ToString();

        var projected = ToolOutputProjector.Project(text, StoreNote);

        Assert.Equal(1, projected.Split("error: early failure line").Length - 1);
    }

    [Fact]
    public void MaxKeyLines_CapsAt30()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(new string('x', 1300)).Append('\n'); // push past head
        for (var i = 0; i < 50; i++) sb.AppendLine($"ERRORLINE {i} filler filler filler filler filler filler filler filler");
        while (sb.Length < 6000) sb.AppendLine(new string('y', 100));
        var text = sb.ToString();

        var projected = ToolOutputProjector.Project(text, StoreNote);

        Assert.Equal(30, projected.Split("ERRORLINE").Length - 1);
    }

    [Fact]
    public void NoMarkerLines_StillHeadTailAndNote()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(new string('a', 1200));
        for (var i = 0; i < 40; i++) sb.AppendLine(new string('z', 100));
        sb.Append(new string('b', 1200));
        var text = sb.ToString();

        var projected = ToolOutputProjector.Project(text, StoreNote);

        Assert.Contains("[... middle elided — key lines below ...]", projected);
        Assert.Contains("[... tail ...]", projected);
        Assert.Contains(text[..100], projected);
        Assert.Contains(text[^100..], projected);
        Assert.Contains(StoreNote, projected);
    }
}