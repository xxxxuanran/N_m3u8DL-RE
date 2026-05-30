# N_m3u8DL-RE

[See English version here](README.en.md)

跨平台的 DASH/HLS/MSS 下载工具。支持点播、直播 (DASH/HLS)。

> 本仓库为 [nilaoda/N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) 的 Fork，在 upstream 基础上包含额外功能与修复。预编译二进制由本仓库 CI 在推送 `v*` 标签时自动构建并发布。

[![stars](https://img.shields.io/github/stars/xxxxuanran/N_m3u8DL-RE?label=Stars)](https://github.com/xxxxuanran/N_m3u8DL-RE) [![release](https://img.shields.io/github/v/release/xxxxuanran/N_m3u8DL-RE?label=Release)](https://github.com/xxxxuanran/N_m3u8DL-RE/releases) [![license](https://img.shields.io/github/license/xxxxuanran/N_m3u8DL-RE?label=License)](https://github.com/xxxxuanran/N_m3u8DL-RE) [![downloads](https://img.shields.io/github/downloads/xxxxuanran/N_m3u8DL-RE/total?label=Downloads)](https://github.com/xxxxuanran/N_m3u8DL-RE/releases)

遇到 BUG 请先确认是否使用 [Releases](https://github.com/xxxxuanran/N_m3u8DL-RE/releases) 中的最新版本；若问题仍存在，请到本仓库 [Issues](https://github.com/xxxxuanran/N_m3u8DL-RE/issues) 反馈（上游相关问题也可参考 [nilaoda/N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE/issues)）。

---

## 下载

从 [Releases](https://github.com/xxxxuanran/N_m3u8DL-RE/releases) 页面下载与系统匹配的压缩包。推送形如 `v0.6.1-beta` 的 Git 标签后，GitHub Actions 会自动构建并发布各平台产物。

| 平台 | 文件名示例 |
|------|------------|
| Windows x64 | `N_m3u8DL-RE_v0.6.1-beta_win-x64_*.zip` |
| Windows arm64 | `N_m3u8DL-RE_v0.6.1-beta_win-arm64_*.zip` |
| Windows x86 (NT 6.0+) | `N_m3u8DL-RE_v0.6.1-beta_win-NT6.0-x86_*.zip` |
| Linux x64 (musl 静态) | `N_m3u8DL-RE_v0.6.1-beta_linux-x64_*.tar.gz` |
| Linux arm64 (musl 静态) | `N_m3u8DL-RE_v0.6.1-beta_linux-arm64_*.tar.gz` |
| macOS x64 | `N_m3u8DL-RE_v0.6.1-beta_osx-x64_*.tar.gz` |
| macOS arm64 | `N_m3u8DL-RE_v0.6.1-beta_osx-arm64_*.tar.gz` |

Linux 产物为 musl 完全静态链接，可在多数发行版上直接运行。正式 Release 构建启动时版本示例：`N_m3u8DL-RE (Beta version) 20260531+v0.6.1-beta`；在非 tag 提交上本地编译时可能显示 `yyyyMMdd+<commit>`。

---

版本较低的Windows系统自带的终端可能不支持本程序，替代方案：在 [cmder](https://github.com/cmderdev/cmder) 中运行。

Arch Linux 可以从 AUR 获取：[n-m3u8dl-re-bin](https://aur.archlinux.org/packages/n-m3u8dl-re-bin)、[n-m3u8dl-re-git](https://aur.archlinux.org/packages/n-m3u8dl-re-git)

```bash
# Arch Linux 及其衍生版安装 N_m3u8DL-RE 发行版 (该源非本人维护)
yay -Syu n-m3u8dl-re-bin

# Arch Linux 及其衍生版安装 N_m3u8DL-RE 开发版 (该源非本人维护)
yay -Syu n-m3u8dl-re-git
```

---

## 命令行参数

### 相对上游的 Fork 特色功能

本 Fork 在 [nilaoda/N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) 基础上增加了以下能力（上游 `--help` 中通常没有对应选项）：

#### 直播录制增强

| 参数 | 说明 |
|------|------|
| `--live-host-mirror <HOST>` | 为直播分片配置镜像 Host，主 URL 与各镜像**并发拉取**，采用最先成功的结果。可重复指定；支持 `hostname`、`host:port` 或完整 `http(s)://` URL。 |
| `--live-fill-segments-gap` | 刷新播放列表出现序号间隙时，按连续数字规律**自动补齐**缺失分片（默认开启）。仅在首次 media playlist 确认各 segment URL query 一致时才会补齐。 |
| `--live-fill-segments-gap-max <NUM>` | 单次自动补齐允许填补的最大分片数量。未指定时默认为 `max(1, 60 ÷ 刷新间隔秒数)`，其中刷新间隔取自 M3U8 播放列表（约为该次列表内分片总时长的一半再提前 2 秒），也可由 `--live-wait-time` 覆盖。 |
| `--live-restart-on-ext-map-change` | 检测到 `EXT-X-MAP`（初始化分片）变化时，**收尾当前文件并以新 init 分片继续录制**（默认开启）。设为 `false` 时改为直接停止录制（与上游旧行为接近）。 |

#### 输出与网络

| 参数 | 说明 |
|------|------|
| `--save-pattern` 的 `<DateTime>` | 命名模板支持 `<DateTime>`（默认 `yyyy-MM-dd_HH-mm-ss`）及 `<DateTime:格式>`（.NET 日期格式），便于长跑直播按时间分段命名。示例：`--save-pattern "<SaveName>_<DateTime:yyyyMMdd>_<Resolution>"` |
| `--log-file-only` | 日志**仅写入文件**，不在终端输出，适合后台长时间录制。 |
| `-4` / `--ipv4`、`-6` / `--ipv6` | 强制连接仅走 IPv4 或 IPv6。 |
| `--http1.0`、`--http1.1`、`--http2`、`--http2-prior-knowledge` | 显式选择 HTTP 协议版本；默认 HTTPS 通过 ALPN 协商 HTTP/2。 |

#### 其他改进

- 支持 **bilibili** 相关 DRM 密钥类型（`bilidrm`）。
- 修复相同参数重复启动时**临时目录冲突**的问题。
- Release 构建提供 **Linux musl 完全静态**二进制（见上方「下载」表格）。
- 直播实时合并时 fmp4 init 分片**只写入一次**，避免重复头数据。
- 改进 mux 输出命名与最终重命名的安全性。
- 直播 gap fill 在首次 media playlist 确认各 segment **URL query 一致**后才启用，避免误补分片。

```
Description:
  N_m3u8DL-RE (Beta version) 20260531+v0.6.1-beta

Usage:
  N_m3u8DL-RE <input> [options]

Arguments:
  <input>  链接或文件

Options:
  --tmp-dir <tmp-dir>                                     设置临时文件存储目录
  --save-dir <save-dir>                                   设置输出目录
  --save-name <save-name>                                 设置保存文件名
  --save-pattern <save-pattern>                           设置保存文件命名模板, 支持使用变量:
                                                          <SaveName>, <Id>, <Codecs>, <Language>, <Resolution>,
                                                          <Bandwidth>, <MediaType>, <Channels>, <FrameRate>,
                                                          <VideoRange>, <GroupId>, <Ext>, <DateTime>
                                                          <DateTime> 默认为 yyyy-MM-dd_HH-mm-ss, 也可使用 <DateTime:格式> 自定义 (.NET DateTime 格式)
                                                          示例: --save-pattern "<SaveName>_<DateTime:yyyyMMdd>_<Resolution>"
  --log-file-path <log-file-path>                         设置日志文件路径, 例如 C:\Logs\log.txt
  --base-url <base-url>                                   设置BaseURL
  --thread-count <number>                                 设置下载线程数 [default: 12]
  --download-retry-count <number>                         每个分片下载异常时的重试次数 [default: 3]
  --http-request-timeout <seconds>                        HTTP请求的超时时间(秒) [default: 100]
  --force-ansi-console                                    强制认定终端为支持ANSI且可交互的终端
  --no-ansi-color                                         去除ANSI颜色
  --auto-select                                           自动选择所有类型的最佳轨道 [default: False]
  --skip-merge                                            跳过合并分片 [default: False]
  --skip-download                                         跳过下载 [default: False]
  --check-segments-count                                  检测实际下载的分片数量和预期数量是否匹配 [default: True]
  --binary-merge                                          二进制合并 [default: False]
  --use-ffmpeg-concat-demuxer                             使用 ffmpeg 合并时，使用 concat 分离器而非 concat 协议 [default: False]
  --del-after-done                                        完成后删除临时文件 [default: False]
  --no-date-info                                          混流时不写入日期信息 [default: False]
  --no-log                                                关闭日志文件输出 [default: False]
  --log-file-only                                         仅将日志输出到文件，不输出到终端 [default: False]
  --write-meta-json                                       解析后的信息是否输出json文件 [default: True]
  --append-url-params                                     将输入Url的Params添加至分片, 对某些网站很有用, 例如 kakao.com [default: True]
  -mt, --concurrent-download                              并发下载已选择的音频、视频和字幕 [default: False]
  -H, --header <header>                                   为HTTP请求设置特定的请求头, 例如:
                                                          -H "Cookie: mycookie" -H "User-Agent: iOS"
  --sub-only                                              只选取字幕轨道 [default: False]
  --sub-format <SRT|VTT>                                  字幕输出类型 [default: SRT]
  --auto-subtitle-fix                                     自动修正字幕 [default: True]
  --ffmpeg-binary-path <PATH>                             ffmpeg可执行程序全路径, 例如 C:\Tools\ffmpeg.exe
  --log-level <DEBUG|ERROR|INFO|OFF|WARN>                 设置日志级别 [default: INFO]
  --ui-language <en-US|zh-CN|zh-TW>                       设置UI语言
  --urlprocessor-args <urlprocessor-args>                 此字符串将直接传递给URL Processor
  --key <key>                                             设置解密密钥, 程序调用mp4decrpyt/shaka-packager/ffmpeg进行解密. 格式:
                                                          --key KID1:KEY1 --key KID2:KEY2
                                                          对于KEY相同的情况可以直接输入 --key KEY
  --key-text-file <key-text-file>                         设置密钥文件,程序将从文件中按KID搜寻KEY以解密.(不建议使用特大文件)
  --decryption-engine <FFMPEG|MP4DECRYPT|SHAKA_PACKAGER>  设置解密时使用的第三方程序 [default: MP4DECRYPT]
  --decryption-binary-path <PATH>                         MP4解密所用工具的全路径, 例如 C:\Tools\mp4decrypt.exe
  --mp4-real-time-decryption                              实时解密MP4分片 [default: False]
  -R, --max-speed <SPEED>                                 设置限速，单位支持 Mbps 或 Kbps，如：15M 100K
  -M, --mux-after-done <OPTIONS>                          所有工作完成时尝试混流分离的音视频. 输入 "--morehelp mux-after-done" 以查看详细信息
  --custom-hls-method <METHOD>                            指定HLS加密方式 (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)
  --custom-hls-key <FILE|HEX|BASE64>                      指定HLS解密KEY. 可以是文件, HEX或Base64
  --custom-hls-iv <FILE|HEX|BASE64>                       指定HLS解密IV. 可以是文件, HEX或Base64
  --use-system-proxy                                      使用系统默认代理 [default: True]
  --custom-proxy <URL>                                    设置请求代理, 如 http://127.0.0.1:8888
  -4, --ipv4                                              仅使用 IPv4 进行连接 [default: False]
  -6, --ipv6                                              仅使用 IPv6 进行连接 [default: False]
  --http1.0                                               强制使用 HTTP/1.0 [default: False]
  --http1.1                                               强制使用 HTTP/1.1 [default: False]
  --http2                                                 使用 HTTP/2（HTTPS 通过 ALPN 协商） [default: True]
  --http2-prior-knowledge                                 强制使用 HTTP/2；明文 HTTP 使用 H2C [default: False]
  --custom-range <RANGE>                                  仅下载部分分片. 输入 "--morehelp custom-range" 以查看详细信息
  --task-start-at <yyyyMMddHHmmss>                        在此时间之前不会开始执行任务
  --live-perform-as-vod                                   以点播方式下载直播流 [default: False]
  --live-real-time-merge                                  录制直播时实时合并 [default: False]
  --live-keep-segments                                    录制直播并开启实时合并时依然保留分片 [default: True]
  --live-pipe-mux                                         录制直播并开启实时合并时通过管道+ffmpeg实时混流到TS文件 [default: False]
  --live-fix-vtt-by-audio                                 通过读取音频文件的起始时间修正VTT字幕 [default: False]
  --live-host-mirror <HOST>                               录制直播时额外镜像 host；每个分片同时从主 URL 与各镜像拉取，采用最先成功完成的副本。可重复指定。支持 hostname、host:port 或完整 http(s) URL。
  --live-record-limit <HH:mm:ss>                          录制直播时的录制时长限制
  --live-wait-time <SEC>                                  手动设置直播列表刷新间隔
  --live-take-count <NUM>                                 手动设置录制直播时首次获取分片的数量 [default: 16]
  --live-fill-segments-gap                                录制直播刷新播放列表出现间隙时，按可预测的连续数字命名规律自动补齐缺失的分片 [default: True]
  --live-fill-segments-gap-max <NUM>                      录制直播自动补齐缺失分片时允许补齐的最大数量
  --live-restart-on-ext-map-change                        录制直播时若检测到EXT-X-MAP变动，自动收尾当前输出并以新的初始化分片重启录制；关闭时将直接停止录制 [default: True]
  --mux-import <OPTIONS>                                  混流时引入外部媒体文件. 输入 "--morehelp mux-import" 以查看详细信息
  -sv, --select-video <OPTIONS>                           通过正则表达式选择符合要求的视频流. 输入 "--morehelp select-video" 以查看详细信息
  -sa, --select-audio <OPTIONS>                           通过正则表达式选择符合要求的音频流. 输入 "--morehelp select-audio" 以查看详细信息
  -ss, --select-subtitle <OPTIONS>                        通过正则表达式选择符合要求的字幕流. 输入 "--morehelp select-subtitle" 以查看详细信息
  -dv, --drop-video <OPTIONS>                             通过正则表达式去除符合要求的视频流.
  -da, --drop-audio <OPTIONS>                             通过正则表达式去除符合要求的音频流.
  -ds, --drop-subtitle <OPTIONS>                          通过正则表达式去除符合要求的字幕流.
  --ad-keyword <REG>                                      设置广告分片的URL关键字(正则表达式)
  --disable-update-check                                  禁用版本更新检测 [default: False]
  --allow-hls-multi-ext-map                               允许HLS中的多个#EXT-X-MAP(实验性) [default: False]
  --morehelp <OPTION>                                     查看某个选项的详细帮助信息
  -?, -h, --help                                          Show help and usage information
  --version                                               Show version information
```

<details>
<summary>点击查看More Help</summary>

```
More Help:

  --mux-after-done

所有工作完成时尝试混流分离的音视频. 你能够以:分隔形式指定如下参数:

* format=FORMAT: 指定混流容器 mkv, mp4
* muxer=MUXER: 指定混流程序 ffmpeg, mkvmerge (默认: ffmpeg)
* bin_path=PATH: 指定程序路径 (默认: 自动寻找)
* skip_sub=BOOL: 是否忽略字幕文件 (默认: false)
* keep=BOOL: 混流完成是否保留文件 true, false (默认: false)

例如:
# 混流为mp4容器
-M format=mp4
# 使用mkvmerge, 自动寻找程序
-M format=mkv:muxer=mkvmerge
# 使用mkvmerge, 自定义程序路径
-M format=mkv:muxer=mkvmerge:bin_path="C\:\Program Files\MKVToolNix\mkvmerge.exe"
```

```
More Help:

  --mux-import

混流时引入外部媒体文件. 你能够以:分隔形式指定如下参数:

* path=PATH: 指定媒体文件路径
* lang=CODE: 指定媒体文件语言代码 (非必须)
* name=NAME: 指定媒体文件描述信息 (非必须)

例如:
# 引入外部字幕
--mux-import path=zh-Hans.srt:lang=chi:name="中文 (简体)"
# 引入外部音轨+字幕
--mux-import path="D\:\media\atmos.m4a":lang=eng:name="English Description Audio" --mux-import path="D\:\media\eng.vtt":lang=eng:name="English (Description)"
```

```
More Help:

  --select-video

通过正则表达式选择符合要求的视频流. 你能够以:分隔形式指定如下参数:

id=REGEX:lang=REGEX:name=REGEX:codecs=REGEX:res=REGEX:frame=REGEX
segsMin=number:segsMax=number:ch=REGEX:range=REGEX:url=REGEX
plistDurMin=hms:plistDurMax=hms:for=FOR

* for=FOR: 选择方式. best[number], worst[number], all (默认: best)

例如:
# 选择最佳视频
-sv best
# 选择4K+HEVC视频
-sv res="3840*":codecs=hvc1:for=best
# 选择长度大于1小时20分钟30秒的视频
-sv plistDurMin="1h20m30s":for=best
```

```
More Help:

  --select-audio

通过正则表达式选择符合要求的音频流. 参考 --select-video

例如:
# 选择所有音频
-sa all
# 选择最佳英语音轨
-sa lang=en:for=best
# 选择最佳的2条英语(或日语)音轨
-sa lang="ja|en":for=best2
```

```
More Help:

  --select-subtitle

通过正则表达式选择符合要求的字幕流. 参考 --select-video

例如:
# 选择所有字幕
-ss all
# 选择所有带有"中文"的字幕
-ss name="中文":for=all
```

```
More Help:

  --custom-range

下载点播内容时, 仅下载部分分片.

例如:
# 下载[0,10]共11个分片
--custom-range 0-10
# 下载从序号10开始的后续分片
--custom-range 10-
# 下载前100个分片
--custom-range -99
# 下载第5分钟到20分钟的内容
--custom-range 05:00-20:00
```
```
More Help:

  --save-pattern

使用变量设置输出文件命名模板. 支持的变量:

* <SaveName>: 用户指定的保存名称 (--save-name)
* <Id>: 流的任务ID
* <Codecs>: 编解码器信息 (例如: avc1.64001f, mp4a.40.2)
* <Language>: 语言代码 (例如: en, zh-CN)
* <Resolution>: 视频分辨率 (例如: 1920x1080)
* <Bandwidth>: 流的带宽/比特率
* <MediaType>: 媒体类型 (VIDEO, AUDIO, SUBTITLES)
* <Channels>: 音频声道配置
* <FrameRate>: 帧率
* <VideoRange>: 视频色域/HDR信息 (SDR, HDR10等)
* <GroupId>: 流组标识符

使用场景:
当下载多个相同类型的流时(例如多个不同分辨率的视频)，使用此选项可以避免文件名冲突。

例如:
# 下载1080p和720p视频，文件名包含分辨率
--save-pattern "<SaveName>_<Resolution>" --save-name "video"
# 输出: video_1920x1080.mp4, video_1280x720.mp4

# 包含带宽信息
--save-pattern "<SaveName>_<Resolution>_<Bandwidth>kbps"
# 输出: video_1920x1080_5000000kbps.mp4

# 下载多个音频流，包含语言和声道
--save-pattern "<SaveName>_<Language>_<Channels>ch"
# 输出: audio_en_2ch.m4a, audio_es_2ch.m4a, audio_en_6ch.m4a

# 复杂模板
--save-pattern "<MediaType>_<Resolution>_<Codecs>_<Language>"
# 输出: VIDEO_1920x1080_avc1.64001f_en.mp4

注意:
如果不使用 --save-pattern，程序会在文件名冲突时自动使用流的元数据(分辨率、带宽等)
生成唯一的文件名，而不是简单地添加 ".copy" 后缀。
```

</details>

## 运行截图

### 点播

![RE1](img/RE.gif)

还可以并行下载+自动混流

![RE2](img/RE2.gif)

### 直播

录制TS直播源：

[click to show gif](http://pan.iqiyi.com/file/paopao/W0LfmaMRvuA--uCdOpZ1cldM5JCVhMfIm7KFqr4oKCz80jLn0bBb-9PWmeCFZ-qHpAaQydQ1zk-CHYT_UbRLtw.gif)

录制MPD直播源：

[click to show gif](http://pan.iqiyi.com/file/paopao/nmAV5MOh0yIyHhnxdgM_6th_p2nqrFsM4k-o3cUPwUa8Eh8QOU4uyPkLa_BlBrMa3GBnKWSk8rOaUwbsjKN14g.gif)

录制过程中，借助ffmpeg完成对音视频的实时混流

```
ffmpeg -readrate 1 -i 2022-09-21_19-54-42_V.mp4 -i 2022-09-21_19-54-42_V.chi.m4a -c copy 2022-09-21_19-54-42_V.ts
```

从 v0.1.5 开始，可以尝试开启 `live-pipe-mux` 来代替以上命令

> [!NOTE]
> 如果网络环境不够稳定，请不要开启 `live-pipe-mux`。管道内数据读取由 ffmpeg 负责，在某些环境下容易丢失直播数据。

从 v0.1.8 开始，能够通过设置环境变量 `RE_LIVE_PIPE_OPTIONS` 来改变 `live-pipe-mux` 时 ffmpeg 的某些选项： <https://github.com/nilaoda/N_m3u8DL-RE/issues/162#issuecomment-1592462532>

## 赞助

感谢上游作者 nilaoda 的原项目开发与维护。

<a href="https://www.buymeacoffee.com/nilaoda" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/default-orange.png" alt="Buy Me A Coffee" height="41" width="174"></a>

