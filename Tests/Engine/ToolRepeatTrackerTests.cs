using ECAssistant.Core.Engine;
using Xunit;

namespace ECAssistant.Core.Tests.Engine;

/// <summary>
/// Unit tests for the v14.9 orchestrator loop detection (ToolRepeatTracker):
/// identical-repeat levels (nudge at 2, stop at 3+) and A→B→A→B alternation.
/// </summary>
public class ToolRepeatTrackerTests
{
    [Fact]
    public void Record_FirstCall_Count1_NoSignals()
    {
        var t = new ToolRepeatTracker();
        var c = t.Record("sig");
        Assert.Equal(1, c);
        Assert.False(t.IsNudgeLevel(c));
        Assert.False(t.IsStopLevel(c));
    }

    [Fact]
    public void Record_SecondIdentical_NudgeLevel()
    {
        var t = new ToolRepeatTracker();
        t.Record("sig");
        var c = t.Record("sig");
        Assert.Equal(2, c);
        Assert.True(t.IsNudgeLevel(c));
        Assert.False(t.IsStopLevel(c));
    }

    [Fact]
    public void Record_ThirdIdentical_StopLevel()
    {
        var t = new ToolRepeatTracker();
        t.Record("sig"); t.Record("sig");
        var c = t.Record("sig");
        Assert.Equal(3, c);
        Assert.True(t.IsStopLevel(c));
    }

    [Fact]
    public void IsAlternationLoop_RequiresFourSignatures()
    {
        var t = new ToolRepeatTracker();
        Assert.False(t.IsAlternationLoop());
        t.Record("a"); t.Record("b"); t.Record("a");
        Assert.False(t.IsAlternationLoop());
        t.Record("b");
        Assert.True(t.IsAlternationLoop());
    }

    [Fact]
    public void IsAlternationLoop_SameSigRepeated_IsNotAlternation()
    {
        var t = new ToolRepeatTracker();
        t.Record("a"); t.Record("a"); t.Record("a"); t.Record("a");
        Assert.False(t.IsAlternationLoop());
    }

    [Fact]
    public void IsAlternationLoop_WindowSlides()
    {
        var t = new ToolRepeatTracker();
        t.Record("a"); t.Record("b"); t.Record("a"); t.Record("b"); // loop
        Assert.True(t.IsAlternationLoop());
        t.Record("c"); t.Record("c"); // window slides past the loop
        Assert.False(t.IsAlternationLoop());
    }

    [Fact]
    public void CountOf_TracksPerSignature()
    {
        var t = new ToolRepeatTracker();
        t.Record("a"); t.Record("a"); t.Record("b");
        Assert.Equal(2, t.CountOf("a"));
        Assert.Equal(1, t.CountOf("b"));
        Assert.Equal(0, t.CountOf("c"));
    }

    // ── v14.10.2: Reset / Unrecord ──

    [Fact]
    public void Reset_ClearsCounts()
    {
        var tracker = new ToolRepeatTracker();
        tracker.Record("a"); tracker.Record("a");
        tracker.Reset();
        Assert.Equal(1, tracker.Record("a")); // back to first occurrence
    }

    [Fact]
    public void Unrecord_DeniedCall_DoesNotCountTowardStop()
    {
        var tracker = new ToolRepeatTracker();
        tracker.Record("x"); tracker.Record("x"); // recorded but then DENIED
        tracker.Unrecord("x");                     // denial rolled back → 1 executed
        Assert.Equal(2, tracker.Record("x"));      // first retry: nudge, not stop
        Assert.False(tracker.IsStopLevel(2));
        // without Unrecord the same retry would have hit 3 = stop
    }

    [Fact]
    public void Unrecord_UnknownSignature_IsNoop()
    {
        var tracker = new ToolRepeatTracker();
        tracker.Unrecord("never-recorded");
        Assert.Equal(1, tracker.Record("never-recorded"));
    }
}
