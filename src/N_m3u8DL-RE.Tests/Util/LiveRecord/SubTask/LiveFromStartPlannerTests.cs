using N_m3u8DL_RE.Common.Entity;
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
    [InlineData(5000, 0, 1400)]
    [InlineData(3600, 0, 0)]
    [InlineData(3599, 0, 0)]
    [InlineData(5000, 2000, 2000)]
    public void ResolveProbeFloor_LimitsDepthToMaxProbeDepth(long topAvailableSentinel, long floor, long expected)
    {
        LiveFromStartPlanner.ResolveProbeFloor(topAvailableSentinel, floor).ShouldBe(expected);
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

    [Fact]
    public void ResolveInitialAscendingLogicalBatchEnd_CountsCachedLowerAsInitialBatchSlot()
    {
        LiveFromStartPlanner.ResolveInitialAscendingLogicalBatchEnd(613394737, 6).ShouldBe(613394742);
    }

    [Fact]
    public void TryResolveInitialTimeoutFastForwardLatestFailure_WithPredictableContinuousSegments_ReturnsLatestFailure()
    {
        var timeoutSegments = Enumerable.Range(613394738, 5)
            .Select(number => Segment(number))
            .ToList();

        LiveFromStartPlanner.TryResolveInitialTimeoutFastForwardLatestFailure(timeoutSegments, out var latestFailure).ShouldBeTrue();
        latestFailure.ShouldBe(613394742);

        var step = LiveFromStartPlanner.ResolveInitialTimeoutFastForwardStep(12d, 1d);
        step.ShouldBe(12);
        LiveFromStartPlanner.ResolveInitialTimeoutFastForwardStart(latestFailure, step).ShouldBe(613394754);
    }

    [Fact]
    public void TryResolveInitialTimeoutFastForwardLatestFailure_RejectsGappedSegments()
    {
        var timeoutSegments = new[] { Segment(613394738), Segment(613394740) };

        LiveFromStartPlanner.TryResolveInitialTimeoutFastForwardLatestFailure(timeoutSegments, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveInitialTimeoutFastForwardLatestFailure_RejectsUnpredictableUrls()
    {
        var timeoutSegments = new[]
        {
            Segment(613394738),
            new MediaSegment { Index = 613394739, Duration = 1, Url = "https://example.test/live/613394740.m4s" }
        };

        LiveFromStartPlanner.TryResolveInitialTimeoutFastForwardLatestFailure(timeoutSegments, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(12d, 1d, 12)]
    [InlineData(12d, 2d, 6)]
    [InlineData(12d, 5d, 3)]
    [InlineData(12d, 0d, 12)]
    public void ResolveInitialTimeoutFastForwardStep_UsesSegmentTimeoutOverTargetDuration(
        double segmentTimeoutSec,
        double targetDurationSec,
        long expected)
    {
        LiveFromStartPlanner.ResolveInitialTimeoutFastForwardStep(segmentTimeoutSec, targetDurationSec).ShouldBe(expected);
    }

    private static MediaSegment Segment(long number)
    {
        return new MediaSegment
        {
            Index = number,
            Duration = 1,
            Url = $"https://example.test/live/{number}.m4s"
        };
    }
}
