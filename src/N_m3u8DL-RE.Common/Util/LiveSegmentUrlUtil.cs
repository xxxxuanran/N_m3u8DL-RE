using System.Globalization;
using N_m3u8DL_RE.Common.Entity;

namespace N_m3u8DL_RE.Common.Util;

/// <summary>分片 URL 拆解结果：路径、查询串、文件名（不含扩展名）、扩展名。</summary>
public readonly record struct SegmentUrlParts(string Path, string Query, string FileNameWithoutExtension, string Extension);

/// <summary>
/// 流的分片 URL 是否「可预测」的判定结果：查询串一致、文件名数字号==Index、号严格递增。
/// 三者同时成立才允许按号推算未出现的分片（live-from-start / 缺口补齐均依赖此判定）。
/// </summary>
public readonly record struct SegmentUrlPatternCheck(bool SameQuery, bool NumericFileNameMatchesIndex, bool StrictlyIncreasing);

/// <summary>
/// 直播分片 URL 的解析与「按号推算分片」的纯工具集。
///
/// 存在需求：live-from-start（历史回填）与缺口补齐两条 subTask 都需要把分片 URL 里的数字号
/// 解析出来、并据此构造 playlist 中尚未出现的分片。这部分逻辑原先内联在 SimpleLiveRecordManager2，
/// 现抽到 Common 形成无状态工具，既能在两处复用、避免行为漂移，也便于单测覆盖号解析/模板填充/模式判定。
/// </summary>
public static class LiveSegmentUrlUtil
{
    /// <summary>解析分片 URL 文件名中的数字号；文件名非纯数字时返回 false。</summary>
    public static bool TryGetSegmentUrlNumber(MediaSegment segment, out long number)
    {
        var parts = ParseSegmentUrl(segment.Url);
        return TryParseSegmentNumber(parts.FileNameWithoutExtension, out number);
    }

    /// <summary>在分片列表中按 URL 数字号定位下标；找不到返回 -1。</summary>
    public static int FindSegmentIndexByUrlNumber(IReadOnlyList<MediaSegment> segments, long number)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            var urlParts = ParseSegmentUrl(segments[i].Url);
            if (TryParseSegmentNumber(urlParts.FileNameWithoutExtension, out var segmentNumber) && segmentNumber == number)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// 以一个真实分片为模板，按目标号 <paramref name="index"/> 推算出一个待下载分片。
    ///
    /// 用于补齐 playlist 中尚未出现的历史/缺口分片：URL 仅替换文件名数字（保留路径、扩展名、查询串、零填充宽度），
    /// 时间戳按与模板号的差值反推，并深拷贝加密信息（IV 单独 Clone，避免共享同一引用被后续改写）。
    /// 替换文件名失败时返回 null。
    /// </summary>
    public static MediaSegment? CreateFilledSegment(
        MediaSegment template,
        SegmentUrlParts templateUrlParts,
        long index,
        long templateNumber,
        double? segmentDuration = null)
    {
        var newUrl = ReplaceUrlFileName(templateUrlParts, FormatSegmentNumber(index, templateUrlParts.FileNameWithoutExtension));
        if (newUrl == null) return null;

        var duration = segmentDuration ?? template.Duration;
        return new MediaSegment
        {
            Index = index,
            Duration = duration,
            Title = template.Title,
            DateTime = template.DateTime?.AddSeconds(-(templateNumber - index) * duration),
            StartRange = template.StartRange,
            ExpectLength = template.ExpectLength,
            Url = newUrl,
            NameFromVar = null,
            EncryptInfo = new EncryptInfo
            {
                Method = template.EncryptInfo.Method,
                Key = template.EncryptInfo.Key,
                IV = template.EncryptInfo.IV != null ? (byte[])template.EncryptInfo.IV.Clone() : null,
                HasExplicitIV = template.EncryptInfo.HasExplicitIV,
            },
        };
    }

    /// <summary>按模板文件名格式化号：模板带前导零时保持等宽零填充，否则原样输出。</summary>
    public static string FormatSegmentNumber(long number, string templateFileName)
    {
        if (templateFileName.Length > 1 && templateFileName[0] == '0')
            return number.ToString($"D{templateFileName.Length}", CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 仅接受无符号十进制数字串（拒绝负号、正号、空白、千分位等），避免把非号文件名误判为可推算号。
    /// </summary>
    public static bool TryParseSegmentNumber(string value, out long number)
    {
        number = 0;
        if (value.Length == 0)
            return false;

        foreach (var ch in value)
        {
            if (ch is < '0' or > '9')
                return false;
        }

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// 扫描一段分片，判定其 URL 是否可预测（查询串一致 / 数字号==Index / 严格递增），
    /// 供调用方决定能否安全地按号推算缺失分片。三项判定均已失败时提前退出扫描。
    /// </summary>
    public static SegmentUrlPatternCheck CheckSegmentUrlPattern(IReadOnlyList<MediaSegment> segments)
    {
        if (segments.Count == 0)
            return new SegmentUrlPatternCheck(SameQuery: true, NumericFileNameMatchesIndex: false, StrictlyIncreasing: false);

        var firstParts = ParseSegmentUrl(segments[0].Url);
        var sameQuery = true;
        var numericFileNameMatchesIndex = true;
        var strictlyIncreasing = true;
        long? lastNumber = null;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var urlParts = i == 0 ? firstParts : ParseSegmentUrl(segment.Url);

            if (urlParts.Query != firstParts.Query)
                sameQuery = false;

            if (!TryParseSegmentNumber(urlParts.FileNameWithoutExtension, out var num))
            {
                numericFileNameMatchesIndex = false;
                strictlyIncreasing = false;
            }
            else
            {
                if (num != segment.Index)
                    numericFileNameMatchesIndex = false;

                if (lastNumber != null && num <= lastNumber.Value)
                    strictlyIncreasing = false;

                lastNumber = num;
            }

            if (!sameQuery && !numericFileNameMatchesIndex && !strictlyIncreasing)
                break;
        }

        return new SegmentUrlPatternCheck(sameQuery, numericFileNameMatchesIndex, strictlyIncreasing);
    }

    /// <summary>把分片 URL 拆为路径 / 查询串 / 文件名（去扩展名）/ 扩展名四部分，供按号替换文件名复用。</summary>
    public static SegmentUrlParts ParseSegmentUrl(string url)
    {
        var questionIdx = url.IndexOf('?');
        var path = questionIdx >= 0 ? url[..questionIdx] : url;
        var query = questionIdx >= 0 ? url[questionIdx..] : string.Empty;
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        var dot = name.LastIndexOf('.');
        var fileNameWithoutExtension = dot >= 0 ? name[..dot] : name;
        var extension = dot >= 0 ? name[dot..] : string.Empty;

        return new SegmentUrlParts(path, query, fileNameWithoutExtension, extension);
    }

    /// <summary>仅替换 URL 文件名（保留同级目录、扩展名与查询串）；URL 无路径分隔符时返回 null。</summary>
    public static string? ReplaceUrlFileName(SegmentUrlParts urlParts, string newFileNameNoExt)
    {
        var path = urlParts.Path;
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0) return null;

        return path[..(lastSlash + 1)] + newFileNameNoExt + urlParts.Extension + urlParts.Query;
    }
}
