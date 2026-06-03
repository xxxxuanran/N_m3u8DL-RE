namespace N_m3u8DL_RE.Entity;

internal class SpeedContainer
{
    private const int DefaultDownloadBufferSize = 16 * 1024;

    public bool SingleSegment { get; set; } = false;
    public long NowSpeed { get; set; } = 0L; // 当前每秒速度
    public long SpeedLimit
    {
        get => _speedLimiter.Limit;
        set => _speedLimiter = new SpeedLimiter(value);
    }
    public long? ResponseLength { get; set; }
    public long RDownloaded => Interlocked.Read(ref _Rdownloaded);
    private int _zeroSpeedCount = 0;
    public int LowSpeedCount => _zeroSpeedCount;
    public bool ShouldStop => LowSpeedCount >= 20;

    ///////////////////////////////////////////////////

    private long _downloaded = 0;
    private long _Rdownloaded = 0;
    private SpeedLimiter _speedLimiter;
    public long Downloaded => Interlocked.Read(ref _downloaded);

    public SpeedContainer()
        : this(null)
    {
    }

    public SpeedContainer(SpeedLimiter? speedLimiter)
    {
        _speedLimiter = speedLimiter ?? new SpeedLimiter(long.MaxValue);
    }

    public int AddLowSpeedCount()
    {
        return Interlocked.Add(ref _zeroSpeedCount, 1);
    }

    public int ResetLowSpeedCount()
    {
        return Interlocked.Exchange(ref _zeroSpeedCount, 0);
    }

    public long Add(long size)
    {
        Interlocked.Add(ref _Rdownloaded, size);
        return Interlocked.Add(ref _downloaded, size);
    }

    public int GetDownloadBufferSize(int defaultSize = DefaultDownloadBufferSize)
    {
        return _speedLimiter.GetDownloadBufferSize(defaultSize);
    }

    public ValueTask WaitForSpeedLimitAsync(long size, CancellationToken cancellationToken = default)
    {
        return _speedLimiter.WaitAsync(size, cancellationToken);
    }

    internal TimeSpan ReserveSpeedLimitDelay(long size, long timestamp)
    {
        return _speedLimiter.ReserveDelay(size, timestamp);
    }

    public long Reset()
    {
        return Interlocked.Exchange(ref _downloaded, 0);
    }

    public void ResetVars()
    {
        Reset();
        ResetLowSpeedCount();
        SingleSegment = false;
        ResponseLength = null;
        Interlocked.Exchange(ref _Rdownloaded, 0L);
    }
}
