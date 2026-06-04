using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Util.LiveRecord.SubTask;

internal static class LiveFromStartPlanner
{
    public const long MaxProbeDepth = 3600;
    public static readonly TimeSpan InitialTimeoutBatchCompletionWindow = TimeSpan.FromSeconds(1);

    public readonly record struct FuzzyBoundaryPlan(
        long FillStart,
        long ExploreFloor,
        long ExploreUpper,
        bool HasExploreRange);

    public static FuzzyBoundaryPlan PlanFuzzyBoundary(long windowLower, long windowUpper)
    {
        return new FuzzyBoundaryPlan(
            FillStart: windowUpper,
            ExploreFloor: windowLower,
            ExploreUpper: windowUpper - 1,
            HasExploreRange: windowUpper - 1 >= windowLower);
    }

    public static double ResolveProbeTimeoutSec(int waitSec, double httpRequestTimeoutSec)
    {
        var probeTimeoutSec = Math.Clamp(waitSec * 2d, 2d, 5d);
        return BoundTimeoutToHttpRequestTimeout(probeTimeoutSec, httpRequestTimeoutSec);
    }

    public static long ResolveProbeFloor(long topAvailableSentinel, long floor)
    {
        var maxDepthFloor = topAvailableSentinel <= MaxProbeDepth
            ? 0
            : topAvailableSentinel - MaxProbeDepth;

        return Math.Max(floor, maxDepthFloor);
    }

    public static double ResolveSegmentTimeoutSec(
        int parallelism,
        double targetDurationSeconds,
        double probeTimeoutSec,
        double httpRequestTimeoutSec)
    {
        var td = double.IsFinite(targetDurationSeconds) && targetDurationSeconds > 0 ? targetDurationSeconds : 1d;
        var baseTimeoutSec = td * Math.Max(1, parallelism) * 2d;
        var maxTimeoutSec = double.IsFinite(httpRequestTimeoutSec) && httpRequestTimeoutSec > 0
            ? httpRequestTimeoutSec
            : baseTimeoutSec;
        var minTimeoutSec = Math.Min(probeTimeoutSec, maxTimeoutSec);

        return Math.Clamp(baseTimeoutSec, minTimeoutSec, maxTimeoutSec);
    }

    public static long ResolveInitialAscendingLogicalBatchEnd(long lower, int parallelism)
    {
        var offset = Math.Max(1L, parallelism) - 1;
        return lower > long.MaxValue - offset ? long.MaxValue : lower + offset;
    }

    public static bool TryResolveInitialTimeoutFastForwardLatestFailure(
        IReadOnlyList<MediaSegment> timeoutSegments,
        out long latestFailure)
    {
        latestFailure = 0;
        if (timeoutSegments.Count == 0)
            return false;

        var ordered = timeoutSegments.OrderBy(segment => segment.Index).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Index != ordered[i - 1].Index + 1)
                return false;
        }

        var patternCheck = LiveSegmentUrlUtil.CheckSegmentUrlPattern(ordered);
        if (!patternCheck.SameQuery || !patternCheck.NumericFileNameMatchesIndex || !patternCheck.StrictlyIncreasing)
            return false;

        latestFailure = ordered[^1].Index;
        return true;
    }

    public static long ResolveInitialTimeoutFastForwardStep(double segmentTimeoutSec, double targetDurationSeconds)
    {
        var timeout = double.IsFinite(segmentTimeoutSec) && segmentTimeoutSec > 0 ? segmentTimeoutSec : 1d;
        var td = double.IsFinite(targetDurationSeconds) && targetDurationSeconds > 0 ? targetDurationSeconds : 1d;
        var step = Math.Ceiling(timeout / td);

        if (step >= long.MaxValue)
            return long.MaxValue;

        return Math.Max(1L, (long)step);
    }

    public static long ResolveInitialTimeoutFastForwardStart(long latestBatchNumber, long step)
    {
        var safeStep = Math.Max(1L, step);
        return latestBatchNumber > long.MaxValue - safeStep
            ? long.MaxValue
            : latestBatchNumber + safeStep;
    }

    private static double BoundTimeoutToHttpRequestTimeout(double timeoutSec, double httpRequestTimeoutSec)
    {
        if (!double.IsFinite(httpRequestTimeoutSec) || httpRequestTimeoutSec <= 0)
            return timeoutSec;

        return Math.Min(timeoutSec, httpRequestTimeoutSec);
    }
}
