using System.Collections.Concurrent;
using System.Globalization;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
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

    /// <summary>升序回填中一个号的解析结果：分片、下载结果，以及失败是否由 live-from-start 单片 deadline 触发。</summary>
    private readonly record struct AscendingResolved(MediaSegment? Segment, DownloadResult? Result, bool TimedOut, DateTimeOffset CompletedAt);

    /// <summary>带 live-from-start 单片 deadline 元数据的异步结果。</summary>
    private readonly record struct LiveSegmentTimedResult<T>(T Value, bool TimedOut, DateTimeOffset CompletedAt);

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

    private enum LiveFromStartStopReason
    {
        RangeCompleted,
        RecordingStopping,
        Cancelled,
        NoPendingWork,
        LoopExited,
        BoundaryFound,
        FloorReached
    }

    private static string LiveMarkUp(string key) => $"[darkorange3_1]{ResString.GetText(key)}[/]";

    private static string LiveText(string key, params object[] ps)
    {
        var text = ResString.GetText(key);
        foreach (var p in ps)
        {
            var index = text.IndexOf("{}", StringComparison.Ordinal);
            if (index < 0)
                break;

            var value = p is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : p?.ToString() ?? string.Empty;
            text = text[..index] + value + text[(index + 2)..];
        }

        return text;
    }

    private static string FormatStopReason(LiveFromStartStopReason reason)
    {
        var key = reason switch
        {
            LiveFromStartStopReason.RangeCompleted => "liveLogStopRangeCompleted",
            LiveFromStartStopReason.RecordingStopping => "liveLogStopRecordingStopping",
            LiveFromStartStopReason.Cancelled => "liveLogStopCancelled",
            LiveFromStartStopReason.NoPendingWork => "liveLogStopNoPendingWork",
            LiveFromStartStopReason.LoopExited => "liveLogStopLoopExited",
            LiveFromStartStopReason.BoundaryFound => "liveLogStopBoundaryFound",
            LiveFromStartStopReason.FloorReached => "liveLogStopFloorReached",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
        return ResString.GetText(key);
    }

    private LiveFromStartStopReason ResolveAscendingStopReason(long nextCommit, long upper, int inFlightCount, long nextDispatch, CancellationToken cancellationToken)
    {
        if (nextCommit > upper)
            return LiveFromStartStopReason.RangeCompleted;
        if (isStopping())
            return LiveFromStartStopReason.RecordingStopping;
        if (cancellationToken.IsCancellationRequested)
            return LiveFromStartStopReason.Cancelled;
        if (inFlightCount == 0 && nextDispatch > upper)
            return LiveFromStartStopReason.NoPendingWork;

        return LiveFromStartStopReason.LoopExited;
    }

    private LiveFromStartStopReason ResolveDescendingBackfillStopReason(long? boundary, CancellationToken cancellationToken)
    {
        if (boundary != null)
            return LiveFromStartStopReason.BoundaryFound;
        if (isStopping())
            return LiveFromStartStopReason.RecordingStopping;
        if (cancellationToken.IsCancellationRequested)
            return LiveFromStartStopReason.Cancelled;

        return LiveFromStartStopReason.FloorReached;
    }

    private LiveFromStartStopReason ResolveDescendingScanStopReason(long? boundary, long nextCommit, long floor, CancellationToken cancellationToken)
    {
        if (boundary != null)
            return LiveFromStartStopReason.BoundaryFound;
        if (isStopping())
            return LiveFromStartStopReason.RecordingStopping;
        if (cancellationToken.IsCancellationRequested)
            return LiveFromStartStopReason.Cancelled;
        if (nextCommit < floor)
            return LiveFromStartStopReason.FloorReached;

        return LiveFromStartStopReason.LoopExited;
    }

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
            var streamLabel = streamSpec.ToShortShortString().EscapeMarkup();
            var task = request.Task;
            var segments = streamSpec.Playlist?.MediaParts[0].MediaSegments.ToList();
            if (segments == null || segments.Count == 0)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartSkippedNoTemplate"), streamLabel);
                return;
            }

            if (!request.PredictableSegmentUrlPattern)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartSkippedUnpredictable"), streamLabel);
                return;
            }

            var firstSegment = segments[0];
            var firstUrlParts = ParseSegmentUrl(firstSegment.Url);
            if (!TryParseSegmentNumber(firstUrlParts.FileNameWithoutExtension, out var firstNumber) || firstNumber <= 0)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartSkippedInvalidFirstNumber"), streamLabel, firstUrlParts.FileNameWithoutExtension.EscapeMarkup());
                return;
            }

            var allHasDatetime = segments.All(s => s.DateTime != null);
            var allName = segments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false)).ToList();
            var allSamePath = allName.Count > 1 && allName.Distinct().Count() == 1;
            var backfillSegmentDuration = streamSpec.Playlist?.TargetDuration is > 0
                ? streamSpec.Playlist.TargetDuration.Value
                : firstSegment.Duration;
            if (backfillSegmentDuration <= 0)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartDurationFallback"), streamLabel);
                backfillSegmentDuration = 1;
            }

            var subTaskParallelism = LiveSubTaskSegmentDownloader.ResolveParallelism(request.ThreadCount);
            var subTaskHostMirrors = LiveSubTaskSegmentDownloader.ResolveMirrorHosts(request.LiveHostMirrors);
            var hasMirrors = subTaskHostMirrors.Length > 0;

            if (hasMirrors)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartHostMirrorRace"), streamLabel, string.Join(", ", subTaskHostMirrors.Select(m => m.EscapeMarkup())));
            }
            else
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartHostMirrorOriginal"), streamLabel);
            }

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
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartTimeoutDecision"), streamLabel, subTaskParallelism, FormatDurationSeconds(ctx.SegmentDuration), FormatDurationSeconds(ResolveProbeTimeoutSec()), FormatDurationSeconds(liveSegmentTimeoutSec));

            Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocatingEarliest"), firstNumber, streamLabel);

            var locateResult = await LocateEarliestAvailableNumberAsync(ctx, firstNumber, 0, subTaskParallelism, downloadCache, backfillCts.Token);
            var upper = firstNumber - 1;

            if (locateResult.UseDescending)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartStrategyDescending"), streamLabel, FormatRange(0, upper));
                await BackfillDescendingAsync(ctx, task, streamLabel, upper, 0, subTaskParallelism, request.FileDic, downloadCache, backfillCts, liveSegmentTimeoutSec);
                return;
            }

            var earliestNumber = locateResult.Earliest;
            if (earliestNumber == null || earliestNumber.Value > upper)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartNoEarlierSegment"), firstNumber, streamLabel);
                return;
            }

            if (locateResult.FuzzyWindowLower is { } fuzzyLower && locateResult.FuzzyWindowUpper is { } fuzzyUpper)
            {
                var fuzzyPlan = LiveFromStartPlanner.PlanFuzzyBoundary(fuzzyLower, fuzzyUpper);
                var exploreRange = fuzzyPlan.HasExploreRange
                    ? FormatRange(fuzzyPlan.ExploreFloor, fuzzyPlan.ExploreUpper)
                    : ResString.GetText("liveLogNone");
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartStrategyFuzzy"), streamLabel, FormatRange(fuzzyPlan.FillStart, upper), exploreRange);
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
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartStrategyAscending"), streamLabel, earliestNumber.Value, FormatRange(earliestNumber.Value, upper));

            var ascending = await BackfillAscendingAsync(
                ctx,
                task,
                streamLabel,
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
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartDownloadFailed"), ex.Message.EscapeMarkup());
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
        var exploreRange = fuzzyExploreUpper >= fuzzyLower
            ? FormatRange(fuzzyLower, fuzzyExploreUpper)
            : ResString.GetText("liveLogNone");
        Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyBoundaryDecision"), streamLabel, FormatRange(fuzzyLower, fillStart), FormatRange(fillStart, upper), exploreRange);

        FuzzyBoundaryExploreResult? explore = null;
        Task<FuzzyBoundaryExploreResult>? exploreTask = null;
        using var exploreCts = CancellationTokenSource.CreateLinkedTokenSource(backfillCts.Token);
        var exploreCancelledForInitialFastForward = false;

        if (fuzzyExploreUpper >= fuzzyLower)
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyStartExplore"), streamLabel, FormatRange(fuzzyLower, fuzzyExploreUpper));
            exploreTask = Task.Run(
                () => ExploreFuzzyBoundaryDescendingAsync(
                    ctx,
                    streamLabel,
                    fuzzyExploreUpper,
                    fuzzyLower,
                    parallelism,
                    downloadCache,
                    exploreCts.Token,
                    segmentTimeoutSec),
                CancellationToken.None);
        }
        else
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyNoExploreRange"), streamLabel);
        }

        var ascending = await BackfillAscendingAsync(
            ctx,
            task,
            streamLabel,
            fillStart,
            upper,
            parallelism,
            fileDic,
            downloadCache,
            backfillCts,
            segmentTimeoutSec,
            onInitialTimeoutFastForward: () =>
            {
                if (exploreCts.IsCancellationRequested)
                    return;

                exploreCancelledForInitialFastForward = true;
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyStopExploreInitialFastForward"), streamLabel);
                exploreCts.Cancel();
            });

        if (exploreTask != null)
        {
            try
            {
                if (!exploreCancelledForInitialFastForward)
                {
                    Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyStopExplore"), streamLabel);
                    exploreCts.Cancel();
                }
                explore = await exploreTask;
            }
            catch (Exception ex)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyIgnoreExploreFailure"), streamLabel, ex.Message.EscapeMarkup());
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
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyBoundaryFound"), streamLabel, boundary);
            }

            if (fillHasNoUnfillableGap)
            {
                var adoptedRange = FormatContiguousIndexRanges(explore.ExploredSegments.Select(s => s.Number).OrderBy(n => n).ToList());
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyAdopt"), streamLabel, explore.ExploredSegments.Count, adoptedRange);
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
                var discardedRange = FormatContiguousIndexRanges(explore.ExploredSegments.Select(s => s.Number).OrderBy(n => n).ToList());
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyDiscard"), streamLabel, ascending.DiscardedExpiredNumbers.Count, explore.ExploredSegments.Count, discardedRange);
                foreach (var item in explore.ExploredSegments)
                {
                    if (!item.Reused && TryDeleteDownloadResult(item.Result))
                        extraDiscardedCleanupCount++;
                }
            }
        }
        else
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyNoExploreResult"), streamLabel);
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
        string streamLabel,
        long lower,
        long upper,
        int parallelism,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        CancellationTokenSource backfillCts,
        double segmentTimeoutSec,
        Action? onInitialTimeoutFastForward = null)
    {
        var cancellationToken = backfillCts.Token;
        var nextDispatch = lower;
        var nextCommit = lower;
        var inFlight = new Dictionary<long, Task<LiveSegmentTimedResult<(MediaSegment? Segment, DownloadResult? Result)>>>();
        var resolved = new Dictionary<long, AscendingResolved>();
        var downloadedSegments = new List<MediaSegment>();
        var committed = new HashSet<long>();
        var discardedExpiredNumbers = new List<long>();
        var dispatchCount = 0;
        var cacheReuseCount = 0;
        var discardedCleanupCount = 0;
        var initialLogicalBatchEnd = LiveFromStartPlanner.ResolveInitialAscendingLogicalBatchEnd(lower, parallelism);
        var initialActualNumbers = new HashSet<long>();
        var initialActualResults = new Dictionary<long, AscendingResolved>();
        long? initialAnchorNumber = null;
        var initialFastForwardPending = true;
        var initialFastForwardEvaluated = false;

        Logger.InfoMarkUp(LiveMarkUp("liveFromStartAscendingStart"), streamLabel, FormatRange(lower, upper), parallelism, FormatDurationSeconds(segmentTimeoutSec));

        while (!isStopping() && !cancellationToken.IsCancellationRequested && nextCommit <= upper)
        {
            while (inFlight.Count < parallelism && nextDispatch <= upper
                   && !ShouldHoldInitialFastForwardDispatch()
                   && !isStopping() && !cancellationToken.IsCancellationRequested)
            {
                var number = nextDispatch;
                var isInitialLogicalSlot = initialFastForwardPending && number <= initialLogicalBatchEnd;
                if (downloadCache.TryGetValue(number, out var cached))
                {
                    resolved[number] = new AscendingResolved(cached.Segment, cached.Result, TimedOut: false, DateTimeOffset.UtcNow);
                    cacheReuseCount++;
                }
                else
                {
                    inFlight[number] = RunWithLiveSegmentTimeoutStateAsync(
                        token => subTaskDownloader.DownloadAsync(ctx, number, token),
                        cancellationToken,
                        segmentTimeoutSec);
                    if (isInitialLogicalSlot)
                    {
                        initialActualNumbers.Add(number);
                        initialAnchorNumber ??= number;
                    }
                    dispatchCount++;
                }
                nextDispatch++;
            }

            if (initialFastForwardPending && nextDispatch > initialLogicalBatchEnd && initialActualNumbers.Count == 0)
                initialFastForwardPending = false;

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
                    Logger.InfoMarkUp(LiveMarkUp("liveFromStartAscendingUnavailable"), streamLabel, nextCommit);
                    TryDeleteDownloadResult(done.Result);
                }
                nextCommit++;
            }

            TryApplyInitialTimeoutFastForward();

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
            var timedResult = await finished;
            var doneResult = timedResult.Value;
            var ascendingResolved = new AscendingResolved(doneResult.Segment, doneResult.Result, timedResult.TimedOut, timedResult.CompletedAt);
            resolved[finishedNumber] = ascendingResolved;
            if (initialActualNumbers.Contains(finishedNumber))
                initialActualResults[finishedNumber] = ascendingResolved;
        }

        var stopReason = ResolveAscendingStopReason(nextCommit, upper, inFlight.Count, nextDispatch, cancellationToken);
        Logger.InfoMarkUp(LiveMarkUp("liveFromStartAscendingStop"), streamLabel, FormatStopReason(stopReason), committed.Count, discardedExpiredNumbers.Count, cacheReuseCount, inFlight.Count, resolved.Count);

        if (inFlight.Count > 0 || resolved.Count > 0)
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartAscendingCleanup"), streamLabel, inFlight.Count, resolved.Count);
        }
        backfillCts.Cancel();
        foreach (var kv in inFlight)
        {
            try
            {
                var done = (await kv.Value).Value;
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

        bool ShouldHoldInitialFastForwardDispatch()
        {
            return initialFastForwardPending && nextDispatch > initialLogicalBatchEnd;
        }

        void TryApplyInitialTimeoutFastForward()
        {
            if (!initialFastForwardPending || initialFastForwardEvaluated || initialAnchorNumber == null
                || initialActualResults.Count < initialActualNumbers.Count)
            {
                return;
            }

            initialFastForwardPending = false;
            initialFastForwardEvaluated = true;

            if (!initialActualResults.TryGetValue(initialAnchorNumber.Value, out var anchor)
                || anchor.Result is { Success: true }
                || !anchor.TimedOut
                || anchor.Segment == null)
            {
                return;
            }

            var timeoutSpan = BuildInitialTimeoutSegmentSpan(initialAnchorNumber.Value, anchor.CompletedAt);
            var timeoutSegments = timeoutSpan.Select(item => item.Segment).ToList();
            if (!LiveFromStartPlanner.TryResolveInitialTimeoutFastForwardLatestFailure(timeoutSegments, out var latestFailure))
                return;

            var completionSpan = timeoutSpan.Count > 0
                ? timeoutSpan.Max(item => item.CompletedAt) - timeoutSpan.Min(item => item.CompletedAt)
                : TimeSpan.Zero;
            var step = LiveFromStartPlanner.ResolveInitialTimeoutFastForwardStep(segmentTimeoutSec, ctx.SegmentDuration);
            var desiredJumpStart = LiveFromStartPlanner.ResolveInitialTimeoutFastForwardStart(latestFailure, step);
            var upperExclusive = upper == long.MaxValue ? long.MaxValue : upper + 1;
            var jumpStart = Math.Min(desiredJumpStart, upperExclusive);
            if (jumpStart <= nextCommit)
                return;

            var skippedStart = nextCommit;
            var skippedEnd = jumpStart - 1;
            for (var number = skippedStart; number <= skippedEnd; number++)
                discardedExpiredNumbers.Add(number);

            nextCommit = jumpStart;
            if (nextDispatch < jumpStart)
                nextDispatch = jumpStart;

            onInitialTimeoutFastForward?.Invoke();

            Logger.InfoMarkUp(
                LiveMarkUp("liveFromStartAscendingInitialTimeoutFastForward"),
                streamLabel,
                FormatRange(timeoutSegments[0].Index, latestFailure),
                FormatDurationSeconds(segmentTimeoutSec),
                FormatDurationSeconds(completionSpan.TotalSeconds),
                FormatDurationSeconds(ctx.SegmentDuration),
                step,
                FormatRange(skippedStart, skippedEnd),
                jumpStart);
        }

        List<(MediaSegment Segment, DateTimeOffset CompletedAt)> BuildInitialTimeoutSegmentSpan(long anchorNumber, DateTimeOffset anchorCompletedAt)
        {
            var selected = new List<(MediaSegment Segment, DateTimeOffset CompletedAt)>();
            var minCompletedAt = anchorCompletedAt;
            var maxCompletedAt = anchorCompletedAt;

            for (var number = anchorNumber; number <= upper; number++)
            {
                if (!initialActualResults.TryGetValue(number, out var done)
                    || done.Result is { Success: true }
                    || !done.TimedOut
                    || done.Segment == null)
                {
                    break;
                }

                var nextMinCompletedAt = done.CompletedAt < minCompletedAt ? done.CompletedAt : minCompletedAt;
                var nextMaxCompletedAt = done.CompletedAt > maxCompletedAt ? done.CompletedAt : maxCompletedAt;
                if (nextMaxCompletedAt - nextMinCompletedAt > LiveFromStartPlanner.InitialTimeoutBatchCompletionWindow)
                    break;

                selected.Add((done.Segment, done.CompletedAt));
                minCompletedAt = nextMinCompletedAt;
                maxCompletedAt = nextMaxCompletedAt;
                if (number == long.MaxValue)
                    break;
            }

            return selected;
        }
    }

    /// <summary>倒序探索混沌小窗口，仅收集可连续接到升序起点前一号的片段；是否接入由主升序结果决定。</summary>
    private async Task<FuzzyBoundaryExploreResult> ExploreFuzzyBoundaryDescendingAsync(
        LiveSubTaskDownloadContext ctx,
        string streamLabel,
        long upper,
        long floor,
        int parallelism,
        ConcurrentDictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        CancellationToken cancellationToken,
        double segmentTimeoutSec)
    {
        var explored = new List<NumberedResolved>();

        Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyExploreStart"), streamLabel, FormatRange(floor, upper), parallelism, FormatDurationSeconds(segmentTimeoutSec));

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

        Logger.InfoMarkUp(LiveMarkUp("liveFromStartFuzzyExploreSummary"), streamLabel, explored.Count, scan.Boundary?.ToString(CultureInfo.InvariantCulture) ?? ResString.GetText("liveLogNone"), scan.DispatchCount + scan.ReusedFromCache, discardedCleanupCount);

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
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartFinalClipGap"), streamLabel, highestGap);
            var fragments = downloadedSegments.Where(s => s.Index <= highestGap).ToList();
            if (fragments.Count > 0)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartFinalClipAbandon"), streamLabel, fragments.Count, highestGap);
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
            else
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartFinalClipNoFragments"), streamLabel, highestGap);
            }
        }
        else
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartFinalClipNoGap"), streamLabel);
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
        var failedBoundary = boundaryNumber >= 0 ? boundaryNumber.ToString(CultureInfo.InvariantCulture) : ResString.GetText("liveLogNone");
        var startedDownloads = downloadCache.Count + ascending.DispatchCount + extraDownloadsStarted;
        var abandonedTailRange = abandonedTailCeil != null
            ? FormatRange(abandonedTailFloor!.Value, abandonedTailCeil.Value)
            : ResString.GetText("liveLogNone");
        Logger.InfoMarkUp(LiveMarkUp("liveFromStartSummary"), streamLabel, downloadedSegments.Count, earliestAvailable, failedBoundary, acceptedRange, downloadCache.Count, startedDownloads, abandonedTailRange, abandonedFragmentCount, ascending.DiscardedExpiredNumbers.Count, discardedCleanupCount);

        if (downloadedSegments.Count > 0)
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartDownloaded"), downloadedSegments.Count, streamLabel, acceptedRange);
        }
        else
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartNoAcceptedAfterClip"), streamLabel);
        }
        if (abandonedTailCeil != null)
        {
            Logger.WarnMarkUp(LiveMarkUp("liveFromStartAbandonedTail"), abandonedTailRange, streamLabel, abandonedFragmentCount, ascending.DiscardedExpiredNumbers.Count);
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

        Logger.InfoMarkUp(LiveMarkUp("liveFromStartDescendingStart"), streamLabel, FormatRange(floor, upper), parallelism, FormatDurationSeconds(segmentTimeoutSec));

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

        var descendingStopReason = ResolveDescendingBackfillStopReason(scan.Boundary, backfillCts.Token);
        Logger.InfoMarkUp(LiveMarkUp("liveFromStartDescendingStop"), streamLabel, FormatStopReason(descendingStopReason), scan.Committed.Count, scan.Boundary?.ToString(CultureInfo.InvariantCulture) ?? ResString.GetText("liveLogNone"), scan.InFlight.Count, scan.Resolved.Count);

        if (scan.InFlight.Count > 0 || scan.Resolved.Count > 0 || downloadCache.Count > 0)
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartDescendingCleanup"), streamLabel, scan.InFlight.Count, scan.Resolved.Count, downloadCache.Count);
        }
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
            : ResString.GetText("liveLogNone");
        var failedBoundary = scan.Boundary != null ? scan.Boundary.Value.ToString(CultureInfo.InvariantCulture) : ResString.GetText("liveLogNone");
        var startedDownloads = scan.DispatchCount + scan.ReusedFromCache;
        Logger.InfoMarkUp(LiveMarkUp("liveFromStartDescendingSummary"), streamLabel, downloadedSegments.Count, earliestAvailable, failedBoundary, acceptedRange, downloadCache.Count, startedDownloads, discardedCleanupCount);

        if (downloadedSegments.Count > 0)
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartDownloaded"), downloadedSegments.Count, streamLabel, acceptedRange);
        }
        else
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartDescendingNoAccepted"), streamLabel);
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
                    Logger.InfoMarkUp(LiveMarkUp("liveFromStartDescendingScanUnavailable"), nextCommit);
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

        var scanStopReason = ResolveDescendingScanStopReason(boundary, nextCommit, floor, cancellationToken);
        Logger.InfoMarkUp(LiveMarkUp("liveFromStartDescendingScanSummary"), FormatRange(floor, upper), FormatStopReason(scanStopReason), committed.Count, dispatchCount, reusedFromCache, inFlight.Count, resolved.Count);

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

        var probeFloor = LiveFromStartPlanner.ResolveProbeFloor(topAvailableSentinel, floor);

        Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateStart"), topAvailableSentinel, probeFloor, parallelism);

        async Task<DownloadResult?> ProbeAsync(long number, string phase)
        {
            probeCount++;
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartProbeChecking"), probeCount, phase, number);
            var (_, result) = await ProbeNumberAsync(ctx, cache, number, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result?.RequestUrl))
                lastRequestUrl = result.RequestUrl;

            if (result is { Success: true })
            {
                var host = TryParseUrlAuthority(result.RequestUrl)?.Host;
                if (host != null)
                    Logger.InfoMarkUp(LiveMarkUp("liveFromStartProbeAvailableOnHost"), probeCount, number, host.EscapeMarkup());
                else
                    Logger.InfoMarkUp(LiveMarkUp("liveFromStartProbeAvailable"), probeCount, number);
            }
            else
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartProbeUnavailable"), probeCount, number);
            }
            return result;
        }

        long step = 1;
        var lastAvailable = topAvailableSentinel;
        long? firstUnavailable = null;
        var probe = topAvailableSentinel - 1;

        while (probe >= probeFloor && !isStopping() && !cancellationToken.IsCancellationRequested)
        {
            var result = await ProbeAsync(probe, LiveText("liveFromStartProbePhaseExponential", topAvailableSentinel - probe));
            if (result is { Success: true })
            {
                lastAvailable = probe;
                step *= 2;
                probe -= step;
                var nextProbe = probe >= probeFloor
                    ? probe.ToString(CultureInfo.InvariantCulture)
                    : ResString.GetText("liveLogBelowFloor");
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateExpand"), lastAvailable, step, nextProbe);
            }
            else
            {
                firstUnavailable = probe;

                var failedDepth = topAvailableSentinel - probe;
                var lastAvailableDepth = topAvailableSentinel - lastAvailable;
                if (failedDepth == 1)
                {
                    Logger.InfoMarkUp(LiveMarkUp("liveFromStartImmediateUnavailable"), probe);
                    return new LocateResult(null, lastRequestUrl, false, null, null);
                }

                if (lastAvailableDepth <= 60)
                {
                    Logger.InfoMarkUp(LiveMarkUp("liveFromStartShallowAvailable"), failedDepth, lastAvailableDepth);
                    return new LocateResult(null, lastRequestUrl, true, null, null);
                }
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateFirstUnavailable"), firstUnavailable.Value, firstUnavailable.Value + 1, lastAvailable);
                break;
            }
        }

        if (isStopping() || cancellationToken.IsCancellationRequested)
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateCancelledBeforeWindow"));
            return new LocateResult(null, lastRequestUrl, false, null, null);
        }

        long lo;
        long hi;
        if (firstUnavailable == null)
        {
            if (lastAvailable >= topAvailableSentinel)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateNoEarlierConfirmed"), topAvailableSentinel);
                return new LocateResult(null, lastRequestUrl, false, null, null);
            }
            lo = probeFloor;
            hi = lastAvailable;
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateReachedFloor"), FormatRange(lo, hi));
        }
        else
        {
            if (lastAvailable >= topAvailableSentinel)
            {
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateUnavailableBeforeAvailable"));
                return new LocateResult(null, lastRequestUrl, false, null, null);
            }
            lo = firstUnavailable.Value + 1;
            hi = lastAvailable;
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateBoundedWindow"), FormatRange(lo, hi), firstUnavailable.Value);
        }

        var finishWindowSegments = Math.Max(1, (int)Math.Ceiling(40d / ctx.SegmentDuration));
        if (lo < hi)
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateNarrowing"), FormatRange(lo, hi), finishWindowSegments, ctx.SegmentDuration.ToString("0.###", CultureInfo.InvariantCulture));
        else
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateExactNoBinary"), lo);

        while (lo < hi && hi - lo + 1 > finishWindowSegments
               && !isStopping() && !cancellationToken.IsCancellationRequested)
        {
            var mid = lo + (hi - lo) / 2;
            var result = await ProbeAsync(mid, LiveText("liveFromStartProbePhaseBinary", lo, hi));
            if (result is { Success: true })
            {
                hi = mid;
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateBinaryAvailable"), mid, FormatRange(lo, hi));
            }
            else
            {
                lo = mid + 1;
                Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateBinaryUnavailable"), mid, FormatRange(lo, hi));
            }
        }

        if (lo < hi && !isStopping() && !cancellationToken.IsCancellationRequested)
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateFuzzyWindow"), FormatRange(lo, hi), hi);
            return new LocateResult(hi, lastRequestUrl, false, lo, hi);
        }

        if (isStopping() || cancellationToken.IsCancellationRequested)
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateCancelledNarrowing"));
            return new LocateResult(null, lastRequestUrl, false, null, null);
        }

        Logger.InfoMarkUp(LiveMarkUp("liveFromStartLocateExactSelected"), lo);
        return new LocateResult(lo, lastRequestUrl, false, null, null);
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
        {
            var cachedAvailability = cached.Result is { Success: true }
                ? ResString.GetText("liveLogAvailableMarkup")
                : ResString.GetText("liveLogUnavailableMarkup");
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartProbeCacheReuse"), number, cachedAvailability);
            return cached;
        }

        var candidate = CreateFilledSegment(ctx.Template, ctx.TemplateUrlParts, number, ctx.TemplateNumber, ctx.SegmentDuration);
        if (candidate == null)
        {
            Logger.InfoMarkUp(LiveMarkUp("liveFromStartProbeCannotGenerate"), number);
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

    /// <summary>套用 live-from-start 的单片超时，并返回本次失败是否由该 deadline 触发。</summary>
    private static async Task<LiveSegmentTimedResult<T>> RunWithLiveSegmentTimeoutStateAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken,
        double timeoutSec)
    {
        using var timeoutCts = CreateTimeoutTokenSource(cancellationToken, timeoutSec);
        var value = await action(timeoutCts.Token);
        var timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
        return new LiveSegmentTimedResult<T>(value, timedOut, DateTimeOffset.UtcNow);
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

    /// <summary>为日志格式化秒数；无穷超时显示为 infinite，普通秒数保留最多三位小数。</summary>
    private static string FormatDurationSeconds(double seconds)
    {
        if (double.IsPositiveInfinity(seconds))
            return ResString.GetText("liveLogInfinite");

        if (!double.IsFinite(seconds))
            return seconds.ToString(CultureInfo.InvariantCulture);

        return $"{seconds.ToString("0.###", CultureInfo.InvariantCulture)}s";
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
            return ResString.GetText("liveLogNone");

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
