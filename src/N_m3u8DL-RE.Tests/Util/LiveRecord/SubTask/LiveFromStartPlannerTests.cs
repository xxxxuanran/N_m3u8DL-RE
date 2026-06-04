using N_m3u8DL_RE.Util.LiveRecord.SubTask;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Util.LiveRecord.SubTask;

public class LiveFromStartPlannerTests
{
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
