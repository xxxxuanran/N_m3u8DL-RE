using N_m3u8DL_RE.Column;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Column;

public class DownloadSpeedColumnTests
{
    [Theory]
    [InlineData(0, "0.00KiB/s")]
    [InlineData(512, "0.50KiB/s")]
    [InlineData(1536, "1.50KiB/s")]
    [InlineData(1023989, "999.99KiB/s")]
    [InlineData(1023990, "0.98MiB/s")]
    [InlineData(1024 * 1024, "1.00MiB/s")]
    [InlineData(15728640, "15.00MiB/s")]
    public void FormatSpeed_UsesBinaryRateUnits(long bytesPerSecond, string expected)
    {
        DownloadSpeedColumn.FormatSpeed(bytesPerSecond).ShouldBe(expected);
    }
}
