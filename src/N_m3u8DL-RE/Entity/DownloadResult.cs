namespace N_m3u8DL_RE.Entity;

internal class DownloadResult
{
    public bool Success => (ActualContentLength != null && RespContentLength != null) ? (RespContentLength == ActualContentLength) : (ActualContentLength != null);
    public long? RespContentLength { get; set; }
    public long? ActualContentLength { get; set; }
    public bool ImageHeader { get; set; } = false; // 图片伪装
    public bool GzipHeader { get; set; } = false; // GZip压缩
    public required string ActualFilePath { get; set; }
    public string? RequestUrl { get; set; } // 实际成功下载所用的 URL（多 host 竞速时为胜出 host 的 URL）
}