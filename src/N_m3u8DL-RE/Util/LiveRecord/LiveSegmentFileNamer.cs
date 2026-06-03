using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Util.LiveRecord;

/// <summary>
/// 为直播分片计算落盘文件名 / 临时文件路径，统一主下载循环与各补洞 subTask 的命名规则。
///
/// 存在需求：分片命名要兼顾「能跨轮次稳定排序」与「避免重名覆盖」，规则随 ExtractorType 与 playlist 特征而变
/// （HLS 按时间戳或 Index 命名、同路径不同 query 时退回用 query 区分）。把这套规则收敛到单一组件，
/// 可保证主流程与 live-from-start / 缺口补齐对同一分片算出完全一致的文件名，否则补洞文件会与主流程错位。
/// </summary>
internal sealed class LiveSegmentFileNamer(ExtractorType extractorType)
{
    /// <summary>
    /// 计算分片的落盘文件名（不含扩展名）：优先用模板变量名；HLS 在全有时间戳时用 Unix 时间戳、否则用 Index；
    /// allSamePath（路径相同仅 query 不同）时退回用 query 区分以避免重名。
    /// </summary>
    public string GetSegmentName(MediaSegment segment, bool allHasDatetime, bool allSamePath)
    {
        if (!string.IsNullOrEmpty(segment.NameFromVar))
            return segment.NameFromVar;

        var name = OtherUtil.GetFileNameFromInput(segment.Url, false);
        if (allSamePath)
            name = OtherUtil.GetValidFileName(segment.Url.Split('?').Last(), "_");

        if (extractorType == ExtractorType.HLS && allHasDatetime)
            name = GetUnixTimestamp(segment.DateTime!.Value).ToString();
        else if (extractorType == ExtractorType.HLS)
            name = segment.Index.ToString();

        return name;
    }

    /// <summary>在 <see cref="GetSegmentName"/> 基础上拼出临时分片完整路径（tmpDir/名字.扩展名.tmp）。</summary>
    public string GetSegmentFilePath(MediaSegment segment, bool allHasDatetime, bool allSamePath, string tmpDir, string extension)
    {
        var filename = GetSegmentName(segment, allHasDatetime, allSamePath);
        return Path.Combine(tmpDir, filename + $".{extension}.tmp");
    }

    /// <summary>把分片时间转换为 Unix 秒级时间戳，作为 HLS 带时间戳时的稳定有序文件名。</summary>
    private static long GetUnixTimestamp(DateTime dateTime)
    {
        return new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeSeconds();
    }
}
