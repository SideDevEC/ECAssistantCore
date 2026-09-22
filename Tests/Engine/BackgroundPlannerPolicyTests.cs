using Xunit;
using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Tests.Engine;

// v14.10.3: merge policy for the non-blocking background planner.
public class BackgroundPlannerPolicyTests
{
    private readonly BackgroundPlannerPolicy _policy = new();

    [Theory]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(2, false)]
    public void Decide_MultiStepPlan_ActiveLoop_FoldsIn(int planSize, bool loopFinished)
    {
        Assert.Equal(BackgroundPlanAction.FoldIn, _policy.Decide(planSize, loopFinished));
    }

    [Fact]
    public void Decide_LoopFinished_Discards()
    {
        Assert.Equal(BackgroundPlanAction.Discard, _policy.Decide(5, loopFinished: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Decide_TrivialOrFailedPlan_Discards(int planSize)
    {
        Assert.Equal(BackgroundPlanAction.Discard, _policy.Decide(planSize, loopFinished: false));
    }

    [Fact]
    public void AdjustTurnBudget_MultiStep_RaisesNeverLowers()
    {
        Assert.Equal(8, _policy.AdjustTurnBudget(4, planSize: 3));   // 3*2+2
        Assert.Equal(10, _policy.AdjustTurnBudget(10, planSize: 3)); // never lowers
        Assert.Equal(6, _policy.AdjustTurnBudget(6, planSize: 1));   // trivial plan: unchanged
    }
}
