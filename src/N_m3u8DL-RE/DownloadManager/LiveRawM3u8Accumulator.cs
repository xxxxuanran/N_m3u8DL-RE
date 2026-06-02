using N_m3u8DL_RE.Common.Util;
using System.Globalization;
using System.Text;

namespace N_m3u8DL_RE.DownloadManager;

internal sealed class LiveRawM3u8Accumulator
{
    private readonly HashSet<string> segmentIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> currentStateLines = new(StringComparer.Ordinal);
    private string? content;
    private bool endListWritten;

    private readonly record struct SegmentBlock(string Id, string[] Lines);
    private readonly record struct ParseResult(List<SegmentBlock> Segments, bool HasEndList);

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

    private void CollectStateLines(string rawM3u8)
    {
        foreach (var line in ReadLines(rawM3u8))
        {
            if (TryGetStateLineKey(line, out var stateKey))
                currentStateLines[stateKey] = line;
        }
    }

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

    private static bool LooksLikeMediaPlaylist(string rawM3u8)
    {
        if (rawM3u8.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal))
            return false;

        return rawM3u8.Contains("#EXTINF", StringComparison.Ordinal)
               || rawM3u8.Contains("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal)
               || rawM3u8.Contains("#EXT-X-TARGETDURATION:", StringComparison.Ordinal);
    }

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

    private static string Normalize(string rawM3u8)
    {
        return string.Join(Environment.NewLine, ReadLines(rawM3u8));
    }

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
