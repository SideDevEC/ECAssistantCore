using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// v14.12.2: mid-run steering seam — single pending slot, newest wins, drained once.
/// </summary>
public sealed class SteeringQueueTests
{
    [Fact]
    public void SteerThenDrain_ReturnsMessageOnce_SecondDrainNull()
    {
        var q = new SteeringQueue();
        q.Steer("focus on tests");
        Assert.True(q.HasPending);
        Assert.Equal("focus on tests", q.Drain());
        Assert.Null(q.Drain());
        Assert.False(q.HasPending);
    }

    [Fact]
    public void SecondSteerBeforeDrain_NewestWins()
    {
        var q = new SteeringQueue();
        q.Steer("first");
        q.Steer("second");
        Assert.Equal("second", q.Drain());
    }

    [Fact]
    public void WhitespaceOnlySteer_Ignored()
    {
        var q = new SteeringQueue();
        q.Steer("   ");
        Assert.False(q.HasPending);
        Assert.Null(q.Drain());
    }

    [Fact]
    public void HasPending_TrueAfterSteer_FalseAfterDrain()
    {
        var q = new SteeringQueue();
        Assert.False(q.HasPending);
        q.Steer("go");
        Assert.True(q.HasPending);
        q.Drain();
        Assert.False(q.HasPending);
    }
}