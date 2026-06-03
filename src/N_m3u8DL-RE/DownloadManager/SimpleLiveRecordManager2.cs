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
using N_m3u8DL_RE.Util.LiveRecord;
using N_m3u8DL_RE.Util.LiveRecord.SubTask;
using static N_m3u8DL_RE.Common.Util.LiveSegmentUrlUtil;

namespace N_m3u8DL_RE.DownloadManager;

internal class SimpleLiveRecordManager2
{
    IDownloader Downloader;
    DownloaderConfig DownloaderConfig;
    StreamExtractor StreamExtractor;
    LiveSegmentFileNamer SegmentFileNamer;
    LiveSubTaskSegmentDownloader SubTaskSegmentDownloader;
    LiveFromStartSubTask LiveFromStartSubTask;
    LiveGapFillCoordinator GapFillCoordinator;
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

    private enum SegmentGapSource
    {
        BetweenPlaylistRefreshes,
        CurrentPlaylist,
    }

    private readonly record struct MissingSegmentRange(long Start, long End, SegmentGapSource Source);

    public SimpleLiveRecordManager2(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
    {
        this.DownloaderConfig = downloaderConfig;
        Downloader = new SimpleDownloader(DownloaderConfig);
        PublishDateTime = selectedSteams.FirstOrDefault()?.PublishTime;
        StreamExtractor = streamExtractor;
        SegmentFileNamer = new LiveSegmentFileNamer(StreamExtractor.ExtractorType);
        SubTaskSegmentDownloader = new LiveSubTaskSegmentDownloader(Downloader, SegmentFileNamer);
        LiveFromStartSubTask = new LiveFromStartSubTask(
            SubTaskSegmentDownloader,
            () => STOP_FLAG,
            () => WAIT_SEC,
            action =>
            {
                lock (lockObj)
                {
                    action();
                }
            },
            (taskId, duration) => RefreshedDurDic.AddOrUpdate(taskId, duration, (_, old) => old + duration));
        GapFillCoordinator = new LiveGapFillCoordinator(
            SubTaskSegmentDownloader,
            () => DownloaderConfig.MyOptions.LiveFillSegmentsGap,
            () => DownloaderConfig.MyOptions.ThreadCount,
            () => DownloaderConfig.MyOptions.DownloadRetryCount,
            () => DownloaderConfig.MyOptions.HttpRequestTimeout,
            () => DownloaderConfig.MyOptions.LiveHostMirrors,
            () => CancellationTokenSource.Token,
            action =>
            {
                lock (lockObj)
                {
                    action();
                }
            },
            (taskId, duration) => RefreshedDurDic.AddOrUpdate(taskId, duration, (_, old) => old + duration));
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
        long baseTimestamp,
        long maxWritableNumberExclusive = long.MaxValue)
    {
        List<MediaSegment> writableSubtitleKeys = streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
            ? fileDic
                .Where(f => f.Key != streamSpec.Playlist?.MediaInit && f.Value is { Success: true } && f.Key.Index < maxWritableNumberExclusive)
                .Select(f => f.Key)
                .OrderBy(k => k.Index)
                .ToList()
            : [];

        // 自动修复VTT raw字幕
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("vtt"))
        {
            // 排序字幕并修正时间戳
            var keys = writableSubtitleKeys;
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
                var mp4s = writableSubtitleKeys
                    .Select(s => fileDic[s]!.ActualFilePath)
                    .Where(p => p.EndsWith(".m4s"))
                    .ToArray();
                if (mp4s.Length > 0 && firstSub)
                {
                    currentVtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                    firstSub = false;
                }
                else if (mp4s.Length > 0)
                {
                    var vtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                    currentVtt.AddCuesFromOne(vtt);
                }
            }
        }

