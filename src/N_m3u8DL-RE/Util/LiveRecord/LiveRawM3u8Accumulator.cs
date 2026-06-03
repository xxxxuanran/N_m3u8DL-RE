using N_m3u8DL_RE.Common.Util;
using System.Globalization;
using System.Text;

namespace N_m3u8DL_RE.Util.LiveRecord;

/// <summary>
/// 直播录制时把每轮刷新到的原始媒体 playlist 增量累加成一份完整的 raw .m3u8 存档（保存 --write-meta-json 之外的原始清单）。
///
/// 存在需求：直播 playlist 是滑动窗口，每次只暴露一小段分片；若直接覆盖写出会丢失历史。
/// 本累加器以分片唯一标识去重（优先用 EXT-X-MEDIA-SEQUENCE 号，回退到 URI），仅把新出现的分片块追加到文件末尾，
/// 从而在整场直播结束后还原出一份连续的媒体 playlist。同时去除重复的状态行（EXT-X-KEY/MAP），并在收到 ENDLIST 后封口。
/// 维护可恢复状态（已落盘内容、已写 ID、当前 KEY/MAP），因此是有状态实例而非纯工具。
/// </summary>
internal sealed class LiveRawM3u8Accumulator
{
    private readonly HashSet<string> segmentIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> currentStateLines = new(StringComparer.Ordinal);
    private string? content;
    private bool endListWritten;

    /// <summary>单个分片块：去重用的标识 + 该块包含的所有清单行。</summary>
    private readonly record struct SegmentBlock(string Id, string[] Lines);
    /// <summary>一次解析结果：解析出的分片块序列，以及是否出现了 EXT-X-ENDLIST。</summary>
    private readonly record struct ParseResult(List<SegmentBlock> Segments, bool HasEndList);

    /// <summary>
    /// 用本轮刷新到的原始清单更新存档：首轮初始化（可从已有文件断点续写），之后只把新分片块追加落盘。
    /// 已写入 ENDLIST 后视为收尾，后续调用直接忽略。
    /// </summary>
    public async Task UpdateAsync(string file, string rawM3u8)
    {
        if (content == null)
        {
            await InitializeAsync(file, rawM3u8);
            return;
        }

        if (endListWritten)
            return;

        var parsed = Parse(rawM3u8);
        var appendedLines = new List<string>();
        foreach (var segment in parsed.Segments)
        {
            if (!segmentIds.Add(segment.Id))
                continue;

            appendedLines.AddRange(FilterSegmentBlockLines(segment.Lines));
        }

        if (parsed.HasEndList && !endListWritten)
        {
            appendedLines.Add("#EXT-X-ENDLIST");
            endListWritten = true;
        }

        if (appendedLines.Count == 0)
            return;

        var appendText = BuildAppendText(appendedLines);
        await File.AppendAllTextAsync(file, appendText, GlobalUtil.Utf8NoBom);
        content += appendText;
    }

    /// <summary>
    /// 首轮初始化存档：若目标文件已有可解析的历史内容则续写其后（断点续录），否则以本轮清单为基底写入。
    /// 同时建立去重所需的初始状态（已写分片 ID、当前 KEY/MAP 行、是否已 ENDLIST）。
    /// </summary>
    private async Task InitializeAsync(string file, string rawM3u8)
    {
        var initialContent = Normalize(rawM3u8);
        var shouldWriteInitialContent = true;

        if (File.Exists(file))
        {
            var fileContent = await File.ReadAllTextAsync(file, Encoding.UTF8);
            if (Parse(fileContent).Segments.Count > 0)
            {
                initialContent = fileContent;
                shouldWriteInitialContent = false;
            }
        }

        var parsed = Parse(initialContent);
        if (parsed.Segments.Count == 0)
            return;

        content = initialContent;
        endListWritten = parsed.HasEndList;
        segmentIds.Clear();
        currentStateLines.Clear();
        CollectStateLines(initialContent);

        foreach (var segment in parsed.Segments)
        {
            segmentIds.Add(segment.Id);
        }

        if (shouldWriteInitialContent)
        {
            await File.WriteAllTextAsync(file, initialContent + Environment.NewLine, GlobalUtil.Utf8NoBom);
            content += Environment.NewLine;
        }
    }

    /// <summary>过滤分片块内的行：丢弃与当前生效值重复的状态行（EXT-X-KEY/MAP），仅在状态变化时保留，避免存档膨胀。</summary>
    private List<string> FilterSegmentBlockLines(IEnumerable<string> blockLines)
    {
        var filteredLines = new List<string>();

        foreach (var line in blockLines)
        {
            if (TryGetStateLineKey(line, out var stateKey))
            {
                if (currentStateLines.TryGetValue(stateKey, out var currentStateLine) && currentStateLine == line)
                    continue;

                currentStateLines[stateKey] = line;
            }

            filteredLines.Add(line);
        }

        return filteredLines;
    }

