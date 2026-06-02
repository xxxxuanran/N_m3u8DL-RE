using Mp4SubtitleParser;
using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Mp4;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks.Dataflow;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.DownloadManager;

internal class SimpleLiveRecordManager2
{
    IDownloader Downloader;
    DownloaderConfig DownloaderConfig;
    StreamExtractor StreamExtractor;
    List<StreamSpec> SelectedSteams;
    ConcurrentDictionary<int, string> PipeSteamNamesDic = new();
    MuxFormat? resolvedLivePipeMuxFormat;
    List<OutputFile> OutputFiles = [];
    DateTime? PublishDateTime;
    bool STOP_FLAG = false;
    int WAIT_SEC = 0; // 基础刷新间隔（正常轮询）
    bool WAIT_FROM_TARGET_DURATION = false; // 基础间隔是否来自 #EXT-X-TARGETDURATION（决定是否启用降级轮询）
    ConcurrentDictionary<int, double> RecordedDurDic = new(); // 已录制时长（保留小数，渲染时再取整）
    ConcurrentDictionary<int, double> RefreshedDurDic = new(); // 已刷新出的时长（保留小数，渲染时再取整）
    ConcurrentDictionary<int, BufferBlock<List<MediaSegment>>> BlockDic = new(); // 各流的Block
    ConcurrentDictionary<int, bool> SamePathDic = new(); // 各流是否allSamePath
    ConcurrentDictionary<int, SegmentUrlPatternCheck> SegmentUrlPatternDic = new(); // 各已选 media playlist 的首次 segment URL 规律预检结果
    ConcurrentDictionary<int, bool> LiveFromStartMergeDelayLoggedDic = new(); // 各流延后实时合并提示是否已输出
    ConcurrentDictionary<int, bool> RecordLimitReachedDic = new(); // 各流是否达到上限
    ConcurrentDictionary<int, string> LastFileNameDic = new(); // 上次下载的文件名
    ConcurrentDictionary<int, long> MaxIndexDic = new(); // 最大Index
    ConcurrentDictionary<int, long> DateTimeDic = new(); // 上次下载的dateTime
    CancellationTokenSource CancellationTokenSource = new(); // 取消Wait

    private readonly Lock lockObj = new();
    private readonly LiveRawM3u8Accumulator rawM3u8Accumulator = new();
    TimeSpan? audioStart = null;
    public bool ShouldRestartOnMediaInitChanged { get; private set; } = false;

    private readonly record struct SegmentUrlParts(string Path, string Query, string FileNameWithoutExtension, string Extension);

    private readonly record struct SegmentUrlPatternCheck(bool SameQuery, bool NumericFileNameMatchesIndex, bool StrictlyIncreasing);

    private enum SegmentGapSource
    {
        BetweenPlaylistRefreshes,
        CurrentPlaylist,
    }

    private enum LiveFromStartBackfillMode
    {
        MirrorFirst,
        PinnedFirst,
    }

    private readonly record struct MissingSegmentRange(long Start, long End, SegmentGapSource Source);
    // Live from start 回溯下载的不变上下文（模板分片、命名规律、临时目录、并发等），
    // 在定界探测与升序回填之间共享，避免重复传一长串参数。
    private sealed record LiveFromStartContext(
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
        int ProbeRetryCount,
        // Live-from-start 镜像竞速统一使用 HostMirror 前半部分；空数组表示仅原始 URL。
        string[] HostMirrors);

    private readonly record struct LiveFromStartDescendingResolved(MediaSegment? Segment, DownloadResult? Result, bool Reused);

    private readonly record struct LiveFromStartBackfillResolved(
        MediaSegment? Segment,
        DownloadResult? Result,
        bool Recovered,
        LiveFromStartBackfillMode Mode,
        bool CountForStats);

    private sealed record LiveFromStartDescendingScanResult(
        Dictionary<long, Task<(MediaSegment Segment, DownloadResult? Result)>> InFlight,
        Dictionary<long, LiveFromStartDescendingResolved> Resolved,
        HashSet<long> Committed,
        long? Boundary,
        int DispatchCount,
        int ReusedFromCache);

    public SimpleLiveRecordManager2(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
    {
        this.DownloaderConfig = downloaderConfig;
        Downloader = new SimpleDownloader(DownloaderConfig);
        PublishDateTime = selectedSteams.FirstOrDefault()?.PublishTime;
        StreamExtractor = streamExtractor;
        SelectedSteams = selectedSteams;
    }

    private void StopForMediaInitChange()
    {
        lock (lockObj)
        {
            if (STOP_FLAG) return;

            if (DownloaderConfig.MyOptions.LiveRestartOnExtMapChange)
            {
                ShouldRestartOnMediaInitChanged = true;
                Logger.WarnMarkUp("[darkorange3_1]Detected EXT-X-MAP change. Will finish current output and start a new live recording file.[/]");
            }
            else
            {
                Logger.WarnMarkUp("[darkorange3_1]Detected EXT-X-MAP change. Will stop recording soon (auto-restart disabled).[/]");
            }
            STOP_FLAG = true;
            CancellationTokenSource.Cancel();
        }
    }

    public void PrepareRestartAfterMediaInitChange()
    {
        foreach (var streamSpec in SelectedSteams)
        {
            var playlist = streamSpec.Playlist;
            if (playlist == null) continue;

            if (playlist.PendingMediaInit != null)
            {
                playlist.MediaInit = playlist.PendingMediaInit;
                streamSpec.Extension = "m4s";
            }

            playlist.PendingMediaInit = null;
            playlist.MediaInitChanged = false;
        }

        resolvedLivePipeMuxFormat = null;
    }

    private MuxFormat GetLivePipeMuxFormat()
    {
        return resolvedLivePipeMuxFormat ??= PipeUtil.ResolveLivePipeMuxFormat(
            DownloaderConfig.MyOptions.LivePipeMuxOptions?.MuxFormat, SelectedSteams);
    }

    private void DisableLiveSynthesisForImplicitIV()
    {
        if (StreamExtractor.ExtractorType != ExtractorType.HLS)
            return;

        if (!DownloaderConfig.MyOptions.LiveFillSegmentsGap && !DownloaderConfig.MyOptions.LiveFromStart)
            return;

        if (!SelectedSteams.Any(HasSegmentWithImplicitHlsIV))
            return;

        var disabledOptions = new List<string>();
        if (DownloaderConfig.MyOptions.LiveFillSegmentsGap)
        {
            DownloaderConfig.MyOptions.LiveFillSegmentsGap = false;
            disabledOptions.Add("--live-fill-segments-gap");
        }

        if (DownloaderConfig.MyOptions.LiveFromStart)
        {
            DownloaderConfig.MyOptions.LiveFromStart = false;
            disabledOptions.Add("--live-from-start");
        }

        if (disabledOptions.Count > 0)
        {
            Logger.WarnMarkUp($"[darkorange3_1]{string.Join(" and ", disabledOptions)} disabled because AES-128 segments use HLS default IVs derived from media sequence.[/]");
        }
    }

    private static bool HasSegmentWithImplicitHlsIV(StreamSpec streamSpec)
    {
        var playlist = streamSpec.Playlist;
        if (playlist == null)
            return false;

        if (playlist.MediaInit != null && HasImplicitHlsIV(playlist.MediaInit))
            return true;

        return playlist.MediaParts.Any(part => part.MediaSegments.Any(HasImplicitHlsIV));
    }

    private static bool HasImplicitHlsIV(MediaSegment segment)
    {
        return segment.EncryptInfo.Method == EncryptMethod.AES_128
               && segment.EncryptInfo.IV != null
               && !segment.EncryptInfo.HasExplicitIV;
    }

    // 从文件读取KEY
    private async Task SearchKeyAsync(string? currentKID)
    {
        var _key = await MP4DecryptUtil.SearchKeyFromFileAsync(DownloaderConfig.MyOptions.KeyTextFile, currentKID);
        if (_key != null)
        {
            if (DownloaderConfig.MyOptions.Keys == null)
                DownloaderConfig.MyOptions.Keys = [_key];
            else
                DownloaderConfig.MyOptions.Keys = [..DownloaderConfig.MyOptions.Keys, _key];
        }
    }

    /// <summary>
    /// 获取时间戳
    /// </summary>
    /// <param name="dateTime"></param>
    /// <returns></returns>
    private long GetUnixTimestamp(DateTime dateTime)
    {
        return new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeSeconds();
    }

    /// <summary>
    /// 获取分段文件夹
    /// </summary>
    /// <param name="segment"></param>
    /// <param name="allHasDatetime"></param>
    /// <returns></returns>
    private string GetSegmentName(MediaSegment segment, bool allHasDatetime, bool allSamePath)
    {
        if (!string.IsNullOrEmpty(segment.NameFromVar))
        {
            return segment.NameFromVar;
        }

        bool hls = StreamExtractor.ExtractorType == ExtractorType.HLS;

        string name = OtherUtil.GetFileNameFromInput(segment.Url, false);
        if (allSamePath)
        {
            name = OtherUtil.GetValidFileName(segment.Url.Split('?').Last(), "_");
        }

        if (hls && allHasDatetime)
        {
            name = GetUnixTimestamp(segment.DateTime!.Value).ToString();
        }
        else if (hls)
        {
            name = segment.Index.ToString();
        }

        return name;
    }

    private void ChangeSpecInfo(StreamSpec streamSpec, List<Mediainfo> mediainfos, ref bool useAACFilter)
    {
        if (!DownloaderConfig.MyOptions.BinaryMerge && mediainfos.Any(m => m.DolbyVison))
        {
            DownloaderConfig.MyOptions.BinaryMerge = true;
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge2}[/]");
        }

        if (DownloaderConfig.MyOptions.MuxAfterDone && mediainfos.Any(m => m.DolbyVison))
        {
            DownloaderConfig.MyOptions.MuxAfterDone = false;
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge5}[/]");
        }

        if (mediainfos.Where(m => m.Type == "Audio").All(m => m.BaseInfo!.Contains("aac")))
        {
            useAACFilter = true;
        }

