using N_m3u8DL_RE.Common.Util;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Common.Util;

public class LiveSegmentGapPlannerTests
{
    // 回归：本次事故序列 711 -> 735（中间 712~734 丢失）。
    // 旧逻辑因 LastFileName/MaxIndex 状态错位漏检；新逻辑必须把 712~734 识别为缺口并交由待补队列补齐。
    [Fact]
    public void Plan_BigJumpAfterReconnect_DetectsMissingRange()
    {
        var plan = LiveSegmentGapPlanner.Plan(highWaterMark: 711, playlistNumbers: [735]);

        plan.FreshNumbers.ShouldBe([735]);
        plan.NewHighWaterMark.ShouldBe(735);
        plan.GapRanges.Count.ShouldBe(1);
        plan.GapRanges[0].Start.ShouldBe(712);
        plan.GapRanges[0].End.ShouldBe(734);
        plan.GapRanges[0].Count.ShouldBe(23);
    }

    // 回归：完整 682 -> 704 -> 711 -> 735 推进序列，断言每步缺口检测正确、不丢号。
    [Fact]
    public void Plan_FullIncidentSequence_NoSilentLoss()
    {
        // 第一段：高水位 682，playlist 跳到 705（首片）-> 缺口 683~704（22 片）。
        var step1 = LiveSegmentGapPlanner.Plan(682, [705]);
        step1.GapRanges.Count.ShouldBe(1);
        step1.GapRanges[0].Start.ShouldBe(683);
        step1.GapRanges[0].End.ShouldBe(704);
        step1.GapRanges[0].Count.ShouldBe(22);
        step1.NewHighWaterMark.ShouldBe(705);

        // 中段：705 -> 711 逐步推进（无洞）。
        var step2 = LiveSegmentGapPlanner.Plan(705, [706, 707, 708, 709, 710, 711]);
        step2.GapRanges.ShouldBeEmpty();
        step2.FreshNumbers.ShouldBe([706, 707, 708, 709, 710, 711]);
        step2.NewHighWaterMark.ShouldBe(711);

        // 关键回归：711 -> 735，712~734 必须被识别为缺口（旧逻辑此处静默丢弃）。
        var step3 = LiveSegmentGapPlanner.Plan(711, [735]);
        step3.GapRanges.Count.ShouldBe(1);
        step3.GapRanges[0].Start.ShouldBe(712);
        step3.GapRanges[0].End.ShouldBe(734);
        step3.NewHighWaterMark.ShouldBe(735);
    }

    [Fact]
    public void Plan_DropsStaleOrOverlappingSegmentsAtOrBelowHighWaterMark()
    {
        // 断网恢复中 CDN 返回回退/重叠窗口（含 <= 高水位号 的旧号）：旧号必须被丢弃，不产生状态错位。
        var plan = LiveSegmentGapPlanner.Plan(highWaterMark: 711, playlistNumbers: [705, 706, 707, 711]);

        plan.FreshNumbers.ShouldBeEmpty();
        plan.GapRanges.ShouldBeEmpty();
        plan.NewHighWaterMark.ShouldBe(711); // 高水位号不回退
    }

    [Fact]
    public void Plan_LargeGap_ProducedAsSingleGapRangeNotDropped()
    {
        // 大跳变：缺口 100。统一作为单个缺口区间产出，交由待补队列受控补齐，绝不丢弃。
        var plan = LiveSegmentGapPlanner.Plan(highWaterMark: 1000, playlistNumbers: [1101]);

        plan.FreshNumbers.ShouldBe([1101]);
        plan.GapRanges.Count.ShouldBe(1);
        plan.GapRanges[0].Start.ShouldBe(1001);
        plan.GapRanges[0].End.ShouldBe(1100);
        plan.GapRanges[0].Count.ShouldBe(100);
    }

    [Fact]
    public void Plan_InternalGapWithinPlaylist_IsDetected()
    {
        // playlist 内部缺口（800 与 803 之间缺 801、802）。
        var plan = LiveSegmentGapPlanner.Plan(highWaterMark: 799, playlistNumbers: [800, 803, 804]);

        plan.FreshNumbers.ShouldBe([800, 803, 804]);
        plan.GapRanges.Count.ShouldBe(1);
        plan.GapRanges[0].Start.ShouldBe(801);
        plan.GapRanges[0].End.ShouldBe(802);
    }

    [Fact]
    public void Plan_BetweenAndInternalGaps_BothDetected()
    {
        // 既有 between-refresh 缺口（501~709），又有 playlist 内部缺口（711）。
        var plan = LiveSegmentGapPlanner.Plan(highWaterMark: 500, playlistNumbers: [710, 712]);

        plan.FreshNumbers.ShouldBe([710, 712]);
        plan.GapRanges.Count.ShouldBe(2);
        plan.GapRanges[0].Start.ShouldBe(501);
        plan.GapRanges[0].End.ShouldBe(709);
        plan.GapRanges[1].Start.ShouldBe(711);
        plan.GapRanges[1].End.ShouldBe(711);
    }

    [Fact]
    public void Plan_NoNewSegments_KeepsHighWaterMark()
    {
        var plan = LiveSegmentGapPlanner.Plan(highWaterMark: 500, playlistNumbers: [498, 499, 500]);

        plan.FreshNumbers.ShouldBeEmpty();
        plan.GapRanges.ShouldBeEmpty();
        plan.NewHighWaterMark.ShouldBe(500);
    }

    [Fact]
    public void Plan_ContiguousAdvance_NoGap()
    {
        var plan = LiveSegmentGapPlanner.Plan(highWaterMark: 500, playlistNumbers: [501, 502, 503]);

        plan.FreshNumbers.ShouldBe([501, 502, 503]);
        plan.GapRanges.ShouldBeEmpty();
        plan.NewHighWaterMark.ShouldBe(503);
    }

