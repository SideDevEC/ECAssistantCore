using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Tools.Code;

namespace ECAssistant.Core.Tests.Tools;

/// <summary>
/// v14.15 fuzzy diff-based edits: layered match strategies, pipeline ordering,
/// ambiguity errors, indentation restoration, tier-flavored tool description.
/// Pure logic + Mock IFileSystem — no real builds/DB/LLM.
/// </summary>
public class TextMatchStrategyTests
{
    // ── ExactMatchStrategy ──

    [Fact]
    public void Exact_UniqueMatch_Found()
    {
        var strategy = new ExactMatchStrategy();

        var result = strategy.Find("alpha beta gamma", "beta");

        Assert.Equal(TextMatchStatus.Found, result.Status);
        Assert.Equal("exact", result.StrategyName);
        Assert.Equal("beta", result.MatchedText);
        Assert.Equal(6, result.StartIndex);
    }

    [Fact]
    public void Exact_NoMatch_NotFound()
    {
        var result = new ExactMatchStrategy().Find("alpha", "omega");

        Assert.Equal(TextMatchStatus.NotFound, result.Status);
    }

    [Fact]
    public void Exact_MultipleMatches_AmbiguousWithLineNumbers()
    {
        var result = new ExactMatchStrategy().Find("foo\nbar\nfoo", "foo");

        Assert.Equal(TextMatchStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.CandidateLines.Count);
        Assert.Equal(new[] { 1, 3 }, result.CandidateLines);
    }

    // ── WhitespaceTolerantMatchStrategy ──

    [Fact]
    public void WhitespaceTolerant_IndentationMismatch_FindsOriginalBlock()
    {
        // Model sends old_text without indentation; file has it indented.
        var content = "def run():\n    if ok:\n        go()\n    return 1\n";
        var oldText = "if ok:\n    go()";

        var result = new WhitespaceTolerantMatchStrategy().Find(content, oldText);

        Assert.Equal(TextMatchStatus.Found, result.Status);
        Assert.Equal("whitespace-tolerant", result.StrategyName);
        Assert.Equal("    if ok:\n        go()", result.MatchedText);
        Assert.Contains("if ok:", result.MatchedText);
    }

    [Fact]
    public void WhitespaceTolerant_InternalSpaceRuns_Found()
    {
        var content = "int  x   = 1;";
        var result = new WhitespaceTolerantMatchStrategy().Find(content, "int x = 1;");

        Assert.Equal(TextMatchStatus.Found, result.Status);
        Assert.Equal("int  x   = 1;", result.MatchedText);
    }

    [Fact]
    public void WhitespaceTolerant_MultipleCandidates_AmbiguousWithLines()
    {
        var content = "  a  b\nx\n  a  b";
        var result = new WhitespaceTolerantMatchStrategy().Find(content, "a b");

        Assert.Equal(TextMatchStatus.Ambiguous, result.Status);
        Assert.Equal(new[] { 1, 3 }, result.CandidateLines);
    }

    [Fact]
    public void WhitespaceTolerant_DifferentContent_NotFound()
    {
        var result = new WhitespaceTolerantMatchStrategy().Find("alpha beta", "delta");

        Assert.Equal(TextMatchStatus.NotFound, result.Status);
    }

    // ── LineAnchoredMatchStrategy ──

    [Fact]
    public void LineAnchored_IndentationAndBlankDrift_FindsOriginalBlock()
    {
        var content = "class A {\n    void M() {\n        Work();\n    }\n}";
        var oldText = "void M() {\n\n    Work();\n}"; // extra blank line + wrong indentation

        var result = new LineAnchoredMatchStrategy().Find(content, oldText);

        Assert.Equal(TextMatchStatus.Found, result.Status);
        Assert.Equal("line-anchored", result.StrategyName);
        // Original block with ORIGINAL indentation is restored
        Assert.Equal("    void M() {\n        Work();\n    }", result.MatchedText);
    }

    [Fact]
    public void LineAnchored_MultipleCandidates_Ambiguous()
    {
        var content = "  return 1\nx\nreturn 1";
        var result = new LineAnchoredMatchStrategy().Find(content, "return 1");

        Assert.Equal(TextMatchStatus.Ambiguous, result.Status);
        Assert.Equal(new[] { 1, 3 }, result.CandidateLines);
    }

