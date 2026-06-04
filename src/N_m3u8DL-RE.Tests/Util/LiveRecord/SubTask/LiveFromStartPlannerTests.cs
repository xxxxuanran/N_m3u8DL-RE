using N_m3u8DL_RE.Util.LiveRecord.SubTask;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Util.LiveRecord.SubTask;

public class LiveFromStartPlannerTests
{
    [Fact]
    public void PlanFuzzyBoundary_AssignsBoundaryPointToAscendingFill()
    {
        var plan = LiveFromStartPlanner.PlanFuzzyBoundary(613351018, 613351049);

        plan.FillStart.ShouldBe(613351049);
        plan.ExploreFloor.ShouldBe(613351018);
        plan.ExploreUpper.ShouldBe(613351048);
        plan.HasExploreRange.ShouldBeTrue();
    }

    [Fact]
    public void PlanFuzzyBoundary_SinglePointWindow_HasNoDescendingExploreRange()
    {
        var plan = LiveFromStartPlanner.PlanFuzzyBoundary(100, 100);

        plan.FillStart.ShouldBe(100);
        plan.ExploreFloor.ShouldBe(100);
        plan.ExploreUpper.ShouldBe(99);
        plan.HasExploreRange.ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, 100d, 2d)]
    [InlineData(3, 100d, 5d)]
    [InlineData(10, 100d, 5d)]
    [InlineData(10, 3d, 3d)]
    public void ResolveProbeTimeoutSec_ClampsByProbeRangeAndHttpRequestTimeout(int waitSec, double httpRequestTimeoutSec, double expected)
    {
        LiveFromStartPlanner.ResolveProbeTimeoutSec(waitSec, httpRequestTimeoutSec).ShouldBe(expected);
    }

    [Theory]
    [InlineData(8, 1d, 5d, 100d, 16d)]
    [InlineData(8, 1d, 5d, 4d, 4d)]
    [InlineData(1, 1d, 5d, 100d, 5d)]
    [InlineData(8, 0d, 5d, 100d, 16d)]
    public void ResolveSegmentTimeoutSec_UsesTargetDurationParallelismAndHttpCap(
        int parallelism,
        double targetDuration,
        double probeTimeoutSec,
        double httpRequestTimeoutSec,
        double expected)
    {
        LiveFromStartPlanner.ResolveSegmentTimeoutSec(
            parallelism,
            targetDuration,
            probeTimeoutSec,
            httpRequestTimeoutSec).ShouldBe(expected);
    }
}
