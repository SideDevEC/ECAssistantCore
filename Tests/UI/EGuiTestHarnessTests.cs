using ECAssistant.TestSupport;
using ECAssistant.Core.UI;

namespace ECAssistant.Core.Tests.UI;

/// <summary>
/// Tests for GuiTestHarness — verifies it captures output correctly.
/// </summary>
public class GuiTestHarnessTests
{
    [Fact]
    public void WriteLine_CapturesText()
    {
        var harness = new GuiTestHarness();
        harness.WriteLine("hello");
        Assert.Contains("hello", harness.CapturedOutput);
    }

    [Fact]
    public void WriteLineColored_CapturesText()
    {
        var harness = new GuiTestHarness();
        harness.WriteLineColored("\x1b[31mRed\x1b[0m");
        Assert.Contains("Red", harness.CapturedOutput);
    }

    [Fact]
    public void BlankLine_CapturesNewline()
    {
        var harness = new GuiTestHarness();
        harness.BlankLine();
        Assert.Contains("\n", harness.CapturedOutput);
    }

    [Fact]
    public void ClearCanvas_LogsClear()
    {
        var harness = new GuiTestHarness();
        harness.ClearCanvas();
        Assert.Contains("[CLEAR]", harness.CapturedOutput);
    }

    [Fact]
    public void QueueInput_ReturnsQueuedValue()
    {
        var harness = new GuiTestHarness();
        harness.QueueInput("yes");
        var result = harness.PromptRaw("> ");
        Assert.Equal("yes", result);
    }

    [Fact]
    public void QueueInputs_ReturnsInOrder()
    {
        var harness = new GuiTestHarness();
        harness.QueueInputs("first", "second", "third");
        Assert.Equal("first", harness.PromptRaw(""));
        Assert.Equal("second", harness.PromptRaw(""));
        Assert.Equal("third", harness.PromptRaw(""));
    }

    [Fact]
    public void ClearOutput_ResetsLog()
    {
        var harness = new GuiTestHarness();
        harness.WriteLine("data");
        harness.ClearOutput();
        Assert.Equal("", harness.CapturedOutput);
    }

    [Fact]
    public void OutputContains_CaseInsensitive()
    {
        var harness = new GuiTestHarness();
        harness.WriteLine("Hello World");
        Assert.True(harness.OutputContains("hello world"));
    }

    [Fact]
    public void GetLastLines_ReturnsLastN()
    {
        var harness = new GuiTestHarness();
        harness.WriteLine("line1");
        harness.WriteLine("line2");
        harness.WriteLine("line3");
        var last = harness.GetLastLines(2);
        Assert.Contains("line2", last);
        Assert.Contains("line3", last);
        Assert.DoesNotContain("line1", last);
    }
}