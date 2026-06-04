namespace N_m3u8DL_RE.Util.LiveRecord.SubTask;

internal static class LiveFromStartPlanner
{
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

    private static double BoundTimeoutToHttpRequestTimeout(double timeoutSec, double httpRequestTimeoutSec)
    {
        if (!double.IsFinite(httpRequestTimeoutSec) || httpRequestTimeoutSec <= 0)
            return timeoutSec;

        return Math.Min(timeoutSec, httpRequestTimeoutSec);
    }
}
