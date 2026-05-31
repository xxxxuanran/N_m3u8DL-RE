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
    List<OutputFile> OutputFiles = [];
    DateTime? PublishDateTime;
    bool STOP_FLAG = false;
    int WAIT_SEC = 0; // 基础刷新间隔（正常轮询）
    bool WAIT_FROM_TARGET_DURATION = false; // 基础间隔是否来自 #EXT-X-TARGETDURATION（决定是否启用降级轮询）
    ConcurrentDictionary<int, int> RecordedDurDic = new(); // 已录制时长
    ConcurrentDictionary<int, int> RefreshedDurDic = new(); // 已刷新出的时长
    ConcurrentDictionary<int, BufferBlock<List<MediaSegment>>> BlockDic = new(); // 各流的Block
    ConcurrentDictionary<int, bool> SamePathDic = new(); // 各流是否allSamePath
    ConcurrentDictionary<int, SegmentUrlPatternCheck> SegmentUrlPatternDic = new(); // 各已选 media playlist 的首次 segment URL 规律预检结果
    ConcurrentDictionary<int, bool> RecordLimitReachedDic = new(); // 各流是否达到上限
    ConcurrentDictionary<int, string> LastFileNameDic = new(); // 上次下载的文件名
    ConcurrentDictionary<int, long> MaxIndexDic = new(); // 最大Index
    ConcurrentDictionary<int, long> DateTimeDic = new(); // 上次下载的dateTime
    CancellationTokenSource CancellationTokenSource = new(); // 取消Wait

    private readonly Lock lockObj = new();
    TimeSpan? audioStart = null;
    public bool ShouldRestartOnMediaInitChanged { get; private set; } = false;

    private readonly record struct SegmentUrlParts(string Path, string Query, string FileNameWithoutExtension, string Extension);

    private readonly record struct SegmentUrlPatternCheck(bool SameQuery, bool NumericFileNameMatchesIndex);

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
        List<Mediainfo> mediaInfos = [];
        Stream? fileOutputStream = null;
        WebVttSub currentVtt = new(); // 字幕流始终维护一个实例
        bool firstSub = true;
        task.StartTask();

        var name = streamSpec.ToShortString();
        var type = streamSpec.MediaType ?? Common.Enum.MediaType.VIDEO;
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

        while (true && await source.OutputAvailableAsync())
        {
            // 接收新片段 且总是拿全部未处理的片段
            // 有时每次只有很少的片段，但是之前的片段下载慢，导致后面还没下载的片段都失效了
            // TryReceiveAll可以稍微缓解一下
            source.TryReceiveAll(out IList<List<MediaSegment>>? segmentsList);
            var segments = segmentsList!.SelectMany(s => s);
            if (segments == null || !segments.Any()) continue;
            var segmentsDuration = segments.Sum(s => s.Duration);
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
                    if ((streamSpec.Playlist.MediaInit.IsEncrypted || !string.IsNullOrEmpty(currentKID)) && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID) && StreamExtractor.ExtractorType != ExtractorType.MSS)
                    {
                        var enc = result.ActualFilePath;
                        var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                        var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                        if (dResult)
                        {
                            FileDic[streamSpec.Playlist.MediaInit]!.ActualFilePath = dec;
                        }
                    }
                    // ffmpeg读取信息
                    if (!readInfo)
                    {
                        Logger.WarnMarkUp(ResString.readingInfo);
                        mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result.ActualFilePath);
                        mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                        lock (lockObj)
                        {
                            if (audioStart == null) audioStart = mediaInfos.FirstOrDefault(x => x.Type == "Audio")?.StartTime;
                        }
                        ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
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
                segments = segments.Skip(1);
                // 获取文件名
                var filename = GetSegmentName(seg, allHasDatetime, SamePathDic[task.Id]);
                var index = seg.Index;
                var path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
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
                            var enc = FileDic[streamSpec.Playlist!.MediaInit!]!.ActualFilePath;
                            var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                            var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                            if (dResult)
                            {
                                FileDic[streamSpec.Playlist!.MediaInit!]!.ActualFilePath = dec;
                            }
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
                    if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                    {
                        var enc = result.ActualFilePath;
                        var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                        var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile);
                        if (dResult)
                        {
                            File.Delete(enc);
                            result.ActualFilePath = dec;
                        }
                    }
                    if (!readInfo)
                    {
                        // ffmpeg读取信息
                        Logger.WarnMarkUp(ResString.readingInfo);
                        mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result!.ActualFilePath);
                        mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                        lock (lockObj)
                        {
                            if (audioStart == null) audioStart = mediaInfos.FirstOrDefault(x => x.Type == "Audio")?.StartTime;
                        }
                        ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
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
                // 获取文件名
                var filename = GetSegmentName(seg, allHasDatetime, SamePathDic[task.Id]);
                var index = seg.Index;
                var path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
                var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                FileDic[seg] = result;
                if (result is { Success: true })
                    task.Increment(1);
                // 实时解密
                if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && result is { Success: true } && !string.IsNullOrEmpty(currentKID))
                {
                    var enc = result.ActualFilePath;
                    var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                    var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile);
                    if (dResult)
                    {
                        File.Delete(enc);
                        result.ActualFilePath = dec;
                    }
                }
            });

            // 自动修复VTT raw字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("vtt"))
            {
                // 排序字幕并修正时间戳
                var keys = FileDic.Keys.OrderBy(k => k.Index).ToList();
                foreach (var seg in keys)
                {
                    var vttContent = await File.ReadAllTextAsync(FileDic[seg]!.ActualFilePath);
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
                        vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration);
                    }
                    if (firstSub) { currentVtt = vtt; firstSub = false; }
                    else currentVtt.AddCuesFromOne(vtt);
                }
            }

            // 自动修复VTT mp4字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                                                           && streamSpec.Codecs != "stpp" && streamSpec.Extension != null && streamSpec.Extension.Contains("m4s"))
            {
                var initFile = FileDic.Values.FirstOrDefault(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init"));
                var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                var (sawVtt, timescale) = MP4VttUtil.CheckInit(iniFileBytes);
                if (sawVtt)
                {
                    var mp4s = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s")).ToArray();
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
                var keys = FileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key).ToList();
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
                        var vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                        // 手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration);
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
                        var vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                        // 手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (RecordedDurDic[task.Id] + (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
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
                // var initFile = FileDic.Values.Where(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).FirstOrDefault();
                // var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                // var sawTtml = MP4TtmlUtil.CheckInit(iniFileBytes);
                var keys = FileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key);
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
                        var vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                        // 手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration);
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
                        var vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                        // 手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (RecordedDurDic[task.Id] + (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                        }
                        currentVtt.AddCuesFromOne(vtt);
                    }
                }
            }

            RecordedDurDic[task.Id] += (int)segmentsDuration;

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

                // 移除无效片段
                var badKeys = FileDic.Where(i => i.Value == null).Select(i => i.Key);
                foreach (var badKey in badKeys)
                {
                    FileDic!.Remove(badKey, out _);
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
                        output = Path.ChangeExtension(output, ".ts");
                        var pipeName = $"RE_pipe_{Guid.NewGuid()}";
                        fileOutputStream = PipeUtil.CreatePipe(pipeName);
                        Logger.InfoMarkUp($"{ResString.namedPipeCreated} [cyan]{pipeName.EscapeMarkup()}[/]");
                        PipeSteamNamesDic[task.Id] = pipeName;
                        if (PipeSteamNamesDic.Count == SelectedSteams.Count(x => x.MediaType != MediaType.SUBTITLES))
                        {
                            var names = PipeSteamNamesDic.OrderBy(i => i.Key).Select(k => k.Value).ToArray();
                            Logger.WarnMarkUp($"{ResString.namedPipeMux} [deepskyblue1]{Path.GetFileName(output).EscapeMarkup()}[/]");
                            var t = PipeUtil.StartPipeMuxAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, names, output);
                        }

                        // Windows only
                        if (OperatingSystem.IsWindows())
                            await (fileOutputStream as NamedPipeServerStream)!.WaitForConnectionAsync();
                    }
                }

                if (streamSpec.MediaType != MediaType.SUBTITLES)
                {
                    var initResult = streamSpec.Playlist!.MediaInit != null ? FileDic[streamSpec.Playlist!.MediaInit!]! : null;
                    var files = FileDic.Where(f => f.Key != streamSpec.Playlist!.MediaInit).OrderBy(s => s.Key.Index).Select(f => f.Value).Select(v => v!.ActualFilePath).ToArray();
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
                    FileDic.Clear();
                    if (initResult != null)
                    {
                        FileDic[streamSpec.Playlist!.MediaInit!] = initResult;
                    }
                }
                else
                {
                    var initResult = streamSpec.Playlist!.MediaInit != null ? FileDic[streamSpec.Playlist!.MediaInit!]! : null;
                    var files = FileDic.OrderBy(s => s.Key.Index).Select(f => f.Value).Select(v => v!.ActualFilePath).ToArray();
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
                    FileDic.Clear();
                    if (initResult != null)
                    {
                        FileDic[streamSpec.Playlist!.MediaInit!] = initResult;
                    }
                }

                // 刷新buffer
                if (fileOutputStream != null)
                {
                    fileOutputStream.Flush();
                }
            }

            if (STOP_FLAG && source.Count == 0)
                break;
        }

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
                    RefreshedDurDic[task.Id] += (int)newList.Sum(s => s.Duration);
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
                if (!STOP_FLAG) await StreamExtractor.RefreshPlayListAsync(dic.Keys.ToList());
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

    private void FilterMediaSegments(StreamSpec streamSpec, ProgressTask task, bool allHasDatetime, bool allSamePath)
    {
        if (string.IsNullOrEmpty(LastFileNameDic[task.Id]) && DateTimeDic[task.Id] == 0) return;

        var index = -1;
        var dateTime = DateTimeDic[task.Id];
        var lastName = LastFileNameDic[task.Id];
        var lastUrlNumber = 0L;
        var usePredictableUrlPattern = DownloaderConfig.MyOptions.LiveFillSegmentsGap
            && TryGetPredictableSegmentUrlPattern(task, out _)
            && long.TryParse(lastName, out lastUrlNumber);

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
    ///   2. 首次 media playlist 时所有 segment 的 URL query string 相同，且文件名是与 Index 相等的纯数字
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
        if (!long.TryParse(LastFileNameDic[task.Id], out var lastNumber) || lastNumber != oldMax)
            return;

        var filledSegments = ApplyPredictableSegmentUrlPattern(newSegments, task, lastNumber, SegmentGapSource.BetweenPlaylistRefreshes);
        if (filledSegments == null)
            return;

        streamSpec.Playlist!.MediaParts[0].MediaSegments = filledSegments;
    }

    private bool TryGetPredictableSegmentUrlPattern(ProgressTask task, out SegmentUrlPatternCheck pattern)
    {
        return SegmentUrlPatternDic.TryGetValue(task.Id, out pattern)
            && pattern.SameQuery
            && pattern.NumericFileNameMatchesIndex;
    }

    private static int FindSegmentIndexByUrlNumber(IReadOnlyList<MediaSegment> segments, long number)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            var urlParts = ParseSegmentUrl(segments[i].Url);
            if (long.TryParse(urlParts.FileNameWithoutExtension, out var segmentNumber) && segmentNumber == number)
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
            if (!long.TryParse(urlParts.FileNameWithoutExtension, out var number))
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

    private static MediaSegment? CreateFilledSegment(MediaSegment template, SegmentUrlParts templateUrlParts, long index, long templateNumber)
    {
        var newUrl = ReplaceUrlFileName(templateUrlParts, index.ToString());
        if (newUrl == null) return null;

        return new MediaSegment
        {
            Index = index,
            Duration = template.Duration,
            Title = template.Title,
            DateTime = template.DateTime?.AddSeconds(-(templateNumber - index) * template.Duration),
            StartRange = template.StartRange,
            ExpectLength = template.ExpectLength,
            Url = newUrl,
            NameFromVar = null, // 强制按 URL 推导文件名
            EncryptInfo = new EncryptInfo
            {
                Method = template.EncryptInfo.Method,
                Key = template.EncryptInfo.Key,
                IV = template.EncryptInfo.IV != null ? (byte[])template.EncryptInfo.IV.Clone() : null,
            },
        };
    }

    /// <summary>
    /// 检查首次 playlist 的 segment URL 是否具备可补片的基础规律
    /// </summary>
    private static SegmentUrlPatternCheck CheckSegmentUrlPattern(IReadOnlyList<MediaSegment> segments)
    {
        if (segments.Count == 0)
            return new SegmentUrlPatternCheck(SameQuery: true, NumericFileNameMatchesIndex: false);

        var firstParts = ParseSegmentUrl(segments[0].Url);
        var sameQuery = true;
        var numericFileNameMatchesIndex = true;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var urlParts = i == 0 ? firstParts : ParseSegmentUrl(segment.Url);

            if (urlParts.Query != firstParts.Query)
                sameQuery = false;

            if (!long.TryParse(urlParts.FileNameWithoutExtension, out var num) || num != segment.Index)
                numericFileNameMatchesIndex = false;

            if (!sameQuery && !numericFileNameMatchesIndex)
                break;
        }

        return new SegmentUrlPatternCheck(sameQuery, numericFileNameMatchesIndex);
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

    public async Task<bool> StartRecordAsync()
    {
        var takeLastCount = DownloaderConfig.MyOptions.LiveTakeCount;
        ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); // 速度计算
        ConcurrentDictionary<StreamSpec, bool?> Results = new();
        // 同步流
        FilterUtil.SyncStreams(SelectedSteams, takeLastCount);
        // 设置等待时间
        if (WAIT_SEC == 0)
        {
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
            DownloaderConfig.MyOptions.LiveFillSegmentsGapMax ??= Math.Max(1L, 60L / WAIT_SEC);
        }
        // 如果没有选中音频 取消通过音频修复vtt时间轴
        if (SelectedSteams.All(x => x.MediaType != MediaType.AUDIO))
        {
            DownloaderConfig.MyOptions.LiveFixVttByAudio = false;
        }

        /*// 写出master
        if (DownloaderConfig.MyOptions.LiveWriteHLS)
        {
            var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            var saveName = DownloaderConfig.MyOptions.SaveName ?? DateTime.Now.ToString("yyyyMMddHHmmss");
            await StreamingUtil.WriteMasterListAsync(SelectedSteams, saveName, saveDir);
        }*/

        var progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
        progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;

        // 进度条的列定义
        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn() { Alignment = Justify.Left },
            new RecordingDurationColumn(RecordedDurDic, RefreshedDurDic), // 时长显示
            new RecordingStatusColumn(),
            new PercentageColumn(),
            new DownloadSpeedColumn(SpeedContainerDic), // 速度计算
            new SpinnerColumn(),
        };
        if (DownloaderConfig.MyOptions.NoAnsiColor)
        {
            progressColumns = progressColumns.SkipLast(1).ToArray();
        }
        progress.Columns(progressColumns);

        await progress.StartAsync(async ctx =>
        {
            // 创建任务
            var dic = SelectedSteams.Select(item =>
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
                SegmentUrlPatternDic[task.Id] = CheckSegmentUrlPattern(item.Playlist?.MediaParts[0].MediaSegments ?? []);
                BlockDic[task.Id] = new BufferBlock<List<MediaSegment>>();
                return (item, task);
            }).ToDictionary(item => item.item, item => item.task);

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
            await Task.Delay(200);
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
