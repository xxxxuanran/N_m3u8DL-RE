using N_m3u8DL_RE.Entity;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace N_m3u8DL_RE.Column;

internal sealed class DownloadSpeedColumn : ProgressColumn
{
    private const double MaxKiBPerSecond = 999.99D;
    private long _stopSpeed = 0;
    private ConcurrentDictionary<int, long> SampleTimestampDic = new();
    protected override bool NoWrap => true;
    private ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic { get; set; }

    public DownloadSpeedColumn(ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic)
    {
        this.SpeedContainerDic = SpeedContainerDic;
    }

    public Style MyStyle { get; set; } = new Style(foreground: Color.Green);

    internal static string FormatSpeed(long bytesPerSecond)
    {
        return bytesPerSecond / 1024D > MaxKiBPerSecond
            ? $"{bytesPerSecond / (1024D * 1024D):0.00}MiB/s"
            : $"{bytesPerSecond / 1024D:0.00}KiB/s";
    }

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var taskId = task.Id;
        var speedContainer = SpeedContainerDic[taskId];
        var now = Stopwatch.GetTimestamp();
        var flag = task.IsFinished || !task.IsStarted;
        // 单文件下载汇报进度
        if (!flag && speedContainer is { SingleSegment: true, ResponseLength: not null })
        {
            task.MaxValue = (double)speedContainer.ResponseLength;
            task.Value = speedContainer.RDownloaded;
        }
        if (flag)
        {
            SampleTimestampDic[taskId] = now;
        }
        else
        {
            var lastTimestamp = SampleTimestampDic.GetOrAdd(taskId, now);
            var elapsed = Stopwatch.GetElapsedTime(lastTimestamp, now);
            if (elapsed >= TimeSpan.FromSeconds(1) && SampleTimestampDic.TryUpdate(taskId, now, lastTimestamp))
            {
                var downloaded = speedContainer.Reset();
                speedContainer.NowSpeed = elapsed.TotalSeconds > 0 ? (long)(downloaded / elapsed.TotalSeconds) : downloaded;
                // 速度为0，计数增加
                if (downloaded <= _stopSpeed) { speedContainer.AddLowSpeedCount(); }
                else speedContainer.ResetLowSpeedCount();
            }
        }
        var style = flag ? Style.Plain : MyStyle;
        return flag ? new Text("-", style).Centered() : new Text(FormatSpeed(speedContainer.NowSpeed) + (speedContainer.LowSpeedCount > 0 ? $"({speedContainer.LowSpeedCount})" : ""), style).Centered();
    }
}
