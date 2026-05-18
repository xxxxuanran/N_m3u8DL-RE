using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Config;

internal class DownloaderConfig
{
    public required MyOption MyOptions { get; set; }

    /// <summary>
    /// 前置阶段生成的文件夹名（运行时派生，可能因冲突附加时间戳后缀）
    /// </summary>
    public required string DirPrefix { get; set; }
    /// <summary>
    /// 运行时派生的输出文件名基础（不含扩展），通常与 <see cref="DirPrefix"/> 的目录名一致，
    /// 用于无 <see cref="SavePattern"/> 时拼接最终输出文件路径，保证与 tmpDir 命名同步
    /// </summary>
    public required string FileName { get; set; }
    /// <summary>
    /// 文件名模板
    /// </summary>
    public string? SavePattern { get; set; }
    /// <summary>
    /// 校验响应头的文件大小和实际大小
    /// </summary>
    public bool CheckContentLength { get; set; } = true;
    /// <summary>
    /// 请求头
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
}