    [Theory]
    // TD=1：下限=Max(30, ceil(30/1))=30，上限=Max(3600, ceil(3600/1))=3600。
    [InlineData(1, 1.0, 3, 30)]       // 基准3 被下限抬到30
    [InlineData(10, 1.0, 3, 30)]      // 基准30 在[30,1800]内
    [InlineData(22, 1.0, 3, 66)]      // 基准66 在[30,1800]内
    [InlineData(23, 1.0, 3, 69)]      // 本次事故缺口 -> 69（不变）
    [InlineData(100, 1.0, 3, 300)]    // 基准300 在[30,1800]内
    [InlineData(1000, 1.0, 3, 3000)]  // 基准3000 在[30,3600]内
    [InlineData(10, 1.0, 5, 50)]      // 基准随 --download-retry-count 变化
    [InlineData(10, 1.0, 0, 30)]      // retry=0 时仍保留下限窗口
    // TD=4：下限=Max(30, ceil(30/4))=30，上限=3600。
    [InlineData(1, 4.0, 3, 30)]
    [InlineData(10, 4.0, 3, 30)]
    // TD=0.5：下限=60，上限=7200。
    [InlineData(10, 0.5, 3, 60)]
    [InlineData(2000, 0.5, 3, 6000)]
    public void ComputeGapWindow_ClampsRetryScaledBaseBetweenFloorAndCap(long count, double targetDuration, int retryCount, long expected)
    {
        LiveSegmentGapPlanner.ComputeGapWindow(count, targetDuration, retryCount).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1.0, 3600)]
    [InlineData(4.0, 900)]
    [InlineData(6.0, 600)]
    [InlineData(7.0, 514)]
    [InlineData(0.0, 3600)]
    [InlineData(7200.0, 1)]
    public void ComputeMaxExpandableGapCount_DefaultsToOneHourOfTargetDuration(double targetDuration, long expected)
    {
        LiveSegmentGapPlanner.ComputeMaxExpandableGapCount(targetDuration).ShouldBe(expected);
    }

    [Fact]
    public void CapGapRangeToLatest_LargeRange_ExpandsOnlyLatestWindow()
    {
        var (expand, omitted) = LiveSegmentGapPlanner.CapGapRangeToLatest(new LiveSegmentGapPlanner.GapRange(101, 999999), 3600);

        expand.Start.ShouldBe(996400);
        expand.End.ShouldBe(999999);
        expand.Count.ShouldBe(3600);
        omitted.ShouldNotBeNull();
        omitted.Value.Start.ShouldBe(101);
        omitted.Value.End.ShouldBe(996399);
    }

    [Fact]
    public void CapGapRangeToLatest_SmallRange_KeepsWholeRange()
    {
        var range = new LiveSegmentGapPlanner.GapRange(712, 734);
        var (expand, omitted) = LiveSegmentGapPlanner.CapGapRangeToLatest(range, 3600);

        expand.ShouldBe(range);
        omitted.ShouldBeNull();
    }

    [Fact]
    public void ShouldEvictByWindow_OnlyWhenLagExceedsWindow()
    {
        // window=69（缺口 23×3）。号 712 在高水位推进越过 712+69=781 后才驱逐。
        LiveSegmentGapPlanner.ShouldEvictByWindow(highWaterMark: 781, number: 712, window: 69).ShouldBeFalse();
        LiveSegmentGapPlanner.ShouldEvictByWindow(highWaterMark: 782, number: 712, window: 69).ShouldBeTrue();
    }

    [Fact]
    public void ShouldEvictByRetry_OnlyAfterExceedingRetryCount()
    {
        // 默认 --download-retry-count = 3。
        LiveSegmentGapPlanner.ShouldEvictByRetry(attempts: 3, retryCount: 3).ShouldBeFalse();
        LiveSegmentGapPlanner.ShouldEvictByRetry(attempts: 4, retryCount: 3).ShouldBeTrue();
    }

    [Fact]
    public void ResolveUnfillableHistoryCeil_NoHoles_ReturnsNull()
    {
        // 无空洞：全部已下载碎片都可保留，无需放弃。
        LiveSegmentGapPlanner.ResolveUnfillableHistoryCeil([100, 101, 102], []).ShouldBeNull();
    }

    [Fact]
    public void ResolveUnfillableHistoryCeil_HoleAboveAllDownloaded_ReturnsHighestHole()
    {
        // 已下载 100~102 全部落在最高空洞 105 之下 -> 应以 105 为界全部放弃。
        LiveSegmentGapPlanner.ResolveUnfillableHistoryCeil([100, 101, 102], [103, 105]).ShouldBe(105);
    }

    [Fact]
    public void ResolveUnfillableHistoryCeil_OnlyTailAboveHighestHole_AbandonsBelow()
    {
        // 空洞最高为 200；已下载 {150, 250, 260}。150 在洞下方需放弃，(200, ..] 的 250/260 是连接直播边缘的连续尾段。
        LiveSegmentGapPlanner.ResolveUnfillableHistoryCeil([150, 250, 260], [100, 200]).ShouldBe(200);
    }

    [Fact]
    public void ResolveUnfillableHistoryCeil_AllDownloadedAboveHoles_ReturnsNull()
    {
        // 空洞最高 200，但已下载都在其上（210~212）-> 无碎片需裁剪，返回 null。
        LiveSegmentGapPlanner.ResolveUnfillableHistoryCeil([210, 211, 212], [100, 200]).ShouldBeNull();
    }
}
