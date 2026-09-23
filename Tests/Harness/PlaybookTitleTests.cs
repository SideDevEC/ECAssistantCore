using ECAssistant.Core;
using ECAssistant.Core.Playbooks;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.19.1: playbook titles use the goal's FIRST SENTENCE only — prompts that
/// append tool-call instructions ("Make EXACTLY this tool call: ...") must not
/// become the title. Covered through the public Extract API (BuildTitle is private).
/// </summary>
public sealed class PlaybookTitleTests
{
    private static readonly IReadOnlyList<CapturedToolCall> Calls =
        new[] { new CapturedToolCall("ProbeTool", "action=probe") };

    private static string? Title(string goal) =>
        new PlaybookExtractor().Extract(goal, Calls)?.Title;

    [Fact]
    public void Extract_MultiSentenceGoal_TitleIsFirstSentence()
    {
        var title = Title(
            "Create a file named note.txt containing hello. Make EXACTLY this tool call: ECodeEditor(action=create).");
        Assert.Equal("Create a file named note.txt containing hello.", title);
    }

    [Fact]
    public void Extract_NoSentenceEnd_TitleIsWholeGoal()
    {
        var title = Title("create file hello txt");
        Assert.Equal("create file hello txt", title);
    }

    [Fact]
    public void Extract_LongSingleSentence_TitleTruncatedAt80()
    {
        var goal = new string('x', 120);
        var title = Title(goal);
        Assert.Equal(81, title!.Length); // 80 + ellipsis
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Extract_QuestionGoal_TitleIsFullQuestion()
    {
        var title = Title("Can you analyze the config file? Then make changes.");
        Assert.Equal("Can you analyze the config file?", title);
    }

    [Fact]
    public void Extract_ShortSentence_RespectsMinLength()
    {
        // Sentence end at position <= 10 must NOT truncate ("Fix it. Then do lots of other stuff.").
        var title = Title("Fix it. Then do lots of other stuff and more and more text here.");
        Assert.Contains("Then do lots", title);
    }
}
