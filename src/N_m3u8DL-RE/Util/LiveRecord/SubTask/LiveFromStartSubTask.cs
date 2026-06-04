using System.Collections.Concurrent;
using System.Globalization;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using static N_m3u8DL_RE.Common.Util.LiveSegmentUrlUtil;

namespace N_m3u8DL_RE.Util.LiveRecord.SubTask;

/// <summary>
/// live-from-start 历史回填一次任务的入参集合：流、进度任务、限速器、临时目录、请求头、结果字典、
/// URL 是否可预测、线程数与镜像主机。打包传递以避免下载方法签名膨胀，并保证一轮回填内参数一致。
/// </summary>
internal sealed record LiveFromStartSubTaskRequest(
    StreamSpec StreamSpec,
    ProgressTask Task,
    SpeedContainer SpeedContainer,
    string TmpDir,
    Dictionary<string, string> Headers,
    ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic,
    bool PredictableSegmentUrlPattern,
    int ThreadCount,
    string[]? LiveHostMirrors);

/// <summary>
/// 「从直播起点开始录制」(--live-real-time-merge / live-from-start) 的历史回填子任务：在录制接入直播边缘后，
/// 反向把录制起点之前、CDN 仍可访问的历史分片尽量补齐，使产物尽可能从直播开播处开始。
///
/// 存在需求：直播 playlist 只暴露滑动窗口内的少量分片，起点之前的历史无法从清单直接获得，只能依靠可预测 URL
/// 按号推算并探测可用性。本任务承担三件事：① 定位 CDN 上仍可访问的最早分片号（指数+二分探测，必要时切降序）；
/// ② 顺序/降序两种策略并发回填并按号顺序提交；③ 对无法跨越的永久空洞（所有镜像 404）整体放弃其下方碎片，
/// 避免产物出现断点。逻辑自成一体且与主录制循环解耦，故独立成类；通过注入的回调与主管理器交互（停止标志、进度更新等）。
/// </summary>
internal sealed class LiveFromStartSubTask(
    LiveSubTaskSegmentDownloader subTaskDownloader,
    Func<bool> isStopping,
    Func<int> getWaitSec,
    Action<Action> updateProgress,
    Action<int, double> addRefreshedDuration)
{
    /// <summary>降序竞速中一个号的解析结果：分片、下载结果，及是否来自探测缓存（复用则不重复清理/计数）。</summary>
    private readonly record struct DescendingResolved(MediaSegment? Segment, DownloadResult? Result, bool Reused);

    /// <summary>已按号确认可用的下载结果；用于模糊边界探索完成后再决定是否接入主连续尾段。</summary>
    private readonly record struct NumberedResolved(long Number, MediaSegment Segment, DownloadResult Result, bool Reused);

    /// <summary>定位阶段的结果；小窗口不再精确收尾，而是把模糊窗口交给后续并发探索。</summary>
    private sealed record LocateResult(
        long? Earliest,
        string? LastRequestUrl,
        bool UseDescending,
        long? FuzzyWindowLower,
        long? FuzzyWindowUpper);

    /// <summary>升序抢救主区间的结果集合，最终由汇总阶段按最高不可填洞裁剪为可连接直播边缘的连续尾段。</summary>
    private sealed record AscendingBackfillResult(
        List<MediaSegment> DownloadedSegments,
        HashSet<long> Committed,
        List<long> DiscardedExpiredNumbers,
        int DispatchCount,
        int DiscardedCleanupCount);

    /// <summary>模糊小窗口倒序探索结果；仅包含已按倒序闸门确认连续的候选片段，是否采用取决于主升序区间是否无洞。</summary>
    private sealed record FuzzyBoundaryExploreResult(
        List<NumberedResolved> ExploredSegments,
        long? Boundary,
        int DispatchCount,
        int DiscardedCleanupCount);

    /// <summary>降序竞速扫描的产出：在途/已解析任务、已提交号集合、命中的不可用边界，以及下发/复用计数（供收尾清理与汇总）。</summary>
    private sealed record DescendingScanResult(
        Dictionary<long, Task<(MediaSegment Segment, DownloadResult? Result)>> InFlight,
        Dictionary<long, DescendingResolved> Resolved,
        HashSet<long> Committed,
        long? Boundary,
        int DispatchCount,
        int ReusedFromCache);

    /// <summary>
    /// 历史回填主入口：校验 URL 可预测后，先定位最早可用号，再据探测结论选择升序或降序策略并发回填，
    /// 按号顺序提交成功分片、清理未采用的下载，并对不可补齐的空洞整体放弃其下方碎片，最后输出汇总。整段异常均被吞掉以不影响主录制。
    /// </summary>
    public async Task DownloadAsync(LiveFromStartSubTaskRequest request, CancellationToken cancellationToken)
    {
        using var backfillCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var streamSpec = request.StreamSpec;
            var task = request.Task;
            var segments = streamSpec.Playlist?.MediaParts[0].MediaSegments.ToList();
            if (segments == null || segments.Count == 0)
                return;

            var streamLabel = streamSpec.ToShortShortString().EscapeMarkup();
            if (!request.PredictableSegmentUrlPattern)
            {
                Logger.InfoMarkUp($"[darkorange3_1]Live from start skipped for {streamLabel}: segment URL pattern is not predictable.[/]");
                return;
            }

            var firstSegment = segments[0];
            var firstUrlParts = ParseSegmentUrl(firstSegment.Url);
            if (!TryParseSegmentNumber(firstUrlParts.FileNameWithoutExtension, out var firstNumber) || firstNumber <= 0)
                return;

            var allHasDatetime = segments.All(s => s.DateTime != null);
            var allName = segments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false)).ToList();
            var allSamePath = allName.Count > 1 && allName.Distinct().Count() == 1;
            var backfillSegmentDuration = streamSpec.Playlist?.TargetDuration is > 0
                ? streamSpec.Playlist.TargetDuration.Value
                : firstSegment.Duration;
            if (backfillSegmentDuration <= 0)
                backfillSegmentDuration = 1;

            var subTaskParallelism = LiveSubTaskSegmentDownloader.ResolveParallelism(request.ThreadCount);
            var subTaskHostMirrors = LiveSubTaskSegmentDownloader.ResolveMirrorHosts(request.LiveHostMirrors);
            var hasMirrors = subTaskHostMirrors.Length > 0;

            Logger.InfoMarkUp($"[darkorange3_1]Live from start: host mirrors for racing -> {string.Join(", ", subTaskHostMirrors.Select(m => m.EscapeMarkup()))}.[/]");

            var ctx = new LiveSubTaskDownloadContext(
                Template: firstSegment,
                TemplateUrlParts: firstUrlParts,
                TemplateNumber: firstNumber,
                SegmentDuration: backfillSegmentDuration,
                AllHasDatetime: allHasDatetime,
                AllSamePath: allSamePath,
                TmpDir: request.TmpDir,
                Extension: streamSpec.Extension ?? "clip",
                SpeedContainer: request.SpeedContainer,
                Headers: request.Headers,
                RetryCount: hasMirrors ? 0 : 1,
                HostMirrors: subTaskHostMirrors);

            var downloadCache = new ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)>();
            var liveSegmentTimeoutSec = ResolveLiveFromStartSegmentTimeoutSec(subTaskParallelism, ctx.SegmentDuration);

            Logger.InfoMarkUp($"[darkorange3_1]Live from start: locating earliest available segment before {firstNumber} for {streamLabel}.[/]");

            var locateResult = await LocateEarliestAvailableNumberAsync(ctx, firstNumber, 0, subTaskParallelism, downloadCache, backfillCts.Token);
            var upper = firstNumber - 1;

            if (locateResult.UseDescending)
            {
                await BackfillDescendingAsync(ctx, task, streamLabel, upper, 0, subTaskParallelism, request.FileDic, downloadCache, backfillCts, liveSegmentTimeoutSec);
                return;
            }

            var earliestNumber = locateResult.Earliest;
            if (earliestNumber == null || earliestNumber.Value > upper)
            {
                Logger.InfoMarkUp($"[darkorange3_1]Live from start: no earlier segment available before {firstNumber} for {streamLabel}.[/]");
                return;
            }

            if (locateResult.FuzzyWindowLower is { } fuzzyLower && locateResult.FuzzyWindowUpper is { } fuzzyUpper)
            {
                var fuzzyPlan = LiveFromStartPlanner.PlanFuzzyBoundary(fuzzyLower, fuzzyUpper);
                await BackfillFuzzyBoundaryAsync(
                    ctx,
                    task,
                    streamLabel,
                    fuzzyPlan.FillStart,
                    upper,
                    fuzzyPlan.ExploreFloor,
                    fuzzyPlan.ExploreUpper,
                    subTaskParallelism,
                    request.FileDic,
                    downloadCache,
                    backfillCts,
                    liveSegmentTimeoutSec);
                return;
            }

            var boundaryNumber = earliestNumber.Value - 1;
            Logger.InfoMarkUp($"[darkorange3_1]Live from start: earliest available segment is {earliestNumber.Value} for {streamLabel}; backfilling ascending {earliestNumber.Value} ~ {upper}.[/]");

            var ascending = await BackfillAscendingAsync(
                ctx,
                task,
                earliestNumber.Value,
                upper,
                subTaskParallelism,
                request.FileDic,
                downloadCache,
                backfillCts,
                liveSegmentTimeoutSec);

            LogAndFinalizeAscendingBackfill(
                task,
                streamLabel,
                request.FileDic,
                downloadCache,
                ascending,
                earliestNumber.Value,
                boundaryNumber,
                extraDownloadsStarted: 0,
                extraDiscardedCleanupCount: 0);
        }
        catch (Exception ex)
        {
            Logger.InfoMarkUp($"[darkorange3_1]Live from start download failed: {ex.Message.EscapeMarkup()}[/]");
        }
    }

    /// <summary>
    /// 模糊边界窗口的双 worker 回填：交界点归升序主填充；窗口内部倒序探索只尝试扩展，不能阻塞主填充完成。
    /// </summary>
    private async Task BackfillFuzzyBoundaryAsync(
        LiveSubTaskDownloadContext ctx,
        ProgressTask task,
        string streamLabel,
        long fillStart,
        long upper,
        long fuzzyLower,
        long fuzzyExploreUpper,
        int parallelism,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        CancellationTokenSource backfillCts,
        double segmentTimeoutSec)
    {
        Logger.InfoMarkUp($"[darkorange3_1]Live from start: fuzzy boundary window {fuzzyLower} ~ {fillStart} is small enough; backfilling ascending {fillStart} ~ {upper} while exploring descending {fuzzyLower} ~ {fuzzyExploreUpper}.[/]");

        FuzzyBoundaryExploreResult? explore = null;
        Task<FuzzyBoundaryExploreResult>? exploreTask = null;
        using var exploreCts = CancellationTokenSource.CreateLinkedTokenSource(backfillCts.Token);

        if (fuzzyExploreUpper >= fuzzyLower)
        {
            exploreTask = Task.Run(
                () => ExploreFuzzyBoundaryDescendingAsync(
                    ctx,
                    fuzzyExploreUpper,
                    fuzzyLower,
                    parallelism,
                    downloadCache,
                    exploreCts.Token,
                    segmentTimeoutSec),
                CancellationToken.None);
        }

        var ascending = await BackfillAscendingAsync(
            ctx,
            task,
            fillStart,
            upper,
            parallelism,
            fileDic,
            downloadCache,
            backfillCts,
            segmentTimeoutSec);

        if (exploreTask != null)
        {
            try
            {
                exploreCts.Cancel();
                explore = await exploreTask;
            }
            catch
            {
                // Fuzzy exploration is best-effort and must not fail the main ascending recovery.
            }
        }

        var extraDownloadsStarted = 0;
        var extraDiscardedCleanupCount = 0;
        var failedBoundary = fuzzyLower > 0 ? fuzzyLower - 1 : -1;

        if (explore != null)
        {
            extraDownloadsStarted = explore.DispatchCount;
            extraDiscardedCleanupCount = explore.DiscardedCleanupCount;
            var fillHasNoUnfillableGap = ascending.DiscardedExpiredNumbers.Count == 0;
            if (explore.Boundary is { } boundary)
            {
                failedBoundary = boundary;
                ascending.DiscardedExpiredNumbers.Add(boundary);
            }

            if (fillHasNoUnfillableGap)
            {
                foreach (var item in explore.ExploredSegments)
                {
                    fileDic[item.Segment] = item.Result;
                    ascending.DownloadedSegments.Add(item.Segment);
                    ascending.Committed.Add(item.Number);
                    updateProgress(() => task.MaxValue += 1);
                    task.Increment(1);
                    addRefreshedDuration(task.Id, item.Segment.Duration);
                }
            }
            else
            {
                foreach (var item in explore.ExploredSegments)
                {
                    if (!item.Reused && TryDeleteDownloadResult(item.Result))
                        extraDiscardedCleanupCount++;
                }
            }
        }

        LogAndFinalizeAscendingBackfill(
            task,
            streamLabel,
            fileDic,
            downloadCache,
            ascending,
            fuzzyLower,
            failedBoundary,
            extraDownloadsStarted,
            extraDiscardedCleanupCount);
    }

    /// <summary>
    /// 升序抢救主 DVR 区间；下载按升序派发并按号提交，单片使用 live-from-start 独立超时。
    /// </summary>
    private async Task<AscendingBackfillResult> BackfillAscendingAsync(
        LiveSubTaskDownloadContext ctx,
        ProgressTask task,
        long lower,
        long upper,
        int parallelism,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        CancellationTokenSource backfillCts,
        double segmentTimeoutSec)
    {
        var cancellationToken = backfillCts.Token;
        var nextDispatch = lower;
        var nextCommit = lower;
        var inFlight = new Dictionary<long, Task<(MediaSegment? Segment, DownloadResult? Result)>>();
        var resolved = new Dictionary<long, (MediaSegment? Segment, DownloadResult? Result)>();
        var downloadedSegments = new List<MediaSegment>();
        var committed = new HashSet<long>();
        var discardedExpiredNumbers = new List<long>();
        var dispatchCount = 0;
        var discardedCleanupCount = 0;

        while (!isStopping() && !cancellationToken.IsCancellationRequested && nextCommit <= upper)
        {
            while (inFlight.Count < parallelism && nextDispatch <= upper
                   && !isStopping() && !cancellationToken.IsCancellationRequested)
            {
                var number = nextDispatch;
                if (downloadCache.TryGetValue(number, out var cached))
                {
                    resolved[number] = (cached.Segment, cached.Result);
                }
                else
                {
                    inFlight[number] = RunWithLiveSegmentTimeoutAsync(
                        token => subTaskDownloader.DownloadAsync(ctx, number, token),
                        cancellationToken,
                        segmentTimeoutSec);
                    dispatchCount++;
                }
                nextDispatch++;
            }

            while (resolved.Remove(nextCommit, out var done))
            {
                if (done.Result is { Success: true } result && done.Segment != null)
                {
                    var segment = done.Segment;
                    fileDic[segment] = result;
                    downloadedSegments.Add(segment);
                    committed.Add(nextCommit);
                    updateProgress(() => task.MaxValue += 1);
                    task.Increment(1);
                    addRefreshedDuration(task.Id, segment.Duration);
                }
                else
                {
                    discardedExpiredNumbers.Add(nextCommit);
                    TryDeleteDownloadResult(done.Result);
                }
                nextCommit++;
            }

            if (nextCommit > upper)
                break;

            if (inFlight.Count == 0)
            {
                if (nextDispatch > upper)
                    break;
                continue;
            }

            var finished = await Task.WhenAny(inFlight.Values);
            var finishedNumber = inFlight.First(kv => kv.Value == finished).Key;
            inFlight.Remove(finishedNumber);
            resolved[finishedNumber] = await finished;
        }

        backfillCts.Cancel();
        foreach (var kv in inFlight)
        {
            try
            {
                var done = await kv.Value;
                if (done.Result is { Success: true } && !committed.Contains(kv.Key) && TryDeleteDownloadResult(done.Result))
                    discardedCleanupCount++;
            }
            catch
            {
                // Ignore canceled or failed cleanup.
            }
        }
        foreach (var kv in resolved)
        {
            if (kv.Value.Result is { Success: true } && !committed.Contains(kv.Key) && TryDeleteDownloadResult(kv.Value.Result))
                discardedCleanupCount++;
        }

        return new AscendingBackfillResult(
            downloadedSegments,
            committed,
            discardedExpiredNumbers,
            dispatchCount,
            discardedCleanupCount);
    }

    /// <summary>倒序探索混沌小窗口，仅收集可连续接到升序起点前一号的片段；是否接入由主升序结果决定。</summary>
    private async Task<FuzzyBoundaryExploreResult> ExploreFuzzyBoundaryDescendingAsync(
        LiveSubTaskDownloadContext ctx,
        long upper,
        long floor,
        int parallelism,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        CancellationToken cancellationToken,
        double segmentTimeoutSec)
    {
        var explored = new List<NumberedResolved>();

        var scan = await RunDescendingMirrorRaceScanAsync(
            ctx,
            upper,
            floor,
            parallelism,
            downloadCache,
            cancellationToken,
            cacheCompletedDownloads: false,
            segmentTimeoutSec: segmentTimeoutSec,
            onCommitSuccess: (number, segment, result, reused) =>
            {
                explored.Add(new NumberedResolved(number, segment, result, reused));
            });

        var discardedCleanupCount = 0;
        foreach (var kv in scan.InFlight)
        {
            try
            {
                var (_, result) = await kv.Value;
                if (result is { Success: true } && !scan.Committed.Contains(kv.Key) && TryDeleteDownloadResult(result))
                    discardedCleanupCount++;
            }
            catch
            {
                // Ignore canceled or failed cleanup.
            }
        }
        foreach (var kv in scan.Resolved)
        {
            if (kv.Value.Result is { Success: true } && !scan.Committed.Contains(kv.Key) && TryDeleteDownloadResult(kv.Value.Result))
                discardedCleanupCount++;
        }

        return new FuzzyBoundaryExploreResult(
            explored,
            scan.Boundary,
            scan.DispatchCount,
            discardedCleanupCount);
    }

    /// <summary>按最高不可填洞裁剪升序结果，输出 live-from-start 汇总，并清理所有未采用的缓存下载。</summary>
    private void LogAndFinalizeAscendingBackfill(
        ProgressTask task,
        string streamLabel,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        AscendingBackfillResult ascending,
        long initialFloor,
        long boundaryNumber,
        int extraDownloadsStarted,
        int extraDiscardedCleanupCount)
    {
        var downloadedSegments = ascending.DownloadedSegments;
        downloadedSegments.Sort((left, right) => left.Index.CompareTo(right.Index));

        var abandonedFragmentCount = 0;
        long? abandonedTailFloor = null;
        long? abandonedTailCeil = null;
        var abandonCeil = LiveSegmentGapPlanner.ResolveUnfillableHistoryCeil(
            downloadedSegments.Select(s => s.Index).ToList(), ascending.DiscardedExpiredNumbers);
        if (abandonCeil is { } highestGap)
        {
            var fragments = downloadedSegments.Where(s => s.Index <= highestGap).ToList();
            if (fragments.Count > 0)
            {
                var rolledBackDuration = 0d;
                foreach (var seg in fragments)
                {
                    if (fileDic.TryRemove(seg, out var res))
                        TryDeleteDownloadResult(res);
                    rolledBackDuration += seg.Duration;
                }

                updateProgress(() =>
                {
                    task.MaxValue = Math.Max(0, task.MaxValue - fragments.Count);
                    var completedCount = fileDic.Count(i => i.Value is { Success: true });
                    task.Value = Math.Min(task.MaxValue, completedCount);
                });
                addRefreshedDuration(task.Id, -rolledBackDuration);

                abandonedFragmentCount = fragments.Count;
                abandonedTailFloor = initialFloor;
                abandonedTailCeil = highestGap;
                downloadedSegments = downloadedSegments.Where(s => s.Index > highestGap).ToList();
            }
        }

        var discardedCleanupCount = ascending.DiscardedCleanupCount + extraDiscardedCleanupCount;
        foreach (var kv in downloadCache)
        {
            if (kv.Value.Result is { Success: true } && !ascending.Committed.Contains(kv.Key)
                && TryDeleteDownloadResult(kv.Value.Result))
            {
                discardedCleanupCount++;
            }
        }

        var acceptedRange = FormatContiguousIndexRanges(downloadedSegments.Select(s => s.Index).ToList());
        var earliestAvailable = downloadedSegments.Count > 0
            ? FormatSegmentLabel(downloadedSegments[0])
            : initialFloor.ToString(CultureInfo.InvariantCulture);
        var failedBoundary = boundaryNumber >= 0 ? boundaryNumber.ToString(CultureInfo.InvariantCulture) : "none";
        var startedDownloads = downloadCache.Count + ascending.DispatchCount + extraDownloadsStarted;
        var abandonedTailRange = abandonedTailCeil != null
            ? FormatRange(abandonedTailFloor!.Value, abandonedTailCeil.Value)
            : "none";
        Logger.InfoMarkUp($"[darkorange3_1]Live from start summary for {streamLabel}: accepted_total={downloadedSegments.Count}, earliest_available={earliestAvailable}, failed_boundary={failedBoundary}, accepted_range={acceptedRange}, boundary_probes={downloadCache.Count}, downloads_started={startedDownloads}, abandoned_tail={abandonedTailRange}, abandoned_fragments={abandonedFragmentCount}, unfillable_gaps={ascending.DiscardedExpiredNumbers.Count}, discarded_cleanup={discardedCleanupCount}.[/]");

        if (downloadedSegments.Count > 0)
        {
            Logger.InfoMarkUp($"[darkorange3_1]Live from start downloaded {downloadedSegments.Count} contiguous earlier segment(s) for {streamLabel}: {acceptedRange}.[/]");
        }
        if (abandonedTailCeil != null)
        {
            Logger.WarnMarkUp($"[darkorange3_1]Live from start: abandoned ragged DVR tail {abandonedTailRange} for {streamLabel} ({abandonedFragmentCount} downloaded fragment(s) discarded, {ascending.DiscardedExpiredNumbers.Count} segment(s) unavailable on selected live-from-start hosts) - cannot connect to live edge across unfillable gap(s).[/]");
        }
    }

    /// <summary>
    /// 降序回填：从 upper 向下逐号竞速下载，命中首个不可用号即停（认定已达 CDN 可用历史下边界）。
    /// 用于可用历史区间较浅、无需先二分定位的场景；成功分片随提交推进进度，收尾清理未采用结果并输出汇总。
    /// </summary>
    private async Task BackfillDescendingAsync(
        LiveSubTaskDownloadContext ctx,
        ProgressTask task,
        string streamLabel,
        long upper,
        long floor,
        int parallelism,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        CancellationTokenSource backfillCts,
        double segmentTimeoutSec)
    {
        var downloadedSegments = new List<MediaSegment>();
        var discardedCleanupCount = 0;

        Logger.InfoMarkUp($"[darkorange3_1]Live from start: descending backfill from {upper} downward for {streamLabel} (mirror race, stop at first unavailable).[/]");

        var scan = await RunDescendingMirrorRaceScanAsync(
            ctx,
            upper,
            floor,
            parallelism,
            downloadCache,
            backfillCts.Token,
            cacheCompletedDownloads: false,
            segmentTimeoutSec: segmentTimeoutSec,
            onCommitSuccess: (_, segment, result, _) =>
            {
                fileDic[segment] = result;
                downloadedSegments.Add(segment);
                updateProgress(() => task.MaxValue += 1);
                task.Increment(1);
                addRefreshedDuration(task.Id, segment.Duration);
            });

        backfillCts.Cancel();
        foreach (var kv in scan.InFlight)
        {
            try
            {
                var (_, result) = await kv.Value;
                if (result is { Success: true } && !scan.Committed.Contains(kv.Key) && TryDeleteDownloadResult(result))
                    discardedCleanupCount++;
            }
            catch
            {
                // Ignore canceled or failed cleanup.
            }
        }
        foreach (var kv in scan.Resolved)
        {
            if (kv.Value.Result is { Success: true } && !scan.Committed.Contains(kv.Key) && TryDeleteDownloadResult(kv.Value.Result))
                discardedCleanupCount++;
        }
        foreach (var kv in downloadCache)
        {
            if (kv.Value.Result is { Success: true } && !scan.Committed.Contains(kv.Key)
                && !scan.Resolved.ContainsKey(kv.Key) && !scan.InFlight.ContainsKey(kv.Key)
                && TryDeleteDownloadResult(kv.Value.Result))
            {
                discardedCleanupCount++;
            }
        }

        downloadedSegments.Sort((left, right) => left.Index.CompareTo(right.Index));
        var acceptedRange = FormatContiguousIndexRanges(downloadedSegments.Select(s => s.Index).ToList());
        var earliestAvailable = downloadedSegments.Count > 0
            ? FormatSegmentLabel(downloadedSegments[0])
            : "none";
        var failedBoundary = scan.Boundary != null ? scan.Boundary.Value.ToString(CultureInfo.InvariantCulture) : "none";
        var startedDownloads = scan.DispatchCount + scan.ReusedFromCache;
        Logger.InfoMarkUp($"[darkorange3_1]Live from start summary (descending) for {streamLabel}: accepted_total={downloadedSegments.Count}, earliest_available={earliestAvailable}, failed_boundary={failedBoundary}, accepted_range={acceptedRange}, boundary_probes={downloadCache.Count}, downloads_started={startedDownloads}, discarded_cleanup={discardedCleanupCount}.[/]");

        if (downloadedSegments.Count > 0)
        {
            Logger.InfoMarkUp($"[darkorange3_1]Live from start downloaded {downloadedSegments.Count} contiguous earlier segment(s) for {streamLabel}: {acceptedRange}.[/]");
        }
    }

    /// <summary>
    /// 降序镜像竞速扫描核心：在 [floor, upper] 内自高向低、受控并发下载并严格按号顺序提交，遇首个失败号即记为边界并停止。
    /// 既供降序回填直接使用，也被二分收尾的小窗口下载复用；通过 onCommitSuccess 回调让不同调用方各自处理提交副作用。
    /// </summary>
    private async Task<DescendingScanResult> RunDescendingMirrorRaceScanAsync(
        LiveSubTaskDownloadContext ctx,
        long upper,
        long floor,
        int parallelism,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        CancellationToken cancellationToken,
        bool cacheCompletedDownloads,
        double segmentTimeoutSec,
        Action<long, MediaSegment, DownloadResult, bool>? onCommitSuccess = null)
    {
        var nextDispatch = upper;
        var nextCommit = upper;
        var inFlight = new Dictionary<long, Task<(MediaSegment Segment, DownloadResult? Result)>>();
        var resolved = new Dictionary<long, DescendingResolved>();
        var committed = new HashSet<long>();
        var dispatchCount = 0;
        var reusedFromCache = 0;
        long? boundary = null;
        var stop = false;

        while (!isStopping() && !cancellationToken.IsCancellationRequested && !stop && nextCommit >= floor)
        {
            while (inFlight.Count < parallelism && nextDispatch >= floor
                   && !isStopping() && !cancellationToken.IsCancellationRequested)
            {
                var number = nextDispatch;
                if (downloadCache.TryGetValue(number, out var cached) && cached.Result is { Success: true })
                {
                    resolved[number] = new DescendingResolved(cached.Segment, cached.Result, true);
                    reusedFromCache++;
                }
                else
                {
                    inFlight[number] = RunWithLiveSegmentTimeoutAsync(
                        token => subTaskDownloader.DownloadRequiredAsync(ctx, number, token),
                        cancellationToken,
                        segmentTimeoutSec);
                    dispatchCount++;
                }
                nextDispatch--;
            }

            while (resolved.Remove(nextCommit, out var done))
            {
                if (done.Result is { Success: true } result && done.Segment != null)
                {
                    committed.Add(nextCommit);
                    onCommitSuccess?.Invoke(nextCommit, done.Segment, result, done.Reused);
                    nextCommit--;
                }
                else
                {
                    boundary = nextCommit;
                    TryDeleteDownloadResult(done.Result);
                    stop = true;
                    break;
                }
            }

            if (stop || nextCommit < floor)
                break;

            if (inFlight.Count == 0)
            {
                if (nextDispatch < floor)
                    break;
                continue;
            }

            var finished = await Task.WhenAny(inFlight.Values);
            var finishedNumber = inFlight.First(kv => kv.Value == finished).Key;
            inFlight.Remove(finishedNumber);
            var finishedResult = await finished;
            if (cacheCompletedDownloads)
                downloadCache[finishedNumber] = (finishedResult.Segment, finishedResult.Result);
            resolved[finishedNumber] = new DescendingResolved(finishedResult.Segment, finishedResult.Result, false);
        }

        return new DescendingScanResult(
            inFlight,
            resolved,
            committed,
            boundary,
            dispatchCount,
            reusedFromCache);
    }

    /// <summary>
    /// 定位 CDN 上仍可访问的最早分片号：先指数级向下探测圈定「最深可用 / 首个不可用」，再二分收窄。
    /// 窗口足够小时返回模糊窗口，由后续升序主填充和倒序探索并发处理，不在定位阶段等待精确边界。
    /// 返回最早可用号；当紧邻当前清单的前一号即不可用（无法连续衔接）时返回 null，
    /// 当可用区间过浅、直接降序回填更划算时通过 UseDescending=true 通知调用方改走降序策略。
    /// </summary>
    private async Task<LocateResult> LocateEarliestAvailableNumberAsync(
        LiveSubTaskDownloadContext ctx,
        long topAvailableSentinel,
        long floor,
        int parallelism,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> cache,
        CancellationToken cancellationToken)
    {
        var probeCount = 0;
        string? lastRequestUrl = null;

        async Task<DownloadResult?> ProbeAsync(long number, string phase)
        {
            probeCount++;
            Logger.InfoMarkUp($"[darkorange3_1]Live from start probe #{probeCount} ({phase}): checking segment {number}...[/]");
            var (_, result) = await ProbeNumberAsync(ctx, cache, number, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result?.RequestUrl))
                lastRequestUrl = result.RequestUrl;

            if (result is { Success: true })
            {
                var host = TryParseUrlAuthority(result.RequestUrl)?.Host;
                Logger.InfoMarkUp(host != null
                    ? $"[darkorange3_1]Live from start probe #{probeCount}: segment {number} [green]available[/] on [cyan]{host.EscapeMarkup()}[/].[/]"
                    : $"[darkorange3_1]Live from start probe #{probeCount}: segment {number} [green]available[/].[/]");
            }
            else
            {
                Logger.InfoMarkUp($"[darkorange3_1]Live from start probe #{probeCount}: segment {number} [red]unavailable[/].[/]");
            }
            return result;
        }

        long step = 1;
        var lastAvailable = topAvailableSentinel;
        long? firstUnavailable = null;
        var probe = topAvailableSentinel - 1;

        while (probe >= floor && !isStopping() && !cancellationToken.IsCancellationRequested)
        {
            var result = await ProbeAsync(probe, $"exponential, depth={topAvailableSentinel - probe}");
            if (result is { Success: true })
            {
                lastAvailable = probe;
                step *= 2;
                probe -= step;
            }
            else
            {
                firstUnavailable = probe;

                var failedDepth = topAvailableSentinel - probe;
                var lastAvailableDepth = topAvailableSentinel - lastAvailable;
                if (failedDepth == 1)
                {
                    Logger.InfoMarkUp($"[darkorange3_1]Live from start: segment {probe} immediately before current playlist is unavailable; no contiguous earlier segment can connect to live edge.[/]");
                    return new LocateResult(null, lastRequestUrl, false, null, null);
                }

                if (lastAvailableDepth <= 60)
                {
                    Logger.InfoMarkUp($"[darkorange3_1]Live from start: shallow available region (first failure at depth {failedDepth}, deepest confirmed at depth {lastAvailableDepth}); switching to descending backfill.[/]");
                    return new LocateResult(null, lastRequestUrl, true, null, null);
                }
                break;
            }
        }

        if (isStopping() || cancellationToken.IsCancellationRequested)
            return new LocateResult(null, lastRequestUrl, false, null, null);

        long lo;
        long hi;
        if (firstUnavailable == null)
        {
            if (lastAvailable >= topAvailableSentinel)
                return new LocateResult(null, lastRequestUrl, false, null, null);
            lo = floor;
            hi = lastAvailable;
        }
        else
        {
            if (lastAvailable >= topAvailableSentinel)
                return new LocateResult(null, lastRequestUrl, false, null, null);
            lo = firstUnavailable.Value + 1;
            hi = lastAvailable;
        }

        var finishWindowSegments = Math.Max(1, (int)Math.Ceiling(40d / ctx.SegmentDuration));
        if (lo < hi)
            Logger.InfoMarkUp($"[darkorange3_1]Live from start: narrowing earliest available within {lo} ~ {hi} (binary search until window <= {finishWindowSegments} segment(s), 40s / targetDuration={ctx.SegmentDuration.ToString("0.###", CultureInfo.InvariantCulture)}).[/]");

        while (lo < hi && hi - lo + 1 > finishWindowSegments
               && !isStopping() && !cancellationToken.IsCancellationRequested)
        {
            var mid = lo + (hi - lo) / 2;
            var result = await ProbeAsync(mid, $"binary, window {lo}~{hi}");
            if (result is { Success: true })
                hi = mid;
            else
                lo = mid + 1;
        }

        if (lo < hi && !isStopping() && !cancellationToken.IsCancellationRequested)
        {
            Logger.InfoMarkUp($"[darkorange3_1]Live from start: boundary window {lo} ~ {hi} is small enough; using fuzzy boundary and starting concurrent ascending fill from {hi}.[/]");
            return new LocateResult(hi, lastRequestUrl, false, lo, hi);
        }

        return isStopping() || cancellationToken.IsCancellationRequested
            ? new LocateResult(null, lastRequestUrl, false, null, null)
            : new LocateResult(lo, lastRequestUrl, false, null, null);
    }

    /// <summary>
    /// 探测某个号是否可下载（带缓存与短超时）：命中缓存直接返回；否则按号推算分片并以受限超时尝试下载，
    /// 结果（含失败）写入缓存供后续探测/回填复用，避免对同一号重复请求。
    /// </summary>
    private async Task<(MediaSegment? Segment, DownloadResult? Result)> ProbeNumberAsync(
        LiveSubTaskDownloadContext ctx,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> cache,
        long number,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(number, out var cached))
            return cached;

        var candidate = CreateFilledSegment(ctx.Template, ctx.TemplateUrlParts, number, ctx.TemplateNumber, ctx.SegmentDuration);
        if (candidate == null)
        {
            var miss = ((MediaSegment?)null, (DownloadResult?)null);
            cache[number] = miss;
            return miss;
        }

        var path = subTaskDownloader.GetSegmentPath(ctx, candidate);

        var probeTimeoutSec = ResolveProbeTimeoutSec();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(probeTimeoutSec));
        var (_, result) = await subTaskDownloader.DownloadRawAsync(candidate, path, ctx.SpeedContainer, ctx.Headers, timeoutCts.Token, ctx.RetryCount, ctx.HostMirrors);
        var entry = ((MediaSegment?)candidate, result);
        cache[number] = entry;
        return entry;
    }

    /// <summary>套用 live-from-start 的单片超时执行下载任务。</summary>
    private static async Task<T> RunWithLiveSegmentTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken,
        double timeoutSec)
    {
        using var timeoutCts = CreateTimeoutTokenSource(cancellationToken, timeoutSec);
        return await action(timeoutCts.Token);
    }

    /// <summary>探测超时仍保留短 deadline，但尊重全局 HTTP 超时作为硬上限。</summary>
    private double ResolveProbeTimeoutSec()
    {
        return LiveFromStartPlanner.ResolveProbeTimeoutSec(getWaitSec(), ResolveHttpRequestTimeoutSec());
    }

    /// <summary>live-from-start 历史分片下载超时：targetDuration * 并发 * 2，并限制在 [probeTimeout, --http-request-timeout] 内。</summary>
    private double ResolveLiveFromStartSegmentTimeoutSec(int parallelism, double targetDurationSeconds)
    {
        var td = double.IsFinite(targetDurationSeconds) && targetDurationSeconds > 0 ? targetDurationSeconds : 1d;
        var probeTimeoutSec = ResolveProbeTimeoutSec();
        var httpTimeoutSec = ResolveHttpRequestTimeoutSec();
        return LiveFromStartPlanner.ResolveSegmentTimeoutSec(parallelism, td, probeTimeoutSec, httpTimeoutSec);
    }

    /// <summary>读取全局 HTTP 超时；Program 会把 --http-request-timeout 写入 HTTPUtil.AppHttpClient.Timeout。</summary>
    private static double ResolveHttpRequestTimeoutSec()
    {
        var timeout = HTTPUtil.AppHttpClient.Timeout;
        if (timeout == Timeout.InfiniteTimeSpan)
            return double.PositiveInfinity;

        return timeout.TotalSeconds > 0 ? timeout.TotalSeconds : 100d;
    }

    private static CancellationTokenSource CreateTimeoutTokenSource(CancellationToken cancellationToken, double timeoutSec)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (double.IsFinite(timeoutSec) && timeoutSec > 0)
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        return timeoutCts;
    }

    /// <summary>把请求 URL 解析为 Uri 以便提取命中的主机名（用于日志展示在哪个镜像上探测成功）；非 http 或无效返回 null。</summary>
    private static Uri? TryParseUrlAuthority(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>删除一个未被采用的回填/探测结果对应的临时文件（如被裁掉的碎片、收尾时的在途结果）；清理失败静默忽略。</summary>
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
            // Ignore cleanup failures for uncommitted live-from-start segments.
        }

        return true;
    }

    /// <summary>为日志格式化分片标签（文件名 + Index），文件名缺失时退回用 Index。</summary>
    private static string FormatSegmentLabel(MediaSegment segment)
    {
        var fileName = OtherUtil.GetFileNameFromInput(segment.Url, false);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = segment.Index.ToString(CultureInfo.InvariantCulture);

        return $"{fileName.EscapeMarkup()}(index={segment.Index.ToString(CultureInfo.InvariantCulture)})";
    }

    /// <summary>把已升序的 Index 列表压缩成连续区间字符串（如 "10 ~ 14, 20"），用于汇总日志展示已接受范围。</summary>
    private static string FormatContiguousIndexRanges(IReadOnlyList<long> sortedIndices)
    {
        if (sortedIndices.Count == 0)
            return "none";

        var parts = new List<string>();
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

            parts.Add(FormatRange(start, prev));
            start = current;
            prev = current;
        }

        parts.Add(FormatRange(start, prev));
        return string.Join(", ", parts);
    }

    /// <summary>格式化单个区间：start==end 时只输出单值，否则输出 "start ~ end"。</summary>
    private static string FormatRange(long start, long end)
    {
        return start == end
            ? start.ToString(CultureInfo.InvariantCulture)
            : $"{start.ToString(CultureInfo.InvariantCulture)} ~ {end.ToString(CultureInfo.InvariantCulture)}";
    }
}