    [Fact]
    public void LineAnchored_NoSimilarLines_NotFound()
    {
        var result = new LineAnchoredMatchStrategy().Find("alpha\nbeta", "gamma");

        Assert.Equal(TextMatchStatus.NotFound, result.Status);
    }

    // ── TextMatchPipeline ordering ──

    [Fact]
    public void Pipeline_ExactWinsFirst()
    {
        var exact = new StubStrategy("exact", TextMatchStatus.Found);
        var ws = new StubStrategy("ws", TextMatchStatus.Found);
        var pipeline = new TextMatchPipeline(new ITextMatchStrategy[] { exact, ws });

        var result = pipeline.Find("c", "s");

        Assert.Equal("exact", result.StrategyName);
        Assert.Equal(1, exact.Calls);
        Assert.Equal(0, ws.Calls);
    }

    [Fact]
    public void Pipeline_FallsThroughToLaterStrategy()
    {
        var pipeline = new TextMatchPipeline(new ITextMatchStrategy[]
        {
            new StubStrategy("exact", TextMatchStatus.NotFound),
            new StubStrategy("ws", TextMatchStatus.Found)
        });

        var result = pipeline.Find("c", "s");

        Assert.Equal(TextMatchStatus.Found, result.Status);
        Assert.Equal("ws", result.StrategyName);
    }

    [Fact]
    public void Pipeline_AmbiguityStopsImmediately_NeverGuessed()
    {
        var exact = new StubStrategy("exact", TextMatchStatus.Ambiguous, new[] { 2, 5 });
        var ws = new StubStrategy("ws", TextMatchStatus.Found);
        var pipeline = new TextMatchPipeline(new ITextMatchStrategy[] { exact, ws });

        var result = pipeline.Find("c", "s");

        Assert.Equal(TextMatchStatus.Ambiguous, result.Status);
        Assert.Equal(new[] { 2, 5 }, result.CandidateLines);
        Assert.Equal(0, ws.Calls); // never guessed through to a later strategy
    }

    [Fact]
    public void Pipeline_AllFail_NotFound()
    {
        var pipeline = new TextMatchPipeline(new ITextMatchStrategy[]
        {
            new StubStrategy("a", TextMatchStatus.NotFound),
            new StubStrategy("b", TextMatchStatus.NotFound)
        });

        Assert.Equal(TextMatchStatus.NotFound, pipeline.Find("c", "s").Status);
    }

    [Fact]
    public void Pipeline_RealStrategies_WhitespaceFallbackUsedAndReported()
    {
        var content = "def run():\n    if ok:\n        go()\n";
        var pipeline = new TextMatchPipeline();

        var result = pipeline.Find(content, "if ok:\n    go()");

        Assert.Equal(TextMatchStatus.Found, result.Status);
        Assert.Equal("whitespace-tolerant", result.StrategyName);
    }

    // ── Tool integration (Mock IFileSystem) ──

    private readonly Mock<IFileSystem> _fileSystem = new();
    private readonly EAgentConfig _config = new();