        // 自动修复TTML raw字幕
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("ttml"))
        {
            var keys = writableSubtitleKeys.Where(s => fileDic[s]!.ActualFilePath.EndsWith(".m4s")).ToList();
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
            var keys = writableSubtitleKeys.Where(s => fileDic[s]!.ActualFilePath.EndsWith(".m4s")).ToList();
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
        WebVttSub currentVtt,
        long maxWritableNumberExclusive = long.MaxValue)
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
            // P2-1：仅写出"合并前沿之前"（Index < maxWritableNumberExclusive）的分片，
            // 空洞之后的分片暂缓保留在 fileDic，等待补片或宽限超时后再写，避免静默断裂与乱序。
            var writableEntries = fileDic
                .Where(f => f.Key != streamSpec.Playlist!.MediaInit && f.Value is { Success: true } && f.Key.Index < maxWritableNumberExclusive)
                .OrderBy(s => s.Key.Index)
                .ToList();
            var files = writableEntries.Select(f => f.Value!.ActualFilePath).ToArray();
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
            // 更新已写出连续号（P2-1）。
            if (writableEntries.Count > 0)
            {
                var maxWritten = long.MinValue;
                foreach (var kv in writableEntries)
                {
                    if (TryGetSegmentUrlNumber(kv.Key, out var wn) && wn > maxWritten)
                        maxWritten = wn;
                    else if (kv.Key.Index > maxWritten)
                        maxWritten = kv.Key.Index;
                }
                if (maxWritten != long.MinValue)
                    GapFillCoordinator.MarkMerged(task.Id, maxWritten);
            }
            // 移除"写出前沿之前"的全部非 init 分片（含写出成功的与极少数失败残留），
            // 保留暂缓（Index >= maxWritableNumberExclusive，即空洞之后）的分片到下一轮，等待补片或宽限超时。
            foreach (var key in fileDic.Keys.Where(k => k != streamSpec.Playlist!.MediaInit && k.Index < maxWritableNumberExclusive).ToList())
            {
                fileDic.TryRemove(key, out _);
            }
            if (initResult != null)
            {
                fileDic[streamSpec.Playlist!.MediaInit!] = initResult;
            }
        }
        else
        {
            var initResult = streamSpec.Playlist!.MediaInit != null ? fileDic[streamSpec.Playlist!.MediaInit!]! : null;
            var writableEntries = fileDic
                .Where(f => f.Key != streamSpec.Playlist!.MediaInit && f.Value is { Success: true } && f.Key.Index < maxWritableNumberExclusive)
                .OrderBy(s => s.Key.Index)
                .ToList();
            var files = writableEntries.Select(f => f.Value!.ActualFilePath).ToArray();
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
            if (writableEntries.Count > 0)
            {
                var maxWritten = long.MinValue;
                foreach (var kv in writableEntries)
                {
                    if (TryGetSegmentUrlNumber(kv.Key, out var wn) && wn > maxWritten)
                        maxWritten = wn;
                    else if (kv.Key.Index > maxWritten)
                        maxWritten = kv.Key.Index;
                }
                if (maxWritten != long.MinValue)
                    GapFillCoordinator.MarkMerged(task.Id, maxWritten);
            }
            foreach (var key in fileDic.Keys.Where(k => k != streamSpec.Playlist!.MediaInit && k.Index < maxWritableNumberExclusive).ToList())
            {
                fileDic.TryRemove(key, out _);
            }
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

        // P0-3/P1-1：待补缺口填充用的合成模板。
        MediaSegment? fillTemplate = null;

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
                Logger.DebugMarkUp(string.Join(",", segments.Select(sss => SegmentFileNamer.GetSegmentName(sss, false, false))));

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
                    var path = SegmentFileNamer.GetSegmentFilePath(seg, allHasDatetime, SamePathDic[task.Id], tmpDir, streamSpec.Extension ?? "clip");
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
                    var path = SegmentFileNamer.GetSegmentFilePath(seg, allHasDatetime, SamePathDic[task.Id], tmpDir, streamSpec.Extension ?? "clip");
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
                // P1-2：真实分片下载失败 -> 记入待补队列做有界重试，而非永久丢弃。
                GapFillCoordinator.EnqueueFailedSegmentGap(streamSpec, task.Id, badKey);
            }

            // P0-3：捕获一个可解析编号的真实分片作为补片合成模板（持久化跨轮）。
            foreach (var seg in segments)
            {
                if (TryGetSegmentUrlNumber(seg, out _))
                {
                    fillTemplate = seg;
                }
            }

            // P0-3/P1-1：受控并发补发待补缺口（成功并入 FileDic 参与按序合并）。
            if (fillTemplate != null)
            {
                await GapFillCoordinator.DrainPendingGapsAsync(streamSpec, task, speedContainer, tmpDir, headers, FileDic, fillTemplate, SamePathDic[task.Id]);
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

            // P2：实时合并时，音视频与字幕共用同一空洞边界；空洞之后的成功分片继续留在 FileDic。
            var mergeWritableBound = DownloaderConfig.MyOptions.LiveRealTimeMerge
                ? (STOP_FLAG ? long.MaxValue : GapFillCoordinator.ResolveMergeWritableBound(task.Id, FileDic))
                : long.MaxValue;
            var writablePendingSegments = DownloaderConfig.MyOptions.LiveRealTimeMerge
                ? pendingSegments.Where(s => s.Index < mergeWritableBound).ToList()
                : pendingSegments;

            var segmentsDuration = DownloaderConfig.MyOptions.LiveRealTimeMerge
                ? writablePendingSegments.Sum(s => s.Duration)
                : segments.Sum(s => s.Duration);

            (currentVtt, firstSub, baseTimestamp) = await UpdateSubtitlesAsync(
                streamSpec,
                task,
                FileDic,
                segmentsDuration,
                currentVtt,
                firstSub,
                baseTimestamp,
                mergeWritableBound);

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
                    currentVtt,
                    mergeWritableBound);
            }

            if (STOP_FLAG && source.Count == 0)
                break;
        }

        await liveFromStartDownloadTask;

        // P2-2：实时录制补洞收尾汇总（filled/deferred/lost 区间），让缺片可见可追溯。
        GapFillCoordinator.LogSummary(task.Id, streamSpec.ToShortShortString().EscapeMarkup());

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
                    MaxIndexDic[task.Id] = Math.Max(MaxIndexDic[task.Id], newList.Max(s => s.Index));
                    // P0-1：以 URL 数字推导并单调更新高水位号（缺口检测/滑窗驱逐的锚点）。
                    GapFillCoordinator.UpdateHighestEnqueuedNumber(task.Id, newList);
                    // 更新最新链接
                    LastFileNameDic[task.Id] = SegmentFileNamer.GetSegmentName(newList.Last(), allHasDatetime, SamePathDic[task.Id]);
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
                    // P2-3：刷新失败健壮化。瞬时刷新错误（断网/超时/CDN 抖动）不应终止整个录制，
                    // 也不应推进任何状态——记一条 WARN 后跳过本轮，等待下一轮重试；
                    // 网络恢复后高水位号缺口检测会把空档识别为缺口并补齐。
                    try
                    {
                        await StreamExtractor.RefreshPlayListAsync(dic.Keys.ToList(), CancellationTokenSource.Token);
                        DisableLiveSynthesisForImplicitIV();
                        await UpdateRawM3u8Async();
                    }
                    catch (OperationCanceledException) when (CancellationTokenSource.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException refreshEx)
                    {
                        Logger.WarnMarkUp($"[darkorange3_1]Playlist refresh timed out transiently, will retry next round: {refreshEx.Message.EscapeMarkup()}[/]");
                    }
                    catch (Exception refreshEx)
                    {
                        Logger.WarnMarkUp($"[darkorange3_1]Playlist refresh failed transiently, will retry next round: {refreshEx.Message.EscapeMarkup()}[/]");
                    }
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

        // P0-2：可预测 URL 流改走基于高水位号的稳健缺口检测（去重/单调/缺口补齐或入队），
        // 与脆弱的 LastFileName/MaxIndex 配对状态解耦，避免回退/重叠 playlist 造成状态错位而漏检缺口。
        if (GapFillCoordinator.TryFilterPredictableSegments(streamSpec, task.Id))
            return;

        var index = -1;
        var dateTime = DateTimeDic[task.Id];
        var lastName = LastFileNameDic[task.Id];
        var lastUrlNumber = 0L;
        var usePredictableUrlPattern = DownloaderConfig.MyOptions.LiveFillSegmentsGap
            && GapFillCoordinator.TryGetPredictableSegmentUrlPattern(task.Id, out _)
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
            index = streamSpec.Playlist!.MediaParts[0].MediaSegments.FindIndex(s => SegmentFileNamer.GetSegmentName(s, allHasDatetime, allSamePath) == lastName);
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
        MaxIndexDic[task.Id] = Math.Max(MaxIndexDic[task.Id], filledSegments.Max(s => s.Index));
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
        try
        {
            var request = new LiveFromStartSubTaskRequest(
                StreamSpec: streamSpec,
                Task: task,
                SpeedContainer: speedContainer,
                TmpDir: tmpDir,
                Headers: headers,
                FileDic: fileDic,
                PredictableSegmentUrlPattern: GapFillCoordinator.TryGetPredictableSegmentUrlPattern(task.Id, out _),
                ThreadCount: DownloaderConfig.MyOptions.ThreadCount,
                LiveHostMirrors: DownloaderConfig.MyOptions.LiveHostMirrors);

            await LiveFromStartSubTask.DownloadAsync(request, CancellationTokenSource.Token);
        }
        finally
        {
            // 唤醒消费者：即使没有回溯分片，也要让已经下载的当前分片开始首次合并。
            await source.SendAsync([]);
        }
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

    private bool ShouldDelayRealTimeMergeForLiveFromStart(Stream? fileOutputStream, bool liveFromStartMergeReady)
    {
        return DownloaderConfig.MyOptions.LiveFromStart
            && DownloaderConfig.MyOptions.LiveRealTimeMerge
            && fileOutputStream == null
            && !liveFromStartMergeReady;
    }

    private List<MediaSegment>? ApplyPredictableSegmentUrlPattern(
        IReadOnlyList<MediaSegment> segments,
        ProgressTask task,
        long previousNumber,
        SegmentGapSource firstGapSource)
    {
        if (segments.Count == 0)
            return [];

        if (!GapFillCoordinator.TryGetPredictableSegmentUrlPattern(task.Id, out _))
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

        // 注：可预测流的主路径已由 TryFilterPredictableSegments + 待补队列接管；
        // 本方法仅作非可预测流/中途解析异常的兜底，缺口直接补齐（无上限、无丢弃）。
        var shouldFill = missingCount > 0;

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
                MaxIndexDic[task.Id] = item.Playlist?.MediaParts[0].MediaSegments.LastOrDefault()?.Index ?? 0L; // 最大Index
                GapFillCoordinator.Initialize(task.Id, item);
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
