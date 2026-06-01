using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;

namespace N_m3u8DL_RE.Column;

internal class RecordingDurationColumn : ProgressColumn
{
    protected override bool NoWrap => true;
    private ConcurrentDictionary<int, double> _recodingDurDic;
    private ConcurrentDictionary<int, double>? _refreshedDurDic;
    public Style GreyStyle { get; set; } = new Style(foreground: Color.Grey);
    public Style MyStyle { get; set; } = new Style(foreground: Color.DarkGreen);
    public RecordingDurationColumn(ConcurrentDictionary<int, double> recodingDurDic)
    {
        _recodingDurDic = recodingDurDic;
    }
    public RecordingDurationColumn(ConcurrentDictionary<int, double> recodingDurDic, ConcurrentDictionary<int, double> refreshedDurDic)
    {
        _recodingDurDic = recodingDurDic;
        _refreshedDurDic = refreshedDurDic;
    }
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        // 内部以 double 累加完整时长，渲染时再四舍五入取整，避免逐分片截断造成的偏差
        if (_refreshedDurDic == null)
            return new Text($"{GlobalUtil.FormatTime((int)Math.Round(_recodingDurDic[task.Id]))}", MyStyle).LeftJustified();
        return new Text($"{GlobalUtil.FormatTime((int)Math.Round(_recodingDurDic[task.Id]))}/{GlobalUtil.FormatTime((int)Math.Round(_refreshedDurDic[task.Id]))}", GreyStyle);
    }
}