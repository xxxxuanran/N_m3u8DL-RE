using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Crypto;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;
using Spectre.Console;

namespace N_m3u8DL_RE.Downloader;

/// <summary>
/// 简单下载器
/// </summary>
internal class SimpleDownloader : IDownloader
{
    DownloaderConfig DownloaderConfig;

    public SimpleDownloader(DownloaderConfig config)
    {
        DownloaderConfig = config;
    }

    public async Task<DownloadResult?> DownloadSegmentAsync(MediaSegment segment, string savePath, SpeedContainer speedContainer, Dictionary<string, string>? headers = null)
    {
        var url = segment.Url;
        var (des, dResult) = await DownClipAsync(url, savePath, speedContainer, segment.StartRange, segment.StopRange, headers, DownloaderConfig.MyOptions.DownloadRetryCount);
        if (dResult is { Success: true } && dResult.ActualFilePath != des)
        {
            switch (segment.EncryptInfo.Method)
            {
                case EncryptMethod.AES_128:
                {
                    var key = segment.EncryptInfo.Key;
                    var iv = segment.EncryptInfo.IV;
                    AESUtil.AES128Decrypt(dResult.ActualFilePath, key!, iv!);
                    break;
                }
                case EncryptMethod.AES_128_ECB:
                {
                    var key = segment.EncryptInfo.Key;
                    var iv = segment.EncryptInfo.IV;
                    AESUtil.AES128Decrypt(dResult.ActualFilePath, key!, iv!, System.Security.Cryptography.CipherMode.ECB);
                    break;
                }
                case EncryptMethod.CHACHA20:
                {
                    var key = segment.EncryptInfo.Key;
                    var nonce = segment.EncryptInfo.IV;

                    var fileBytes = File.ReadAllBytes(dResult.ActualFilePath);
                    var decrypted = ChaCha20Util.DecryptPer1024Bytes(fileBytes, key!, nonce!);
                    await File.WriteAllBytesAsync(dResult.ActualFilePath, decrypted);
                    break;
                }
                case EncryptMethod.SAMPLE_AES_CTR:
                    // throw new NotSupportedException("SAMPLE-AES-CTR");
                    break;
            }

            // Image头处理
            if (dResult.ImageHeader)
            {
                await ImageHeaderUtil.ProcessAsync(dResult.ActualFilePath);
            }
            // Gzip解压
            if (dResult.GzipHeader)
            {
                await OtherUtil.DeGzipFileAsync(dResult.ActualFilePath);
            }

            // 处理完成后改名
            File.Move(dResult.ActualFilePath, des);
            dResult.ActualFilePath = des;
        }
        return dResult;
    }

    private static List<string> BuildCandidateUrls(string url, string[]? mirrors)
    {
        var list = new List<string> { url };
        if (mirrors == null || mirrors.Length == 0)
            return list;

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return list;

        var baseUri = new Uri(url);
        var seen = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);

        foreach (var raw in mirrors)
        {
            var m = raw?.Trim();
            if (string.IsNullOrEmpty(m))
                continue;

            var ub = new UriBuilder(baseUri);
            if (m.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var u = new Uri(m);
                ub.Scheme = u.Scheme;
                ub.Host = u.Host;
                ub.Port = u.IsDefaultPort ? -1 : u.Port;
            }
            else if (m.Contains(':', StringComparison.Ordinal))
            {
                var parts = m.Split(':', 2, StringSplitOptions.TrimEntries);
                ub.Host = parts[0];
                if (parts.Length > 1 && int.TryParse(parts[1], out var p))
                    ub.Port = p;
            }
            else
            {
                ub.Host = m;
            }

            var candidate = ub.Uri.AbsoluteUri;
            if (seen.Add(candidate))
                list.Add(candidate);
        }

