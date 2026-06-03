using System.Diagnostics;
using N_m3u8DL_RE.Entity;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Entity;

public class SpeedContainerTests
{
    [Fact]
    public void GetDownloadBufferSize_UnlimitedSpeed_UsesDefaultSize()
    {
        var container = new SpeedContainer();

        var size = container.GetDownloadBufferSize(16 * 1024);

        size.ShouldBe(16 * 1024);
    }

    [Fact]
    public void GetDownloadBufferSize_LimitedSpeed_UsesSmallerSubSecondChunk()
    {
        var container = new SpeedContainer { SpeedLimit = 5 * 1024 };

        var size = container.GetDownloadBufferSize(16 * 1024);

        size.ShouldBe(1024);
    }

    [Fact]
    public void ReserveSpeedLimitDelay_ImmediateOverLimitChunk_ReturnsSubSecondDelay()
    {
        var container = new SpeedContainer { SpeedLimit = 10 * 1024 };
        var timestamp = Stopwatch.Frequency;

        container.ReserveSpeedLimitDelay(1024, timestamp).ShouldBe(TimeSpan.Zero);
        var delay = container.ReserveSpeedLimitDelay(1024, timestamp);

        Assert.InRange(delay.TotalMilliseconds, 99D, 101D);
    }

    [Fact]
    public void Reset_DoesNotResetSpeedLimitDebt()
    {
        var container = new SpeedContainer { SpeedLimit = 10 * 1024 };
        var timestamp = Stopwatch.Frequency;

        container.ReserveSpeedLimitDelay(1024, timestamp).ShouldBe(TimeSpan.Zero);
        container.ReserveSpeedLimitDelay(1024, timestamp);
        container.Reset();

        var delay = container.ReserveSpeedLimitDelay(1024, timestamp);

        Assert.InRange(delay.TotalMilliseconds, 199D, 201D);
    }

    [Fact]
    public void Reset_ReturnsAndClearsSampledBytes()
    {
        var container = new SpeedContainer();

        container.Add(100);
        container.Add(50);

        container.Reset().ShouldBe(150);
        container.Downloaded.ShouldBe(0);
    }

    [Fact]
    public void SharedSpeedLimiter_ContainersShareLimitDebt()
    {
        var limiter = new SpeedLimiter(10 * 1024);
        var first = new SpeedContainer(limiter);
        var second = new SpeedContainer(limiter);
        var timestamp = Stopwatch.Frequency;

        first.ReserveSpeedLimitDelay(1024, timestamp).ShouldBe(TimeSpan.Zero);
        var delay = second.ReserveSpeedLimitDelay(1024, timestamp);

        Assert.InRange(delay.TotalMilliseconds, 99D, 101D);
    }
}