    [Fact]
    public async Task Patch_ExactFailsWhitespaceRescues_ReportsStrategy()
    {
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.ReadFile(It.IsAny<string>()))
            .Returns("def run():\n    if ok:\n        go()\n    return 1\n");
        var tool = new ECodeEditorTool(_fileSystem.Object, _config);

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["action"] = "patch", ["file"] = "a.py",
            ["old_text"] = "if ok:\n    go()", ["new_text"] = "if ok:\n    stop()"
        });

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("whitespace-tolerant", result.Output + result.Error);
        _fileSystem.Verify(f => f.WriteFile(It.IsAny<string>(),
            It.Is<string>(s => s.Contains("        stop()"))), Times.Once);
    }

    [Fact]
    public async Task Patch_IndentationRestoredToNewText()
    {
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.ReadFile(It.IsAny<string>()))
            .Returns("class A:\n    def M():\n        Work()\n");
        var tool = new ECodeEditorTool(_fileSystem.Object, _config);

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["action"] = "patch", ["file"] = "a.cs",
            ["old_text"] = "def M():\n    Work()", ["new_text"] = "def M():\n    Other()"
        });

        Assert.True(result.Succeeded, result.Error);
        // new_text re-indented by +4 (file block is indented deeper than model's old_text)
        _fileSystem.Verify(f => f.WriteFile(It.IsAny<string>(),
            It.Is<string>(s => s.Contains("        Other()"))), Times.Once);
    }

    [Fact]
    public async Task Patch_AmbiguousFuzzyMatch_ReturnsStructuredError()
    {
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.ReadFile(It.IsAny<string>())).Returns("dup\nmid\ndup");
        var tool = new ECodeEditorTool(_fileSystem.Object, _config);

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["action"] = "patch", ["file"] = "a.txt",
            ["old_text"] = "dup", ["new_text"] = "x"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("ambiguous match: 2 candidates at lines 1,3", result.Error);
        _fileSystem.Verify(f => f.WriteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Patch_FuzzyDisabled_ExactBehaviorOnly()
    {
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.ReadFile(It.IsAny<string>()))
            .Returns("def run():\n    if ok:\n        go()\n");
        var tool = new ECodeEditorTool(_fileSystem.Object, _config);

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["action"] = "patch", ["file"] = "a.py", ["fuzzy"] = "false",
            ["old_text"] = "if ok:\n    go()", ["new_text"] = "if ok:\n    stop()"
        });

        // fuzzy=false → no whitespace rescue: plain not-found error
        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error);
        _fileSystem.Verify(f => f.WriteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Patch_ExactMatch_ReportsExactStrategy()
    {
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.ReadFile(It.IsAny<string>())).Returns("Hello World");
        var tool = new ECodeEditorTool(_fileSystem.Object, _config);

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["action"] = "patch", ["file"] = "a.txt",
            ["old_text"] = "Hello", ["new_text"] = "Hi"
        });

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("match strategy: exact", result.Output);
    }

    [Fact]
    public async Task Patch_ModelSentOwnIndentation_ReplacementVerbatim()
    {
        // Model copies the block WITH its real indentation — delta 0, replacement as-is.
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.ReadFile(It.IsAny<string>())).Returns("class A\n{\n    Run(1);\n}\n");
        var tool = new ECodeEditorTool(_fileSystem.Object, _config);

        await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["action"] = "patch", ["file"] = "a.cs",
            ["old_text"] = "    Run(1);", ["new_text"] = "    Run(2);"
        });

        _fileSystem.Verify(f => f.WriteFile(It.IsAny<string>(),
            It.Is<string>(s => s.Contains("    Run(2);"))), Times.Once);
    }

    [Fact]
    public void Description_RemoteAutoTier_ResolvesLarge()
    {
        // auto (null mode) + remote provider → large tier
        var config = new EAgentConfig();
        config.LlmProvider.Mode = "remote";
        var tool = new ECodeEditorTool(new Mock<IFileSystem>().Object, config);

        Assert.Contains("Approximate matches tolerated", tool.Description);
    }

    // ── Tier-flavored description ──

    [Fact]
    public void Description_SmallTier_ExplicitFuzzyGuidance()
    {
        // default config: mode null + local provider → small tier
        var tool = new ECodeEditorTool(new Mock<IFileSystem>().Object, new EAgentConfig());

        Assert.Contains("Copy text exactly first; fuzzy fallback will rescue small mismatches", tool.Description);
    }

    [Fact]
    public void Description_LargeTier_TerseHint()
    {
        var config = new EAgentConfig { ModelTier = new ModelTierConfig { Mode = "large" } };
        var tool = new ECodeEditorTool(new Mock<IFileSystem>().Object, config);

        Assert.Contains("Approximate matches tolerated", tool.Description);
        Assert.DoesNotContain("rescue", tool.Description);
    }

    [Fact]
    public void Description_ContainsOptionalFuzzyBehaviorForSchema()
    {
        var tool = new ECodeEditorTool(new Mock<IFileSystem>().Object, new EAgentConfig());
        Assert.Contains("\"fuzzy\"", tool.GetParameterSchema());
    }

    // ── Stub strategy (test-only) ──

    private sealed class StubStrategy : ITextMatchStrategy
    {
        private readonly TextMatchStatus _status;
        private readonly int[]? _candidates;
        public int Calls { get; private set; }

        public StubStrategy(string name, TextMatchStatus status, int[]? candidates = null)
        {
            Name = name;
            _status = status;
            _candidates = candidates;
        }

        public string Name { get; }

        public TextMatchResult Find(string content, string searchText)
        {
            Calls++;
            return _status switch
            {
                TextMatchStatus.Found => TextMatchResult.Matched(Name, 0, searchText),
                TextMatchStatus.Ambiguous => TextMatchResult.Ambiguous(Name, _candidates ?? Array.Empty<int>()),
                _ => TextMatchResult.NoMatch(Name)
            };
        }
    }
}