        return list;
    }

    private static async Task<DownloadResult> DownloadFirstSuccessfulHostAsync(
        IReadOnlyList<string> candidates,
        string path,
        SpeedContainer speedContainer,
        CancellationTokenSource parentCts,
        Dictionary<string, string>? headers,
        long? fromPosition,
        long? toPosition)
    {
        if (candidates.Count == 1)
        {
            return await DownloadUtil.DownloadToFileAsync(
                candidates[0], path, speedContainer, parentCts, headers, fromPosition, toPosition);
        }

        var entries = new List<(Task<DownloadResult> Task, string TmpPath, CancellationTokenSource Cts, string CandidateUrl)>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(parentCts.Token);
            var tmp = path + $".host{i}";
            var t = DownloadUtil.DownloadToFileAsync(
                candidates[i], tmp, speedContainer, linked, headers, fromPosition, toPosition);
            entries.Add((t, tmp, linked, candidates[i]));
        }

        DownloadResult? winnerResult = null;
        string? winnerTmp = null;
        string? winnerHostForLog = null;
        Exception? lastException = null;
        var pending = entries.ToList();

        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending.Select(e => e.Task));
            var hit = pending.First(e => e.Task == finished);
            pending.Remove(hit);

            if (finished.Status == TaskStatus.RanToCompletion)
            {
                winnerResult = finished.Result;
                winnerTmp = hit.TmpPath;
                winnerHostForLog = new Uri(hit.CandidateUrl).Host;
                foreach (var p in pending)
                {
                    try { p.Cts.Cancel(); } catch { /* ignore */ }
                }

                break;
            }

            if (finished.Exception != null)
                lastException = finished.Exception.GetBaseException();
        }

        foreach (var e in entries)
        {
            if (e.TmpPath != winnerTmp)
            {
                try { await e.Task.ConfigureAwait(false); } catch { /* ignore */ }
                try
                {
                    if (File.Exists(e.TmpPath))
                        File.Delete(e.TmpPath);
                }
                catch { /* ignore */ }
            }

            e.Cts.Dispose();
        }

        if (winnerResult == null)
            throw lastException ?? new InvalidOperationException("All host mirrors failed");

        if (!string.Equals(winnerTmp, path, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
                File.Delete(path);
            File.Move(winnerTmp!, path);
            winnerResult.ActualFilePath = path;
        }

        if (winnerHostForLog != null)
        {
            var segmentLabel = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(segmentLabel))
                segmentLabel = Path.GetFileName(path);
            Logger.WarnMarkUp($"[grey]race winner: {winnerHostForLog.EscapeMarkup()} in file({segmentLabel.EscapeMarkup()})[/]");
        }

        return winnerResult;
    }

    private async Task<(string des, DownloadResult? dResult)> DownClipAsync(string url, string path, SpeedContainer speedContainer, long? fromPosition, long? toPosition, Dictionary<string, string>? headers = null, int retryCount = 3)
    {
        CancellationTokenSource? cancellationTokenSource = null;
        retry:
        try
        {
            cancellationTokenSource = new();
            var des = Path.ChangeExtension(path, null);

            // 已下载跳过
            if (File.Exists(des))
            {
                speedContainer.Add(new FileInfo(des).Length);
                return (des, new DownloadResult() { ActualContentLength = 0, ActualFilePath = des });
            }

            // 已解密跳过
            var dec = Path.Combine(Path.GetDirectoryName(des)!, Path.GetFileNameWithoutExtension(des) + "_dec" + Path.GetExtension(des));
            if (File.Exists(dec))
            {
                speedContainer.Add(new FileInfo(dec).Length);
                return (dec, new DownloadResult() { ActualContentLength = 0, ActualFilePath = dec });
            }

            // 另起线程进行监控
            var cts = cancellationTokenSource;
            using var watcher = Task.Factory.StartNew(async () =>
            {
                while (true)
                {
                    if (cts.IsCancellationRequested) break;
                    if (speedContainer.ShouldStop)
                    {
                        cts.Cancel();
                        Logger.DebugMarkUp("Cancel...");
                        break;
                    }
                    await Task.Delay(500);
                }
            });

            // 调用下载（可选多 host 竞速）
            var candidates = BuildCandidateUrls(url, DownloaderConfig.MyOptions.LiveHostMirrors);
            var result = await DownloadFirstSuccessfulHostAsync(
                candidates, path, speedContainer, cancellationTokenSource, headers, fromPosition, toPosition);
            return (des, result);

            throw new Exception("please retry");
        }
        catch (Exception ex)
        {
            Logger.DebugMarkUp($"[grey]{ex.Message.EscapeMarkup()} retryCount: {retryCount}[/]");
            Logger.Debug(url + " " + ex);
            Logger.Extra($"Ah oh!{Environment.NewLine}RetryCount => {retryCount}{Environment.NewLine}Exception  => {ex.Message}{Environment.NewLine}Url        => {url}");
            if (retryCount-- > 0)
            {
                await Task.Delay(1000);
                goto retry;
            }
            else
            {
                Logger.Extra($"The retry attempts have been exhausted and the download of this segment has failed.{Environment.NewLine}Exception  => {ex.Message}{Environment.NewLine}Url        => {url}");
                Logger.WarnMarkUp($"[grey]{ex.Message.EscapeMarkup()}[/]");
            }
            // throw new Exception("download failed", ex);
            return default;
        }
        finally
        {
            if (cancellationTokenSource != null)
            {
                // 调用后销毁
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
        }
    }
}