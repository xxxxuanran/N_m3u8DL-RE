using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using Spectre.Console;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.Util;

internal static class PipeUtil
{
    public static Stream CreatePipe(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut);
        }

        var path = Path.Combine(Path.GetTempPath(), pipeName);
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo()
        {
            FileName = "mkfifo",
            Arguments = path,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        p.Start();
        p.WaitForExit();
        Thread.Sleep(200);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
    }

    /// <summary>
    /// 解析 live-pipe-mux 输出格式：用户指定优先；否则 fMP4 输入默认 MP4，其余默认 TS。
    /// </summary>
    public static MuxFormat ResolveLivePipeMuxFormat(MuxFormat? userFormat, IEnumerable<StreamSpec> streams)
    {
        if (userFormat != null)
            return userFormat.Value;

        var mediaStreams = streams.Where(s => s.MediaType != MediaType.SUBTITLES).ToList();
        if (mediaStreams.Count == 0)
            return MuxFormat.TS;

        return mediaStreams.All(IsFmp4Stream) ? MuxFormat.MP4 : MuxFormat.TS;
    }

    private static bool IsFmp4Stream(StreamSpec streamSpec) =>
        streamSpec.Playlist?.MediaInit != null || streamSpec.Extension is "m4s" or "mp4";

    public static async Task<bool> StartPipeMuxAsync(string binary, string[] pipeNames, string outputPath, MuxFormat muxFormat)
    {
        return await Task.Run(async () =>
        {
            await Task.Delay(1000);
            return StartPipeMux(binary, pipeNames, outputPath, muxFormat);
        });
    }

    public static bool StartPipeMux(string binary, string[] pipeNames, string outputPath, MuxFormat muxFormat)
    {
        var command = new StringBuilder();
        command.Append("-y");                             // 覆盖已有输出文件
        command.Append(" -fflags +genpts+nobuffer");     // genpts：管道分片缺 PTS 时自动生成；nobuffer：减少输入端解复用缓冲
        // command.Append(" -probesize 32768");              // 缩小管道探测窗口（默认 probesize≈5MB 会带来秒级起播延迟）
        // command.Append(" -analyzeduration 0");            // 缩短流分析时间，尽快开始混流
        command.Append(" -loglevel quiet");               // 抑制 ffmpeg 日志，避免刷屏

        var customDest = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReLivePipeOptions);
        var pipeDir = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReLivePipeTmpDir, Path.GetTempPath());
        var outputArgs = OtherUtil.GetFfmpegPipeMuxOutputArgs(muxFormat);

        if (!string.IsNullOrEmpty(customDest))
        {
            command.Append(" -re");                      // 按源时间戳速率读取，推 RTMP/UDP 等实时目标时避免瞬间灌满缓冲
        }

        foreach (var item in pipeNames)
        {
            if (OperatingSystem.IsWindows())
                command.Append($" -i \"\\\\.\\pipe\\{item}\"");
            else
                command.Append($" -i \"{Path.Combine(pipeDir, item)}\"");
        }

        for (var i = 0; i < pipeNames.Length; i++)
        {
            command.Append($" -map {i}");              // 将第 N 路管道输入映射到输出（音视频分轨时一轨一管道）
        }

        command.Append(" -strict unofficial");            // 允许非严格标准码流（直播 copy 常见）
        command.Append(" -c copy");                      // 不重编码，降低 CPU 与额外延迟
        command.Append(" -ignore_unknown");              // 忽略未知流类型报错
        command.Append(" -copy_unknown");                // 保留未知流类型，避免丢轨

        if (!string.IsNullOrEmpty(customDest))
        {
            if (customDest.Trim().StartsWith('-'))
                command.Append(customDest);
            else
            {
                command.Append($" {outputArgs}");        // 容器实时写入参数
                command.Append(" -shortest");            // 多路输入时以最短的流为准结束
                command.Append($" \"{customDest}\"");
            }
        }
        else
        {
            command.Append($" {outputArgs}");
            command.Append(" -shortest");
            command.Append($" \"{outputPath}\"");
        }

        var arguments = command.ToString();
        Logger.WarnMarkUp($"[deepskyblue1]\"{binary.EscapeMarkup()}\" {arguments.EscapeMarkup()}[/]");

        using var p = new Process();
        p.StartInfo = new ProcessStartInfo()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = binary,
            Arguments = command.ToString(),
            CreateNoWindow = true,
            UseShellExecute = false
        };
        p.Start();
        p.WaitForExit();

        return p.ExitCode == 0;
    }
}
