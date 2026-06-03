namespace N_m3u8DL_RE.Common.Util;

/// <summary>
/// 直播可预测 URL 流的缺口规划与待补缺口驱逐策略（纯函数，便于单测）。
///
/// 设计要点（修复断网恢复后整段分片被静默丢弃的根因）：
/// - 以"已入队高水位号"为唯一锚点做缺口检测，对 number &lt;= 高水位号的回退/重叠分片直接丢弃（去重、抗回退）；
/// - 缺口恒为 (高水位号+1 .. 最小新号-1)，与历史快照状态解耦，不再依赖脆弱的 LastFileName/MaxIndex 配对；
/// - 所有缺口统一转入待补队列，由 subTask（与 live-from-start 同属补洞逻辑）受控并发补齐；
/// - 大缺口在入队前按目标时长裁剪到靠近直播边缘的有限窗口，队列规模再由滑动窗口 + 有界重试驱逐继续收敛。
/// </summary>
public static class LiveSegmentGapPlanner
{
    public const double DefaultMaxExpandableGapSeconds = 3600d;

    /// <summary>闭区间 [Start, End]。</summary>
    public readonly record struct GapRange(long Start, long End)
    {
        public long Count => End - Start + 1;
    }

    public sealed class GapPlan
    {
        /// <summary>本轮处理后应更新到的高水位号（= 最大真实新号；无新号时维持原值）。</summary>
        public long NewHighWaterMark { get; init; }

        /// <summary>真实新分片号（&gt; 高水位号），升序。</summary>
        public List<long> FreshNumbers { get; } = [];

        /// <summary>检测到的缺口区间（升序），统一转入待补队列受控补齐。</summary>
        public List<GapRange> GapRanges { get; } = [];
    }

    /// <summary>
    /// 基于高水位号规划缺口。
    /// </summary>
    /// <param name="highWaterMark">已入队的最大 URL 数字号。</param>
    /// <param name="playlistNumbers">当前 playlist 解析出的号，要求升序且严格递增。</param>
    public static GapPlan Plan(long highWaterMark, IReadOnlyList<long> playlistNumbers)
    {
        var fresh = new List<long>();
        foreach (var n in playlistNumbers)
        {
            if (n > highWaterMark)
                fresh.Add(n);
        }

        if (fresh.Count == 0)
            return new GapPlan { NewHighWaterMark = highWaterMark };

        var plan = new GapPlan { NewHighWaterMark = fresh[^1] };

        void AddGap(long start, long end)
        {
            if (end >= start)
                plan.GapRanges.Add(new GapRange(start, end));
        }

        // between-refresh 缺口：高水位号+1 .. 最小新号-1
        AddGap(highWaterMark + 1, fresh[0] - 1);

        for (var i = 0; i < fresh.Count; i++)
        {
            // playlist 内部缺口（一般极少且小）
            if (i > 0)
                AddGap(fresh[i - 1] + 1, fresh[i] - 1);

            plan.FreshNumbers.Add(fresh[i]);
        }

        return plan;
    }

    /// <summary>
    /// live-from-start 历史回填收尾：在确认不可补齐的空洞存在时，决定应放弃的上界。
    ///
    /// 历史回填的目标是把一段"连续衔接到直播边缘"的历史分片接在录制起点之前；若历史区间里存在永久空洞
    /// （所有镜像 404），则空洞之下的已下载碎片无法跨洞连续拼接，留着只会在产物里制造断点，应整体放弃。
    /// 以"最高空洞 G"为界：仅保留 (G, upper] 的连续尾段，丢弃所有号 &lt;= G 的碎片。
    /// </summary>
    /// <param name="downloadedNumbers">已成功下载并提交的分片号。</param>
    /// <param name="unfillableNumbers">确认不可补齐（镜像竞速 404）的空洞号。</param>
    /// <returns>需要放弃的上界 G（丢弃所有 &lt;= G 的已下载碎片）；无需放弃时为 null。</returns>
    public static long? ResolveUnfillableHistoryCeil(IReadOnlyList<long> downloadedNumbers, IReadOnlyList<long> unfillableNumbers)
    {
        if (unfillableNumbers.Count == 0 || downloadedNumbers.Count == 0)
            return null;

        var highestHole = unfillableNumbers.Max();
        // 仅当确有已下载碎片落在最高空洞及其下方（需要被裁掉）时才放弃。
        return downloadedNumbers.Any(n => n <= highestHole) ? highestHole : null;
    }

    /// <summary>
    /// 滑动窗口（单位：分片数）。号落后直播边缘超过该窗口即判定 CDN 已滑出而放弃。
    /// 基准 = 缺片数 × --download-retry-count，并 clamp 到 [下限, 上限]：
    ///  - 下限 = Max(30, ceil(30 秒 / EXT-X-TARGETDURATION))，保证小缺口也有足够补救余量；
    ///  - 上限 = Max(3600, ceil(3600 秒 / EXT-X-TARGETDURATION))，避免 pending 队列无限滞留。
    /// </summary>
    public static long ComputeGapWindow(long missingCount, double targetDurationSeconds, int downloadRetryCount)
    {
        var td = double.IsFinite(targetDurationSeconds) && targetDurationSeconds > 0 ? targetDurationSeconds : 1d;
        var lower = Math.Max(30L, (long)Math.Ceiling(30d / td));
        var upper = Math.Max(3600L, (long)Math.Ceiling(3600d / td));
        var baseValue = Math.Max(0L, missingCount) * Math.Max(0L, downloadRetryCount);
        return Math.Min(Math.Max(baseValue, lower), upper);
    }

    /// <summary>
    /// 默认最多展开约一小时的缺口分片数，避免大跳号同步创建海量 pending entry。
    /// </summary>
    public static long ComputeMaxExpandableGapCount(double targetDurationSeconds, double maxGapSeconds = DefaultMaxExpandableGapSeconds)
    {
        var td = double.IsFinite(targetDurationSeconds) && targetDurationSeconds > 0 ? targetDurationSeconds : 1d;
        var seconds = double.IsFinite(maxGapSeconds) && maxGapSeconds > 0 ? maxGapSeconds : DefaultMaxExpandableGapSeconds;
        var count = Math.Floor(seconds / td);

        if (count >= long.MaxValue)
            return long.MaxValue;

        return Math.Max(1L, (long)count);
    }

    /// <summary>
    /// 将大缺口裁剪为靠近直播边缘的尾部窗口；被裁掉的旧范围应视为 lost，不再逐项入队。
    /// </summary>
    public static (GapRange ExpandRange, GapRange? OmittedRange) CapGapRangeToLatest(GapRange range, long maxExpandableCount)
    {
        var limit = Math.Max(1L, maxExpandableCount);
        if (range.Count <= limit)
            return (range, null);

        var expandStart = range.End - limit + 1;
        return (new GapRange(expandStart, range.End), new GapRange(range.Start, expandStart - 1));
    }

    /// <summary>滑动窗口驱逐判据：高水位号 - 号 &gt; window。</summary>
    public static bool ShouldEvictByWindow(long highWaterMark, long number, long window) => highWaterMark - number > window;

    /// <summary>有界重试判据：已尝试次数 &gt; --download-retry-count。</summary>
    public static bool ShouldEvictByRetry(int attempts, int retryCount) => attempts > retryCount;
}
