using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Util.LiveRecord.SubTask;

/// <summary>
/// 补洞 subTask 单次下载所需的不可变上下文：模板分片及其 URL 拆解、模板号、分片时长、命名标志、临时目录/扩展名、
/// 限速器、请求头、重试次数与镜像主机列表。
///
/// 存在需求：live-from-start 与缺口补齐会就同一条流反复构造「按号推算的分片」并发下载，这些参数在一轮补洞内固定不变。
/// 用一个 record 打包传递，既避免各下载方法签名爆炸式增长，也保证探测、回填、补缺各阶段使用完全一致的下载参数。
/// </summary>
internal sealed record LiveSubTaskDownloadContext(
    MediaSegment Template,
    SegmentUrlParts TemplateUrlParts,
    long TemplateNumber,
    double SegmentDuration,
    bool AllHasDatetime,
    bool AllSamePath,
    string TmpDir,
    string Extension,
    SpeedContainer SpeedContainer,
    Dictionary<string, string> Headers,
    int RetryCount,
    string[] HostMirrors);