        if (mediainfos.All(m => m.Type == "Audio") && streamSpec.MediaType != MediaType.AUDIO)
        {
            streamSpec.MediaType = MediaType.AUDIO;
        }
        else if (mediainfos.All(m => m.Type == "Subtitle") && streamSpec.MediaType != MediaType.SUBTITLES)
        {
            streamSpec.MediaType = MediaType.SUBTITLES;

            if (streamSpec.Extension is null or "ts")
                streamSpec.Extension = "vtt";
        }
    }

    private string GetSegmentFilePath(MediaSegment segment, bool allHasDatetime, bool allSamePath, string tmpDir, string extension)
    {
        var filename = GetSegmentName(segment, allHasDatetime, allSamePath);
        return Path.Combine(tmpDir, filename + $".{extension}.tmp");
    }

    private async Task<(List<Mediainfo> MediaInfos, bool UseAACFilter)> ReadAndApplyMediaInfoAsync(
        StreamSpec streamSpec,
        string filePath,
        bool useAACFilter)
    {
        Logger.WarnMarkUp(ResString.readingInfo);
        var mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, filePath);
        mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));

        lock (lockObj)
        {
            audioStart ??= mediaInfos.FirstOrDefault(x => x.Type == "Audio")?.StartTime;
        }

        ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
        return (mediaInfos, useAACFilter);
    }

    private async Task DecryptMediaInitIfNeededAsync(
        MediaSegment mediaInit,
        DownloadResult result,
        string? currentKID,
        DecryptEngine decryptEngine,
        string decryptionBinaryPath,
        bool allowMss = false)
    {
        if ((!mediaInit.IsEncrypted && string.IsNullOrEmpty(currentKID))
            || !DownloaderConfig.MyOptions.MP4RealTimeDecryption
            || string.IsNullOrEmpty(currentKID)
            || (!allowMss && StreamExtractor.ExtractorType == ExtractorType.MSS))
        {
            return;
        }

        var enc = result.ActualFilePath;
        var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
        var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
        if (dResult)
        {
            result.ActualFilePath = dec;
        }
    }

    private async Task<bool> TryDecryptMediaSegmentAsync(
        MediaSegment segment,
        DownloadResult result,
        string? currentKID,
        string mp4InitFile,
        DecryptEngine decryptEngine,
        string decryptionBinaryPath)
    {
        if (!segment.IsEncrypted
            || !DownloaderConfig.MyOptions.MP4RealTimeDecryption
            || string.IsNullOrEmpty(currentKID))
        {
            return false;
        }

        var enc = result.ActualFilePath;
        if (!File.Exists(enc))
            return false;

        var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
        var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile);
        if (!dResult)
            return false;

        File.Delete(enc);
        result.ActualFilePath = dec;
        return true;
    }

    private async Task<(WebVttSub CurrentVtt, bool FirstSub, long BaseTimestamp)> UpdateSubtitlesAsync(
        StreamSpec streamSpec,
        ProgressTask task,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        double segmentsDuration,
        WebVttSub currentVtt,
        bool firstSub,
        long baseTimestamp)
    {
        // 自动修复VTT raw字幕
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("vtt"))
        {
            // 排序字幕并修正时间戳
            var keys = fileDic.Keys.OrderBy(k => k.Index).ToList();
            foreach (var seg in keys)
            {
                var vttContent = await File.ReadAllTextAsync(fileDic[seg]!.ActualFilePath);
                var waitCount = 0;
                while (DownloaderConfig.MyOptions.LiveFixVttByAudio && audioStart == null && waitCount++ < 5)
                {
                    await Task.Delay(1000);
                }
                var subOffset = audioStart != null ? (long)audioStart.Value.TotalMilliseconds : 0L;
                var vtt = WebVttSub.Parse(vttContent, subOffset);
                // 手动计算MPEGTS
                if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                {
                    vtt.MpegtsTimestamp = (long)(90000 * keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                }
                if (firstSub) { currentVtt = vtt; firstSub = false; }
                else currentVtt.AddCuesFromOne(vtt);
            }
        }

        // 自动修复VTT mp4字幕
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                                                       && streamSpec.Codecs != "stpp" && streamSpec.Extension != null && streamSpec.Extension.Contains("m4s"))
        {
            var initFile = fileDic.Values.FirstOrDefault(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init"));
            var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
            var (sawVtt, timescale) = MP4VttUtil.CheckInit(iniFileBytes);
            if (sawVtt)
            {
                var mp4s = fileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s")).ToArray();
                if (firstSub)
                {
                    currentVtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                    firstSub = false;
                }
                else
                {
                    var vtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                    currentVtt.AddCuesFromOne(vtt);
                }
            }
        }

        // 自动修复TTML raw字幕
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("ttml"))
        {
            var keys = fileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key).ToList();
            if (firstSub)
            {
                if (baseTimestamp != 0)
                {
                    var total = segmentsDuration;
                    baseTimestamp -= (long)TimeSpan.FromSeconds(total).TotalMilliseconds;
                }
                var first = true;
                foreach (var seg in keys)
                {
                    var vtt = MP4TtmlUtil.ExtractFromTTML(fileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                    // 手动计算MPEGTS
                    if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = (long)(90000 * keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                    }
                    if (first) { currentVtt = vtt; first = false; }
                    else currentVtt.AddCuesFromOne(vtt);
                }
                firstSub = false;
            }
            else
            {
                foreach (var seg in keys)
                {
                    var vtt = MP4TtmlUtil.ExtractFromTTML(fileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                    // 手动计算MPEGTS
                    if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = (long)(90000 * (RecordedDurDic[task.Id] + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration)));
                    }
                    currentVtt.AddCuesFromOne(vtt);
                }
            }
        }

        // 自动修复TTML mp4字幕
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("m4s")
            && streamSpec.Codecs != null && streamSpec.Codecs.Contains("stpp"))
        {
            // sawTtml暂时不判断
            // var initFile = fileDic.Values.Where(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).FirstOrDefault();
            // var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
            // var sawTtml = MP4TtmlUtil.CheckInit(iniFileBytes);
            var keys = fileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key);
            if (firstSub)
            {
                if (baseTimestamp != 0)
                {
                    var total = segmentsDuration;
                    baseTimestamp -= (long)TimeSpan.FromSeconds(total).TotalMilliseconds;
                }
                var first = true;
                foreach (var seg in keys)
                {
                    var vtt = MP4TtmlUtil.ExtractFromMp4(fileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                    // 手动计算MPEGTS
                    if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = (long)(90000 * keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                    }
                    if (first) { currentVtt = vtt; first = false; }
                    else currentVtt.AddCuesFromOne(vtt);
                }
                firstSub = false;
            }
            else
            {
                foreach (var seg in keys)
                {
                    var vtt = MP4TtmlUtil.ExtractFromMp4(fileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                    // 手动计算MPEGTS
                    if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = (long)(90000 * (RecordedDurDic[task.Id] + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration)));
                    }
                    currentVtt.AddCuesFromOne(vtt);
                }
            }
        }

        return (currentVtt, firstSub, baseTimestamp);
    }

    private async Task<(Stream? FileOutputStream, bool InitWritten)> WriteRealTimeMergeAsync(
        StreamSpec streamSpec,
        ProgressTask task,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        string saveDir,
        string saveName,
        string tmpDir,
        string mp4InitFile,
        string? currentKID,
        DecryptEngine decryptEngine,
        Stream? fileOutputStream,
        bool initWritten,
        WebVttSub currentVtt)
    {
        // 合并
        var outputExt = "." + streamSpec.Extension;
        if (streamSpec.Extension == null) outputExt = ".ts";
        else if (streamSpec is { MediaType: MediaType.AUDIO, Extension: "m4s" or "mp4" }) outputExt = ".m4a";
        else if (streamSpec.MediaType != MediaType.SUBTITLES && streamSpec.Extension is "m4s" or "mp4") outputExt = ".mp4";
        else if (streamSpec.MediaType == MediaType.SUBTITLES)
        {
            outputExt = DownloaderConfig.MyOptions.SubtitleFormat == Enum.SubtitleFormat.SRT ? ".srt" : ".vtt";
        }

        var output = Path.Combine(saveDir, saveName + outputExt);
        MuxFormat? livePipeMuxFormat = null;
        if (DownloaderConfig.MyOptions.LivePipeMux && streamSpec.MediaType != MediaType.SUBTITLES)
        {
            livePipeMuxFormat = GetLivePipeMuxFormat();
            output = Path.ChangeExtension(output, OtherUtil.GetMuxExtension(livePipeMuxFormat.Value));
        }

        // 移除无效片段
        var badKeys = fileDic.Where(i => i.Value == null).Select(i => i.Key);
        foreach (var badKey in badKeys)
        {
            fileDic.Remove(badKey, out _);
        }

        // 设置输出流
        if (fileOutputStream == null)
        {
            // 检测目标文件是否存在，使用智能重命名
            var finalOutput = OtherUtil.HandlePathCollision(output, streamSpec);
            if (finalOutput != output)
            {
                Logger.WarnMarkUp($"{Path.GetFileName(output)} => {Path.GetFileName(finalOutput)}");
                output = finalOutput;
            }

            if (!DownloaderConfig.MyOptions.LivePipeMux || streamSpec.MediaType == MediaType.SUBTITLES)
            {
                fileOutputStream = new FileStream(output, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            }
            else
            {
                // 创建管道
                var muxFormat = livePipeMuxFormat ?? GetLivePipeMuxFormat();
                var muxOutput = Path.ChangeExtension(output, OtherUtil.GetMuxExtension(muxFormat));
                var finalMuxOutput = OtherUtil.HandlePathCollision(muxOutput, streamSpec);
                if (finalMuxOutput != muxOutput)
                {
                    Logger.WarnMarkUp($"{Path.GetFileName(muxOutput)} => {Path.GetFileName(finalMuxOutput)}");
                    muxOutput = finalMuxOutput;
                }
                output = muxOutput;
                var pipeName = $"RE_pipe_{Guid.NewGuid()}";
                fileOutputStream = PipeUtil.CreatePipe(pipeName);
                Logger.InfoMarkUp($"{ResString.namedPipeCreated} [cyan]{pipeName.EscapeMarkup()}[/]");
                PipeSteamNamesDic[task.Id] = pipeName;
                if (PipeSteamNamesDic.Count == SelectedSteams.Count(x => x.MediaType != MediaType.SUBTITLES))
                {
                    var names = PipeSteamNamesDic.OrderBy(i => i.Key).Select(k => k.Value).ToArray();
                    Logger.WarnMarkUp($"{ResString.namedPipeMux} [deepskyblue1]{Path.GetFileName(output).EscapeMarkup()}[/]");
                    var t = PipeUtil.StartPipeMuxAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, names, output, muxFormat);
                }

                // Windows only
                if (OperatingSystem.IsWindows())
                    await (fileOutputStream as NamedPipeServerStream)!.WaitForConnectionAsync();
            }
        }

        if (streamSpec.MediaType != MediaType.SUBTITLES)
        {
            var initResult = streamSpec.Playlist!.MediaInit != null ? fileDic[streamSpec.Playlist!.MediaInit!]! : null;
            var files = fileDic.Where(f => f.Key != streamSpec.Playlist!.MediaInit).OrderBy(s => s.Key.Index).Select(f => f.Value).Select(v => v!.ActualFilePath).ToArray();
            // fmp4 的 init(ftyp+moov) 只需在输出流开头写入一次。
            // 直播每轮刷新都会进入本方法，若每轮都前置 init，会在单个输出文件里
            // 嵌入大量重复的 moov，导致 ffmpeg 报 "Found duplicated MOOV Atom" 且播放器索引缓慢。
            if (initResult != null && mp4InitFile != "" && !initWritten)
            {
                // shaka/ffmpeg实时解密不需要init文件用于合并，mp4decrpyt需要
                if (string.IsNullOrEmpty(currentKID) || decryptEngine == DecryptEngine.MP4DECRYPT)
                {
                    files = [initResult.ActualFilePath, ..files];
                    initWritten = true;
                }
            }
            foreach (var inputFilePath in files)
            {
                using (var inputStream = File.OpenRead(inputFilePath))
                {
                    inputStream.CopyTo(fileOutputStream);
                }
            }
            if (!DownloaderConfig.MyOptions.LiveKeepSegments)
            {
                foreach (var inputFilePath in files.Where(x => !Path.GetFileName(x).StartsWith("_init")))
                {
                    File.Delete(inputFilePath);
                }
            }
            fileDic.Clear();
            if (initResult != null)
            {
                fileDic[streamSpec.Playlist!.MediaInit!] = initResult;
            }
        }
        else
        {
            var initResult = streamSpec.Playlist!.MediaInit != null ? fileDic[streamSpec.Playlist!.MediaInit!]! : null;
            var files = fileDic.OrderBy(s => s.Key.Index).Select(f => f.Value).Select(v => v!.ActualFilePath).ToArray();
            foreach (var inputFilePath in files)
            {
                if (!DownloaderConfig.MyOptions.LiveKeepSegments && !Path.GetFileName(inputFilePath).StartsWith("_init"))
                {
                    File.Delete(inputFilePath);
                }
            }

            // 处理图形字幕
            await SubtitleUtil.TryWriteImagePngsAsync(currentVtt, tmpDir);

            var subText = currentVtt.ToVtt();
            if (outputExt == ".srt")
            {
                subText = currentVtt.ToSrt();
            }
            var subBytes = Encoding.UTF8.GetBytes(subText);
            fileOutputStream.Position = 0;
            fileOutputStream.Write(subBytes);
            fileDic.Clear();
            if (initResult != null)
            {
                fileDic[streamSpec.Playlist!.MediaInit!] = initResult;
            }
        }

        // 刷新buffer
        fileOutputStream?.Flush();
        return (fileOutputStream, initWritten);
    }

    private async Task<bool> RecordStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer, BufferBlock<List<MediaSegment>> source)
    {
        var baseTimestamp = PublishDateTime == null ? 0L : (long)(PublishDateTime.Value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
        var decryptionBinaryPath = DownloaderConfig.MyOptions.DecryptionBinaryPath!;
        var mp4InitFile = "";
        var currentKID = "";
        var readInfo = false; // 是否读取过
        bool useAACFilter = false; // ffmpeg合并flag
        bool initDownloaded = false; // 是否下载过init文件
        bool initWritten = false; // fmp4的init(ftyp+moov)是否已写入输出流(只能写一次)
        ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();
        ConcurrentDictionary<MediaSegment, bool> Mp4DecryptedSegments = new();
        List<Mediainfo> mediaInfos = [];
        Stream? fileOutputStream = null;
        WebVttSub currentVtt = new(); // 字幕流始终维护一个实例
        bool firstSub = true;
        bool liveFromStartMergeReady = !DownloaderConfig.MyOptions.LiveFromStart;
        task.StartTask();

        var dirName = $"{task.Id}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
        var tmpDir = Path.Combine(DownloaderConfig.DirPrefix, dirName);
        var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;

        // SavePattern 优先（<SaveName> 始终展开为用户原值 MyOptions.SaveName，
        // <DateTime> 使用实时时间，长跑直播/EXT-X-MAP 重启场景下能反映当前时刻）；
        // 否则使用运行时派生的 FileName（含 tmpDir 冲突时的时间戳后缀，直播重启时也已同步更新）
        var saveName = !string.IsNullOrWhiteSpace(DownloaderConfig.MyOptions.SavePattern)
            ? OtherUtil.FormatSavePattern(DownloaderConfig.MyOptions.SavePattern, streamSpec, DownloaderConfig.MyOptions.SaveName, task.Id)
            : $"{DownloaderConfig.FileName}.{streamSpec.Language}".TrimEnd('.');
        var headers = DownloaderConfig.Headers;
        var decryptEngine = DownloaderConfig.MyOptions.DecryptionEngine;

        Logger.Debug($"dirName: {dirName}; tmpDir: {tmpDir}; saveDir: {saveDir}; saveName: {saveName}");

        // 创建文件夹
        if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir);
        if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

        var liveFromStartDownloadTask = DownloaderConfig.MyOptions.LiveFromStart
            ? Task.Run(() => DownloadLiveFromStartSegmentsAsync(streamSpec, task, speedContainer, tmpDir, headers, FileDic, source))
            : Task.CompletedTask;

        while (await source.OutputAvailableAsync() || (DownloaderConfig.MyOptions.LiveFromStart && !liveFromStartMergeReady))
        {
            // 接收新片段 且总是拿全部未处理的片段
            // 有时每次只有很少的片段，但是之前的片段下载慢，导致后面还没下载的片段都失效了
            // TryReceiveAll可以稍微缓解一下
            var received = source.TryReceiveAll(out IList<List<MediaSegment>>? segmentsList);
            if (!received && DownloaderConfig.MyOptions.LiveFromStart && !liveFromStartMergeReady)
            {
                await liveFromStartDownloadTask;
                liveFromStartMergeReady = true;
                segmentsList = new List<List<MediaSegment>> { new() };
            }
            if (segmentsList?.Any(s => s.Count == 0) == true)
                liveFromStartMergeReady = true;
            var segments = segmentsList?.SelectMany(s => s).ToList() ?? [];
            if (segments.Count > 0)
            {
                Logger.DebugMarkUp(string.Join(",", segments.Select(sss => GetSegmentName(sss, false, false))));

                // 下载init
                if (!initDownloaded && streamSpec.Playlist?.MediaInit != null)
                {
                    task.MaxValue += 1;
                    // 对于fMP4，自动开启二进制合并
                    if (!DownloaderConfig.MyOptions.BinaryMerge && streamSpec.MediaType != MediaType.SUBTITLES)
                    {
                        DownloaderConfig.MyOptions.BinaryMerge = true;
                        Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge}[/]");
                    }

                    var path = Path.Combine(tmpDir, "_init.mp4.tmp");
                    var result = await Downloader.DownloadSegmentAsync(streamSpec.Playlist.MediaInit, path, speedContainer, headers);
                    FileDic[streamSpec.Playlist.MediaInit] = result;
                    if (result is not { Success: true })
                    {
                        throw new Exception("Download init file failed!");
                    }
                    mp4InitFile = result.ActualFilePath;
                    task.Increment(1);

                    // 读取mp4信息
                    if (result is { Success: true })
                    {
                        currentKID = MP4DecryptUtil.GetMP4Info(result.ActualFilePath).KID;
                        // 从文件读取KEY
                        await SearchKeyAsync(currentKID);
                        // 实时解密
                        await DecryptMediaInitIfNeededAsync(streamSpec.Playlist.MediaInit, result, currentKID, decryptEngine, decryptionBinaryPath);
                        // ffmpeg读取信息
                        if (!readInfo)
                        {
                            (mediaInfos, useAACFilter) = await ReadAndApplyMediaInfoAsync(streamSpec, result.ActualFilePath, useAACFilter);
                            readInfo = true;
                        }
                        initDownloaded = true;
                    }
                }

                var allHasDatetime = segments.All(s => s.DateTime != null);
                if (!SamePathDic.ContainsKey(task.Id))
                {
                    var allName = segments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
                    var allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
                    SamePathDic[task.Id] = allSamePath;
                }

                // 下载第一个分片
                if (!readInfo || StreamExtractor.ExtractorType == ExtractorType.MSS)
                {
                    var seg = segments.First();
                    segments = segments.Skip(1).ToList();
                    var path = GetSegmentFilePath(seg, allHasDatetime, SamePathDic[task.Id], tmpDir, streamSpec.Extension ?? "clip");
                    var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                    FileDic[seg] = result;
                    if (result is not { Success: true })
                    {
                        throw new Exception("Download first segment failed!");
                    }
                    task.Increment(1);
                    if (result is { Success: true })
                    {
                        // 修复MSS init
                        if (StreamExtractor.ExtractorType == ExtractorType.MSS)
                        {
                            var processor = new MSSMoovProcessor(streamSpec);
                            var header = processor.GenHeader(File.ReadAllBytes(result.ActualFilePath));
                            await File.WriteAllBytesAsync(FileDic[streamSpec.Playlist!.MediaInit!]!.ActualFilePath, header);
                            if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                            {
                                // 需要重新解密init
                                await DecryptMediaInitIfNeededAsync(streamSpec.Playlist!.MediaInit!, FileDic[streamSpec.Playlist!.MediaInit!]!, currentKID, decryptEngine, decryptionBinaryPath, allowMss: true);
                            }
                        }
                        // 读取init信息
                        if (string.IsNullOrEmpty(currentKID))
                        {
                            currentKID = MP4DecryptUtil.GetMP4Info(result.ActualFilePath).KID;
                        }
                        // 从文件读取KEY
                        await SearchKeyAsync(currentKID);
                        // 实时解密
                        if (await TryDecryptMediaSegmentAsync(seg, result, currentKID, mp4InitFile, decryptEngine, decryptionBinaryPath))
                        {
                            Mp4DecryptedSegments[seg] = true;
                        }
                        if (!readInfo)
                        {
                            // ffmpeg读取信息
                            (mediaInfos, useAACFilter) = await ReadAndApplyMediaInfoAsync(streamSpec, result.ActualFilePath, useAACFilter);
                            readInfo = true;
                        }
                    }
                }

                // 开始下载
                var options = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = DownloaderConfig.MyOptions.ThreadCount
                };
                await Parallel.ForEachAsync(segments, options, async (seg, _) =>
                {
                    var path = GetSegmentFilePath(seg, allHasDatetime, SamePathDic[task.Id], tmpDir, streamSpec.Extension ?? "clip");
                    var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                    FileDic[seg] = result;
                    if (result is { Success: true })
                        task.Increment(1);
                    // 实时解密
                    if (result is { Success: true } && await TryDecryptMediaSegmentAsync(seg, result, currentKID, mp4InitFile, decryptEngine, decryptionBinaryPath))
                    {
                        Mp4DecryptedSegments[seg] = true;
                    }
                });
            }

            var badDownloadedKeys = FileDic.Where(i => i.Value == null).Select(i => i.Key).ToList();
            foreach (var badKey in badDownloadedKeys)
            {
                FileDic.Remove(badKey, out _);
            }

            if (!DownloaderConfig.MyOptions.LiveRealTimeMerge && segments.Count == 0)
                continue;

            var pendingSegments = FileDic
                .Where(f => f.Key != streamSpec.Playlist?.MediaInit && f.Value is { Success: true })
                .Select(f => f.Key)
                .OrderBy(k => k.Index)
                .ToList();
            if (pendingSegments.Count == 0)
                continue;

            if (ShouldDelayRealTimeMergeForLiveFromStart(fileOutputStream, liveFromStartMergeReady))
            {
                if (LiveFromStartMergeDelayLoggedDic.TryAdd(task.Id, true))
                    Logger.InfoMarkUp($"[darkorange3_1]Live from start is downloading earlier segments for {streamSpec.ToShortShortString().EscapeMarkup()}; delaying real-time merge output only.[/]");
                continue;
            }

            await DecryptPendingMp4SegmentsAsync(pendingSegments, FileDic, Mp4DecryptedSegments, currentKID, mp4InitFile, decryptEngine, decryptionBinaryPath);

            var segmentsDuration = DownloaderConfig.MyOptions.LiveRealTimeMerge
                ? pendingSegments.Sum(s => s.Duration)
                : segments.Sum(s => s.Duration);

            (currentVtt, firstSub, baseTimestamp) = await UpdateSubtitlesAsync(
                streamSpec,
                task,
                FileDic,
                segmentsDuration,
                currentVtt,
                firstSub,
                baseTimestamp);

            RecordedDurDic[task.Id] += segmentsDuration;

            /*// 写出m3u8
            if (DownloaderConfig.MyOptions.LiveWriteHLS)
            {
                var _saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
                var _saveName = DownloaderConfig.MyOptions.SaveName ?? DateTime.Now.ToString("yyyyMMddHHmmss");
                await StreamingUtil.WriteStreamListAsync(FileDic, task.Id, 0, _saveName, _saveDir);
            }*/

            // 合并逻辑
            if (DownloaderConfig.MyOptions.LiveRealTimeMerge)
            {
                (fileOutputStream, initWritten) = await WriteRealTimeMergeAsync(
                    streamSpec,
                    task,
                    FileDic,
                    saveDir,
                    saveName,
                    tmpDir,
                    mp4InitFile,
                    currentKID,
                    decryptEngine,
                    fileOutputStream,
                    initWritten,
                    currentVtt);
            }

            if (STOP_FLAG && source.Count == 0)
                break;
        }

        await liveFromStartDownloadTask;

        if (fileOutputStream == null) return true;

        if (!DownloaderConfig.MyOptions.LivePipeMux)
        {
            // 记录所有文件信息
            OutputFiles.Add(new OutputFile()
            {
                Index = task.Id,
                FilePath = (fileOutputStream as FileStream)!.Name,
                LangCode = streamSpec.Language,
                Description = streamSpec.Name,
                Mediainfos = mediaInfos,
                MediaType = streamSpec.MediaType,
            });
        }
        fileOutputStream.Close();
        fileOutputStream.Dispose();

        return true;
    }

    private async Task PlayListProduceAsync(Dictionary<StreamSpec, ProgressTask> dic)
    {
        while (!STOP_FLAG)
        {
            if (WAIT_SEC == 0) continue;

            // 本轮刷新是否抓到了新分片（任一流的最后一个分片发生了变化）
            // 用于决定下一次等待采用正常轮询还是降级轮询
            var updatedStreamCount = 0;

            // 1. MPD 所有URL相同 单次请求即可获得所有轨道的信息
            // 2. M3U8 所有URL不同 才需要多次请求
            await Parallel.ForEachAsync(dic, async (dic, _) =>
            {
                var streamSpec = dic.Key;
                var task = dic.Value;

                // 达到上限时 不需要刷新了
                if (RecordLimitReachedDic[task.Id])
                    return;

                if (STOP_FLAG)
                    return;

                var allHasDatetime = streamSpec.Playlist!.MediaParts[0].MediaSegments.All(s => s.DateTime != null);
                if (!SamePathDic.ContainsKey(task.Id))
                {
                    var allName = streamSpec.Playlist!.MediaParts[0].MediaSegments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
                    var allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
                    SamePathDic[task.Id] = allSamePath;
                }
                // 过滤不需要下载的片段
                if (streamSpec.Playlist!.MediaInitChanged)
                {
                    StopForMediaInitChange();
                    return;
                }

                FilterMediaSegments(streamSpec, task, allHasDatetime, SamePathDic[task.Id]);
                if (STOP_FLAG)
                    return;

                var newList = streamSpec.Playlist!.MediaParts[0].MediaSegments;
                if (newList.Count > 0)
                {
                    // 最后一个分片发生了变化，说明服务器有更新
                    Interlocked.Increment(ref updatedStreamCount);
                    task.MaxValue += newList.Count;
                    // 推送给消费者
                    await BlockDic[task.Id].SendAsync(newList);
                    // 更新最新链接
                    LastFileNameDic[task.Id] = GetSegmentName(newList.Last(), allHasDatetime, SamePathDic[task.Id]);
                    // 尝试更新时间戳
                    var dt = newList.Last().DateTime;
                    DateTimeDic[task.Id] = dt != null ? GetUnixTimestamp(dt.Value) : 0L;
                    // 累加已获取到的时长
                    RefreshedDurDic[task.Id] += newList.Sum(s => s.Duration);
                }

                if (!STOP_FLAG && RefreshedDurDic[task.Id] >= DownloaderConfig.MyOptions.LiveRecordLimit?.TotalSeconds)
                {
                    RecordLimitReachedDic[task.Id] = true;
                }

                // 检测时长限制
                if (!STOP_FLAG && RecordLimitReachedDic.Values.All(x => x))
                {
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimitReached}[/]");
                    STOP_FLAG = true;
                    CancellationTokenSource.Cancel();
                }
            });

            if (STOP_FLAG)
            {
                foreach (var target in BlockDic.Values)
                {
                    target.Complete();
                }
            }

            // 计算本次等待时长：
            // - 正常轮询：距离上一次请求满 WAIT_SEC（基础间隔，HLS 下为 #EXT-X-TARGETDURATION）后再请求
            // - 降级轮询：基础间隔来自 TARGETDURATION 且本轮未抓到任何新分片（服务器产出慢），
            //   则改为 TARGETDURATION / 2 后重试，尽快追上更新
            var waitSec = WAIT_SEC;
            if (WAIT_FROM_TARGET_DURATION && updatedStreamCount == 0)
            {
                waitSec = Math.Max(1, WAIT_SEC / 2);
                Logger.Debug($"no new segments, degrade refresh interval to {waitSec} seconds");
            }

            try
            {
                // Logger.WarnMarkUp($"wait {waitSec}s");
                if (!STOP_FLAG) await Task.Delay(waitSec * 1000, CancellationTokenSource.Token);
                // 刷新列表
                if (!STOP_FLAG)
                {
                    await StreamExtractor.RefreshPlayListAsync(dic.Keys.ToList());
                    DisableLiveSynthesisForImplicitIV();
                    await UpdateRawM3u8Async();
                }
            }
            catch (OperationCanceledException oce) when (oce.CancellationToken == CancellationTokenSource.Token)
            {
                // 不需要做事
            }
            catch (Exception e)
            {
                Logger.ErrorMarkUp(e);
                STOP_FLAG = true;
                // 停止所有Block
                foreach (var target in BlockDic.Values)
                {
                    target.Complete();
                }
            }
        }
    }

    private async Task UpdateRawM3u8Async()
    {
        if (!DownloaderConfig.MyOptions.LiveKeepM3u8Updated)
            return;

        if (!StreamExtractor.RawFiles.TryGetValue("raw.m3u8", out var rawM3u8) || string.IsNullOrWhiteSpace(rawM3u8))
            return;

        if (!Directory.Exists(DownloaderConfig.DirPrefix))
            Directory.CreateDirectory(DownloaderConfig.DirPrefix);

        var file = Path.Combine(DownloaderConfig.DirPrefix, "raw.m3u8");
        await rawM3u8Accumulator.UpdateAsync(file, rawM3u8);
    }

    private void FilterMediaSegments(StreamSpec streamSpec, ProgressTask task, bool allHasDatetime, bool allSamePath)
    {
        if (string.IsNullOrEmpty(LastFileNameDic[task.Id]) && DateTimeDic[task.Id] == 0) return;

        var index = -1;
        var dateTime = DateTimeDic[task.Id];
        var lastName = LastFileNameDic[task.Id];
        var lastUrlNumber = 0L;
        var usePredictableUrlPattern = DownloaderConfig.MyOptions.LiveFillSegmentsGap
            && TryGetPredictableSegmentUrlPattern(task, out _)
            && TryParseSegmentNumber(lastName, out lastUrlNumber);

        // 优先使用dateTime判断
        if (dateTime != 0 && streamSpec.Playlist!.MediaParts[0].MediaSegments.All(s => s.DateTime != null))
        {
            index = streamSpec.Playlist!.MediaParts[0].MediaSegments.FindIndex(s => GetUnixTimestamp(s.DateTime!.Value) == dateTime);
        }
        else if (usePredictableUrlPattern)
        {
            index = FindSegmentIndexByUrlNumber(streamSpec.Playlist!.MediaParts[0].MediaSegments, lastUrlNumber);
        }
        else
        {
            index = streamSpec.Playlist!.MediaParts[0].MediaSegments.FindIndex(s => GetSegmentName(s, allHasDatetime, allSamePath) == lastName);
        }

        if (index > -1)
        {
            // 修正Index
            var list = streamSpec.Playlist!.MediaParts[0].MediaSegments.Skip(index + 1).ToList();
            if (list.Count > 0)
            {
                if (usePredictableUrlPattern)
                {
                    list = ApplyPredictableSegmentUrlPattern(list, task, lastUrlNumber, SegmentGapSource.CurrentPlaylist) ?? list;
                }

                var newMin = list.Min(s => s.Index);
                var oldMax = MaxIndexDic[task.Id];
                if (newMin < oldMax)
                {
                    var offset = oldMax - newMin + 1;
                    foreach (var item in list)
                    {
                        item.Index += offset;
                    }
                }
                MaxIndexDic[task.Id] = list.Max(s => s.Index);
            }
            streamSpec.Playlist!.MediaParts[0].MediaSegments = list;
        }
        else if (DownloaderConfig.MyOptions.LiveFillSegmentsGap)
        {
            // lastName 在新 playlist 中找不到 -> 尝试按可预测命名规律补齐中间缺失的 segment
            // 典型场景：B 站直播这类 fmp4 流，文件名为连续递增的数字（与 EXT-X-MEDIA-SEQUENCE 一致），
            // 当刷新间隔过长时新 playlist 起点已经跳到上次最后下载片段之后多个位置。
            if (streamSpec.Playlist!.MediaInitChanged)
            {
                StopForMediaInitChange();
                return;
            }

            TryFillMissingSegments(streamSpec, task);
        }
    }

    /// <summary>
    /// 尝试基于可预测的文件名规律补齐 playlist 中间缺失的 segment
    /// 触发条件（全部满足）：
    ///   1. 新 playlist 至少有 1 个 segment
    ///   2. 首次 media playlist 时所有 segment 的 URL query string 相同，且文件名是与 Index 相等的纯数字并严格递增
    ///   3. 新 playlist 所有 segment 的 URL query string 相同，文件名是严格递增的纯数字
    ///   4. 上次记录的 LastFileName 也是纯数字且与 MaxIndexDic 一致
    ///   5. 缺失数量不超过上限（防止过度补齐）
    /// </summary>
    private void TryFillMissingSegments(StreamSpec streamSpec, ProgressTask task)
    {
        var newSegments = streamSpec.Playlist!.MediaParts[0].MediaSegments;
        if (newSegments.Count < 1) return;

        var oldMax = MaxIndexDic[task.Id];

        // 校验 LastFileName 也是纯数字且与 oldMax 一致
        if (!TryParseSegmentNumber(LastFileNameDic[task.Id], out var lastNumber) || lastNumber != oldMax)
            return;

        var filledSegments = ApplyPredictableSegmentUrlPattern(newSegments, task, lastNumber, SegmentGapSource.BetweenPlaylistRefreshes);
        if (filledSegments == null)
            return;

        streamSpec.Playlist!.MediaParts[0].MediaSegments = filledSegments;
    }

    private async Task DownloadLiveFromStartSegmentsAsync(
        StreamSpec streamSpec,
        ProgressTask task,
        SpeedContainer speedContainer,
        string tmpDir,
        Dictionary<string, string> headers,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        BufferBlock<List<MediaSegment>> source)
    {
        using var backfillCts = new CancellationTokenSource();
        try
        {
            var segments = streamSpec.Playlist?.MediaParts[0].MediaSegments.ToList();
            if (segments == null || segments.Count == 0)
                return;

            var streamLabel = streamSpec.ToShortShortString().EscapeMarkup();
            if (!TryGetPredictableSegmentUrlPattern(task, out _))
            {
                Logger.InfoMarkUp($"[darkorange3_1]Live from start skipped for {streamLabel}: segment URL pattern is not predictable.[/]");
                return;
            }

            var firstSegment = segments[0];
            var firstUrlParts = ParseSegmentUrl(firstSegment.Url);
            if (!TryParseSegmentNumber(firstUrlParts.FileNameWithoutExtension, out var firstNumber) || firstNumber <= 0)
                return;

            var allHasDatetime = segments.All(s => s.DateTime != null);
            var allName = segments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
            var allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
            var backfillSegmentDuration = streamSpec.Playlist?.TargetDuration is > 0
                ? streamSpec.Playlist.TargetDuration.Value
                : firstSegment.Duration;
            if (backfillSegmentDuration <= 0)
                backfillSegmentDuration = 1;

            var liveFromStartParallel = GetLiveFromStartParallel();

            // 增强③（多镜像分流，精细化）：
            // live-from-start 中所有镜像竞速统一只使用按顺序的前半部分，避免对全部镜像逐一施压。
            var effectiveMirrors = DownloaderConfig.MyOptions.LiveHostMirrors?
                .Where(m => !string.IsNullOrWhiteSpace(m)).ToArray() ?? [];
            // 镜像取前半部分（向下取整），但至少保留 1 个：1→1、2→1、3→1、4→2 ...
            var liveFromStartHostMirrors = effectiveMirrors.Length > 0
                ? effectiveMirrors.Take(Math.Max(1, effectiveMirrors.Length / 2)).ToArray()
                : [];
            var hasMirrors = liveFromStartHostMirrors.Length > 0;

            Logger.InfoMarkUp($"[darkorange3_1]Live from start: host mirrors for racing -> {string.Join(", ", liveFromStartHostMirrors.Select(m => m.EscapeMarkup()))}.[/]");

            var ctx = new LiveFromStartContext(
                Template: firstSegment,
                TemplateUrlParts: firstUrlParts,
                TemplateNumber: firstNumber,
                SegmentDuration: backfillSegmentDuration,
                AllHasDatetime: allHasDatetime,
                AllSamePath: allSamePath,
                TmpDir: tmpDir,
                Extension: streamSpec.Extension ?? "clip",
                SpeedContainer: speedContainer,
                Headers: headers,
                // 探测重试：候选总 host > 1（含原始URL + 前半镜像）时单 host 偶发失败由竞速兜底，
                // 且边界点是"当前 live-from-start host 集合不可用"，无需逐 host 重试（重试的 1000ms 延迟是探测主要耗时点），故置 0；
                // 单 host 时才保留 1 次，避免把瞬时抖动误判成可用边界。
                ProbeRetryCount: hasMirrors ? 0 : 1,
                HostMirrors: liveFromStartHostMirrors);

            // 增强②（缓存复用）：探测阶段命中过的 number 进缓存，回填阶段直接复用其已下载文件，绝不重复请求。
            // 增强③（多镜像分流）：所有探测与回填下载都经由 Downloader.DownloadSegmentAsync，
            // 并统一传入 live-from-start 的前半镜像列表，天然把探测/回填压力分散到镜像。
            var downloadCache = new Dictionary<long, (MediaSegment? Segment, DownloadResult? Result)>();

            Logger.InfoMarkUp($"[darkorange3_1]Live from start: locating earliest available segment before {firstNumber} for {streamLabel}.[/]");

            // ===== 阶段 1：指数探测 + 二分，对数次请求内定位最早可用边界 E0 =====
            var (earliestNumber, lastRequestUrl, useDescending) = await LocateEarliestAvailableLiveFromStartNumberAsync(ctx, firstNumber, 0, downloadCache, backfillCts.Token);

            var upper = firstNumber - 1;

            // 边界情况：可用区很浅 -> 倒序下载方案（从 S-1 并发向下镜像竞速、遇首个确认不可得即停、保留连续段接边）。
            // 倒序不锁定 host，故此处无需计算/锁定 pinned host。
            if (useDescending)
            {
                await BackfillDescendingAsync(ctx, task, streamLabel, upper, 0, fileDic, downloadCache, backfillCts);
                return;
            }

            if (earliestNumber == null || earliestNumber.Value > upper)
            {
                Logger.InfoMarkUp($"[darkorange3_1]Live from start: no earlier segment available before {firstNumber} for {streamLabel}.[/]");
                return;
            }

            // 仅在确实启用了镜像时才锁定到最后一次请求的 host（升序回填用）；否则保持默认（本就单 host）。
            var pinnedAuthority = hasMirrors ? TryParseUrlAuthority(lastRequestUrl) : null;
            if (pinnedAuthority != null)
                Logger.InfoMarkUp($"[darkorange3_1]Live from start: pinning backfill host to [cyan]{pinnedAuthority.Host.EscapeMarkup()}[/] for {streamLabel} (no multi-host racing).[/]");

            var boundaryNumber = earliestNumber.Value - 1;
            Logger.InfoMarkUp($"[darkorange3_1]Live from start: earliest available segment is {earliestNumber.Value} for {streamLabel}; backfilling ascending {earliestNumber.Value} ~ {upper}.[/]");

            // ===== 阶段 2：从 E0 升序高并发回填，跑在过期边界前面 =====
            // 关键：始终优先派发"当前最旧"未下载分片；只要下载快于实时（R·segDur>1），
            // 升序前沿恒高于回退的过期边界，理论零丢失。若某段确已过期则按需跳过（reactive，无需预测安全余量）。
            // E0 附近最贴近过期边界，先用镜像竞速抢救；连续稳定成功后切到 pinned host 降低带宽压力。
            // pinned-first 中若连续失败或靠 fallback 恢复过多，说明 pinned host 不稳定，切回 mirror-first。
            var mirrorStableSuccessThreshold = Math.Max(3, liveFromStartParallel * 2);
            var pinnedUnstableThreshold = Math.Max(6, liveFromStartParallel);
            var mirrorFirstMode = true;
            var consecutiveMirrorFirstSuccesses = 0;
            var consecutivePinnedFailures = 0;
            var pinnedFallbackRecoveries = 0;

            var nextDispatch = earliestNumber.Value;
            var nextCommit = earliestNumber.Value;
            var inFlight = new Dictionary<long, Task<LiveFromStartBackfillResolved>>();
            var resolved = new Dictionary<long, LiveFromStartBackfillResolved>();
            var downloadedSegments = new List<MediaSegment>();
            var committed = new HashSet<long>();
            var discardedExpiredNumbers = new List<long>();
            var dispatchCount = 0;
            var discardedCleanupCount = 0;
            var recoveredByFallbackCount = 0;

            while (!STOP_FLAG && !backfillCts.IsCancellationRequested && nextCommit <= upper)
            {
                // 升序派发
                while (inFlight.Count < liveFromStartParallel && nextDispatch <= upper
                       && !STOP_FLAG && !backfillCts.IsCancellationRequested)
                {
                    var number = nextDispatch;
                    if (downloadCache.TryGetValue(number, out var cached))
                    {
                        // 探测/小窗口缓存命中只复用文件，不参与 mirror-first / pinned-first 状态统计。
                        var mode = mirrorFirstMode ? LiveFromStartBackfillMode.MirrorFirst : LiveFromStartBackfillMode.PinnedFirst;
                        resolved[number] = new LiveFromStartBackfillResolved(cached.Segment, cached.Result, false, mode, CountForStats: false);
                    }
                    else
                    {
                        var mode = mirrorFirstMode || pinnedAuthority == null
                            ? LiveFromStartBackfillMode.MirrorFirst
                            : LiveFromStartBackfillMode.PinnedFirst;
                        inFlight[number] = CreateAndDownloadBackfillSegmentAsync(ctx, number, pinnedAuthority, backfillCts.Token, mode);
                        dispatchCount++;
                    }
                    nextDispatch++;
                }

                // 升序提交，保证后续合并顺序
                while (resolved.Remove(nextCommit, out var done))
                {
                    if (done.Result is { Success: true } && done.Segment != null)
                    {
                        var segment = done.Segment;
                        fileDic[segment] = done.Result;
                        downloadedSegments.Add(segment);
                        committed.Add(nextCommit);
                        lock (lockObj)
                        {
                            task.MaxValue += 1;
                        }
                        task.Increment(1);
                        RefreshedDurDic.AddOrUpdate(task.Id, segment.Duration, (_, old) => old + segment.Duration);

                        if (done.Mode == LiveFromStartBackfillMode.MirrorFirst)
                        {
                            if (done.CountForStats)
                            {
                                consecutivePinnedFailures = 0;
                                pinnedFallbackRecoveries = 0;
                                if (pinnedAuthority != null && mirrorFirstMode && ++consecutiveMirrorFirstSuccesses >= mirrorStableSuccessThreshold)
                                {
                                    mirrorFirstMode = false;
                                    consecutiveMirrorFirstSuccesses = 0;
                                    Logger.InfoMarkUp($"[darkorange3_1]Live from start: switching to pinned-first at segment {nextCommit} for {streamLabel} after {mirrorStableSuccessThreshold} mirror-first success(es); reducing mirror pressure.[/]");
                                }
                            }
                        }
                        else
                        {
                            if (done.CountForStats)
                            {
                                consecutiveMirrorFirstSuccesses = 0;
                                consecutivePinnedFailures = 0;
                                if (done.Recovered && ++pinnedFallbackRecoveries >= pinnedUnstableThreshold)
                                {
                                    mirrorFirstMode = true;
                                    pinnedFallbackRecoveries = 0;
                                    Logger.InfoMarkUp($"[darkorange3_1]Live from start: switching back to mirror-first at segment {nextCommit} for {streamLabel} after {pinnedUnstableThreshold} fallback recovery event(s); pinned host is unstable.[/]");
                                }
                            }
                        }
                    }
                    else
                    {
                        // 该分片经当前策略确认不可得——记录编号、跳过并清理
                        discardedExpiredNumbers.Add(nextCommit);
                        TryDeleteDownloadResult(done.Result);

                        if (done.CountForStats)
                            consecutiveMirrorFirstSuccesses = 0;
                        if (done.CountForStats && done.Mode == LiveFromStartBackfillMode.PinnedFirst && ++consecutivePinnedFailures >= pinnedUnstableThreshold)
                        {
                            if (!mirrorFirstMode)
                            {
                                mirrorFirstMode = true;
                                pinnedFallbackRecoveries = 0;
                                Logger.InfoMarkUp($"[darkorange3_1]Live from start: switching back to mirror-first at segment {nextCommit} for {streamLabel} after {pinnedUnstableThreshold} consecutive pinned-first failure(s); pinned host is unstable.[/]");
                            }
                        }
                    }
                    nextCommit++;
                }

                if (nextCommit > upper)
                    break;

                if (inFlight.Count == 0)
                {
                    // 提交前沿暂未就绪且无在途任务：若已派发到顶则结束，否则继续派发
                    if (nextDispatch > upper)
                        break;
                    continue;
                }

                var finished = await Task.WhenAny(inFlight.Values);
                var finishedNumber = inFlight.First(kv => kv.Value == finished).Key;
                inFlight.Remove(finishedNumber);
                var finishedResult = await finished;
                if (finishedResult.Recovered)
                    recoveredByFallbackCount++;
                resolved[finishedNumber] = finishedResult;
            }

            // ===== 收尾：取消并清理未提交的下载文件 =====
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
                    // 忽略已取消/失败的清理
                }
            }
            foreach (var kv in resolved)
            {
                if (kv.Value.Result is { Success: true } && !committed.Contains(kv.Key) && TryDeleteDownloadResult(kv.Value.Result))
                    discardedCleanupCount++;
            }
            foreach (var kv in downloadCache)
            {
                if (kv.Value.Result is { Success: true } && !committed.Contains(kv.Key)
                    && !resolved.ContainsKey(kv.Key) && !inFlight.ContainsKey(kv.Key)
                    && TryDeleteDownloadResult(kv.Value.Result))
                {
                    discardedCleanupCount++;
                }
            }

            downloadedSegments.Sort((left, right) => left.Index.CompareTo(right.Index));
            discardedExpiredNumbers.Sort();

            // ===== 关键：只保留"连接到直播边缘的连续段" =====
            // DVR 窗口最旧端往往参差不齐（老片正被 CDN 逐步轮转删除），二分只能定位到"个体可用的最低片" E0，
            // 它可能落在参差区底部，其上方仍存在无法补齐的空洞（当前 live-from-start host 集合 404）。这些空洞之下的碎片即使下到了，
            // 也无法跨过空洞与直播边缘连续拼接，对输出毫无价值，反而会在合并产物里制造断点。
            // 因此以"最高不可补齐空洞 G"为界：丢弃所有 index <= G 的已下载碎片，仅保留连续的 (G, upper]。
            var abandonedFragmentCount = 0;
            long? abandonedTailFloor = null;
            long? abandonedTailCeil = null;
            if (discardedExpiredNumbers.Count > 0 && downloadedSegments.Count > 0)
            {
                var highestGap = discardedExpiredNumbers[^1];
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
                    // 回退进度条与已刷新时长（这些碎片不计入最终可用产物）。
                    // 回填和实时下载并发更新同一个 ProgressTask，直接 Increment(-fragments.Count)
                    // 可能扣到并未计入 Value 的临时碎片，导致 Recording 计数出现负数。
                    // 此处处于 LiveFromStart 首次合并前，FileDic 仍保存了当前可用产物，按它重新校准完成数。
                    lock (lockObj)
                    {
                        task.MaxValue = Math.Max(0, task.MaxValue - fragments.Count);
                        var completedCount = fileDic.Count(i => i.Value is { Success: true });
                        task.Value = Math.Min(task.MaxValue, completedCount);
                    }
                    RefreshedDurDic.AddOrUpdate(task.Id, -rolledBackDuration, (_, old) => old - rolledBackDuration);

                    abandonedFragmentCount = fragments.Count;
                    abandonedTailFloor = earliestNumber.Value;
                    abandonedTailCeil = highestGap;
                    downloadedSegments = downloadedSegments.Where(s => s.Index > highestGap).ToList();
                }
            }

            // 裁剪后 downloadedSegments 即"连接到直播边缘的连续段"，accepted_range 现在应为单一连续区间。
            var acceptedRange = FormatContiguousIndexRanges(downloadedSegments.Select(s => s.Index).ToList());
            var earliestAvailable = downloadedSegments.Count > 0
                ? FormatLiveFromStartSegmentLabel(downloadedSegments[0])
                : earliestNumber.Value.ToString(CultureInfo.InvariantCulture);
            var failedBoundary = boundaryNumber >= 0 ? boundaryNumber.ToString(CultureInfo.InvariantCulture) : "none";
            var startedDownloads = downloadCache.Count + dispatchCount;
            var abandonedTailRange = abandonedTailCeil != null
                ? FormatSegmentRanges([new MissingSegmentRange(abandonedTailFloor!.Value, abandonedTailCeil.Value, SegmentGapSource.CurrentPlaylist)])
                : "none";
            Logger.InfoMarkUp($"[darkorange3_1]Live from start summary for {streamLabel}: accepted_total={downloadedSegments.Count}, earliest_available={earliestAvailable}, failed_boundary={failedBoundary}, accepted_range={acceptedRange}, boundary_probes={downloadCache.Count}, downloads_started={startedDownloads}, recovered_by_fallback={recoveredByFallbackCount}, abandoned_tail={abandonedTailRange}, abandoned_fragments={abandonedFragmentCount}, unfillable_gaps={discardedExpiredNumbers.Count}, discarded_cleanup={discardedCleanupCount}.[/]");

            if (downloadedSegments.Count > 0)
            {
                Logger.InfoMarkUp($"[darkorange3_1]Live from start downloaded {downloadedSegments.Count} contiguous earlier segment(s) for {streamLabel}: {acceptedRange}.[/]");
            }
            if (abandonedTailCeil != null)
            {
                Logger.WarnMarkUp($"[darkorange3_1]Live from start: abandoned ragged DVR tail {abandonedTailRange} for {streamLabel} ({abandonedFragmentCount} downloaded fragment(s) discarded, {discardedExpiredNumbers.Count} segment(s) unavailable on selected live-from-start hosts) — cannot connect to live edge across unfillable gap(s).[/]");
            }
        }
        catch (Exception ex)
        {
            Logger.InfoMarkUp($"[darkorange3_1]Live from start download failed: {ex.Message.EscapeMarkup()}[/]");
        }
        finally
        {
            // 唤醒消费者：即使没有回溯分片，也要让已经下载的当前分片开始首次合并。
            await source.SendAsync([]);
        }
    }

    /// <summary>
    /// 边界情况的倒序回填方案（可用区很浅，如刚开播只缺开头一分钟以内的分片）。
    /// 从 upper(=S-1) 并发向下下载，按降序提交，遇到第一个"镜像竞速后仍不可得"的分片即停，
    /// 保留 [boundary+1, upper] 这段连续片接到直播边缘。区间短、几乎不受过期影响，且天然连续、无需参差裁剪。
    /// 与升序回填不同：倒序**不锁定 host、直接使用 live-from-start 镜像子集竞速**（区间短，竞速代价可接受且更快更稳）；
    /// 重试次数走 GetLiveFromStartDownloadRetryCount()（有镜像=0、无镜像=1）。
    /// </summary>
    private async Task BackfillDescendingAsync(
        LiveFromStartContext ctx,
        ProgressTask task,
        string streamLabel,
        long upper,
        long floor,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        Dictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        CancellationTokenSource backfillCts)
    {
        var downloadedSegments = new List<MediaSegment>();
        var discardedCleanupCount = 0;

        Logger.InfoMarkUp($"[darkorange3_1]Live from start: descending backfill from {upper} downward for {streamLabel} (mirror race, stop at first unavailable).[/]");

        var scan = await RunDescendingMirrorRaceScanAsync(
            ctx,
            upper,
            floor,
            downloadCache,
            backfillCts.Token,
            cacheCompletedDownloads: false,
            onCommitSuccess: (_, segment, result, _) =>
            {
                fileDic[segment] = result;
                downloadedSegments.Add(segment);
                lock (lockObj)
                {
                    task.MaxValue += 1;
                }
                task.Increment(1);
                RefreshedDurDic.AddOrUpdate(task.Id, segment.Duration, (_, old) => old + segment.Duration);
            });

        // 收尾：取消并清理"已派发到 boundary 以下、未提交"的下载（它们在空洞下方，无法连续利用）
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
                // 忽略已取消/失败的清理
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
            ? FormatLiveFromStartSegmentLabel(downloadedSegments[0])
            : "none";
        var failedBoundary = scan.Boundary != null ? scan.Boundary.Value.ToString(CultureInfo.InvariantCulture) : "none";
        var startedDownloads = scan.DispatchCount + scan.ReusedFromCache;
        Logger.InfoMarkUp($"[darkorange3_1]Live from start summary (descending) for {streamLabel}: accepted_total={downloadedSegments.Count}, earliest_available={earliestAvailable}, failed_boundary={failedBoundary}, accepted_range={acceptedRange}, boundary_probes={downloadCache.Count}, downloads_started={startedDownloads}, discarded_cleanup={discardedCleanupCount}.[/]");

        if (downloadedSegments.Count > 0)
        {
            Logger.InfoMarkUp($"[darkorange3_1]Live from start downloaded {downloadedSegments.Count} contiguous earlier segment(s) for {streamLabel}: {acceptedRange}.[/]");
        }
    }

    private async Task<LiveFromStartDescendingScanResult> RunDescendingMirrorRaceScanAsync(
        LiveFromStartContext ctx,
        long upper,
        long floor,
        Dictionary<long, (MediaSegment? Segment, DownloadResult? Result)> downloadCache,
        CancellationToken cancellationToken,
        bool cacheCompletedDownloads,
        Action<long, MediaSegment, DownloadResult, bool>? onCommitSuccess = null)
    {
        var parallel = GetLiveFromStartParallel();
        var nextDispatch = upper;
        var nextCommit = upper;
        var inFlight = new Dictionary<long, Task<(MediaSegment Segment, DownloadResult? Result)>>();
        var resolved = new Dictionary<long, LiveFromStartDescendingResolved>();
        var committed = new HashSet<long>();
        var dispatchCount = 0;
        var reusedFromCache = 0;
        long? boundary = null;
        var stop = false;

        while (!STOP_FLAG && !cancellationToken.IsCancellationRequested && !stop && nextCommit >= floor)
        {
            // 降序派发；缓存里的失败需重新走镜像竞速确认边界。
            while (inFlight.Count < parallel && nextDispatch >= floor
                   && !STOP_FLAG && !cancellationToken.IsCancellationRequested)
            {
                var number = nextDispatch;
                if (downloadCache.TryGetValue(number, out var cached) && cached.Result is { Success: true })
                {
                    resolved[number] = new LiveFromStartDescendingResolved(cached.Segment, cached.Result, true);
                    reusedFromCache++;
                }
                else
                {
                    inFlight[number] = CreateAndDownloadMirrorRaceSegmentAsync(ctx, number, cancellationToken);
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
            resolved[finishedNumber] = new LiveFromStartDescendingResolved(finishedResult.Segment, finishedResult.Result, false);
        }

        return new LiveFromStartDescendingScanResult(
            inFlight,
            resolved,
            committed,
            boundary,
            dispatchCount,
            reusedFromCache);
    }

    /// <summary>
    /// 构造并下载指定编号的回填分片：使用 live-from-start 镜像子集竞速。
    /// 重试次数走 GetLiveFromStartDownloadRetryCount()（有镜像=0、无镜像=1）。用于倒序回填方案。
    /// </summary>
    private async Task<(MediaSegment Segment, DownloadResult? Result)> CreateAndDownloadMirrorRaceSegmentAsync(
        LiveFromStartContext ctx,
        long number,
        CancellationToken cancellationToken)
    {
        var candidate = CreateFilledSegment(ctx.Template, ctx.TemplateUrlParts, number, ctx.TemplateNumber, ctx.SegmentDuration);
        if (candidate == null)
            return (new MediaSegment { Index = number }, null);

        var filename = GetSegmentName(candidate, ctx.AllHasDatetime, ctx.AllSamePath);
        var path = Path.Combine(ctx.TmpDir, filename + $".{ctx.Extension}.tmp");
        return await DownloadLiveFromStartSegmentAsync(candidate, path, ctx.SpeedContainer, ctx.Headers, cancellationToken, hostMirrorsOverride: ctx.HostMirrors);
    }

    /// <summary>
    /// 阶段 1：指数探测 + 二分缩窗 + 小窗口倒序收尾，定位 [floor, topAvailableSentinel) 区间内"最低可用"的 segment 编号（即 DVR 窗口的最早可用边界 E0）。
    /// 假定可用性单调：&gt;= E0 可用、&lt; E0 不可用。探测/收尾期间下载到的可用分片全部进缓存，供升序回填复用。
    /// </summary>
    /// <returns>(最早可用编号 E0, 最后一次请求的 URL, 是否改用倒序下载)；
    /// 若 sentinel 之下没有任何可用分片则 E0 为 null；
    /// 若检测到"可用区很浅"（首次失败发生在 depth&gt;60 且此前最深仅确认到 depth≤60）则 UseDescending 为 true（E0 不再有意义，交由倒序方案处理）。</returns>
    private async Task<(long? Earliest, string? LastRequestUrl, bool UseDescending)> LocateEarliestAvailableLiveFromStartNumberAsync(
        LiveFromStartContext ctx,
        long topAvailableSentinel,
        long floor,
        Dictionary<long, (MediaSegment? Segment, DownloadResult? Result)> cache,
        CancellationToken cancellationToken)
    {
        var probeCount = 0;
        string? lastRequestUrl = null;

        async Task<DownloadResult?> ProbeAsync(long number, string phase)
        {
            probeCount++;
            Logger.InfoMarkUp($"[darkorange3_1]Live from start probe #{probeCount} ({phase}): checking segment {number}...[/]");
            var (_, result) = await ProbeLiveFromStartNumberAsync(ctx, cache, number, cancellationToken);
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

        async Task<long?> FinishSmallWindowDescendingAsync(long lo, long hi)
        {
            using var finishCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Logger.InfoMarkUp($"[darkorange3_1]Live from start: boundary window {lo} ~ {hi} is small enough; finishing by descending concurrent download.[/]");

            var scan = await RunDescendingMirrorRaceScanAsync(
                ctx,
                hi,
                lo,
                cache,
                finishCts.Token,
                cacheCompletedDownloads: true,
                onCommitSuccess: (_, _, result, reused) =>
                {
                    if (!reused && !string.IsNullOrWhiteSpace(result.RequestUrl))
                        lastRequestUrl = result.RequestUrl;
                });

            finishCts.Cancel();

            foreach (var kv in scan.InFlight)
            {
                try
                {
                    var (_, result) = await kv.Value;
                    TryDeleteDownloadResult(result);
                }
                catch
                {
                    // 忽略已取消/失败的清理
                }
            }

            foreach (var item in scan.Resolved.Values)
            {
                if (!item.Reused)
                    TryDeleteDownloadResult(item.Result);
            }

            if (STOP_FLAG || cancellationToken.IsCancellationRequested)
                return null;

            // 若 hi 自身失效，视为窗口锚点过期：这个混沌小窗口可完全放弃，直接从 hi+1 继续接边。
            return scan.Boundary != null ? scan.Boundary.Value + 1 : lo;
        }

        // 指数探测：从 sentinel-1 向下，步长翻倍，直到命中一个不可用编号作为下界
        long step = 1;
        var lastAvailable = topAvailableSentinel; // sentinel 自身来自当前 playlist，假定可用
        long? firstUnavailable = null;
        var probe = topAvailableSentinel - 1;

        while (probe >= floor && !STOP_FLAG && !cancellationToken.IsCancellationRequested)
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

                // 边界情况：可用区很浅（典型为刚开播不久、只缺开头一分钟以内的分片）。
                // 只要首次失败时"此前最深仅确认到 depth≤60"（即整段可用区都被夹在 ~60 深以内，含极浅情形），
                // 就不再做二分+升序，转交"倒序下载"方案——区间短、几乎不受过期影响，且天然停在第一个空洞、保证连续接边。
                // 深 DVR 会先在 depth>60 处探测成功（使 lastAvailableDepth>60），不会命中此分支，仍走二分+升序。
                var failedDepth = topAvailableSentinel - probe;
                var lastAvailableDepth = topAvailableSentinel - lastAvailable;
                if (failedDepth == 1)
                {
                    Logger.InfoMarkUp($"[darkorange3_1]Live from start: segment {probe} immediately before current playlist is unavailable; no contiguous earlier segment can connect to live edge.[/]");
                    return (null, lastRequestUrl, false);
                }

                if (lastAvailableDepth <= 60)
                {
                    Logger.InfoMarkUp($"[darkorange3_1]Live from start: shallow available region (first failure at depth {failedDepth}, deepest confirmed at depth {lastAvailableDepth}); switching to descending backfill.[/]");
                    return (null, lastRequestUrl, true);
                }
                break;
            }
        }

        if (STOP_FLAG || cancellationToken.IsCancellationRequested)
            return (null, lastRequestUrl, false);

        long lo;
        long hi;
        if (firstUnavailable == null)
        {
            // 一路探到 floor 仍全部可用
            if (lastAvailable >= topAvailableSentinel)
                return (null, lastRequestUrl, false); // sentinel 之下没有任何可用分片
            lo = floor;
            hi = lastAvailable;
        }
        else
        {
            if (lastAvailable >= topAvailableSentinel)
                return (null, lastRequestUrl, false); // 连 sentinel-1 都不可用，没有更早分片可下
            lo = firstUnavailable.Value + 1;
            hi = lastAvailable;
        }

        // small-window 是 DVR 最旧端的混沌小窗口：可用性可能快速变化，且窗口足够短；
        // 一旦 high 锚点失效，允许完全放弃该小窗口，改从 high+1 接续回填。
        var finishWindowSegments = Math.Max(1, (int)Math.Ceiling(40d / ctx.SegmentDuration));
        if (lo < hi)
            Logger.InfoMarkUp($"[darkorange3_1]Live from start: narrowing earliest available within {lo} ~ {hi} (binary search until window <= {finishWindowSegments} segment(s), 40s / targetDuration={ctx.SegmentDuration.ToString("0.###", CultureInfo.InvariantCulture)}).[/]");

        // 二分：在 [lo, hi] 中把候选窗口缩小到约 40 秒；小窗口交给倒序并发下载收尾。
        while (lo < hi && hi - lo + 1 > finishWindowSegments
               && !STOP_FLAG && !cancellationToken.IsCancellationRequested)
        {
            var mid = lo + (hi - lo) / 2;
            var result = await ProbeAsync(mid, $"binary, window {lo}~{hi}");
            if (result is { Success: true })
                hi = mid;
            else
                lo = mid + 1;
        }

        if (lo < hi && !STOP_FLAG && !cancellationToken.IsCancellationRequested)
        {
            var earliest = await FinishSmallWindowDescendingAsync(lo, hi);
            return (earliest, lastRequestUrl, false);
        }

        return STOP_FLAG || cancellationToken.IsCancellationRequested ? (null, lastRequestUrl, false) : (lo, lastRequestUrl, false);
    }

    /// <summary>
    /// 顺序探测单个编号（带缓存）。仅用于阶段 1（单线程），因此可安全写入共享缓存字典。
    /// 探测使用 ctx.HostMirrors（多镜像时为前半部分），以减小对数次探测对镜像的压力。
    /// </summary>
    private async Task<(MediaSegment? Segment, DownloadResult? Result)> ProbeLiveFromStartNumberAsync(
        LiveFromStartContext ctx,
        Dictionary<long, (MediaSegment? Segment, DownloadResult? Result)> cache,
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

        var filename = GetSegmentName(candidate, ctx.AllHasDatetime, ctx.AllSamePath);
        var path = Path.Combine(ctx.TmpDir, filename + $".{ctx.Extension}.tmp");

        // 探测整体超时：不可用分片的多 host 竞速需要等所有 host 都失败才退出，
        // 若某 host 卡住（无响应），会一直拖到下载器内部的停滞判定（约 10s）才放弃，得不偿失。
        // 给整个探测请求设一个 WAIT_SEC*2 的硬超时（下限 2s、上限 5s），到点取消全部竞速、快速判失败。
        var probeTimeoutSec = Math.Clamp(WAIT_SEC * 2, 2, 5);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(probeTimeoutSec));
        var (_, result) = await DownloadLiveFromStartSegmentAsync(candidate, path, ctx.SpeedContainer, ctx.Headers, timeoutCts.Token, ctx.ProbeRetryCount, ctx.HostMirrors);
        var entry = ((MediaSegment?)candidate, result);
        cache[number] = entry;
        return entry;
    }

    /// <summary>
    /// 阶段 2：构造并下载指定编号的回填分片（并发安全，不写共享缓存）。
    /// MirrorFirst 直接使用 live-from-start 镜像子集竞速；PinnedFirst 优先锁定到最后一次请求的 host，失败后再镜像竞速兜底。
    /// 返回值 Recovered 标识 PinnedFirst 中 pinned host 失败后由镜像竞速救回；CountForStats 标识是否参与策略切换统计。
    /// </summary>
    private async Task<LiveFromStartBackfillResolved> CreateAndDownloadBackfillSegmentAsync(
        LiveFromStartContext ctx,
        long number,
        Uri? pinnedAuthority,
        CancellationToken cancellationToken,
        LiveFromStartBackfillMode mode)
    {
        var candidate = CreateFilledSegment(ctx.Template, ctx.TemplateUrlParts, number, ctx.TemplateNumber, ctx.SegmentDuration);
        if (candidate == null)
            return new LiveFromStartBackfillResolved(new MediaSegment { Index = number }, null, false, mode, CountForStats: false);

        var filename = GetSegmentName(candidate, ctx.AllHasDatetime, ctx.AllSamePath);
        var path = Path.Combine(ctx.TmpDir, filename + $".{ctx.Extension}.tmp");

        if (mode == LiveFromStartBackfillMode.MirrorFirst || pinnedAuthority == null)
        {
            var mirrorRace = await DownloadLiveFromStartSegmentAsync(candidate, path, ctx.SpeedContainer, ctx.Headers, cancellationToken, hostMirrorsOverride: ctx.HostMirrors);
            return new LiveFromStartBackfillResolved(mirrorRace.Segment, mirrorRace.Result, false, LiveFromStartBackfillMode.MirrorFirst, CountForStats: true);
        }

        // 锁定 host 单点下载（hostMirrorsOverride 传空数组 => 不竞速）
        var originalUrl = candidate.Url;
        if (!UrlHasAuthority(originalUrl, pinnedAuthority))
            candidate.Url = RewriteUrlAuthority(originalUrl, pinnedAuthority);
        var pinned = await DownloadLiveFromStartSegmentAsync(candidate, path, ctx.SpeedContainer, ctx.Headers, cancellationToken, hostMirrorsOverride: []);
        if (pinned.Result is { Success: true })
            return new LiveFromStartBackfillResolved(pinned.Segment, pinned.Result, false, LiveFromStartBackfillMode.PinnedFirst, CountForStats: true);

        if (STOP_FLAG || cancellationToken.IsCancellationRequested)
            return new LiveFromStartBackfillResolved(candidate, null, false, LiveFromStartBackfillMode.PinnedFirst, CountForStats: false);

        // 兜底：锁定 host 缺该片 -> 恢复原始 URL，回退一次 live-from-start 镜像子集竞速。
        candidate.Url = originalUrl;
        var fallbackRace = await DownloadLiveFromStartSegmentAsync(candidate, path, ctx.SpeedContainer, ctx.Headers, cancellationToken, hostMirrorsOverride: ctx.HostMirrors);
        var recovered = fallbackRace.Result is { Success: true };
        if (recovered)
            Logger.DebugMarkUp($"[grey]Live from start: segment {number} missing on pinned host, recovered via mirror race.[/]");
        return new LiveFromStartBackfillResolved(fallbackRace.Segment, fallbackRace.Result, recovered, LiveFromStartBackfillMode.PinnedFirst, CountForStats: true);
    }

    private static Uri? TryParseUrlAuthority(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static bool UrlHasAuthority(string url, Uri authority)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, authority.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(uri.Host, authority.Host, StringComparison.OrdinalIgnoreCase)
               && uri.Port == authority.Port;
    }

    private static string RewriteUrlAuthority(string url, Uri authority)
    {
        try
        {
            var ub = new UriBuilder(new Uri(url))
            {
                Scheme = authority.Scheme,
                Host = authority.Host,
                Port = authority.IsDefaultPort ? -1 : authority.Port,
            };
            return ub.Uri.AbsoluteUri;
        }
        catch
        {
            return url;
        }
    }

    private async Task<(MediaSegment Segment, DownloadResult? Result)> DownloadLiveFromStartSegmentAsync(
        MediaSegment segment,
        string path,
        SpeedContainer speedContainer,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken,
        int? retryCount = null,
        string[]? hostMirrorsOverride = null)
    {
        try
        {
            var effectiveHostMirrors = hostMirrorsOverride ?? [];
            var result = await Downloader.DownloadSegmentAsync(segment, path, speedContainer, headers, retryCount ?? GetLiveFromStartDownloadRetryCount(effectiveHostMirrors), cancellationToken, effectiveHostMirrors);
            return (segment, result);
        }
        catch
        {
            return (segment, null);
        }
    }

    private int GetLiveFromStartParallel()
    {
        const int maxLiveFromStartParallel = 16;
        var threadCount = Math.Max(1, DownloaderConfig.MyOptions.ThreadCount);
        return Math.Min(maxLiveFromStartParallel, Math.Max(threadCount / 2, 1));
    }

    private static int GetLiveFromStartDownloadRetryCount(string[] hostMirrors)
    {
        return hostMirrors.Length > 0 && hostMirrors.Any(m => !string.IsNullOrWhiteSpace(m)) ? 0 : 1;
    }

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
            // 忽略未提交分片的清理失败
        }

        return true;
    }

    private static string FormatLiveFromStartSegmentLabel(MediaSegment segment)
    {
        var fileName = OtherUtil.GetFileNameFromInput(segment.Url, false);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = segment.Index.ToString(CultureInfo.InvariantCulture);

        return $"{fileName.EscapeMarkup()}(index={segment.Index.ToString(CultureInfo.InvariantCulture)})";
    }

    private async Task DecryptPendingMp4SegmentsAsync(
        IReadOnlyList<MediaSegment> segments,
        ConcurrentDictionary<MediaSegment, DownloadResult?> fileDic,
        ConcurrentDictionary<MediaSegment, bool> decryptedSegments,
        string? currentKID,
        string mp4InitFile,
        DecryptEngine decryptEngine,
        string decryptionBinaryPath)
    {
        if (!DownloaderConfig.MyOptions.MP4RealTimeDecryption || string.IsNullOrEmpty(currentKID))
            return;

        foreach (var seg in segments)
        {
            if (!seg.IsEncrypted || decryptedSegments.ContainsKey(seg))
                continue;

            if (!fileDic.TryGetValue(seg, out var result) || result is not { Success: true })
                continue;

            if (await TryDecryptMediaSegmentAsync(seg, result, currentKID, mp4InitFile, decryptEngine, decryptionBinaryPath))
                decryptedSegments[seg] = true;
        }
    }

    private bool TryGetPredictableSegmentUrlPattern(ProgressTask task, out SegmentUrlPatternCheck pattern)
    {
        return SegmentUrlPatternDic.TryGetValue(task.Id, out pattern)
            && pattern.SameQuery
            && pattern.NumericFileNameMatchesIndex
            && pattern.StrictlyIncreasing;
    }

    private bool ShouldDelayRealTimeMergeForLiveFromStart(Stream? fileOutputStream, bool liveFromStartMergeReady)
    {
        return DownloaderConfig.MyOptions.LiveFromStart
            && DownloaderConfig.MyOptions.LiveRealTimeMerge
            && fileOutputStream == null
            && !liveFromStartMergeReady;
    }

    private static int FindSegmentIndexByUrlNumber(IReadOnlyList<MediaSegment> segments, long number)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            var urlParts = ParseSegmentUrl(segments[i].Url);
            if (TryParseSegmentNumber(urlParts.FileNameWithoutExtension, out var segmentNumber) && segmentNumber == number)
                return i;
        }

        return -1;
    }

    private List<MediaSegment>? ApplyPredictableSegmentUrlPattern(
        IReadOnlyList<MediaSegment> segments,
        ProgressTask task,
        long previousNumber,
        SegmentGapSource firstGapSource)
    {
        if (segments.Count == 0)
            return [];

        if (!TryGetPredictableSegmentUrlPattern(task, out _))
            return null;

        var segmentInfos = new List<(MediaSegment Segment, SegmentUrlParts UrlParts, long Number)>(segments.Count);
        long? lastNumber = null;
        string? query = null;
        var missingCount = 0L;
        var missingRanges = new List<MissingSegmentRange>();

        foreach (var segment in segments)
        {
            var urlParts = ParseSegmentUrl(segment.Url);
            if (!TryParseSegmentNumber(urlParts.FileNameWithoutExtension, out var number))
                return null;

            query ??= urlParts.Query;
            if (urlParts.Query != query)
                return null;

            if (lastNumber != null && number <= lastNumber.Value)
                return null;

            var previous = lastNumber ?? previousNumber;
            if (number <= previous)
                return null;

            var currentMissingCount = number - previous - 1;
            if (currentMissingCount > 0)
            {
                var source = lastNumber == null ? firstGapSource : SegmentGapSource.CurrentPlaylist;
                missingRanges.Add(new MissingSegmentRange(previous + 1, number - 1, source));
                missingCount += currentMissingCount;
            }

            lastNumber = number;
            segmentInfos.Add((segment, urlParts, number));
        }

        var shouldFill = missingCount > 0;
        var maxFill = DownloaderConfig.MyOptions.LiveFillSegmentsGapMax!.Value;
        if (missingCount > maxFill)
        {
            Logger.WarnMarkUp($"[darkorange3_1]Detected {missingCount} missing segment(s) in predictable URL pattern ({FormatMissingSegmentRanges(missingRanges)}), which exceeds max fill limit ({maxFill}). Skipping fill.[/]");
            shouldFill = false;
        }

        var result = new List<MediaSegment>(segments.Count + (shouldFill ? (int)missingCount : 0));
        long prevNumber = previousNumber;
        long? firstFilled = null;
        long? lastFilled = null;

        foreach (var (segment, urlParts, number) in segmentInfos)
        {
            if (shouldFill)
            {
                for (var idx = prevNumber + 1; idx < number; idx++)
                {
                    var filledSegment = CreateFilledSegment(segment, urlParts, idx, number);
                    if (filledSegment == null)
                        return null;

                    firstFilled ??= idx;
                    lastFilled = idx;
                    result.Add(filledSegment);
                }
            }

            segment.Index = number;
            result.Add(segment);
            prevNumber = number;
        }

        if (firstFilled != null && lastFilled != null)
        {
            Logger.WarnMarkUp($"[darkorange3_1]Detected {missingCount} missing segment(s) in predictable URL pattern ({FormatMissingSegmentRanges(missingRanges)}), filling.[/]");
        }

        return result;
    }

    private static string FormatMissingSegmentRanges(IReadOnlyList<MissingSegmentRange> ranges)
    {
        if (ranges.Count == 0)
            return "none";

        var betweenPlaylistRanges = ranges.Where(r => r.Source == SegmentGapSource.BetweenPlaylistRefreshes).ToList();
        var currentPlaylistRanges = ranges.Where(r => r.Source == SegmentGapSource.CurrentPlaylist).ToList();
        var parts = new List<string>(2);

        if (betweenPlaylistRanges.Count > 0)
            parts.Add($"between playlist refreshes: {FormatSegmentRanges(betweenPlaylistRanges)}");

        if (currentPlaylistRanges.Count > 0)
            parts.Add($"inside current media playlist: {FormatSegmentRanges(currentPlaylistRanges)}");

        return string.Join("; ", parts);
    }

    private static string FormatSegmentRanges(IEnumerable<MissingSegmentRange> ranges)
    {
        return string.Join(", ", ranges.Select(r => r.Start == r.End ? r.Start.ToString() : $"{r.Start} ~ {r.End}"));
    }

    /// <summary>
    /// 把一组升序编号压缩成连续子区间字符串（遇到断点就断开），例如 [1,2,3,7,8] -> "1 ~ 3, 7 ~ 8"。
    /// 用于真实反映 live-from-start 已接受/缺失片段的范围，避免把空洞误并入单一区间。
    /// </summary>
    private static string FormatContiguousIndexRanges(IReadOnlyList<long> sortedIndices)
    {
        if (sortedIndices.Count == 0)
            return "none";

        var ranges = new List<MissingSegmentRange>();
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

            ranges.Add(new MissingSegmentRange(start, prev, SegmentGapSource.CurrentPlaylist));
            start = current;
            prev = current;
        }

        ranges.Add(new MissingSegmentRange(start, prev, SegmentGapSource.CurrentPlaylist));
        return FormatSegmentRanges(ranges);
    }

    private static MediaSegment? CreateFilledSegment(MediaSegment template, SegmentUrlParts templateUrlParts, long index, long templateNumber, double? segmentDuration = null)
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
            NameFromVar = null, // 强制按 URL 推导文件名
            EncryptInfo = new EncryptInfo
            {
                Method = template.EncryptInfo.Method,
                Key = template.EncryptInfo.Key,
                IV = template.EncryptInfo.IV != null ? (byte[])template.EncryptInfo.IV.Clone() : null,
                HasExplicitIV = template.EncryptInfo.HasExplicitIV,
            },
        };
    }

    private static string FormatSegmentNumber(long number, string templateFileName)
    {
        if (templateFileName.Length > 1 && templateFileName[0] == '0')
            return number.ToString($"D{templateFileName.Length}", CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParseSegmentNumber(string value, out long number)
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
    /// 检查首次 playlist 的 segment URL 是否具备可补片的基础规律
    /// </summary>
    private static SegmentUrlPatternCheck CheckSegmentUrlPattern(IReadOnlyList<MediaSegment> segments)
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

    /// <summary>
    /// 解析 segment URL 的 path、query、文件名与扩展名
    /// </summary>
    private static SegmentUrlParts ParseSegmentUrl(string url)
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

    /// <summary>
    /// 将 URL 中文件名部分替换为新文件名，保留 query string
    /// </summary>
    private static string? ReplaceUrlFileName(SegmentUrlParts urlParts, string newFileNameNoExt)
    {
        var path = urlParts.Path;
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0) return null;

        return path[..(lastSlash + 1)] + newFileNameNoExt + urlParts.Extension + urlParts.Query;
    }

    private static long GetDefaultLiveFillSegmentsGapMax(int waitTime)
    {
        return Math.Max(1L, Math.Min(60L, (long)Math.Ceiling(60D / waitTime)));
    }

    private void ResolveLiveRefreshInterval()
    {
        if (WAIT_SEC != 0)
            return;

        if (DownloaderConfig.MyOptions.LiveWaitTime != null)
        {
            // 用户手动指定刷新间隔，优先级最高
            WAIT_SEC = DownloaderConfig.MyOptions.LiveWaitTime.Value;
        }
        else if (SelectedSteams.All(s => s.Playlist!.TargetDuration is > 0))
        {
            // 优先以 #EXT-X-TARGETDURATION 作为基础轮询等待时长（取各流最小值，保证最快流不丢片）
            WAIT_SEC = (int)Math.Ceiling(SelectedSteams.Min(s => s.Playlist!.TargetDuration!.Value));
            WAIT_FROM_TARGET_DURATION = true;
        }
        else
        {
            // 后备方案：没有 #EXT-X-TARGETDURATION 时，按 总分片时长/2 - 2 估算
            WAIT_SEC = (int)(SelectedSteams.Min(s => s.Playlist!.MediaParts[0].MediaSegments.Sum(s => s.Duration)) / 2);
            WAIT_SEC -= 2; // 再提前两秒吧 留出冗余
        }

        if (WAIT_SEC <= 0) WAIT_SEC = 1;
        Logger.WarnMarkUp($"set refresh interval to {WAIT_SEC} seconds{(WAIT_FROM_TARGET_DURATION ? " (based on #EXT-X-TARGETDURATION)" : "")}");
        DownloaderConfig.MyOptions.LiveFillSegmentsGapMax ??= GetDefaultLiveFillSegmentsGapMax(WAIT_SEC);
    }

    private void DisableAudioBasedVttFixWithoutAudioStream()
    {
        if (SelectedSteams.All(x => x.MediaType != MediaType.AUDIO))
        {
            DownloaderConfig.MyOptions.LiveFixVttByAudio = false;
        }
    }

    private ProgressColumn[] BuildLiveProgressColumns(ConcurrentDictionary<int, SpeedContainer> speedContainerDic)
    {
        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn() { Alignment = Justify.Left },
            new RecordingDurationColumn(RecordedDurDic, RefreshedDurDic),
            new RecordingStatusColumn(),
            new PercentageColumn(),
            new DownloadSpeedColumn(speedContainerDic),
            new SpinnerColumn(),
        };

        return DownloaderConfig.MyOptions.NoAnsiColor
            ? progressColumns.SkipLast(1).ToArray()
            : progressColumns;
    }

    public async Task<bool> StartRecordAsync()
    {
        DisableLiveSynthesisForImplicitIV();

        var takeLastCount = DownloaderConfig.MyOptions.LiveFromStart ? int.MaxValue : DownloaderConfig.MyOptions.LiveTakeCount;
        ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); // 速度计算
        ConcurrentDictionary<StreamSpec, bool?> Results = new();
        // 同步流
        FilterUtil.SyncStreams(SelectedSteams, takeLastCount);
        ResolveLiveRefreshInterval();
        // 如果没有选中音频 取消通过音频修复vtt时间轴
        DisableAudioBasedVttFixWithoutAudioStream();

        /*// 写出master
        if (DownloaderConfig.MyOptions.LiveWriteHLS)
        {
            var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            var saveName = DownloaderConfig.MyOptions.SaveName ?? DateTime.Now.ToString("yyyyMMddHHmmss");
            await StreamingUtil.WriteMasterListAsync(SelectedSteams, saveName, saveDir);
        }*/

        await UpdateRawM3u8Async();

        var progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
        progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;

        progress.Columns(BuildLiveProgressColumns(SpeedContainerDic));

        await progress.StartAsync(async ctx =>
        {
            // 创建任务
            var dic = new Dictionary<StreamSpec, ProgressTask>();
            foreach (var item in SelectedSteams)
            {
                var task = ctx.AddTask(item.ToShortShortString(), autoStart: false, maxValue: 0);
                SpeedContainerDic[task.Id] = new SpeedContainer(); // 速度计算
                // 限速设置
                if (DownloaderConfig.MyOptions.MaxSpeed != null)
                {
                    SpeedContainerDic[task.Id].SpeedLimit = DownloaderConfig.MyOptions.MaxSpeed.Value;
                }
                LastFileNameDic[task.Id] = "";
                RecordLimitReachedDic[task.Id] = false;
                DateTimeDic[task.Id] = 0L;
                RecordedDurDic[task.Id] = 0;
                RefreshedDurDic[task.Id] = 0;
                SegmentUrlPatternDic[task.Id] = CheckSegmentUrlPattern(item.Playlist?.MediaParts[0].MediaSegments ?? []);
                MaxIndexDic[task.Id] = item.Playlist?.MediaParts[0].MediaSegments.LastOrDefault()?.Index ?? 0L; // 最大Index
                BlockDic[task.Id] = new BufferBlock<List<MediaSegment>>();
                dic[item] = task;
            }

            DownloaderConfig.MyOptions.ConcurrentDownload = true;
            DownloaderConfig.MyOptions.MP4RealTimeDecryption = true;
            DownloaderConfig.MyOptions.LiveRecordLimit ??= TimeSpan.MaxValue;
            if (DownloaderConfig.MyOptions is { MP4RealTimeDecryption: true, DecryptionEngine: not DecryptEngine.SHAKA_PACKAGER, Keys.Length: > 0 })
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.realTimeDecMessage}[/]");
            var limit = DownloaderConfig.MyOptions.LiveRecordLimit;
            if (limit != TimeSpan.MaxValue)
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimit}{GlobalUtil.FormatTime((int)limit.Value.TotalSeconds)}[/]");
            // 录制直播时，用户选了几个流就并发录几个
            var options = new ParallelOptions()
            {
                MaxDegreeOfParallelism = SelectedSteams.Count
            };
            // 开始刷新
            var producerTask = PlayListProduceAsync(dic);
            // 并发下载
            await Parallel.ForEachAsync(dic, options, async (kp, _) =>
            {
                var task = kp.Value;
                var consumerTask = RecordStreamAsync(kp.Key, task, SpeedContainerDic[task.Id], BlockDic[task.Id]);
                Results[kp.Key] = await consumerTask;
            });
        });

        var success = Results.Values.All(v => v == true);

        // 删除临时文件夹
        if (DownloaderConfig.MyOptions is { SkipMerge: false, DelAfterDone: true } && success)
        {
            foreach (var item in StreamExtractor.RawFiles)
            {
                var file = Path.Combine(DownloaderConfig.DirPrefix, item.Key);
                if (File.Exists(file)) File.Delete(file);
            }
            OtherUtil.SafeDeleteDir(DownloaderConfig.DirPrefix);
        }

        // 混流
        if (success && DownloaderConfig.MyOptions.MuxAfterDone && OutputFiles.Count > 0)
        {
            OutputFiles = OutputFiles.OrderBy(o => o.Index).ToList();
            // 是否跳过字幕
            if (DownloaderConfig.MyOptions.MuxOptions!.SkipSubtitle)
            {
                OutputFiles = OutputFiles.Where(o => o.MediaType != MediaType.SUBTITLES).ToList();
            }
            if (DownloaderConfig.MyOptions.MuxImports != null)
            {
                OutputFiles.AddRange(DownloaderConfig.MyOptions.MuxImports);
            }
            OutputFiles.ForEach(f => Logger.WarnMarkUp($"[grey]{Path.GetFileName(f.FilePath).EscapeMarkup()}[/]"));
            var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            var ext = OtherUtil.GetMuxExtension(DownloaderConfig.MyOptions.MuxOptions.MuxFormat);
            var dirName = Path.GetFileName(DownloaderConfig.DirPrefix);
            var outName = $"{dirName}.MUX";
            var outPath = Path.Combine(saveDir, outName);
            Logger.WarnMarkUp($"Muxing to [grey]{outName.EscapeMarkup()}{ext}[/]");
            var result = false;
            if (DownloaderConfig.MyOptions.MuxOptions.UseMkvmerge) result = MergeUtil.MuxInputsByMkvmerge(DownloaderConfig.MyOptions.MkvmergeBinaryPath!, OutputFiles.ToArray(), outPath);
            else result = MergeUtil.MuxInputsByFFmpeg(DownloaderConfig.MyOptions.FFmpegBinaryPath!, OutputFiles.ToArray(), outPath, DownloaderConfig.MyOptions.MuxOptions.MuxFormat, !DownloaderConfig.MyOptions.NoDateInfo);
            // 完成后删除各轨道文件
            if (result)
            {
                if (!DownloaderConfig.MyOptions.MuxOptions.KeepFiles)
                {
                    Logger.WarnMarkUp("[grey]Cleaning files...[/]");
                    OutputFiles.ForEach(f => File.Delete(f.FilePath));
                    var tmpDir = DownloaderConfig.MyOptions.TmpDir ?? Environment.CurrentDirectory;
                    OtherUtil.SafeDeleteDir(tmpDir);
                }
            }
            else
            {
                success = false;
                Logger.ErrorMarkUp($"Mux failed");
            }
            // 判断是否要改名
            var newPath = Path.ChangeExtension(outPath, ext);
            if (result && !File.Exists(newPath))
            {
                Logger.WarnMarkUp($"Rename to [grey]{Path.GetFileName(newPath).EscapeMarkup()}[/]");
                File.Move(outPath + ext, newPath);
            }
        }

        return success;
    }
}
