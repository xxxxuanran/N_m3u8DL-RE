using System.Collections.Concurrent;
using System.Globalization;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using static N_m3u8DL_RE.Common.Util.LiveSegmentUrlUtil;

namespace N_m3u8DL_RE.Util.LiveRecord.SubTask;

/// <summary>
/// 直播「实时缺口补齐」的有状态协调器：在录制过程中检测可预测 URL 流的缺号、把缺口转入待补队列受控补齐，
/// 并对实时合并施加按号顺序的写出闸门，避免缺口被静默跳过。
///
/// 存在需求：直播 playlist 是滑动窗口，断网恢复或刷新间隔过长会导致整段分片号缺失；若直接顺序合并，
/// 这些缺口会被无声丢弃，产物出现断点。本协调器把缺口检测/入队/驱逐（委托 <see cref="LiveSegmentGapPlanner"/> 的纯函数）
/// 与并发补洞下载（委托 <see cref="LiveSubTaskSegmentDownloader"/>）编排起来，并按 taskId 维护每条流的高水位号、
/// 待补队列、合并闸门与统计。这部分原内联在 SimpleLiveRecordManager2，抽出后职责单一、状态清晰且可独立演进。
/// </summary>
internal sealed class LiveGapFillCoordinator(
    LiveSubTaskSegmentDownloader subTaskDownloader,
    Func<bool> isEnabled,
    Func<int> getThreadCount,
    Func<int> getDownloadRetryCount,
    Func<double> getHttpRequestTimeout,
    Func<string[]?> getLiveHostMirrors,
    Func<CancellationToken> getCancellationToken,
    Action<Action> updateProgress,
    Action<int, double> addRefreshedDuration)
{
    private readonly ConcurrentDictionary<int, SegmentUrlPatternCheck> segmentUrlPatternDic = new();
    private readonly ConcurrentDictionary<int, long> highestEnqueuedNumberDic = new();
    private readonly ConcurrentDictionary<int, SortedDictionary<long, PendingGapEntry>> pendingGapNumbersDic = new();
    private readonly ConcurrentDictionary<int, long> lastMergedNumberDic = new();
    private readonly ConcurrentDictionary<int, (long BlockNumber, DateTime Since)> mergeHoldDic = new();
    private readonly ConcurrentDictionary<int, LiveGapStats> gapStatsDic = new();

    /// <summary>待补缺口的单项状态：已尝试次数（用于有界重试驱逐）、滑动窗口大小、是否已计入进度总量。</summary>
    private sealed class PendingGapEntry
    {
        public int Attempts;
        public long Window;
        public bool CountedInPlaylist;
    }

    /// <summary>单条流的缺口补齐统计：已补齐 / 已延后 / 最终丢失（散号 + 压缩区间），用于收尾日志。</summary>
    private sealed class LiveGapStats
    {
        public long Filled;
        public long Deferred;
        public readonly SortedSet<long> Lost = [];
        public readonly List<SegmentNumberRange> CompactLostRanges = [];
    }

    /// <summary>闭区间 [Start, End] 的号范围，用于压缩表示大段丢失/缺口。</summary>
    private readonly record struct SegmentNumberRange(long Start, long End)
    {
        public long Count => End - Start + 1;
    }

    /// <summary>为一条流初始化补齐状态（URL 可预测性、初始高水位号、空的待补队列与统计），录制开始前调用。</summary>
    public void Initialize(int taskId, StreamSpec streamSpec)
    {
        segmentUrlPatternDic[taskId] = CheckSegmentUrlPattern(streamSpec.Playlist?.MediaParts[0].MediaSegments ?? []);
        highestEnqueuedNumberDic[taskId] = GetInitialHighestEnqueuedNumber(streamSpec);
        pendingGapNumbersDic[taskId] = new SortedDictionary<long, PendingGapEntry>();
        lastMergedNumberDic[taskId] = 0L;
        gapStatsDic[taskId] = new LiveGapStats();
    }

    /// <summary>取该流的 URL 可预测性判定，并要求三项条件（查询串一致 / 号==Index / 严格递增）全部成立。</summary>
    public bool TryGetPredictableSegmentUrlPattern(int taskId, out SegmentUrlPatternCheck pattern)
    {
        return segmentUrlPatternDic.TryGetValue(taskId, out pattern)
            && pattern.SameQuery
            && pattern.NumericFileNameMatchesIndex
            && pattern.StrictlyIncreasing;
    }

    /// <summary>该流是否启用可预测缺口补齐：开关打开且 URL 模式可预测时才允许按号推算补洞。</summary>
    public bool IsPredictableFillEnabled(int taskId)
    {
        return isEnabled() && TryGetPredictableSegmentUrlPattern(taskId, out _);
    }

    /// <summary>记录已实时合并到的最大号；据此把落在其之前的缺口直接判丢，避免补齐已写出的旧分片造成乱序。</summary>
    public void MarkMerged(int taskId, long maxWrittenNumber)
    {
        lastMergedNumberDic[taskId] = Math.Max(lastMergedNumberDic.GetValueOrDefault(taskId), maxWrittenNumber);
    }

    /// <summary>按本轮已下发分片推进「已入队高水位号」——缺口检测的唯一锚点，保证去重与抗回退。</summary>
    public void UpdateHighestEnqueuedNumber(int taskId, IReadOnlyList<MediaSegment> sent)
    {
        var max = highestEnqueuedNumberDic.GetValueOrDefault(taskId);
        foreach (var s in sent)
        {
            if (TryGetSegmentUrlNumber(s, out var n) && n > max)
                max = n;
        }
        highestEnqueuedNumberDic[taskId] = max;
    }

    /// <summary>把一个主下载失败的分片号转入待补队列，让 subTask 后续重试补齐（计入 playlist 进度）。</summary>
    public void EnqueueFailedSegmentGap(StreamSpec streamSpec, int taskId, MediaSegment segment)
    {
        if (!IsPredictableFillEnabled(taskId) || !TryGetSegmentUrlNumber(segment, out var number))
            return;

        EnqueuePendingGaps(
            taskId,
            [number],
            LiveSegmentGapPlanner.ComputeGapWindow(1, GetGapWindowTargetDuration(streamSpec), GetSanitizedDownloadRetryCount()),
            countedInPlaylist: true);
    }

    /// <summary>
    /// 对可预测流做缺口检测与过滤：基于高水位号用 <see cref="LiveSegmentGapPlanner"/> 规划出新号与缺口区间，
    /// 把缺口（按目标时长裁剪后）转入待补队列，并将 playlist 原地替换为「仅含真实新分片」的列表，
    /// 顺带把 Index 校正为 URL 号以便顺序合并。流不可预测或号校验失败时返回 false 让调用方走原始路径。
    /// </summary>
    public bool TryFilterPredictableSegments(StreamSpec streamSpec, int taskId)
    {
        if (!IsPredictableFillEnabled(taskId))
            return false;

        var segs = streamSpec.Playlist!.MediaParts[0].MediaSegments;
        if (segs.Count == 0)
        {
            streamSpec.Playlist!.MediaParts[0].MediaSegments = [];
            return true;
        }

        var parsed = new List<(MediaSegment Seg, SegmentUrlParts Parts, long Num)>(segs.Count);
        string? query = null;
        foreach (var s in segs)
        {
            var parts = ParseSegmentUrl(s.Url);
            if (!TryParseSegmentNumber(parts.FileNameWithoutExtension, out var num))
                return false;
            query ??= parts.Query;
            if (parts.Query != query)
                return false;
            parsed.Add((s, parts, num));
        }

        for (var i = 1; i < parsed.Count; i++)
        {
            if (parsed[i].Num <= parsed[i - 1].Num)
                return false;
        }

        var hwm = highestEnqueuedNumberDic.GetValueOrDefault(taskId);
        var plan = LiveSegmentGapPlanner.Plan(hwm, parsed.Select(p => p.Num).ToList());
        if (plan.FreshNumbers.Count == 0)
        {
            streamSpec.Playlist!.MediaParts[0].MediaSegments = [];
            return true;
        }

        var gapWindowTd = GetGapWindowTargetDuration(streamSpec);
        var gapWindowRetryCount = GetSanitizedDownloadRetryCount();
        foreach (var range in plan.GapRanges)
        {
            EnqueuePendingGapRange(
                taskId,
                range,
                LiveSegmentGapPlanner.ComputeGapWindow(range.Count, gapWindowTd, gapWindowRetryCount),
                countedInPlaylist: false,
                targetDurationSeconds: gapWindowTd);
            Logger.WarnMarkUp($"[darkorange3_1]Detected {range.Count} missing segment(s) in predictable URL pattern ({range.Start} ~ {range.End}); deferred to subTask fill queue.[/]");
        }

        var result = new List<MediaSegment>(plan.FreshNumbers.Count);
        foreach (var p in parsed)
        {
            if (p.Num <= hwm)
                continue;
            p.Seg.Index = p.Num;
            result.Add(p.Seg);
        }

        streamSpec.Playlist!.MediaParts[0].MediaSegments = result;
        return true;
    }

    /// <summary>
    /// 取出一批待补缺口号并发补齐：先按滑动窗口驱逐已滑出直播边缘的号（记为丢失），再以受控并发下载剩余批次；
    /// 成功的写入 fileDic 并推进进度，失败的累加重试次数、超过 --download-retry-count 即驱逐判丢。每轮刷新调用一次。
    /// </summary>
    public async Task DrainPendingGapsAsync(
        StreamSpec streamSpec,
        ProgressTask task,
        SpeedContainer speedContainer,
        string tmpDir,
        Dictionary<string, string> headers,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        MediaSegment template,
        bool allSamePath)
    {
        if (!IsPredictableFillEnabled(task.Id))
            return;
        if (!pendingGapNumbersDic.TryGetValue(task.Id, out var pending))
            return;

        var parallel = LiveSubTaskSegmentDownloader.ResolveParallelism(getThreadCount());
        var retryCount = GetSanitizedDownloadRetryCount();
        var subTaskHostMirrors = LiveSubTaskSegmentDownloader.ResolveMirrorHosts(getLiveHostMirrors());
        var hwm = highestEnqueuedNumberDic.GetValueOrDefault(task.Id);

        List<long> batch;
        lock (pending)
        {
            var evict = pending.Where(kv => LiveSegmentGapPlanner.ShouldEvictByWindow(hwm, kv.Key, kv.Value.Window)).Select(kv => kv.Key).ToList();
            foreach (var n in evict)
            {
                pending.Remove(n);
                RecordLostGap(task.Id, n);
            }
            if (pending.Count == 0)
                return;
            batch = pending.Keys.Take(parallel).ToList();
        }

        if (batch.Count == 0)
            return;

        var templateParts = ParseSegmentUrl(template.Url);
        if (!TryParseSegmentNumber(templateParts.FileNameWithoutExtension, out var templateNumber))
            return;

        var duration = streamSpec.Playlist?.TargetDuration is > 0
            ? streamSpec.Playlist.TargetDuration.Value
            : (template.Duration > 0 ? template.Duration : 1);

        var ctx = new LiveSubTaskDownloadContext(
            Template: template,
            TemplateUrlParts: templateParts,
            TemplateNumber: templateNumber,
            SegmentDuration: duration,
            AllHasDatetime: false,
            AllSamePath: allSamePath,
            TmpDir: tmpDir,
            Extension: streamSpec.Extension ?? "clip",
            SpeedContainer: speedContainer,
            Headers: headers,
            RetryCount: retryCount,
            HostMirrors: subTaskHostMirrors);

        var cancellationToken = getCancellationToken();
        var downloads = batch.Select(async number =>
        {
            var (s, r) = await subTaskDownloader.DownloadAsync(ctx, number, cancellationToken);
            return (Number: number, Segment: s, Result: r);
        }).ToList();

        var results = await Task.WhenAll(downloads);

        var stats = gapStatsDic.GetOrAdd(task.Id, _ => new LiveGapStats());
        lock (pending)
        {
            foreach (var (number, seg, result) in results)
            {
                if (seg != null && result is { Success: true })
                {
                    var countedInPlaylist = pending.TryGetValue(number, out var entry) && entry.CountedInPlaylist;
                    fileDic[seg] = result;
                    pending.Remove(number);
                    stats.Filled++;
                    if (!countedInPlaylist)
                    {
                        updateProgress(() => task.MaxValue += 1);
                        addRefreshedDuration(task.Id, seg.Duration);
                    }
                    task.Increment(1);
                }
                else if (pending.TryGetValue(number, out var entry))
                {
                    entry.Attempts++;
                    if (LiveSegmentGapPlanner.ShouldEvictByRetry(entry.Attempts, retryCount))
                    {
                        pending.Remove(number);
                        RecordLostGap(task.Id, number);
                        TryDeleteDownloadResult(result);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 计算实时合并此刻可安全写出的号上界（顺序闸门）：存在未补的最小缺口号时，最多写到该号之前；
    /// 若已有更靠后的分片被攒住超过宽限期，则放弃该缺口（判丢）以免直播输出被无限期阻塞。返回 long.MaxValue 表示无阻塞。
    /// </summary>
    public long ResolveMergeWritableBound(int taskId, ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic)
    {
        var graceMs = Math.Max(10 * 1000d, getHttpRequestTimeout() * 1000d);  // 保底 10 秒
        while (true)
        {
            var block = GetMergeBlockNumber(taskId);
            if (block == long.MaxValue)
            {
                mergeHoldDic.TryRemove(taskId, out _);
                return long.MaxValue;
            }

            var hasHeldBack = fileDic.Any(f => f.Value is { Success: true } && f.Key.Index > block);
            if (!hasHeldBack)
            {
                mergeHoldDic.TryRemove(taskId, out _);
                return block;
            }

            var now = DateTime.UtcNow;
            var hold = mergeHoldDic.GetOrAdd(taskId, _ => (block, now));
            if (hold.BlockNumber != block)
            {
                hold = (block, now);
                mergeHoldDic[taskId] = hold;
            }

            if ((now - hold.Since).TotalMilliseconds < graceMs)
                return block;

            if (pendingGapNumbersDic.TryGetValue(taskId, out var pending))
            {
                lock (pending)
                {
                    pending.Remove(block);
                }
            }
            RecordLostGap(taskId, block);
            mergeHoldDic.TryRemove(taskId, out _);
            Logger.WarnMarkUp($"[darkorange3_1]Real-time merge skipped unfilled gap at segment {block} after grace period; marking it lost to avoid stalling live output.[/]");
        }
    }

    /// <summary>录制收尾时输出该流的缺口补齐汇总（已补/已延后/丢失区间/仍待补），全为 0 时静默不打扰。</summary>
    public void LogSummary(int taskId, string streamLabel)
    {
        if (!gapStatsDic.TryGetValue(taskId, out var stats))
            return;

        long lostCount;
        string lostRanges;
        lock (stats.Lost)
        {
            var compactRanges = stats.CompactLostRanges
                .Concat(stats.Lost.Select(n => new SegmentNumberRange(n, n)))
                .ToList();
            lostCount = compactRanges.Sum(r => r.Count);
            lostRanges = compactRanges.Count > 0 ? FormatSegmentNumberRanges(compactRanges) : "none";
        }

        long pendingCount = 0;
        string pendingRanges = "none";
        if (pendingGapNumbersDic.TryGetValue(taskId, out var pending))
        {
            lock (pending)
            {
                pendingCount = pending.Count;
                pendingRanges = pending.Count > 0 ? FormatContiguousIndexRanges(pending.Keys.ToList()) : "none";
            }
        }

        if (stats.Filled == 0 && stats.Deferred == 0 && lostCount == 0)
            return;

        Logger.InfoMarkUp($"[darkorange3_1]Live gap-fill summary for {streamLabel}: filled={stats.Filled}, deferred={stats.Deferred}, lost={lostCount} ({lostRanges}), still_pending={pendingCount} ({pendingRanges}).[/]");
        if (lostCount > 0)
        {
            Logger.WarnMarkUp($"[darkorange3_1]Live gap-fill: {lostCount} segment(s) ultimately lost for {streamLabel}: {lostRanges}.[/]");
        }
    }

    /// <summary>取当前阻塞实时合并的最小待补号（待补队列里最小的号）；无待补时返回 long.MaxValue 表示不阻塞。</summary>
    private long GetMergeBlockNumber(int taskId)
    {
        if (!pendingGapNumbersDic.TryGetValue(taskId, out var pending))
            return long.MaxValue;
        lock (pending)
        {
            return pending.Count > 0 ? pending.Keys.First() : long.MaxValue;
        }
    }

    /// <summary>
    /// 把一组缺口号登记到待补队列：已合并过的旧号直接判丢；已在队列的取较大窗口并合并 CountedInPlaylist 标志；
    /// 新号建项并累加「已延后」统计。是所有入队路径的统一落点。
    /// </summary>
    private void EnqueuePendingGaps(int taskId, IEnumerable<long> numbers, long window, bool countedInPlaylist)
    {
        var pending = pendingGapNumbersDic.GetOrAdd(taskId, _ => new SortedDictionary<long, PendingGapEntry>());
        var lastMerged = lastMergedNumberDic.GetValueOrDefault(taskId);
        var stats = gapStatsDic.GetOrAdd(taskId, _ => new LiveGapStats());

        lock (pending)
        {
            foreach (var n in numbers)
            {
                if (n <= lastMerged)
                {
                    RecordLostGap(taskId, n);
                    continue;
                }

                if (pending.TryGetValue(n, out var existing))
                {
                    if (existing.Window < window)
                        existing.Window = window;
                    existing.CountedInPlaylist |= countedInPlaylist;
                    continue;
                }

                pending[n] = new PendingGapEntry { Attempts = 0, Window = window, CountedInPlaylist = countedInPlaylist };
                stats.Deferred++;
            }
        }
    }

    /// <summary>
    /// 登记一段缺口区间：先按目标时长把大区间裁剪到靠近直播边缘的有限窗口（被裁掉的旧段直接记为丢失），
    /// 再逐号入队，避免大跳号同步生成海量 pending 项压垮队列。
    /// </summary>
    private void EnqueuePendingGapRange(
        int taskId,
        LiveSegmentGapPlanner.GapRange range,
        long window,
        bool countedInPlaylist,
        double targetDurationSeconds)
    {
        var maxExpandableCount = LiveSegmentGapPlanner.ComputeMaxExpandableGapCount(targetDurationSeconds);
        var (expandRange, omittedRange) = LiveSegmentGapPlanner.CapGapRangeToLatest(range, maxExpandableCount);
        if (omittedRange != null)
        {
            RecordLostGapRange(taskId, omittedRange.Value.Start, omittedRange.Value.End);
            Logger.WarnMarkUp($"[darkorange3_1]Large predictable URL gap {range.Start} ~ {range.End} has {range.Count} missing segment(s); capped pending expansion to latest {expandRange.Count} segment(s) ({expandRange.Start} ~ {expandRange.End}) by EXT-X-TARGETDURATION={targetDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s.[/]");
        }

        EnqueuePendingGaps(taskId, RangeInclusive(expandRange.Start, expandRange.End), window, countedInPlaylist);
    }

    /// <summary>把单个号记为最终丢失（无法补齐），计入丢失统计。</summary>
    private void RecordLostGap(int taskId, long number)
    {
        var stats = gapStatsDic.GetOrAdd(taskId, _ => new LiveGapStats());
        lock (stats.Lost)
        {
            stats.Lost.Add(number);
        }
    }

    /// <summary>把一段号区间记为丢失，单号退化为 <see cref="RecordLostGap"/>，多号以压缩区间存储避免统计膨胀。</summary>
    private void RecordLostGapRange(int taskId, long start, long end)
    {
        if (end < start)
            return;

        if (start == end)
        {
            RecordLostGap(taskId, start);
            return;
        }

        var stats = gapStatsDic.GetOrAdd(taskId, _ => new LiveGapStats());
        lock (stats.Lost)
        {
            stats.CompactLostRanges.Add(new SegmentNumberRange(start, end));
        }
    }

    /// <summary>取初始化时的高水位号锚点：用末尾分片的 URL 号，无号则退回其 Index；空 playlist 取 0。</summary>
    private static long GetInitialHighestEnqueuedNumber(StreamSpec item)
    {
        var segs = item.Playlist?.MediaParts[0].MediaSegments;
        if (segs == null || segs.Count == 0) return 0L;
        var last = segs[^1];
        return TryGetSegmentUrlNumber(last, out var n) ? n : last.Index;
    }

    /// <summary>取用于计算窗口/裁剪的目标时长：优先 EXT-X-TARGETDURATION，缺省回退 1 秒。</summary>
    private static double GetGapWindowTargetDuration(StreamSpec streamSpec)
    {
        return streamSpec.Playlist?.TargetDuration is > 0 ? streamSpec.Playlist.TargetDuration!.Value : 1d;
    }

    private int GetSanitizedDownloadRetryCount()
    {
        return Math.Max(0, getDownloadRetryCount());
    }

    /// <summary>生成闭区间 [start, end] 的连续号序列。</summary>
    private static IEnumerable<long> RangeInclusive(long start, long end)
    {
        for (var i = start; i <= end; i++)
            yield return i;
    }

    /// <summary>删除一个未被采用的缺口补齐结果对应的临时文件；清理失败静默忽略。</summary>
    private static bool TryDeleteDownloadResult(DownloadResult? result)
    {
        if (result is not { Success: true } || string.IsNullOrEmpty(result.ActualFilePath))
            return false;

        try
        {
            if (File.Exists(result.ActualFilePath))
                File.Delete(result.ActualFilePath);
        }
        catch
        {
            // Ignore cleanup failures for uncommitted gap-fill segments.
        }

        return true;
    }

    /// <summary>把若干号区间排序、合并相邻/重叠区间后格式化为紧凑可读字符串（如 "3, 7 ~ 9"），用于日志。</summary>
    private static string FormatSegmentNumberRanges(IEnumerable<SegmentNumberRange> ranges)
    {
        var ordered = ranges
            .Where(r => r.End >= r.Start)
            .OrderBy(r => r.Start)
            .ThenBy(r => r.End)
            .ToList();
        if (ordered.Count == 0)
            return "none";

        var merged = new List<SegmentNumberRange>();
        var current = ordered[0];
        for (var i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];
            if (current.End == long.MaxValue || next.Start <= current.End + 1)
            {
                current = new SegmentNumberRange(current.Start, Math.Max(current.End, next.End));
                continue;
            }

            merged.Add(current);
            current = next;
        }
        merged.Add(current);

        return string.Join(", ", merged.Select(r => r.Start == r.End
            ? r.Start.ToString(CultureInfo.InvariantCulture)
            : $"{r.Start.ToString(CultureInfo.InvariantCulture)} ~ {r.End.ToString(CultureInfo.InvariantCulture)}"));
    }

    /// <summary>把已升序的号列表压缩成连续区间并格式化为可读字符串，用于日志展示仍待补的缺口。</summary>
    private static string FormatContiguousIndexRanges(IReadOnlyList<long> sortedIndices)
    {
        if (sortedIndices.Count == 0)
            return "none";

        var ranges = new List<SegmentNumberRange>();
        var start = sortedIndices[0];
        var prev = sortedIndices[0];

        for (var i = 1; i < sortedIndices.Count; i++)
        {
            var current = sortedIndices[i];
            if (current == prev + 1)
            {
                prev = current;
                continue;
            }

            ranges.Add(new SegmentNumberRange(start, prev));
            start = current;
            prev = current;
        }

        ranges.Add(new SegmentNumberRange(start, prev));
        return FormatSegmentNumberRanges(ranges);
    }
}
