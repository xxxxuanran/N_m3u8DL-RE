using System.Diagnostics;

namespace N_m3u8DL_RE.Entity;

internal sealed class SpeedLimiter
{
    private const int DefaultDownloadBufferSize = 16 * 1024;
    private const int MinLimitedDownloadBufferSize = 1024;
    private const double SpeedLimitWindowSeconds = 0.1d;

    private readonly object _lock = new();
    private long _timestamp = 0L;
    private double _availableBytes = 0D;
    private bool _initialized = false;

    public SpeedLimiter(long limit)
    {
        Limit = limit;
    }

    public long Limit { get; }

    public int GetDownloadBufferSize(int defaultSize = DefaultDownloadBufferSize)
    {
        if (Limit <= 0 || Limit == long.MaxValue)
            return defaultSize;

        var upper = Math.Max(1, defaultSize);
        var lower = Math.Min(MinLimitedDownloadBufferSize, upper);
        var target = (int)Math.Ceiling(Limit * SpeedLimitWindowSeconds);
        return Math.Clamp(target, lower, upper);
    }

    public async ValueTask WaitAsync(long size, CancellationToken cancellationToken = default)
    {
        var delay = ReserveDelay(size, Stopwatch.GetTimestamp());
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
    }

    internal TimeSpan ReserveDelay(long size, long timestamp)
    {
        if (size <= 0)
            return TimeSpan.Zero;

        if (Limit <= 0 || Limit == long.MaxValue)
            return TimeSpan.Zero;

        lock (_lock)
        {
            var capacity = GetCapacity();
            if (!_initialized)
            {
                _timestamp = timestamp;
                _availableBytes = capacity;
                _initialized = true;
            }
            else if (timestamp > _timestamp)
            {
                var elapsedSeconds = (timestamp - _timestamp) / (double)Stopwatch.Frequency;
                _availableBytes = Math.Min(capacity, _availableBytes + elapsedSeconds * Limit);
                _timestamp = timestamp;
            }

            _availableBytes -= size;
            if (_availableBytes >= 0)
                return TimeSpan.Zero;

            return TimeSpan.FromSeconds(-_availableBytes / Limit);
        }
    }

    private double GetCapacity()
    {
        return Math.Max(MinLimitedDownloadBufferSize, Limit * SpeedLimitWindowSeconds);
    }
}