    /// <summary>把待追加行拼成文本：必要时先补一个换行，确保与既有内容之间不会粘连成同一行。</summary>
    private string BuildAppendText(IReadOnlyList<string> appendedLines)
    {
        var builder = new StringBuilder();
        if (content is { Length: > 0 } && !content.EndsWith('\n') && !content.EndsWith('\r'))
        {
            builder.AppendLine();
        }

        builder.AppendJoin(Environment.NewLine, appendedLines);
        builder.AppendLine();
        return builder.ToString();
    }

    /// <summary>从已落盘内容中收集当前生效的状态行（KEY/MAP），作为后续去重比对的基线。</summary>
    private void CollectStateLines(string rawM3u8)
    {
        foreach (var line in ReadLines(rawM3u8))
        {
            if (TryGetStateLineKey(line, out var stateKey))
                currentStateLines[stateKey] = line;
        }
    }

    /// <summary>
    /// 把原始清单切成分片块序列：以 EXTINF 等标签 + 紧随的 URI 行聚合为一块，
    /// 并据 MEDIA-SEQUENCE 号（有则用 seq:，无则回退 uri:）赋予稳定的去重标识。非媒体 playlist（如主清单）返回空。
    /// </summary>
    private static ParseResult Parse(string rawM3u8)
    {
        if (!LooksLikeMediaPlaylist(rawM3u8))
            return new ParseResult([], false);

        var segments = new List<SegmentBlock>();
        var blockLines = new List<string>();
        var hasMediaSequence = false;
        var nextSequence = 0L;
        var hasEndList = false;

        foreach (var line in ReadLines(rawM3u8))
        {
            if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal))
            {
                var sequenceText = line["#EXT-X-MEDIA-SEQUENCE:".Length..].Trim();
                if (long.TryParse(sequenceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence))
                {
                    nextSequence = sequence;
                    hasMediaSequence = true;
                }
                continue;
            }

            if (line.StartsWith("#EXT-X-ENDLIST", StringComparison.Ordinal))
            {
                hasEndList = true;
                continue;
            }

            if (IsSegmentBlockLine(line))
            {
                blockLines.Add(line);
                continue;
            }

            if (line.StartsWith('#'))
            {
                if (blockLines.Count > 0)
                    blockLines.Add(line);
                continue;
            }

            blockLines.Add(line);
            var segmentId = hasMediaSequence
                ? $"seq:{nextSequence.ToString(CultureInfo.InvariantCulture)}"
                : $"uri:{line}";
            segments.Add(new SegmentBlock(segmentId, [.. blockLines]));
            blockLines.Clear();

            if (hasMediaSequence)
                nextSequence++;
        }

        return new ParseResult(segments, hasEndList);
    }

    /// <summary>粗判是否为媒体级 playlist：含主清单标记（STREAM-INF）直接排除，否则看是否含 EXTINF / MEDIA-SEQUENCE / TARGETDURATION。</summary>
    private static bool LooksLikeMediaPlaylist(string rawM3u8)
    {
        if (rawM3u8.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal))
            return false;

        return rawM3u8.Contains("#EXTINF", StringComparison.Ordinal)
               || rawM3u8.Contains("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal)
               || rawM3u8.Contains("#EXT-X-TARGETDURATION:", StringComparison.Ordinal);
    }

    /// <summary>识别「状态行」（EXT-X-KEY / EXT-X-MAP）并给出其去重键；这类行表示持续生效状态，需单独跟踪以便去重。</summary>
    private static bool TryGetStateLineKey(string line, out string stateKey)
    {
        if (line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal))
        {
            stateKey = "#EXT-X-KEY";
            return true;
        }

        if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
        {
            stateKey = "#EXT-X-MAP";
            return true;
        }

        stateKey = string.Empty;
        return false;
    }

    /// <summary>判断该行是否属于分片块的组成标签（EXTINF/BYTERANGE/PROGRAM-DATE-TIME/DISCONTINUITY/KEY/MAP 等）。</summary>
    private static bool IsSegmentBlockLine(string line)
    {
        return line.StartsWith("#EXTINF", StringComparison.Ordinal)
               || line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.Ordinal)
               || line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:", StringComparison.Ordinal)
               || line.StartsWith("#EXT-X-DISCONTINUITY", StringComparison.Ordinal)
               || line.StartsWith("#EXT-X-DATERANGE:", StringComparison.Ordinal)
               || line.StartsWith("#EXT-X-GAP", StringComparison.Ordinal)
               || line.StartsWith("#EXT-X-BITRATE:", StringComparison.Ordinal)
               || line.StartsWith("#EXT-X-PART:", StringComparison.Ordinal)
               || TryGetStateLineKey(line, out _);
    }

    /// <summary>规整原始清单：去掉空行/首尾空白并统一为本机换行，作为存档基底，保证后续追加格式一致。</summary>
    private static string Normalize(string rawM3u8)
    {
        return string.Join(Environment.NewLine, ReadLines(rawM3u8));
    }

    /// <summary>逐行读取并 Trim，跳过空行；统一各处的清单遍历入口。</summary>
    private static IEnumerable<string> ReadLines(string rawM3u8)
    {
        using var reader = new StringReader(rawM3u8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0)
                continue;

            yield return line;
        }
    }
}
