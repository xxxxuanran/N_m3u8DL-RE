namespace N_m3u8DL_RE.Common.Resource;

internal static class StaticText
{
    public static readonly Dictionary<string, TextContainer> LANG_DIC = new()
    {
        ["singleFileSplitWarn"] = new TextContainer
        (
            zhCN: "整段文件已被自动切割为小分片以加速下载",
            zhTW: "整段文件已被自動切割為小分片以加速下載",
            enUS: "The entire file has been cut into small segments to accelerate"
        ),
        ["singleFileRealtimeDecryptWarn"] = new TextContainer
        (
            zhCN: "实时解密已被强制关闭",
            zhTW: "即時解密已被強制關閉",
            enUS: "Real-time decryption has been disabled"
        ),
        ["cmd_forceAnsiConsole"] = new TextContainer
        (
            zhCN: "强制认定终端为支持ANSI且可交互的终端",
            zhTW: "強制認定終端為支援ANSI且可交往的終端",
            enUS: "Force assuming the terminal is ANSI-compatible and interactive"
        ),
        ["cmd_noAnsiColor"] = new TextContainer
        (
            zhCN: "去除ANSI颜色",
            zhTW: "關閉ANSI顏色",
            enUS: "Remove ANSI colors"
        ),
        ["customRangeWarn"] = new TextContainer
        (
            zhCN: "请注意，自定义下载范围有时会导致音画不同步",
            zhTW: "請注意，自定義下載範圍有時會導致音畫不同步",
            enUS: "Please note that custom range may sometimes result in audio and video being out of sync"
        ),
        ["customRangeInvalid"] = new TextContainer
        (
            zhCN: "自定义下载范围无效",
            zhTW: "自定義下載範圍無效",
            enUS: "User customed range invalid"
        ),
        ["customAdKeywordsFound"] = new TextContainer
        (
            zhCN: "用户自定义广告分片URL关键字：",
            zhTW: "用戶自定義廣告分片URL關鍵字：",
            enUS: "User customed Ad keyword: "
        ),
        ["customRangeFound"] = new TextContainer
        (
            zhCN: "用户自定义下载范围：",
            zhTW: "用戶自定義下載範圍：",
            enUS: "User customed range: "
        ),
        ["consoleRedirected"] = new TextContainer
        (
            zhCN: "输出被重定向, 将清除ANSI颜色",
            zhTW: "輸出被重定向, 將清除ANSI顏色",
            enUS: "Output is redirected, ANSI colors are cleared."
        ),
        ["processImageSub"] = new TextContainer
        (
            zhCN: "正在处理图形字幕",
            zhTW: "正在處理圖形字幕",
            enUS: "Processing Image Sub"
        ),
        ["newVersionFound"] = new TextContainer
        (
            zhCN: "检测到新版本，请尽快升级！",
            zhTW: "檢測到新版本，請盡快升級！",
            enUS: "New version detected!"
        ),
        ["namedPipeCreated"] = new TextContainer
        (
            zhCN: "已创建命名管道：",
            zhTW: "已創建命名管道：",
            enUS: "Named pipe created: "
        ),
        ["namedPipeMux"] = new TextContainer
        (
            zhCN: "通过命名管道混流到",
            zhTW: "通過命名管道混流到",
            enUS: "Mux with named pipe, to"
        ),
        ["taskStartAt"] = new TextContainer
        (
            zhCN: "程序将等待，直到：",
            zhTW: "程序將等待，直到：",
            enUS: "The program will wait until: "
        ),
        ["autoBinaryMerge"] = new TextContainer
        (
            zhCN: "检测到fMP4，自动开启二进制合并",
            zhTW: "檢測到fMP4，自動開啟二進位制合併",
            enUS: "fMP4 is detected, binary merging is automatically enabled"
        ),
        ["autoBinaryMerge2"] = new TextContainer
        (
            zhCN: "检测到杜比视界内容，自动开启二进制合并",
            zhTW: "檢測到杜比視界內容，自動開啟二進位制合併",
            enUS: "Dolby Vision content is detected, binary merging is automatically enabled"
        ),
        ["autoBinaryMerge3"] = new TextContainer
        (
            zhCN: "检测到无法识别的加密方式，自动开启二进制合并",
            zhTW: "檢測到無法識別的加密方式，自動開啟二進位制合併",
            enUS: "An unrecognized encryption method is detected, binary merging is automatically enabled"
        ),
        ["autoBinaryMerge4"] = new TextContainer
        (
            zhCN: "检测到CENC加密方式，自动开启二进制合并",
            zhTW: "檢測到CENC加密方式，自動開啟二進位制合併",
            enUS: "When CENC encryption is detected, binary merging is automatically enabled"
        ),
        ["autoBinaryMerge5"] = new TextContainer
        (
            zhCN: "检测到杜比视界内容，混流功能已禁用",
            zhTW: "檢測到杜比視界內容，混流功能已禁用",
            enUS: "Dolby Vision content is detected, mux after done is automatically disabled"
        ),
        ["autoBinaryMerge6"] = new TextContainer
        (
            zhCN: "你已开启下载完成后混流，自动开启二进制合并",
            zhTW: "你已開啟下載完成後混流，自動開啟二進制合併",
            enUS: "MuxAfterDone is detected, binary merging is automatically enabled"
        ),
        ["badM3u8"] = new TextContainer
        (
            zhCN: "错误的m3u8",
            zhTW: "錯誤的m3u8",
            enUS: "Bad m3u8"
        ),
        ["binaryMerge"] = new TextContainer
        (
            zhCN: "二进制合并中...",
            zhTW: "二進位制合併中...",
            enUS: "Binary merging..."
        ),
        ["checkingLast"] = new TextContainer
        (
            zhCN: "验证最后一个分片有效性",
            zhTW: "驗證最後一個分片有效性",
            enUS: "Verifying the validity of the last segment"
        ),
        ["cmd_baseUrl"] = new TextContainer
        (
            zhCN: "设置BaseURL",
            zhTW: "設置BaseURL",
            enUS: "Set BaseURL"
        ),
        ["cmd_maxSpeed"] = new TextContainer
        (
            zhCN: "设置限速，单位为 MiB/s 或 KiB/s，如：15M=15MiB/s，100K=100KiB/s",
            zhTW: "設置限速，單位為 MiB/s 或 KiB/s，如：15M=15MiB/s，100K=100KiB/s",
            enUS: "Set speed limit in MiB/s or KiB/s, e.g. 15M=15MiB/s, 100K=100KiB/s."
        ),
        ["cmd_noDateInfo"] = new TextContainer
        (
            zhCN: "混流时不写入日期信息",
            zhTW: "混流時不寫入日期訊息",
            enUS: "Date information is not written during muxing"
        ),
        ["cmd_noLog"] = new TextContainer
        (
            zhCN: "关闭日志文件输出",
            zhTW: "關閉日誌文件輸出",
            enUS: "Disable log file output"
        ),
        ["cmd_logFileOnly"] = new TextContainer
        (
            zhCN: "仅将日志输出到文件，不输出到终端",
            zhTW: "僅將日誌輸出到檔案，不輸出到終端",
            enUS: "Write logs only to file, not to terminal"
        ),
        ["cmd_allowHlsMultiExtMap"] = new TextContainer
        (
            zhCN: "允许HLS中的多个#EXT-X-MAP(实验性)",
            zhTW: "允許HLS中的多個#EXT-X-MAP(實驗性)",
            enUS: "Allow multiple #EXT-X-MAP in HLS (experimental)"
        ),
        ["cmd_appendUrlParams"] = new TextContainer
        (
            zhCN: "将输入Url的Params添加至分片, 对某些网站很有用, 例如 kakao.com",
            zhTW: "將輸入Url的Params添加至分片, 對某些網站很有用, 例如 kakao.com",
            enUS: "Add Params of input Url to segments, useful for some websites, such as kakao.com"
        ),
        ["cmd_autoSelect"] = new TextContainer
        (
            zhCN: "自动选择所有类型的最佳轨道",
            zhTW: "自動選擇所有類型的最佳軌道",
            enUS: "Automatically selects the best tracks of all types"
        ),
        ["cmd_disableUpdateCheck"] = new TextContainer
        (
            zhCN: "禁用版本更新检测",
            zhTW: "禁用版本更新檢測",
            enUS: "Disable version update check"
        ),
        ["cmd_binaryMerge"] = new TextContainer
        (
            zhCN: "二进制合并",
            zhTW: "二進位制合併",
            enUS: "Binary merge"
        ),
        ["cmd_useFFmpegConcatDemuxer"] = new TextContainer
        (
            zhCN: "使用 ffmpeg 合并时，使用 concat 分离器而非 concat 协议",
            zhTW: "使用 ffmpeg 合併時，使用 concat 分離器而非 concat 協議",
            enUS: "When merging with ffmpeg, use the concat demuxer instead of the concat protocol"
        ),
        ["cmd_checkSegmentsCount"] = new TextContainer
        (
            zhCN: "检测实际下载的分片数量和预期数量是否匹配",
            zhTW: "檢測實際下載的分片數量和預期數量是否匹配",
            enUS: "Check if the actual number of segments downloaded matches the expected number"
        ),
        ["cmd_downloadRetryCount"] = new TextContainer
        (
            zhCN: "每个分片下载异常时的重试次数",
            zhTW: "每個分片下載異常時的重試次數",
            enUS: "The number of retries when download segment error"
        ),
        ["cmd_httpRequestTimeout"] = new TextContainer
        (
            zhCN: "HTTP请求的超时时间(秒)",
            zhTW: "HTTP請求的超時時間(秒)",
            enUS: "Timeout duration for HTTP requests (in seconds)"
        ),
        ["cmd_decryptionBinaryPath"] = new TextContainer
        (
            zhCN: @"MP4解密所用工具的全路径, 例如 C:\Tools\mp4decrypt.exe",
            zhTW: @"MP4解密所用工具的全路徑, 例如 C:\Tools\mp4decrypt.exe",
            enUS: @"Full path to the tool used for MP4 decryption, like C:\Tools\mp4decrypt.exe"
        ),
        ["cmd_delAfterDone"] = new TextContainer
        (
            zhCN: "完成后删除临时文件",
            zhTW: "完成後刪除臨時文件",
            enUS: "Delete temporary files when done"
        ),
        ["cmd_ffmpegBinaryPath"] = new TextContainer
        (
            zhCN: @"ffmpeg可执行程序全路径, 例如 C:\Tools\ffmpeg.exe",
            zhTW: @"ffmpeg可執行程序全路徑, 例如 C:\Tools\ffmpeg.exe",
            enUS: @"Full path to the ffmpeg binary, like C:\Tools\ffmpeg.exe"
        ),
        ["cmd_mkvmergeBinaryPath"] = new TextContainer
        (
            zhCN: @"mkvmerge可执行程序全路径, 例如 C:\Tools\mkvmerge.exe",
            zhTW: @"mkvmerge可執行程序全路徑, 例如 C:\Tools\mkvmerge.exe",
            enUS: @"Full path to the mkvmerge binary, like C:\Tools\mkvmerge.exe"
        ),
        ["cmd_liveFixVttByAudio"] = new TextContainer
        (
            zhCN: "通过读取音频文件的起始时间修正VTT字幕",
            zhTW: "透過讀取音訊檔案的起始時間修正VTT字幕",
            enUS: "Correct VTT sub by reading the start time of the audio file"
        ),
        ["cmd_liveFillSegmentsGap"] = new TextContainer
        (
            zhCN: "录制直播刷新播放列表出现间隙时，按可预测的连续数字命名规律自动补齐缺失的分片",
            zhTW: "錄製直播刷新播放列表出現間隙時，按可預測的連續數字命名規律自動補齊缺失的分片",
            enUS: "Auto-fill missing segments by predictable numeric naming pattern when the live playlist refreshes with gaps"
        ),
        ["liveLogNone"] = new TextContainer
        (
            zhCN: "无",
            zhTW: "無",
            enUS: "none"
        ),
        ["liveLogInfinite"] = new TextContainer
        (
            zhCN: "无限制",
            zhTW: "無限制",
            enUS: "infinite"
        ),
        ["liveLogBelowFloor"] = new TextContainer
        (
            zhCN: "低于下限",
            zhTW: "低於下限",
            enUS: "below floor"
        ),
        ["liveLogAvailableMarkup"] = new TextContainer
        (
            zhCN: "[green]可用[/]",
            zhTW: "[green]可用[/]",
            enUS: "[green]available[/]"
        ),
        ["liveLogUnavailableMarkup"] = new TextContainer
        (
            zhCN: "[red]不可用[/]",
            zhTW: "[red]不可用[/]",
            enUS: "[red]unavailable[/]"
        ),
        ["liveLogStopRangeCompleted"] = new TextContainer
        (
            zhCN: "区间完成",
            zhTW: "區間完成",
            enUS: "range completed"
        ),
        ["liveLogStopRecordingStopping"] = new TextContainer
        (
            zhCN: "录制正在停止",
            zhTW: "錄製正在停止",
            enUS: "recording stopping"
        ),
        ["liveLogStopCancelled"] = new TextContainer
        (
            zhCN: "已取消",
            zhTW: "已取消",
            enUS: "cancelled"
        ),
        ["liveLogStopNoPendingWork"] = new TextContainer
        (
            zhCN: "无待处理任务",
            zhTW: "無待處理工作",
            enUS: "no pending work"
        ),
        ["liveLogStopLoopExited"] = new TextContainer
        (
            zhCN: "循环退出",
            zhTW: "迴圈退出",
            enUS: "loop exited"
        ),
        ["liveLogStopBoundaryFound"] = new TextContainer
        (
            zhCN: "已找到边界",
            zhTW: "已找到邊界",
            enUS: "boundary found"
        ),
        ["liveLogStopFloorReached"] = new TextContainer
        (
            zhCN: "已到达下限",
            zhTW: "已到達下限",
            enUS: "floor reached"
        ),
        ["liveFromStartMergeDelayed"] = new TextContainer
        (
            zhCN: "Live from start 正在为 {} 下载更早的分片；仅延后实时合并输出。",
            zhTW: "Live from start 正在為 {} 下載更早的分片；僅延後即時合併輸出。",
            enUS: "Live from start is downloading earlier segments for {}; delaying real-time merge output only."
        ),
        ["liveFromStartSkippedNoTemplate"] = new TextContainer
        (
            zhCN: "Live from start 已跳过 {}：播放列表没有可作为回填模板的媒体分片。",
            zhTW: "Live from start 已跳過 {}：播放清單沒有可作為回填範本的媒體分片。",
            enUS: "Live from start skipped for {}: playlist has no media segment to use as a backfill template."
        ),
        ["liveFromStartSkippedUnpredictable"] = new TextContainer
        (
            zhCN: "Live from start 已跳过 {}：分片 URL 规律不可预测。",
            zhTW: "Live from start 已跳過 {}：分片 URL 規律不可預測。",
            enUS: "Live from start skipped for {}: segment URL pattern is not predictable."
        ),
        ["liveFromStartSkippedInvalidFirstNumber"] = new TextContainer
        (
            zhCN: "Live from start 已跳过 {}：首个分片文件名 [cyan]{}[/] 不包含有效的正整数序号。",
            zhTW: "Live from start 已跳過 {}：首個分片檔名 [cyan]{}[/] 不包含有效的正整數序號。",
            enUS: "Live from start skipped for {}: first segment file name [cyan]{}[/] does not contain a positive numeric sequence."
        ),
        ["liveFromStartDurationFallback"] = new TextContainer
        (
            zhCN: "Live from start：{} 的分片时长不是正数；回填时长回退为 1s。",
            zhTW: "Live from start：{} 的分片時長不是正數；回填時長退回為 1s。",
            enUS: "Live from start: segment duration for {} is not positive; using 1s as the backfill duration fallback."
        ),
        ["liveFromStartHostMirrorRace"] = new TextContainer
        (
            zhCN: "Live from start：{} 的镜像决策：竞速镜像 -> {}；单分片重试次数=0。",
            zhTW: "Live from start：{} 的鏡像決策：競速鏡像 -> {}；單分片重試次數=0。",
            enUS: "Live from start: host mirror decision for {}: racing mirrors -> {}; single-segment retry_count=0."
        ),
        ["liveFromStartHostMirrorOriginal"] = new TextContainer
        (
            zhCN: "Live from start：{} 的镜像决策：未配置竞速镜像；使用原始分片 URL，单分片重试次数=1。",
            zhTW: "Live from start：{} 的鏡像決策：未配置競速鏡像；使用原始分片 URL，單分片重試次數=1。",
            enUS: "Live from start: host mirror decision for {}: no racing mirror configured; using original segment URL with single-segment retry_count=1."
        ),
        ["liveFromStartTimeoutDecision"] = new TextContainer
        (
            zhCN: "Live from start：{} 的超时决策：并发数={}，目标时长={}，探测超时={}，分片超时={}。",
            zhTW: "Live from start：{} 的逾時決策：並行數={}，目標時長={}，探測逾時={}，分片逾時={}。",
            enUS: "Live from start: timeout decision for {}: parallelism={}, target_duration={}, probe_timeout={}, segment_timeout={}"
        ),
        ["liveFromStartLocatingEarliest"] = new TextContainer
        (
            zhCN: "Live from start：正在定位 {} 之前最早可用的分片，流：{}。",
            zhTW: "Live from start：正在定位 {} 之前最早可用的分片，流：{}。",
            enUS: "Live from start: locating earliest available segment before {} for {}."
        ),
        ["liveFromStartStrategyDescending"] = new TextContainer
        (
            zhCN: "Live from start：{} 的策略决策：在 {} 上使用降序回填。",
            zhTW: "Live from start：{} 的策略決策：在 {} 上使用降序回填。",
            enUS: "Live from start: strategy decision for {}: use descending backfill over {}."
        ),
        ["liveFromStartNoEarlierSegment"] = new TextContainer
        (
            zhCN: "Live from start：在 {} 之前，{} 没有可用的更早分片。",
            zhTW: "Live from start：在 {} 之前，{} 沒有可用的更早分片。",
            enUS: "Live from start: no earlier segment available before {} for {}."
        ),
        ["liveFromStartStrategyFuzzy"] = new TextContainer
        (
            zhCN: "Live from start：{} 的策略决策：使用模糊边界；升序填充={}，降序探索={}。",
            zhTW: "Live from start：{} 的策略決策：使用模糊邊界；升序填充={}，降序探索={}。",
            enUS: "Live from start: strategy decision for {}: use fuzzy boundary; ascending_fill={}, descending_explore={}"
        ),
        ["liveFromStartStrategyAscending"] = new TextContainer
        (
            zhCN: "Live from start：{} 的策略决策：最早可用分片为 {}；升序回填 {}。",
            zhTW: "Live from start：{} 的策略決策：最早可用分片為 {}；升序回填 {}。",
            enUS: "Live from start: strategy decision for {}: earliest available segment is {}; backfilling ascending {}."
        ),
        ["liveFromStartDownloadFailed"] = new TextContainer
        (
            zhCN: "Live from start 下载失败：{}",
            zhTW: "Live from start 下載失敗：{}",
            enUS: "Live from start download failed: {}"
        ),
        ["liveFromStartFuzzyBoundaryDecision"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界决策：窗口={}，升序填充={}，降序探索={}。",
            zhTW: "Live from start：{} 的模糊邊界決策：視窗={}，升序填充={}，降序探索={}。",
            enUS: "Live from start: fuzzy boundary decision for {}: window={}, ascending_fill={}, descending_explore={}"
        ),
        ["liveFromStartFuzzyStartExplore"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界决策：升序填充运行时，同时启动 {} 的降序探索。",
            zhTW: "Live from start：{} 的模糊邊界決策：升序填充執行時，同時啟動 {} 的降序探索。",
            enUS: "Live from start: fuzzy boundary decision for {}: start descending exploration over {} while ascending fill runs."
        ),
        ["liveFromStartFuzzyNoExploreRange"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界决策：没有降序探索区间；仅运行升序填充。",
            zhTW: "Live from start：{} 的模糊邊界決策：沒有降序探索區間；僅執行升序填充。",
            enUS: "Live from start: fuzzy boundary decision for {}: no descending exploration range; only ascending fill will run."
        ),
        ["liveFromStartFuzzyStopExplore"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界决策：升序填充已完成，最终裁剪前停止尽力降序探索。",
            zhTW: "Live from start：{} 的模糊邊界決策：升序填充已完成，最終裁剪前停止盡力降序探索。",
            enUS: "Live from start: fuzzy boundary decision for {}: ascending fill finished, stopping best-effort descending exploration before final clipping."
        ),
        ["liveFromStartFuzzyIgnoreExploreFailure"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界决策：忽略降序探索失败（{}）；仅用升序填充完成收尾。",
            zhTW: "Live from start：{} 的模糊邊界決策：忽略降序探索失敗（{}）；僅用升序填充完成收尾。",
            enUS: "Live from start: fuzzy boundary decision for {}: ignoring descending exploration failure ({}); finalizing ascending fill only."
        ),
        ["liveFromStartFuzzyBoundaryFound"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界决策：降序探索在分片 {} 发现不可用边界。",
            zhTW: "Live from start：{} 的模糊邊界決策：降序探索在分片 {} 發現不可用邊界。",
            enUS: "Live from start: fuzzy boundary decision for {}: descending exploration found unavailable boundary at segment {}."
        ),
        ["liveFromStartFuzzyAdopt"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界决策：升序填充没有不可补空洞；采用 {} 个探索分片，范围={}。",
            zhTW: "Live from start：{} 的模糊邊界決策：升序填充沒有不可補空洞；採用 {} 個探索分片，範圍={}。",
            enUS: "Live from start: fuzzy boundary decision for {}: ascending fill has no unfillable gap; adopting {} explored segment(s), range={}"
        ),
        ["liveFromStartFuzzyDiscard"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界决策：升序填充有 {} 个不可补空洞；丢弃 {} 个探索分片，范围={}。",
            zhTW: "Live from start：{} 的模糊邊界決策：升序填充有 {} 個不可補空洞；丟棄 {} 個探索分片，範圍={}。",
            enUS: "Live from start: fuzzy boundary decision for {}: ascending fill has {} unfillable gap(s); discarding {} explored segment(s), range={}"
        ),
        ["liveFromStartFuzzyNoExploreResult"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界决策：没有降序探索结果；仅用升序填充完成收尾。",
            zhTW: "Live from start：{} 的模糊邊界決策：沒有降序探索結果；僅用升序填充完成收尾。",
            enUS: "Live from start: fuzzy boundary decision for {}: no descending exploration result; finalizing ascending fill only."
        ),
        ["liveFromStartAscendingStart"] = new TextContainer
        (
            zhCN: "Live from start：{} 的升序回填决策：区间={}，并发数={}，分片超时={}。",
            zhTW: "Live from start：{} 的升序回填決策：區間={}，並行數={}，分片逾時={}。",
            enUS: "Live from start: ascending backfill decision for {}: range={}, parallelism={}, segment_timeout={}"
        ),
        ["liveFromStartAscendingUnavailable"] = new TextContainer
        (
            zhCN: "Live from start：{} 的升序回填决策：分片 {} 不可用；标记为不可补历史空洞。",
            zhTW: "Live from start：{} 的升序回填決策：分片 {} 不可用；標記為不可補歷史空洞。",
            enUS: "Live from start: ascending backfill decision for {}: segment {} is unavailable; marking it as an unfillable historical gap."
        ),
        ["liveFromStartAscendingStop"] = new TextContainer
        (
            zhCN: "Live from start：{} 的升序回填决策：停止原因={}，已提交={}，不可用={}，复用缓存={}，进行中={}，已解析未提交={}。",
            zhTW: "Live from start：{} 的升序回填決策：停止原因={}，已提交={}，不可用={}，複用快取={}，進行中={}，已解析未提交={}。",
            enUS: "Live from start: ascending backfill decision for {}: stop_reason={}, committed={}, unavailable={}, cache_reused={}, in_flight={}, resolved_uncommitted={}"
        ),
        ["liveFromStartAscendingCleanup"] = new TextContainer
        (
            zhCN: "Live from start：{} 的升序回填决策：取消并清理未提交结果（进行中={}，已解析={}）。",
            zhTW: "Live from start：{} 的升序回填決策：取消並清理未提交結果（進行中={}，已解析={}）。",
            enUS: "Live from start: ascending backfill decision for {}: cancelling and cleaning uncommitted results (in_flight={}, resolved={})."
        ),
        ["liveFromStartFuzzyExploreStart"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界探索决策：降序扫描={}，并发数={}，分片超时={}。",
            zhTW: "Live from start：{} 的模糊邊界探索決策：降序掃描={}，並行數={}，分片逾時={}。",
            enUS: "Live from start: fuzzy boundary exploration decision for {}: descending_scan={}, parallelism={}, segment_timeout={}"
        ),
        ["liveFromStartFuzzyExploreSummary"] = new TextContainer
        (
            zhCN: "Live from start：{} 的模糊边界探索决策：已接受={}，边界={}，已发起下载={}，已清理丢弃={}。",
            zhTW: "Live from start：{} 的模糊邊界探索決策：已接受={}，邊界={}，已發起下載={}，已清理丟棄={}。",
            enUS: "Live from start: fuzzy boundary exploration decision for {}: accepted={}, boundary={}, downloads_started={}, discarded_cleanup={}"
        ),
        ["liveFromStartFinalClipGap"] = new TextContainer
        (
            zhCN: "Live from start：{} 的最终裁剪决策：最高不可补历史空洞是分片 {}；检查是否必须放弃已下载尾段。",
            zhTW: "Live from start：{} 的最終裁剪決策：最高不可補歷史空洞是分片 {}；檢查是否必須放棄已下載尾段。",
            enUS: "Live from start: final clipping decision for {}: highest unfillable historical gap is segment {}; checking whether downloaded tail must be abandoned."
        ),
        ["liveFromStartFinalClipAbandon"] = new TextContainer
        (
            zhCN: "Live from start：{} 的最终裁剪决策：放弃 {} 个位于 {} 及以下的已下载分片，因为它们无法连接到直播边缘。",
            zhTW: "Live from start：{} 的最終裁剪決策：放棄 {} 個位於 {} 及以下的已下載分片，因為它們無法連接到直播邊緣。",
            enUS: "Live from start: final clipping decision for {}: abandoning {} downloaded fragment(s) at or below {} because they cannot connect to the live edge."
        ),
        ["liveFromStartFinalClipNoFragments"] = new TextContainer
        (
            zhCN: "Live from start：{} 的最终裁剪决策：{} 及以下没有已下载分片；无需回滚尾段。",
            zhTW: "Live from start：{} 的最終裁剪決策：{} 及以下沒有已下載分片；無需回滾尾段。",
            enUS: "Live from start: final clipping decision for {}: no downloaded fragment is at or below {}; no tail rollback is needed."
        ),
        ["liveFromStartFinalClipNoGap"] = new TextContainer
        (
            zhCN: "Live from start：{} 的最终裁剪决策：没有需要回滚尾段的不可补历史空洞。",
            zhTW: "Live from start：{} 的最終裁剪決策：沒有需要回滾尾段的不可補歷史空洞。",
            enUS: "Live from start: final clipping decision for {}: no unfillable historical gap requires tail rollback."
        ),
        ["liveFromStartSummary"] = new TextContainer
        (
            zhCN: "Live from start 汇总（{}）：已接受总数={}，最早可用={}，失败边界={}，已接受范围={}，边界探测数={}，已发起下载={}，已放弃尾段={}，已放弃分片数={}，不可补空洞数={}，已清理丢弃={}。",
            zhTW: "Live from start 彙總（{}）：已接受總數={}，最早可用={}，失敗邊界={}，已接受範圍={}，邊界探測數={}，已發起下載={}，已放棄尾段={}，已放棄分片數={}，不可補空洞數={}，已清理丟棄={}。",
            enUS: "Live from start summary for {}: accepted_total={}, earliest_available={}, failed_boundary={}, accepted_range={}, boundary_probes={}, downloads_started={}, abandoned_tail={}, abandoned_fragments={}, unfillable_gaps={}, discarded_cleanup={}"
        ),
        ["liveFromStartDownloaded"] = new TextContainer
        (
            zhCN: "Live from start 已下载 {} 个连续的更早分片，流：{}，范围：{}。",
            zhTW: "Live from start 已下載 {} 個連續的更早分片，流：{}，範圍：{}。",
            enUS: "Live from start downloaded {} contiguous earlier segment(s) for {}: {}."
        ),
        ["liveFromStartNoAcceptedAfterClip"] = new TextContainer
        (
            zhCN: "Live from start：{} 最终裁剪后没有接受任何连续的更早分片。",
            zhTW: "Live from start：{} 最終裁剪後沒有接受任何連續的更早分片。",
            enUS: "Live from start: no contiguous earlier segment accepted for {} after final clipping."
        ),
        ["liveFromStartAbandonedTail"] = new TextContainer
        (
            zhCN: "Live from start：已放弃参差 DVR 尾段 {}，流：{}（丢弃 {} 个已下载分片，所选 live-from-start host 上有 {} 个分片不可用）- 无法跨过不可补空洞连接到直播边缘。",
            zhTW: "Live from start：已放棄參差 DVR 尾段 {}，流：{}（丟棄 {} 個已下載分片，所選 live-from-start host 上有 {} 個分片不可用）- 無法跨過不可補空洞連接到直播邊緣。",
            enUS: "Live from start: abandoned ragged DVR tail {} for {} ({} downloaded fragment(s) discarded, {} segment(s) unavailable on selected live-from-start hosts) - cannot connect to live edge across unfillable gap(s)."
        ),
        ["liveFromStartDescendingStart"] = new TextContainer
        (
            zhCN: "Live from start：{} 的降序回填决策：区间={}，并发数={}，分片超时={}，遇到首个不可用即停止=true。",
            zhTW: "Live from start：{} 的降序回填決策：區間={}，並行數={}，分片逾時={}，遇到首個不可用即停止=true。",
            enUS: "Live from start: descending backfill decision for {}: range={}, parallelism={}, segment_timeout={}, stop_at_first_unavailable=true"
        ),
        ["liveFromStartDescendingStop"] = new TextContainer
        (
            zhCN: "Live from start：{} 的降序回填决策：停止原因={}，已提交={}，边界={}，进行中={}，已解析未提交={}。",
            zhTW: "Live from start：{} 的降序回填決策：停止原因={}，已提交={}，邊界={}，進行中={}，已解析未提交={}。",
            enUS: "Live from start: descending backfill decision for {}: stop_reason={}, committed={}, boundary={}, in_flight={}, resolved_uncommitted={}"
        ),
        ["liveFromStartDescendingCleanup"] = new TextContainer
        (
            zhCN: "Live from start：{} 的降序回填决策：取消并清理未提交或缓存结果（进行中={}，已解析={}，缓存={}）。",
            zhTW: "Live from start：{} 的降序回填決策：取消並清理未提交或快取結果（進行中={}，已解析={}，快取={}）。",
            enUS: "Live from start: descending backfill decision for {}: cancelling and cleaning uncommitted or cached results (in_flight={}, resolved={}, cache={})."
        ),
        ["liveFromStartDescendingNoAccepted"] = new TextContainer
        (
            zhCN: "Live from start：降序回填没有为 {} 接受任何更早分片。",
            zhTW: "Live from start：降序回填沒有為 {} 接受任何更早分片。",
            enUS: "Live from start: descending backfill accepted no earlier segment for {}."
        ),
        ["liveFromStartDescendingScanUnavailable"] = new TextContainer
        (
            zhCN: "Live from start：降序扫描决策：分片 {} 不可用；在此边界停止扫描更小序号。",
            zhTW: "Live from start：降序掃描決策：分片 {} 不可用；在此邊界停止掃描更小序號。",
            enUS: "Live from start: descending scan decision: segment {} is unavailable; stopping lower-number scan at this boundary."
        ),
        ["liveFromStartDescendingScanSummary"] = new TextContainer
        (
            zhCN: "Live from start：降序扫描决策：区间={}，停止原因={}，已提交={}，已派发下载={}，复用缓存={}，进行中={}，已解析未提交={}。",
            zhTW: "Live from start：降序掃描決策：區間={}，停止原因={}，已提交={}，已派發下載={}，複用快取={}，進行中={}，已解析未提交={}。",
            enUS: "Live from start: descending scan decision: range={}, stop_reason={}, committed={}, downloads_dispatched={}, cache_reused={}, in_flight={}, resolved_uncommitted={}"
        ),
        ["liveFromStartDescendingSummary"] = new TextContainer
        (
            zhCN: "Live from start 降序汇总（{}）：已接受总数={}，最早可用={}，失败边界={}，已接受范围={}，边界探测数={}，已发起下载={}，已清理丢弃={}。",
            zhTW: "Live from start 降序彙總（{}）：已接受總數={}，最早可用={}，失敗邊界={}，已接受範圍={}，邊界探測數={}，已發起下載={}，已清理丟棄={}。",
            enUS: "Live from start summary (descending) for {}: accepted_total={}, earliest_available={}, failed_boundary={}, accepted_range={}, boundary_probes={}, downloads_started={}, discarded_cleanup={}"
        ),
        ["liveFromStartLocateStart"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：从 {} 之前开始指数搜索；下限={}，并发数={}。",
            zhTW: "Live from start：定位決策：從 {} 之前開始指數搜尋；下限={}，並行數={}。",
            enUS: "Live from start: locate decision: starting exponential search before {}; floor={}, parallelism={}"
        ),
        ["liveFromStartProbeChecking"] = new TextContainer
        (
            zhCN: "Live from start 探测 #{}（{}）：检查分片 {}...",
            zhTW: "Live from start 探測 #{}（{}）：檢查分片 {}...",
            enUS: "Live from start probe #{} ({}): checking segment {}..."
        ),
        ["liveFromStartProbeAvailableOnHost"] = new TextContainer
        (
            zhCN: "Live from start 探测 #{}：分片 {} [green]可用[/]，命中 [cyan]{}[/]。",
            zhTW: "Live from start 探測 #{}：分片 {} [green]可用[/]，命中 [cyan]{}[/]。",
            enUS: "Live from start probe #{}: segment {} [green]available[/] on [cyan]{}[/]."
        ),
        ["liveFromStartProbeAvailable"] = new TextContainer
        (
            zhCN: "Live from start 探测 #{}：分片 {} [green]可用[/]。",
            zhTW: "Live from start 探測 #{}：分片 {} [green]可用[/]。",
            enUS: "Live from start probe #{}: segment {} [green]available[/]."
        ),
        ["liveFromStartProbeUnavailable"] = new TextContainer
        (
            zhCN: "Live from start 探测 #{}：分片 {} [red]不可用[/]。",
            zhTW: "Live from start 探測 #{}：分片 {} [red]不可用[/]。",
            enUS: "Live from start probe #{}: segment {} [red]unavailable[/]."
        ),
        ["liveFromStartProbePhaseExponential"] = new TextContainer
        (
            zhCN: "指数探测, 深度={}",
            zhTW: "指數探測, 深度={}",
            enUS: "exponential, depth={}"
        ),
        ["liveFromStartProbePhaseBinary"] = new TextContainer
        (
            zhCN: "二分探测, 窗口 {}~{}",
            zhTW: "二分探測, 窗口 {}~{}",
            enUS: "binary, window {}~{}"
        ),
        ["liveFromStartLocateExpand"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：分片 {} 可用；指数步长扩展到 {}，下次探测={}。",
            zhTW: "Live from start：定位決策：分片 {} 可用；指數步長擴展到 {}，下次探測={}。",
            enUS: "Live from start: locate decision: segment {} is available; expanding exponential step to {} and next_probe={}"
        ),
        ["liveFromStartImmediateUnavailable"] = new TextContainer
        (
            zhCN: "Live from start：当前播放列表之前紧邻的分片 {} 不可用；没有可连接到直播边缘的连续更早分片。",
            zhTW: "Live from start：目前播放清單之前緊鄰的分片 {} 不可用；沒有可連接到直播邊緣的連續更早分片。",
            enUS: "Live from start: segment {} immediately before current playlist is unavailable; no contiguous earlier segment can connect to live edge."
        ),
        ["liveFromStartShallowAvailable"] = new TextContainer
        (
            zhCN: "Live from start：可用区间较浅（首次失败深度 {}，最深确认深度 {}）；切换到降序回填。",
            zhTW: "Live from start：可用區間較淺（首次失敗深度 {}，最深確認深度 {}）；切換到降序回填。",
            enUS: "Live from start: shallow available region (first failure at depth {}, deepest confirmed at depth {}); switching to descending backfill."
        ),
        ["liveFromStartLocateFirstUnavailable"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：首个不可用分片是 {}；二分搜索将收窄 {} ~ {}。",
            zhTW: "Live from start：定位決策：首個不可用分片是 {}；二分搜尋將收窄 {} ~ {}。",
            enUS: "Live from start: locate decision: first unavailable segment is {}; binary search will narrow {} ~ {}."
        ),
        ["liveFromStartLocateCancelledBeforeWindow"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：边界窗口选定前搜索已取消。",
            zhTW: "Live from start：定位決策：邊界窗口選定前搜尋已取消。",
            enUS: "Live from start: locate decision: search cancelled before boundary window was selected."
        ),
        ["liveFromStartLocateNoEarlierConfirmed"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：{} 之前没有确认任何更早可用分片。",
            zhTW: "Live from start：定位決策：{} 之前沒有確認任何更早可用分片。",
            enUS: "Live from start: locate decision: no earlier segment was confirmed available before {}."
        ),
        ["liveFromStartLocateReachedFloor"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：指数搜索到达下限仍未探到不可用分片；最早候选窗口为 {}。",
            zhTW: "Live from start：定位決策：指數搜尋到達下限仍未探到不可用分片；最早候選窗口為 {}。",
            enUS: "Live from start: locate decision: exponential search reached floor without an unavailable probe; earliest candidate window is {}."
        ),
        ["liveFromStartLocateUnavailableBeforeAvailable"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：在任何更早可用分片前已经探到不可用分片；无法选择连续的更早分片。",
            zhTW: "Live from start：定位決策：在任何更早可用分片前已經探到不可用分片；無法選擇連續的更早分片。",
            enUS: "Live from start: locate decision: unavailable probe was found before any earlier available segment; no contiguous earlier segment can be selected."
        ),
        ["liveFromStartLocateBoundedWindow"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：将最早候选窗口限定为 {}，它位于不可用分片 {} 之后。",
            zhTW: "Live from start：定位決策：將最早候選窗口限定為 {}，它位於不可用分片 {} 之後。",
            enUS: "Live from start: locate decision: bounded earliest candidate window to {} after unavailable segment {}."
        ),
        ["liveFromStartLocateNarrowing"] = new TextContainer
        (
            zhCN: "Live from start：正在 {} 内收窄最早可用分片（二分到窗口 <= {} 个分片，40s / 目标时长={}）。",
            zhTW: "Live from start：正在 {} 內收窄最早可用分片（二分到視窗 <= {} 個分片，40s / 目標時長={}）。",
            enUS: "Live from start: narrowing earliest available within {} (binary search until window <= {} segment(s), 40s / targetDuration={})."
        ),
        ["liveFromStartLocateExactNoBinary"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：最早可用分片已经精确为 {}；无需二分收窄。",
            zhTW: "Live from start：定位決策：最早可用分片已經精確為 {}；無需二分收窄。",
            enUS: "Live from start: locate decision: earliest available segment is already exact at {}; no binary narrowing needed."
        ),
        ["liveFromStartLocateBinaryAvailable"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：分片 {} 可用；最早候选窗口变为 {}。",
            zhTW: "Live from start：定位決策：分片 {} 可用；最早候選窗口變為 {}。",
            enUS: "Live from start: locate decision: segment {} is available; earliest candidate window becomes {}."
        ),
        ["liveFromStartLocateBinaryUnavailable"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：分片 {} 不可用；最早候选窗口变为 {}。",
            zhTW: "Live from start：定位決策：分片 {} 不可用；最早候選窗口變為 {}。",
            enUS: "Live from start: locate decision: segment {} is unavailable; earliest candidate window becomes {}."
        ),
        ["liveFromStartLocateFuzzyWindow"] = new TextContainer
        (
            zhCN: "Live from start：边界窗口 {} 已足够小；使用模糊边界，并从 {} 开始并发升序填充。",
            zhTW: "Live from start：邊界窗口 {} 已足夠小；使用模糊邊界，並從 {} 開始並發升序填充。",
            enUS: "Live from start: boundary window {} is small enough; using fuzzy boundary and starting concurrent ascending fill from {}."
        ),
        ["liveFromStartLocateCancelledNarrowing"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：收窄边界窗口时搜索已取消。",
            zhTW: "Live from start：定位決策：收窄邊界窗口時搜尋已取消。",
            enUS: "Live from start: locate decision: search cancelled while narrowing boundary window."
        ),
        ["liveFromStartLocateExactSelected"] = new TextContainer
        (
            zhCN: "Live from start：定位决策：最终选择的精确最早可用分片为 {}。",
            zhTW: "Live from start：定位決策：最終選擇的精確最早可用分片為 {}。",
            enUS: "Live from start: locate decision: exact earliest available segment selected as {}."
        ),
        ["liveFromStartProbeCacheReuse"] = new TextContainer
        (
            zhCN: "Live from start 探测缓存决策：分片 {} 复用缓存的 {} 结果。",
            zhTW: "Live from start 探測快取決策：分片 {} 複用快取的 {} 結果。",
            enUS: "Live from start probe cache decision: segment {} reuses cached {} result."
        ),
        ["liveFromStartProbeCannotGenerate"] = new TextContainer
        (
            zhCN: "Live from start 探测缓存决策：无法从模板 URL 生成分片 {}；缓存为不可用。",
            zhTW: "Live from start 探測快取決策：無法從範本 URL 生成分片 {}；快取為不可用。",
            enUS: "Live from start probe cache decision: segment {} cannot be generated from the template URL; caching it as unavailable."
        ),
        ["liveFillGapDetected"] = new TextContainer
        (
            zhCN: "Live fill gap：检测到可预测 URL 规律中缺失 {} 个分片（{}）；已延后到子任务补齐队列。",
            zhTW: "Live fill gap：檢測到可預測 URL 規律中缺失 {} 個分片（{}）；已延後到子任務補齊佇列。",
            enUS: "Live fill gap: detected {} missing segment(s) in predictable URL pattern ({}); deferred to subTask fill queue."
        ),
        ["liveFillGapDetectedFilling"] = new TextContainer
        (
            zhCN: "Live fill gap：检测到可预测 URL 规律中缺失 {} 个分片（{}），正在填充。",
            zhTW: "Live fill gap：檢測到可預測 URL 規律中缺失 {} 個分片（{}），正在填充。",
            enUS: "Live fill gap: detected {} missing segment(s) in predictable URL pattern ({}), filling."
        ),
        ["liveFillGapMergeSkipped"] = new TextContainer
        (
            zhCN: "Live fill gap：实时合并宽限期后跳过未补分片 {}；标记为丢失以避免直播输出停滞。",
            zhTW: "Live fill gap：即時合併寬限期後跳過未補分片 {}；標記為丟失以避免直播輸出停滯。",
            enUS: "Live fill gap: real-time merge skipped unfilled gap at segment {} after grace period; marking it lost to avoid stalling live output."
        ),
        ["liveFillGapSummary"] = new TextContainer
        (
            zhCN: "Live fill gap 汇总（{}）：已补齐={}，已延后={}，已丢失={} ({})，仍待处理={} ({})。",
            zhTW: "Live fill gap 彙總（{}）：已補齊={}，已延後={}，已丟失={} ({})，仍待處理={} ({})。",
            enUS: "Live fill gap summary for {}: filled={}, deferred={}, lost={} ({}), still_pending={} ({})."
        ),
        ["liveFillGapUltimatelyLost"] = new TextContainer
        (
            zhCN: "Live fill gap：最终丢失 {} 个分片，流：{}，范围：{}。",
            zhTW: "Live fill gap：最終丟失 {} 個分片，流：{}，範圍：{}。",
            enUS: "Live fill gap: {} segment(s) ultimately lost for {}: {}."
        ),
        ["liveFillGapLargeCapped"] = new TextContainer
        (
            zhCN: "Live fill gap：可预测 URL 大缺口 {} 有 {} 个缺失分片；将待补扩展限制为最新 {} 个分片（{}），依据 EXT-X-TARGETDURATION={}s。",
            zhTW: "Live fill gap：可預測 URL 大缺口 {} 有 {} 個缺失分片；將待補擴展限制為最新 {} 個分片（{}），依據 EXT-X-TARGETDURATION={}s。",
            enUS: "Live fill gap: large predictable URL gap {} has {} missing segment(s); capped pending expansion to latest {} segment(s) ({}) by EXT-X-TARGETDURATION={}s."
        ),
        ["liveFillGapBatchSummary"] = new TextContainer
        (
            zhCN: "Live fill gap 批次汇总（{}）：已补齐={}，已丢失=0，范围={}，仍待处理={} ({})。",
            zhTW: "Live fill gap 批次彙總（{}）：已補齊={}，已丟失=0，範圍={}，仍待處理={} ({})。",
            enUS: "Live fill gap batch summary for {}: filled={}, lost=0, range={}, still_pending={} ({})."
        ),
        ["cmd_liveRestartOnExtMapChange"] = new TextContainer
        (
            zhCN: "录制直播时若检测到EXT-X-MAP变动，自动收尾当前输出并以新的初始化分片重启录制；关闭时将直接停止录制",
            zhTW: "錄製直播時若檢測到EXT-X-MAP變動，自動收尾當前輸出並以新的初始化分片重啟錄製；關閉時將直接停止錄製",
            enUS: "When EXT-X-MAP changes during live recording, finish the current output and restart recording with the new init segment; disable to stop recording instead"
        ),
        ["cmd_header"] = new TextContainer
        (
            zhCN: "为HTTP请求设置特定的请求头, 例如:\r\n-H \"Cookie: mycookie\" -H \"User-Agent: iOS\"",
            zhTW: "為HTTP請求設置特定的請求頭, 例如:\r\n-H \"Cookie: mycookie\" -H \"User-Agent: iOS\"",
            enUS: "Pass custom header(s) to server, Example:\r\n-H \"Cookie: mycookie\" -H \"User-Agent: iOS\""
        ),
        ["cmd_Input"] = new TextContainer
        (
            zhCN: "链接或文件",
            zhTW: "連結或文件",
            enUS: "Input Url or File"
        ),
        ["cmd_keys"] = new TextContainer
        (
            zhCN: "设置解密密钥, 程序调用mp4decrpyt/shaka-packager/ffmpeg进行解密. 格式:\r\n--key KID1:KEY1 --key KID2:KEY2\r\n对于KEY相同的情况可以直接输入 --key KEY",
            zhTW: "設置解密密鑰, 程序調用mp4decrpyt/shaka-packager/ffmpeg進行解密. 格式:\r\n--key KID1:KEY1 --key KID2:KEY2\r\n對於KEY相同的情況可以直接輸入 --key KEY",
            enUS: "Set decryption key(s) to mp4decrypt/shaka-packager/ffmpeg. format:\r\n--key KID1:KEY1 --key KID2:KEY2\r\nor use --key KEY if all tracks share the same key."
        ),
        ["cmd_keyText"] = new TextContainer
        (
            zhCN: "设置密钥文件,程序将从文件中按KID搜寻KEY以解密.(不建议使用特大文件)",
            zhTW: "設置密鑰文件,程序將從文件中按KID搜尋KEY以解密.(不建議使用特大文件)",
            enUS: "Set the kid-key file, the program will search the KEY with KID from the file.(Very large file are not recommended)"
        ),
        ["cmd_loadKeyFailed"] = new TextContainer
        (
            zhCN: "获取KEY失败，忽略读取.",
            zhTW: "獲取KEY失敗，忽略讀取.",
            enUS: "Failed to get KEY, ignore."
        ),
        ["cmd_logLevel"] = new TextContainer
        (
            zhCN: "设置日志级别",
            zhTW: "設置日誌級別",
            enUS: "Set log level"
        ),
        ["cmd_MP4RealTimeDecryption"] = new TextContainer
        (
            zhCN: "实时解密MP4分片",
            zhTW: "即時解密MP4分片",
            enUS: "Decrypt MP4 segments in real time"
        ),
        ["cmd_saveDir"] = new TextContainer
        (
            zhCN: "设置输出目录",
            zhTW: "設置輸出目錄",
            enUS: "Set output directory"
        ),
        ["cmd_saveName"] = new TextContainer
        (
            zhCN: "设置保存文件名",
            zhTW: "設置保存檔案名",
            enUS: "Set output filename"
        ),
        ["cmd_savePattern"] = new TextContainer
        (
            zhCN: "设置保存文件命名模板, 支持使用变量: \n" +
                  "<SaveName>, <Id>, <Codecs>, <Language>, <Resolution>, \n" +
                  "<Bandwidth>, <MediaType>, <Channels>, <FrameRate>, \n" +
                  "<VideoRange>, <GroupId>, <Ext>, <DateTime>\n" +
                  "<DateTime> 默认为 yyyy-MM-dd_HH-mm-ss, 也可使用 <DateTime:格式> 自定义 (.NET DateTime 格式)\n" +
                  "示例: --save-pattern \"<SaveName>_<DateTime:yyyyMMdd>_<Resolution>\"",
            zhTW: "設置保存檔案命名模板, 支持使用變數: \n" +
                  "<SaveName>, <Id>, <Codecs>, <Language>, <Resolution>, \n" +
                  "<Bandwidth>, <MediaType>, <Channels>, <FrameRate>, \n" +
                  "<VideoRange>, <GroupId>, <Ext>, <DateTime>\n" +
                  "<DateTime> 預設為 yyyy-MM-dd_HH-mm-ss, 也可使用 <DateTime:格式> 自訂 (.NET DateTime 格式)\n" +
                  "示例: --save-pattern \"<SaveName>_<DateTime:yyyyMMdd>_<Resolution>\"",
            enUS: "Set output filename pattern with variables: \n" +
                  "<SaveName>, <Id>, <Codecs>, <Language>, <Resolution>, \n" +
                  "<Bandwidth>, <MediaType>, <Channels>, <FrameRate>, \n" +
                  "<VideoRange>, <GroupId>, <Ext>, <DateTime>\n" +
                  "<DateTime> defaults to yyyy-MM-dd_HH-mm-ss, or use <DateTime:format> for a custom .NET DateTime format\n" +
                  "Example: --save-pattern \"<SaveName>_<DateTime:yyyyMMdd>_<Resolution>\""
        ),
        ["cmd_logFilePath"] = new TextContainer
        (
            zhCN: @"设置日志文件路径, 例如 C:\Logs\log.txt",
            zhTW: @"設定日誌檔案路徑, 例如 C:\Logs\log.txt",
            enUS: @"Set log file path, Example: C:\Logs\log.txt"
        ),
        ["cmd_skipDownload"] = new TextContainer
        (
            zhCN: "跳过下载",
            zhTW: "跳過下載",
            enUS: "Skip download"
        ),
        ["cmd_skipMerge"] = new TextContainer
        (
            zhCN: "跳过合并分片",
            zhTW: "跳過合併分片",
            enUS: "Skip segments merge"
        ),
        ["cmd_subFormat"] = new TextContainer
        (
            zhCN: "字幕输出类型",
            zhTW: "字幕輸出類型",
            enUS: "Subtitle output format"
        ),
        ["cmd_subOnly"] = new TextContainer
        (
            zhCN: "只选取字幕轨道",
            zhTW: "只選取字幕軌道",
            enUS: "Select only subtitle tracks"
        ),
        ["cmd_subtitleFix"] = new TextContainer
        (
            zhCN: "自动修正字幕",
            zhTW: "自動修正字幕",
            enUS: "Automatically fix subtitles"
        ),
        ["cmd_threadCount"] = new TextContainer
        (
            zhCN: "设置下载线程数",
            zhTW: "設置下載執行緒數",
            enUS: "Set download thread count"
        ),
        ["cmd_tmpDir"] = new TextContainer
        (
            zhCN: "设置临时文件存储目录",
            zhTW: "設置臨時文件儲存目錄",
            enUS: "Set temporary file directory"
        ),
        ["cmd_uiLanguage"] = new TextContainer
        (
            zhCN: "设置UI语言",
            zhTW: "設置UI語言",
            enUS: "Set UI language"
        ),
        ["cmd_moreHelp"] = new TextContainer
        (
            zhCN: "查看某个选项的详细帮助信息",
            zhTW: "查看某個選項的詳細幫助訊息",
            enUS: "Set more help info about one option"
        ),
        ["cmd_urlProcessorArgs"] = new TextContainer
        (
            zhCN: "此字符串将直接传递给URL Processor",
            zhTW: "此字符串將直接傳遞給URL Processor",
            enUS: "Give these arguments to the URL Processors."
        ),
        ["cmd_liveRealTimeMerge"] = new TextContainer
        (
            zhCN: "录制直播时实时合并",
            zhTW: "錄製直播時即時合併",
            enUS: "Real-time merge into file when recording live"
        ),
        ["cmd_customProxy"] = new TextContainer
        (
            zhCN: "设置请求代理, 如 http://127.0.0.1:8888",
            zhTW: "設置請求代理, 如 http://127.0.0.1:8888",
            enUS: "Set web request proxy, like http://127.0.0.1:8888"
        ),
        ["cmd_customRange"] = new TextContainer
        (
            zhCN: "仅下载部分分片. 输入 \"--morehelp custom-range\" 以查看详细信息",
            zhTW: "僅下載部分分片. 輸入 \"--morehelp custom-range\" 以查看詳細訊息",
            enUS: "Download only part of the segments. Use \"--morehelp custom-range\" for more details"
        ),
        ["cmd_useSystemProxy"] = new TextContainer
        (
            zhCN: "使用系统默认代理",
            zhTW: "使用系統默認代理",
            enUS: "Use system default proxy"
        ),
        ["cmd_forceIpv4"] = new TextContainer
        (
            zhCN: "仅使用 IPv4 进行连接",
            zhTW: "僅使用 IPv4 進行連線",
            enUS: "Use IPv4 only for connections"
        ),
        ["cmd_forceIpv6"] = new TextContainer
        (
            zhCN: "仅使用 IPv6 进行连接",
            zhTW: "僅使用 IPv6 進行連線",
            enUS: "Use IPv6 only for connections"
        ),
        ["cmd_http10"] = new TextContainer
        (
            zhCN: "强制使用 HTTP/1.0",
            zhTW: "強制使用 HTTP/1.0",
            enUS: "Use HTTP/1.0"
        ),
        ["cmd_http11"] = new TextContainer
        (
            zhCN: "强制使用 HTTP/1.1",
            zhTW: "強制使用 HTTP/1.1",
            enUS: "Use HTTP/1.1"
        ),
        ["cmd_http2"] = new TextContainer
        (
            zhCN: "使用 HTTP/2（HTTPS 通过 ALPN 协商）",
            zhTW: "使用 HTTP/2（HTTPS 透過 ALPN 協商）",
            enUS: "Use HTTP/2 (ALPN on HTTPS)"
        ),
        ["cmd_http2PriorKnowledge"] = new TextContainer
        (
            zhCN: "强制使用 HTTP/2；明文 HTTP 使用 H2C",
            zhTW: "強制使用 HTTP/2；明文 HTTP 使用 H2C",
            enUS: "Use HTTP/2 with prior knowledge; cleartext HTTP uses H2C"
        ),
        ["cmd_livePerformAsVod"] = new TextContainer
        (
            zhCN: "以点播方式下载直播流",
            zhTW: "以點播方式下載直播流",
            enUS: "Download live streams as vod"
        ),
        ["cmd_liveWaitTime"] = new TextContainer
        (
            zhCN: "手动设置直播列表刷新间隔",
            zhTW: "手動設置直播列表刷新間隔",
            enUS: "Manually set the live playlist refresh interval"
        ),
        ["cmd_adKeyword"] = new TextContainer
        (
            zhCN: "设置广告分片的URL关键字(正则表达式)",
            zhTW: "設置廣告分片的URL關鍵字(正則表達式)",
            enUS: "Set URL keywords (regular expressions) for AD segments"
        ),
        ["cmd_liveTakeCount"] = new TextContainer
        (
            zhCN: "手动设置录制直播时首次获取分片的数量",
            zhTW: "手動設置錄製直播時首次獲取分片的數量",
            enUS: "Manually set the number of segments downloaded for the first time when recording live"
        ),
        ["cmd_liveFromStart"] = new TextContainer
        (
            zhCN: "录制直播时，对可预测的分片尽力向前回溯；每生成一个历史分片文件名就立即完整下载，直到回溯下载失败",
            zhTW: "錄製直播時，對可預測的分片盡力向前回溯；每生成一個歷史分片檔名就立即完整下載，直到回溯下載失敗",
            enUS: "When recording live, best-effort backfill predictable segments toward the start; immediately download each generated full segment until a backfill download fails"
        ),
        ["cmd_liveKeepM3u8Updated"] = new TextContainer
        (
            zhCN: "录制直播时持续更新 raw.m3u8，不包含补洞内容",
            zhTW: "錄製直播時持續更新 raw.m3u8，不包含補洞內容",
            enUS: "Keep raw.m3u8 updated while recording live, excluding filled gap content"
        ),
        ["cmd_liveHostMirror"] = new TextContainer
        (
            zhCN: "录制直播时额外镜像 host；每个分片同时从主 URL 与各镜像拉取，采用最先成功完成的副本。可重复指定。支持 hostname、host:port 或完整 http(s) URL。",
            zhTW: "錄製直播時額外鏡像 host；每個分片同時從主 URL 與各鏡像拉取，採用最先成功完成的副本。可重複指定。支援 hostname、host:port 或完整 http(s) URL。",
            enUS: "Extra mirror host(s) for live recording: each segment is fetched concurrently from the primary URL and mirrors; the first successful completion wins. Repeatable. Accepts hostname, host:port, or full http(s) URL."
        ),
        ["cmd_customHLSMethod"] = new TextContainer
        (
            zhCN: "指定HLS加密方式 (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)",
            zhTW: "指定HLS加密方式 (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)",
            enUS: "Set HLS encryption method (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)"
        ),
        ["cmd_customHLSKey"] = new TextContainer
        (
            zhCN: "指定HLS解密KEY. 可以是文件, HEX或Base64",
            zhTW: "指定HLS解密KEY. 可以是文件, HEX或Base64",
            enUS: "Set the HLS decryption key. Can be file, HEX or Base64"
        ),
        ["cmd_customHLSIv"] = new TextContainer
        (
            zhCN: "指定HLS解密IV. 可以是文件, HEX或Base64",
            zhTW: "指定HLS解密IV. 可以是文件, HEX或Base64",
            enUS: "Set the HLS decryption iv. Can be file, HEX or Base64"
        ),
        ["cmd_livePipeMux"] = new TextContainer
        (
            zhCN: "录制直播并开启实时合并时通过管道+ffmpeg实时混流. 输入 \"--morehelp live-pipe-mux\" 以查看详细信息",
            zhTW: "錄製直播並開啟即時合併時通過管道+ffmpeg即時混流. 輸入 \"--morehelp live-pipe-mux\" 以查看詳細訊息",
            enUS: "Real-time muxing through pipeline + ffmpeg (liveRealTimeMerge enabled). Use \"--morehelp live-pipe-mux\" for more details"
        ),
        ["cmd_livePipeMux_more"] = new TextContainer
        (
            zhCN: "录制直播并开启实时合并时通过管道+ffmpeg实时混流. 你能够以:分隔形式指定如下参数:\r\n\r\n" +
                  "* format=FORMAT: 指定混流容器 mkv, mp4, ts, flv (未指定时 fMP4 输入默认 mp4，其余默认 ts)\r\n" +
                  "* bin_path=PATH: 指定 ffmpeg 路径 (默认: 自动寻找)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 使用默认格式\r\n" +
                  "--live-pipe-mux\r\n" +
                  "# 混流为 mkv 容器\r\n" +
                  "--live-pipe-mux format=mkv\r\n" +
                  "# 混流为 mp4 并指定 ffmpeg 路径\r\n" +
                  "--live-pipe-mux format=mp4:bin_path=\"C\\:\\ffmpeg\\bin\\ffmpeg.exe\"\r\n",
            zhTW: "錄製直播並開啟即時合併時通過管道+ffmpeg即時混流. 你能夠以:分隔形式指定如下參數:\r\n\r\n" +
                  "* format=FORMAT: 指定混流容器 mkv, mp4, ts, flv (未指定時 fMP4 輸入預設 mp4，其餘預設 ts)\r\n" +
                  "* bin_path=PATH: 指定 ffmpeg 路徑 (默認: 自動尋找)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 使用默認格式\r\n" +
                  "--live-pipe-mux\r\n" +
                  "# 混流為 mkv 容器\r\n" +
                  "--live-pipe-mux format=mkv\r\n" +
                  "# 混流為 mp4 並指定 ffmpeg 路徑\r\n" +
                  "--live-pipe-mux format=mp4:bin_path=\"C\\:\\ffmpeg\\bin\\ffmpeg.exe\"\r\n",
            enUS: "Real-time muxing through pipeline + ffmpeg when liveRealTimeMerge is enabled. OPTIONS is a colon separated list of:\r\n\r\n" +
                  "* format=FORMAT: set container. mkv, mp4, ts, flv (defaults to mp4 for fMP4 input, ts otherwise)\r\n" +
                  "* bin_path=PATH: set ffmpeg binary path. (Default: auto)\r\n\r\n" +
                  "Examples: \r\n" +
                  "# use auto-detected format\r\n" +
                  "--live-pipe-mux\r\n" +
                  "# mux to mkv\r\n" +
                  "--live-pipe-mux format=mkv\r\n" +
                  "# mux to mp4 with custom ffmpeg path\r\n" +
                  "--live-pipe-mux format=mp4:bin_path=\"C\\:\\ffmpeg\\bin\\ffmpeg.exe\"\r\n"
        ),
        ["cmd_liveKeepSegments"] = new TextContainer
        (
            zhCN: "录制直播并开启实时合并时依然保留分片",
            zhTW: "錄製直播並開啟即時合併時依然保留分片",
            enUS: "Keep segments when recording a live (liveRealTimeMerge enabled)"
        ),
        ["cmd_liveRecordLimit"] = new TextContainer
        (
            zhCN: "录制直播时的录制时长限制",
            zhTW: "錄製直播時的錄製時長限制",
            enUS: "Recording time limit when recording live"
        ),
        ["cmd_taskStartAt"] = new TextContainer
        (
            zhCN: "在此时间之前不会开始执行任务",
            zhTW: "在此時間之前不會開始執行任務",
            enUS: "Task execution will not start before this time"
        ),
        ["cmd_useShakaPackager"] = new TextContainer
        (
            zhCN: "解密时使用shaka-packager替代mp4decrypt",
            zhTW: "解密時使用shaka-packager替代mp4decrypt",
            enUS: "Use shaka-packager instead of mp4decrypt to decrypt"
        ),
        ["cmd_decryptionEngine"] = new TextContainer
        (
            zhCN: "设置解密时使用的第三方程序",
            zhTW: "設置解密時使用的第三方程序",
            enUS: "Set the third-party program used for decryption"
        ),
        ["cmd_concurrentDownload"] = new TextContainer
        (
            zhCN: "并发下载已选择的音频、视频和字幕",
            zhTW: "並發下載已選擇的音訊、影片和字幕",
            enUS: "Concurrently download the selected audio, video and subtitles"
        ),
        ["cmd_selectVideo"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的视频流. 输入 \"--morehelp select-video\" 以查看详细信息",
            zhTW: "通過正則表達式選擇符合要求的影片軌. 輸入 \"--morehelp select-video\" 以查看詳細訊息",
            enUS: "Select video streams by regular expressions. Use \"--morehelp select-video\" for more details"
        ),
        ["cmd_dropVideo"] = new TextContainer
        (
            zhCN: "通过正则表达式去除符合要求的视频流.",
            zhTW: "通過正則表達式去除符合要求的影片串流.",
            enUS: "Drop video streams by regular expressions."
        ),
        ["cmd_selectVideo_more"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的视频流. 你能够以:分隔形式指定如下参数:\r\n\r\n" +
                  "id=REGEX:lang=REGEX:name=REGEX:codecs=REGEX:res=REGEX:frame=REGEX\r\n" +
                  "segsMin=number:segsMax=number:ch=REGEX:range=REGEX:url=REGEX\r\n" +
                  "plistDurMin=hms:plistDurMax=hms:bwMin=int:bwMax=int:role=string:for=FOR\r\n\r\n" +
                  "* for=FOR: 选择方式. best[number], worst[number], all (默认: best)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 选择最佳视频\r\n" +
                  "-sv best\r\n" +
                  "# 选择4K+HEVC视频\r\n" +
                  "-sv res=\"3840*\":codecs=hvc1:for=best\r\n" +
                  "# 选择长度大于1小时20分钟30秒的视频\r\n" +
                  "-sv plistDurMin=\"1h20m30s\":for=best\r\n" +
                  "-sv role=\"main\":for=best\r\n" +
                  "# 选择码率在800Kbps至1Mbps之间的视频\r\n" +
                  "-sv bwMin=800:bwMax=1000\r\n",
            zhTW: "通過正則表達式選擇符合要求的影片軌. 你能夠以:分隔形式指定如下參數:\r\n\r\n" +
                  "id=REGEX:lang=REGEX:name=REGEX:codecs=REGEX:res=REGEX:frame=REGEX\r\n" +
                  "segsMin=number:segsMax=number:ch=REGEX:range=REGEX:url=REGEX\r\n" +
                  "plistDurMin=hms:plistDurMax=hms:bwMin=int:bwMax=int:role=string:for=FOR\r\n\r\n" +
                  "* for=FOR: 選擇方式. best[number], worst[number], all (默認: best)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 選擇最佳影片\r\n" +
                  "-sv best\r\n" +
                  "# 選擇4K+HEVC影片\r\n" +
                  "-sv res=\"3840*\":codecs=hvc1:for=best\r\n" +
                  "# 選擇長度大於1小時20分鐘30秒的影片\r\n" +
                  "-sv plistDurMin=\"1h20m30s\":for=best\r\n" +
                  "-sv role=\"main\":for=best\r\n" +
                  "# 選擇碼率在800Kbps至1Mbps之間的影片\r\n" +
                  "-sv bwMin=800:bwMax=1000\r\n",
            enUS: "Select video streams by regular expressions. OPTIONS is a colon separated list of:\r\n\r\n" +
                  "id=REGEX:lang=REGEX:name=REGEX:codecs=REGEX:res=REGEX:frame=REGEX\r\n" +
                  "segsMin=number:segsMax=number:ch=REGEX:range=REGEX:url=REGEX\r\n" +
                  "plistDurMin=hms:plistDurMax=hms:bwMin=int:bwMax=int:role=string:for=FOR\r\n\r\n" +
                  "* for=FOR: Select type. best[number], worst[number], all (Default: best)\r\n\r\n" +
                  "Examples: \r\n" +
                  "# select best video\r\n" +
                  "-sv best\r\n" +
                  "# select 4K+HEVC video\r\n" +
                  "-sv res=\"3840*\":codecs=hvc1:for=best\r\n" +
                  "# Select best video with duration longer than 1 hour 20 minutes 30 seconds\r\n" +
                  "-sv plistDurMin=\"1h20m30s\":for=best\r\n" +
                  "-sv role=\"main\":for=best\r\n" +
                  "# Select video with bandwidth between 800Kbps and 1Mbps\r\n" +
                  "-sv bwMin=800:bwMax=1000\r\n"
        ),
        ["cmd_selectAudio"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的音频流. 输入 \"--morehelp select-audio\" 以查看详细信息",
            zhTW: "通過正則表達式選擇符合要求的音軌. 輸入 \"--morehelp select-audio\" 以查看詳細訊息",
            enUS: "Select audio streams by regular expressions. Use \"--morehelp select-audio\" for more details"
        ),
        ["cmd_dropAudio"] = new TextContainer
        (
            zhCN: "通过正则表达式去除符合要求的音频流.",
            zhTW: "通過正則表達式去除符合要求的音軌.",
            enUS: "Drop audio streams by regular expressions."
        ),
        ["cmd_selectAudio_more"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的音频流. 参考 --select-video\r\n\r\n" +
                  "例如: \r\n" +
                  "# 选择所有音频\r\n" +
                  "-sa all\r\n" +
                  "# 选择最佳英语音轨\r\n" +
                  "-sa lang=en:for=best\r\n" +
                  "# 选择最佳的2条英语(或日语)音轨\r\n" +
                  "-sa lang=\"ja|en\":for=best2\r\n" +
                  "-sa role=\"main\":for=best\r\n",
            zhTW: "通過正則表達式選擇符合要求的音軌. 參考 --select-video\r\n\r\n" +
                  "例如: \r\n" +
                  "# 選擇所有音訊\r\n" +
                  "-sa all\r\n" +
                  "# 選擇最佳英語音軌\r\n" +
                  "-sa lang=en:for=best\r\n" +
                  "# 選擇最佳的2條英語(或日語)音軌\r\n" +
                  "-sa lang=\"ja|en\":for=best2\r\n" +
                  "-sa role=\"main\":for=best\r\n",
            enUS: "Select audio streams by regular expressions. ref --select-video\r\n\r\n" +
                  "Examples: \r\n" +
                  "# select all\r\n" +
                  "-sa all\r\n" +
                  "# select best eng audio\r\n" +
                  "-sa lang=en:for=best\r\n" +
                  "# select best 2, and language is ja or en\r\n" +
                  "-sa lang=\"ja|en\":for=best2\r\n" +
                  "-sa role=\"main\":for=best\r\n"
        ),
        ["cmd_selectSubtitle"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的字幕流. 输入 \"--morehelp select-subtitle\" 以查看详细信息",
            zhTW: "通過正則表達式選擇符合要求的字幕流. 輸入 \"--morehelp select-subtitle\" 以查看詳細訊息",
            enUS: "Select subtitle streams by regular expressions. Use \"--morehelp select-subtitle\" for more details"
        ),
        ["cmd_dropSubtitle"] = new TextContainer
        (
            zhCN: "通过正则表达式去除符合要求的字幕流.",
            zhTW: "通過正則表達式去除符合要求的字幕流.",
            enUS: "Drop subtitle streams by regular expressions."
        ),
        ["cmd_custom_range"] = new TextContainer
        (
            zhCN: "下载点播内容时, 仅下载部分分片.\r\n\r\n" +
                  "例如: \r\n" +
                  "# 下载[0,10]共11个分片\r\n" +
                  "--custom-range 0-10\r\n" +
                  "# 下载从序号10开始的后续分片\r\n" +
                  "--custom-range 10-\r\n" +
                  "# 下载前100个分片\r\n" +
                  "--custom-range -99\r\n" +
                  "# 下载第5分钟到20分钟的内容\r\n" +
                  "--custom-range 05:00-20:00\r\n",
            zhTW: "下載點播內容時, 僅下載部分分片.\r\n\r\n" +
                  "例如: \r\n" +
                  "# 下載[0,10]共11個分片\r\n" +
                  "--custom-range 0-10\r\n" +
                  "# 下載從序號10開始的後續分片\r\n" +
                  "--custom-range 10-\r\n" +
                  "# 下載前100個分片\r\n" +
                  "--custom-range -99\r\n" +
                  "# 下載第5分鐘到20分鐘的內容\r\n" +
                  "--custom-range 05:00-20:00\r\n",
            enUS: "Download only part of the segments when downloading vod content.\r\n\r\n" +
                  "Examples: \r\n" +
                  "# Download [0,10], a total of 11 segments\r\n" +
                  "--custom-range 0-10\r\n" +
                  "# Download subsequent segments starting from index 10\r\n" +
                  "--custom-range 10-\r\n" +
                  "# Download the first 100 segments\r\n" +
                  "--custom-range -99\r\n" +
                  "# Download content from the 05:00 to 20:00\r\n" +
                  "--custom-range 05:00-20:00\r\n"
        ),
        ["cmd_selectSubtitle_more"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的字幕流. 参考 --select-video\r\n\r\n" +
                  "例如: \r\n" +
                  "# 选择所有字幕\r\n" +
                  "-ss all\r\n" +
                  "# 选择所有带有\"中文\"的字幕\r\n" +
                  "-ss name=\"中文\":for=all\r\n",
            zhTW: "通過正則表達式選擇符合要求的字幕流. 參考 --select-video\r\n\r\n" +
                  "例如: \r\n" +
                  "# 選擇所有字幕\r\n" +
                  "-ss all\r\n" +
                  "# 選擇所有帶有\"中文\"的字幕\r\n" +
                  "-ss name=\"中文\":for=all\r\n",
            enUS: "Select subtitle streams by regular expressions. ref --select-video\r\n\r\n" +
                  "Examples: \r\n" +
                  "# select all subs\r\n" +
                  "-ss all\r\n" +
                  "# select all subs containing \"English\"\r\n" +
                  "-ss name=\"English\":for=all\r\n"
        ),
        ["cmd_muxAfterDone_more"] = new TextContainer
        (
            zhCN: "所有工作完成时尝试混流分离的音视频. 你能够以:分隔形式指定如下参数:\r\n\r\n" +
                  "* format=FORMAT: 指定混流容器 mkv, mp4, ts, flv\r\n" +
                  "* muxer=MUXER: 指定混流程序 ffmpeg, mkvmerge (默认: ffmpeg)\r\n" +
                  "* bin_path=PATH: 指定程序路径 (默认: 自动寻找)\r\n" +
                  "* skip_sub=BOOL: 是否忽略字幕文件 (默认: false)\r\n" +
                  "* keep=BOOL: 混流完成是否保留文件 true, false (默认: false)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 混流为mp4容器\r\n" +
                  "-M format=mp4\r\n" +
                  "# 使用mkvmerge, 自动寻找程序\r\n" +
                  "-M format=mkv:muxer=mkvmerge\r\n" +
                  "# 使用mkvmerge, 自定义程序路径\r\n" +
                  "-M format=mkv:muxer=mkvmerge:bin_path=\"C\\:\\Program Files\\MKVToolNix\\mkvmerge.exe\"\r\n",
            zhTW: "所有工作完成時嘗試混流分離的影音. 你能夠以:分隔形式指定如下參數:\r\n\r\n" +
                  "* format=FORMAT: 指定混流容器 mkv, mp4, ts, flv\r\n" +
                  "* muxer=MUXER: 指定混流程序 ffmpeg, mkvmerge (默認: ffmpeg)\r\n" +
                  "* bin_path=PATH: 指定程序路徑 (默認: 自動尋找)\r\n" +
                  "* skip_sub=BOOL: 是否忽略字幕文件 (默認: false)\r\n" +
                  "* keep=BOOL: 混流完成是否保留文件 true, false (默認: false)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 混流為mp4容器\r\n" +
                  "-M format=mp4\r\n" +
                  "# 使用mkvmerge, 自動尋找程序\r\n" +
                  "-M format=mkv:muxer=mkvmerge\r\n" +
                  "# 使用mkvmerge, 自訂程序路徑\r\n" +
                  "-M format=mkv:muxer=mkvmerge:bin_path=\"C\\:\\Program Files\\MKVToolNix\\mkvmerge.exe\"\r\n",
            enUS: "When all works is done, try to mux the downloaded streams. OPTIONS is a colon separated list of:\r\n\r\n" +
                  "* format=FORMAT: set container. mkv, mp4, ts, flv\r\n" +
                  "* muxer=MUXER: set muxer. ffmpeg, mkvmerge (Default: ffmpeg)\r\n" +
                  "* bin_path=PATH: set binary file path. (Default: auto)\r\n" +
                  "* skip_sub=BOOL: set whether or not skip subtitle files (Default: false)\r\n" +
                  "* keep=BOOL: set whether or not keep files. true, false (Default: false)\r\n\r\n" +
                  "Examples: \r\n" +
                  "# mux to mp4\r\n" +
                  "-M format=mp4\r\n" +
                  "# use mkvmerge, auto detect bin path\r\n" +
                  "-M format=mkv:muxer=mkvmerge\r\n" +
                  "# use mkvmerge, set bin path\r\n" +
                  "-M format=mkv:muxer=mkvmerge:bin_path=\"C\\:\\Program Files\\MKVToolNix\\mkvmerge.exe\"\r\n"
        ),
        ["cmd_muxAfterDone"] = new TextContainer
        (
            zhCN: "所有工作完成时尝试混流分离的音视频. 输入 \"--morehelp mux-after-done\" 以查看详细信息",
            zhTW: "所有工作完成時嘗試混流分離的影音. 輸入 \"--morehelp mux-after-done\" 以查看詳細訊息",
            enUS: "When all works is done, try to mux the downloaded streams. Use \"--morehelp mux-after-done\" for more details"
        ),
        ["cmd_muxImport"] = new TextContainer
        (
            zhCN: "混流时引入外部媒体文件. 输入 \"--morehelp mux-import\" 以查看详细信息",
            zhTW: "混流時引入外部媒體檔案. 輸入 \"--morehelp mux-import\" 以查看詳細訊息",
            enUS: "When MuxAfterDone enabled, allow to import local media files. Use \"--morehelp mux-import\" for more details"
        ),
        ["cmd_muxImport_more"] = new TextContainer
        (
            zhCN: "混流时引入外部媒体文件. 你能够以:分隔形式指定如下参数:\r\n\r\n" +
                  "* path=PATH: 指定媒体文件路径\r\n" +
                  "* lang=CODE: 指定媒体文件语言代码 (非必须)\r\n" +
                  "* name=NAME: 指定媒体文件描述信息 (非必须)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 引入外部字幕\r\n" +
                  "--mux-import path=zh-Hans.srt:lang=chi:name=\"中文 (简体)\"\r\n" +
                  "# 引入外部音轨+字幕\r\n" +
                  "--mux-import path=\"D\\:\\media\\atmos.m4a\":lang=eng:name=\"English Description Audio\" --mux-import path=\"D\\:\\media\\eng.vtt\":lang=eng:name=\"English (Description)\"",
            zhTW: "混流時引入外部媒體檔案. 你能夠以:分隔形式指定如下參數:\r\n\r\n" +
                  "* path=PATH: 指定媒體檔案路徑\r\n" +
                  "* lang=CODE: 指定媒體檔案語言代碼 (非必須)\r\n" +
                  "* name=NAME: 指定媒體檔案描述訊息 (非必須)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 引入外部字幕\r\n" +
                  "--mux-import path=zh-Hant.srt:lang=chi:name=\"中文 (繁體)\"\r\n" +
                  "# 引入外部音軌+字幕\r\n" +
                  "--mux-import path=\"D\\:\\media\\atmos.m4a\":lang=eng:name=\"English Description Audio\" --mux-import path=\"D\\:\\media\\eng.vtt\":lang=eng:name=\"English (Description)\"",
            enUS: "When MuxAfterDone enabled, allow to import local media files. OPTIONS is a colon separated list of:\r\n\r\n" +
                  "* path=PATH: set file path\r\n" +
                  "* lang=CODE: set media language code (not required)\r\n" +
                  "* name=NAME: set description (not required)\r\n\r\n" +
                  "Examples: \r\n" +
                  "# import subtitle\r\n" +
                  "--mux-import path=en-US.srt:lang=eng:name=\"English (Original)\"\r\n" +
                  "# import audio and subtitle\r\n" +
                  "--mux-import path=\"D\\:\\media\\atmos.m4a\":lang=eng:name=\"English Description Audio\" --mux-import path=\"D\\:\\media\\eng.vtt\":lang=eng:name=\"English (Description)\""
        ),
        ["cmd_writeMetaJson"] = new TextContainer
        (
            zhCN: "解析后的信息是否输出json文件",
            zhTW: "解析後的訊息是否輸出json文件",
            enUS: "Write meta json after parsed"
        ),
        ["liveLimit"] = new TextContainer
        (
            zhCN: "本次直播录制时长上限: ",
            zhTW: "本次直播錄製時長上限: ",
            enUS: "Live recording duration limit: "
        ),
        ["realTimeDecMessage"] = new TextContainer
        (
            zhCN: "启用实时解密时，建议用shaka-packager而非mp4decrypt/ffmpeg",
            zhTW: "啟用即時解密時，建議用shaka-packager而非mp4decrypt/ffmpeg",
            enUS: "When enabling real-time decryption, it is recommended to use shaka-packager instead of mp4decrypt/ffmpeg"
        ),
        ["liveLimitReached"] = new TextContainer
        (
            zhCN: "到达直播录制上限，即将停止录制",
            zhTW: "到達直播錄製上限，即將停止錄製",
            enUS: "Live recording limit reached, will stop recording soon"
        ),
        ["saveName"] = new TextContainer
        (
            zhCN: "保存文件名: ",
            zhTW: "保存檔案名: ",
            enUS: "Save Name: "
        ),
        ["fetch"] = new TextContainer
        (
            zhCN: "获取: ",
            zhTW: "獲取: ",
            enUS: "Fetch: "
        ),
        ["ffmpegMerge"] = new TextContainer
        (
            zhCN: "调用ffmpeg合并中...",
            zhTW: "調用ffmpeg合併中...",
            enUS: "ffmpeg merging..."
        ),
        ["ffmpegNotFound"] = new TextContainer
        (
            zhCN: "找不到ffmpeg，请自行下载：https://ffmpeg.org/download.html",
            zhTW: "找不到ffmpeg，請自行下載：https://ffmpeg.org/download.html",
            enUS: "ffmpeg not found, please download at: https://ffmpeg.org/download.html"
        ),
        ["mkvmergeNotFound"] = new TextContainer
        (
            zhCN: "找不到mkvmerge，请自行下载：https://mkvtoolnix.download/downloads.html",
            zhTW: "找不到mkvmerge，請自行下載：https://mkvtoolnix.download/downloads.html",
            enUS: "mkvmerge not found, please download at: https://mkvtoolnix.download/downloads.html"
        ),
        ["shakaPackagerNotFound"] = new TextContainer
        (
            zhCN: "找不到shaka-packager，请自行下载：https://github.com/shaka-project/shaka-packager/releases",
            zhTW: "找不到shaka-packager，請自行下載：https://github.com/shaka-project/shaka-packager/releases",
            enUS: "shaka-packager not found, please download at: https://github.com/shaka-project/shaka-packager/releases"
        ),
        ["mp4decryptNotFound"] = new TextContainer
        (
            zhCN: "找不到mp4decrypt，请自行下载：https://www.bento4.com/downloads/",
            zhTW: "找不到mp4decrypt，請自行下載：https://www.bento4.com/downloads/",
            enUS: "mp4decrypt not found, please download at: https://www.bento4.com/downloads/"
        ),
        ["fixingTTML"] = new TextContainer
        (
            zhCN: "正在提取TTML(raw)字幕...",
            zhTW: "正在提取TTML(raw)字幕...",
            enUS: "Extracting TTML(raw) subtitle..."
        ),
        ["fixingTTMLmp4"] = new TextContainer
        (
            zhCN: "正在提取TTML(mp4)字幕...",
            zhTW: "正在提取TTML(mp4)字幕...",
            enUS: "Extracting TTML(mp4) subtitle..."
        ),
        ["fixingVTT"] = new TextContainer
        (
            zhCN: "正在提取VTT(raw)字幕...",
            zhTW: "正在提取VTT(raw)字幕...",
            enUS: "Extracting VTT(raw) subtitle..."
        ),
        ["fixingVTTmp4"] = new TextContainer
        (
            zhCN: "正在提取VTT(mp4)字幕...",
            zhTW: "正在提取VTT(mp4)字幕...",
            enUS: "Extracting VTT(mp4) subtitle..."
        ),
        ["keyProcessorNotFound"] = new TextContainer
        (
            zhCN: "找不到支持的Processor",
            zhTW: "找不到支持的Processor",
            enUS: "No Processor matched"
        ),
        ["liveFound"] = new TextContainer
        (
            zhCN: "检测到直播流",
            zhTW: "檢測到直播流",
            enUS: "Live stream found"
        ),
        ["loadingUrl"] = new TextContainer
        (
            zhCN: "加载URL: ",
            zhTW: "載入URL: ",
            enUS: "Loading URL: "
        ),
        ["masterM3u8Found"] = new TextContainer
        (
            zhCN: "检测到Master列表，开始解析全部流信息",
            zhTW: "檢測到Master列表，開始解析全部流訊息",
            enUS: "Master List detected, try parse all streams"
        ),
        ["allowHlsMultiExtMap"] = new TextContainer
        (
            zhCN: "已经允许识别多个#EXT-X-MAP标签, 本软件可能无法正确处理, 请手动确认内容完整性",
            zhTW: "已經允許識別多個#EXT-X-MAP標籤, 本軟件可能無法正確處理, 請手動確認內容完整性",
            enUS: "Multiple #EXT-X-MAP tags are now allowed for detection. However, this software may not handle them correctly. Please manually verify the content's integrity"
        ),
        ["matchTS"] = new TextContainer
        (
            zhCN: "内容匹配: [white on green3]HTTP Live MPEG2-TS[/]",
            zhTW: "內容匹配: [white on green3]HTTP Live MPEG2-TS[/]",
            enUS: "Content Matched: [white on green3]HTTP Live MPEG2-TS[/]"
        ),
        ["matchDASH"] = new TextContainer
        (
            zhCN: "内容匹配: [white on mediumorchid1]Dynamic Adaptive Streaming over HTTP[/]",
            zhTW: "內容匹配: [white on mediumorchid1]Dynamic Adaptive Streaming over HTTP[/]",
            enUS: "Content Matched: [white on mediumorchid1]Dynamic Adaptive Streaming over HTTP[/]"
        ),
        ["matchMSS"] = new TextContainer
        (
            zhCN: "内容匹配: [white on steelblue1]Microsoft Smooth Streaming[/]",
            zhTW: "內容匹配: [white on steelblue1]Microsoft Smooth Streaming[/]",
            enUS: "Content Matched: [white on steelblue1]Microsoft Smooth Streaming[/]"
        ),
        ["matchHLS"] = new TextContainer
        (
            zhCN: "内容匹配: [white on deepskyblue1]HTTP Live Streaming[/]",
            zhTW: "內容匹配: [white on deepskyblue1]HTTP Live Streaming[/]",
            enUS: "Content Matched: [white on deepskyblue1]HTTP Live Streaming[/]"
        ),
        ["matchBinaryData"] = new TextContainer
        (
            zhCN: "内容匹配: [white on deepskyblue1]Binary Data[/]",
            zhTW: "內容匹配: [white on deepskyblue1]Binary Data[/]",
            enUS: "Content Matched: [white on deepskyblue1]Binary Data[/]"
        ),
        ["partMerge"] = new TextContainer
        (
            zhCN: "分片数量大于1800个，开始分块合并...",
            zhTW: "分片數量大於1800個，開始分塊合併...",
            enUS: "Segments more than 1800, start partial merge..."
        ),
        ["notSupported"] = new TextContainer
        (
            zhCN: "当前输入不受支持 ",
            zhTW: "當前輸入不受支援 ",
            enUS: "Input not supported "
        ),
        ["parsingStream"] = new TextContainer
        (
            zhCN: "正在解析媒体信息...",
            zhTW: "正在解析媒體信息...",
            enUS: "Parsing streams..."
        ),
        ["promptChoiceText"] = new TextContainer
        (
            zhCN: "[grey](按键盘上下键以浏览更多内容)[/]",
            zhTW: "[grey](按鍵盤上下鍵以瀏覽更多內容)[/]",
            enUS: "[grey](Move up and down to reveal more streams)[/]"
        ),
        ["promptInfo"] = new TextContainer
        (
            zhCN: "(按 [blue]空格键[/] 选择流, [green]回车键[/] 完成选择)",
            zhTW: "(按 [blue]空格鍵[/] 選擇流, [green]確認鍵[/] 完成選擇)",
            enUS: "(Press [blue]<space>[/] to toggle a stream, [green]<enter>[/] to accept)"
        ),
        ["promptTitle"] = new TextContainer
        (
            zhCN: "请选择 [green]你要下载的内容[/]:",
            zhTW: "請選擇 [green]你要下載的內容[/]:",
            enUS: "Please select [green]what you want to download[/]:"
        ),
        ["readingInfo"] = new TextContainer
        (
            zhCN: "读取媒体信息...",
            zhTW: "讀取媒體訊息...",
            enUS: "Reading media info..."
        ),
        ["searchKey"] = new TextContainer
        (
            zhCN: "正在尝试从文本文件搜索KEY...",
            zhTW: "正在嘗試從文本文件搜尋KEY...",
            enUS: "Trying to search for KEY from text file..."
        ),
        ["decryptionFailed"] = new TextContainer
        (
            zhCN: "解密失败",
            zhTW: "解密失敗",
            enUS: "Decryption failed"
        ),
        ["segmentCountCheckNotPass"] = new TextContainer
        (
            zhCN: "分片数量校验不通过, 共{}个,已下载{}.",
            zhTW: "分片數量校驗不通過, 共{}個,已下載{}.",
            enUS: "Segment count check not pass, total: {}, downloaded: {}."
        ),
        ["selectedStream"] = new TextContainer
        (
            zhCN: "已选择的流:",
            zhTW: "已選擇的流:",
            enUS: "Selected streams:"
        ),
        ["startDownloading"] = new TextContainer
        (
            zhCN: "开始下载...",
            zhTW: "開始下載...",
            enUS: "Start downloading..."
        ),
        ["streamsInfo"] = new TextContainer
        (
            zhCN: "已解析, 共计 {} 条媒体流, 基本流 {} 条, 可选音频流 {} 条, 可选字幕流 {} 条",
            zhTW: "已解析, 共計 {} 條媒體流, 基本流 {} 條, 可選音頻流 {} 條, 可選字幕流 {} 條",
            enUS: "Extracted, there are {} streams, with {} basic streams, {} audio streams, {} subtitle streams"
        ),
        ["writeJson"] = new TextContainer
        (
            zhCN: "写出meta json",
            zhTW: "寫出meta json",
            enUS: "Writing meta json"
        ),
        ["noStreamsToDownload"] = new TextContainer
        (
            zhCN: "没有找到需要下载的流",
            zhTW: "沒有找到需要下載的流",
            enUS: "No stream found to download"
        ),
        ["loadUrlFailed"] = new TextContainer
        (
            zhCN: "加载URL失败",
            zhTW: "載入URL失敗",
            enUS: "Failed to load URL"
        ),

    };
}
