using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util.LiveRecord;
using static N_m3u8DL_RE.Common.Util.LiveSegmentUrlUtil;

namespace N_m3u8DL_RE.Util.LiveRecord.SubTask;

/// <summary>
/// 补洞 subTask 的底层「按号下载一个推算分片」执行器，封装 IDownloader 与文件命名细节。
///
/// 存在需求：live-from-start 历史回填与缺口补齐都需要把「号 → 推算分片 → 落盘」这步做成可复用原语，
/// 并提供探测/严格/原始三种语义；同时统一这两条 subTask 的并发度与镜像主机选取策略，
/// 使它们相互之间、以及与主下载循环之间共享一致的限流与容错行为。本类无状态，仅做下载编排。
/// </summary>
internal sealed class LiveSubTaskSegmentDownloader(IDownloader downloader, LiveSegmentFileNamer fileNamer)
{
    /// <summary>从主线程数推导 subTask 并发度（取一半、下限 1、上限 16），避免补洞抢占主下载带宽。</summary>
    public static int ResolveParallelism(int threadCount)
    {
        const int maxSubTaskParallelism = 16;
        return Math.Min(maxSubTaskParallelism, Math.Max(1, threadCount / 2));
    }

    /// <summary>从配置的镜像主机里挑出用于竞速的子集（过滤空值后取约一半），其余主机留给主下载，降低对源站的并发压力。</summary>
    public static string[] ResolveMirrorHosts(IEnumerable<string>? mirrors)
    {
        var filtered = mirrors?.Where(m => !string.IsNullOrWhiteSpace(m)).ToArray() ?? [];
        return filtered.Length > 0
            ? filtered.Take(Math.Max(1, filtered.Length / 2)).ToArray()
            : [];
    }

    /// <summary>
    /// 按号下载一个推算分片（「尽力」语义）：推算失败或下载失败返回的 Segment 可能为 null，由调用方决定丢弃，
    /// 用于结果可缺省的场景（缺口补齐、升序回填的乐观尝试）。
    /// </summary>
    public async Task<(MediaSegment? Segment, DownloadResult? Result)> DownloadAsync(
        LiveSubTaskDownloadContext ctx,
        long number,
        CancellationToken cancellationToken)
    {
        var candidate = CreateFilledSegment(ctx.Template, ctx.TemplateUrlParts, number, ctx.TemplateNumber, ctx.SegmentDuration);
        if (candidate == null)
            return (null, null);

        var path = GetSegmentPath(ctx, candidate);
        var (segment, result) = await DownloadRawAsync(candidate, path, ctx.SpeedContainer, ctx.Headers, cancellationToken, ctx.RetryCount, ctx.HostMirrors);
        return (segment, result);
    }

    /// <summary>
    /// 按号下载一个推算分片（「必返回分片」语义）：即便推算失败也返回一个占位 MediaSegment（Index=号），
    /// 便于降序竞速回填用「号→分片」一一对应地判定可用边界（boundary）。下载失败时仅 Result 为 null。
    /// </summary>
    public async Task<(MediaSegment Segment, DownloadResult? Result)> DownloadRequiredAsync(
        LiveSubTaskDownloadContext ctx,
        long number,
        CancellationToken cancellationToken)
    {
        var candidate = CreateFilledSegment(ctx.Template, ctx.TemplateUrlParts, number, ctx.TemplateNumber, ctx.SegmentDuration);
        if (candidate == null)
            return (new MediaSegment { Index = number }, null);

        var path = GetSegmentPath(ctx, candidate);
        return await DownloadRawAsync(candidate, path, ctx.SpeedContainer, ctx.Headers, cancellationToken, ctx.RetryCount, ctx.HostMirrors);
    }

    /// <summary>
    /// 最底层的单分片下载：直接调用 IDownloader 并吞掉异常（异常转为 Result=null），
    /// 使补洞循环可统一按「成功/失败」二态推进而不被偶发异常打断。供探测与两种语义方法共用。
    /// </summary>
    public async Task<(MediaSegment Segment, DownloadResult? Result)> DownloadRawAsync(
        MediaSegment segment,
        string path,
        SpeedContainer speedContainer,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken,
        int retryCount,
        string[]? hostMirrorsOverride = null)
    {
        try
        {
            var effectiveHostMirrors = hostMirrorsOverride ?? [];
            var result = await downloader.DownloadSegmentAsync(segment, path, speedContainer, headers, retryCount, cancellationToken, effectiveHostMirrors);
            return (segment, result);
        }
        catch
        {
            return (segment, null);
        }
    }

    /// <summary>按上下文命名标志算出该推算分片的临时落盘路径，确保与主下载循环命名一致、不互相覆盖。</summary>
    public string GetSegmentPath(LiveSubTaskDownloadContext ctx, MediaSegment segment)
    {
        var filename = fileNamer.GetSegmentName(segment, ctx.AllHasDatetime, ctx.AllSamePath);
        return Path.Combine(ctx.TmpDir, filename + $".{ctx.Extension}.tmp");
    }
}
