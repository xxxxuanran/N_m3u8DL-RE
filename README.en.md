# N_m3u8DL-RE [EN]

Cross-platform DASH/HLS/MSS download tool. Supports on-demand and live streaming (DASH/HLS).

> This repository is a fork of [nilaoda/N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) with additional features and fixes. Prebuilt binaries are produced by CI on this repo when a `v*` tag is pushed.

[![stars](https://img.shields.io/github/stars/xxxxuanran/N_m3u8DL-RE?label=Stars)](https://github.com/xxxxuanran/N_m3u8DL-RE) [![release](https://img.shields.io/github/v/release/xxxxuanran/N_m3u8DL-RE?label=Release)](https://github.com/xxxxuanran/N_m3u8DL-RE/releases) [![license](https://img.shields.io/github/license/xxxxuanran/N_m3u8DL-RE?label=License)](https://github.com/xxxxuanran/N_m3u8DL-RE) [![downloads](https://img.shields.io/github/downloads/xxxxuanran/N_m3u8DL-RE/total?label=Downloads)](https://github.com/xxxxuanran/N_m3u8DL-RE/releases)

If you encounter a bug, please confirm you are on the latest [Release](https://github.com/xxxxuanran/N_m3u8DL-RE/releases). If the issue persists, open an [Issue](https://github.com/xxxxuanran/N_m3u8DL-RE/issues) here (upstream issues: [nilaoda/N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE/issues)).

---

## Download

Get prebuilt packages from [Releases](https://github.com/xxxxuanran/N_m3u8DL-RE/releases). Pushing a tag such as `v0.6.2-beta` triggers GitHub Actions to build and publish artifacts for all supported platforms.

| Platform | Example filename |
|----------|------------------|
| Windows x64 | `N_m3u8DL-RE_v0.6.2-beta_win-x64_*.zip` |
| Windows arm64 | `N_m3u8DL-RE_v0.6.2-beta_win-arm64_*.zip` |
| Windows x86 (NT 6.0+) | `N_m3u8DL-RE_v0.6.2-beta_win-NT6.0-x86_*.zip` |
| Linux x64 (static musl) | `N_m3u8DL-RE_v0.6.2-beta_linux-x64_*.tar.gz` |
| Linux arm64 (static musl) | `N_m3u8DL-RE_v0.6.2-beta_linux-arm64_*.tar.gz` |
| macOS x64 | `N_m3u8DL-RE_v0.6.2-beta_osx-x64_*.tar.gz` |
| macOS arm64 | `N_m3u8DL-RE_v0.6.2-beta_osx-arm64_*.tar.gz` |

Linux builds are fully static (musl). A release build shows e.g. `N_m3u8DL-RE (Beta version) 20260601+v0.6.2-beta`; local builds off a non-tag commit may show `yyyyMMdd+<commit>`.

---

The built-in terminal in older versions of Windows may not support this program. As an alternative, try running it in [cmder](https://github.com/cmderdev/cmder).

Arch Linux users can install from AUR: [n-m3u8dl-re-bin](https://aur.archlinux.org/packages/n-m3u8dl-re-bin), [n-m3u8dl-re-git](https://aur.archlinux.org/packages/n-m3u8dl-re-git)

```bash
# Install N_m3u8DL-RE release version on Arch Linux and its derivatives (not maintained by the author)
yay -Syu n-m3u8dl-re-bin

# Install N_m3u8DL-RE development version on Arch Linux and its derivatives (not maintained by the author)
yay -Syu n-m3u8dl-re-git
```

---

## Command line parameters

### Fork features (vs upstream)

This fork extends [nilaoda/N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) with the following (generally not present in upstream `--help`):

#### Live recording

| Option | Description |
|--------|-------------|
| `--live-host-mirror <HOST>` | Extra mirror host(s) for live segments: fetch concurrently from the primary URL and mirrors; **first successful response wins**. Repeatable; accepts `hostname`, `host:port`, or full `http(s)://` URL. |
| `--live-from-start` | When recording live, best-effort backfill predictable segments toward the start; immediately download each generated full segment until a backfill download fails. |
| `--live-keep-m3u8-updated` | Keep `raw.m3u8` updated while recording live, excluding filled gap content. |
| `--live-fill-segments-gap` | When the media playlist has sequence gaps, **auto-fill** missing segments by predictable numeric naming (default: on). Gap fill runs only if the initial media playlist has identical URL query strings across segments. |
| `--live-fill-segments-gap-max <NUM>` | Max segments to fill per gap-fill pass. When omitted, defaults to `max(1, min(60, ceil(60/wait_time)))`, where `wait_time` is the refresh interval in seconds and can be overridden by `--live-wait-time`. |
| `--live-restart-on-ext-map-change` | On `EXT-X-MAP` (init segment) change, **finalize current output and restart** with the new init segment (default: on). Set to `false` to stop recording instead (closer to legacy upstream behavior). |

#### Output & networking

| Option | Description |
|--------|-------------|
| `--save-pattern` `<DateTime>` | Template variable `<DateTime>` (default format `yyyy-MM-dd_HH-mm-ss`) and `<DateTime:format>` (.NET date format) for time-based filenames in long live runs. Example: `--save-pattern "<SaveName>_<DateTime:yyyyMMdd>_<Resolution>"` |
| `--log-file-only` | Write logs **to file only**, not to the console. |
| `-4` / `--ipv4`, `-6` / `--ipv6` | Force IPv4-only or IPv6-only connections. |
| `--http1.0`, `--http1.1`, `--http2`, `--http2-prior-knowledge` | Pin HTTP version; default uses HTTP/2 via ALPN on HTTPS. |

#### Other improvements

- **Bilibili** DRM key type (`bilidrm`) support.
- Fix **temp directory reuse** when launching duplicate runs with the same arguments.
- Release builds ship **fully static Linux musl** binaries (see Download table above).
- Write fmp4 init segment **only once** during live real-time merge.
- Improve mux output naming and finalize rename safety.
- Gate live segment gap fill on **consistent URL query** checked at the first media playlist.

> From `N_m3u8DL-RE --help` on a win-x64 Native AOT release build.

```
Description:
  N_m3u8DL-RE (Beta version) 20260601+v0.6.2-beta

Usage:
  N_m3u8DL-RE <input> [options]

Arguments:
  <input>  Input Url or File

Options:
  --tmp-dir <tmp-dir>                                     Set temporary file directory
  --save-dir <save-dir>                                   Set output directory
  --save-name <save-name>                                 Set output filename
  --save-pattern <save-pattern>                           Set output filename pattern with variables: 
                                                          <SaveName>, <Id>, <Codecs>, <Language>, <Resolution>, 
                                                          <Bandwidth>, <MediaType>, <Channels>, <FrameRate>, 
                                                          <VideoRange>, <GroupId>, <Ext>, <DateTime>
                                                          <DateTime> defaults to yyyy-MM-dd_HH-mm-ss, or use <DateTime:format> for a custom .NET DateTime format
                                                          Example: --save-pattern "<SaveName>_<DateTime:yyyyMMdd>_<Resolution>"
  --log-file-path <log-file-path>                         Set log file path, Example: C:\Logs\log.txt
  --base-url <base-url>                                   Set BaseURL
  --thread-count <number>                                 Set download thread count [default: 12]
  --download-retry-count <number>                         The number of retries when download segment error [default: 3]
  --http-request-timeout <seconds>                        Timeout duration for HTTP requests (in seconds) [default: 100]
  --force-ansi-console                                    Force assuming the terminal is ANSI-compatible and interactive
  --no-ansi-color                                         Remove ANSI colors
  --auto-select                                           Automatically selects the best tracks of all types [default: False]
  --skip-merge                                            Skip segments merge [default: False]
  --skip-download                                         Skip download [default: False]
  --check-segments-count                                  Check if the actual number of segments downloaded matches the expected number [default: True]
  --binary-merge                                          Binary merge [default: False]
  --use-ffmpeg-concat-demuxer                             When merging with ffmpeg, use the concat demuxer instead of the concat protocol [default: False]
  --del-after-done                                        Delete temporary files when done [default: False]
  --no-date-info                                          Date information is not written during muxing [default: False]
  --no-log                                                Disable log file output [default: False]
  --log-file-only                                         Write logs only to file, not to terminal [default: False]
  --write-meta-json                                       Write meta json after parsed [default: True]
  --append-url-params                                     Add Params of input Url to segments, useful for some websites, such as kakao.com [default: True]
  -mt, --concurrent-download                              Concurrently download the selected audio, video and subtitles [default: False]
  -H, --header <header>                                   Pass custom header(s) to server, Example:
                                                          -H "Cookie: mycookie" -H "User-Agent: iOS"
  --sub-only                                              Select only subtitle tracks [default: False]
  --sub-format <SRT|VTT>                                  Subtitle output format [default: SRT]
  --auto-subtitle-fix                                     Automatically fix subtitles [default: True]
  --ffmpeg-binary-path <PATH>                             Full path to the ffmpeg binary, like C:\Tools\ffmpeg.exe
  --log-level <DEBUG|ERROR|INFO|OFF|WARN>                 Set log level [default: INFO]
  --ui-language <en-US|zh-CN|zh-TW>                       Set UI language
  --urlprocessor-args <urlprocessor-args>                 Give these arguments to the URL Processors.
  --key <key>                                             Set decryption key(s) to mp4decrypt/shaka-packager/ffmpeg. format:
                                                          --key KID1:KEY1 --key KID2:KEY2
                                                          or use --key KEY if all tracks share the same key.
  --key-text-file <key-text-file>                         Set the kid-key file, the program will search the KEY with KID from the file.(Very large file are not recommended)
  --decryption-engine <FFMPEG|MP4DECRYPT|SHAKA_PACKAGER>  Set the third-party program used for decryption [default: MP4DECRYPT]
  --decryption-binary-path <PATH>                         Full path to the tool used for MP4 decryption, like C:\Tools\mp4decrypt.exe
  --mp4-real-time-decryption                              Decrypt MP4 segments in real time [default: False]
  -R, --max-speed <SPEED>                                 Set speed limit, Mbps or Kbps, for example: 15M 100K.
  -M, --mux-after-done <OPTIONS>                          When all works is done, try to mux the downloaded streams. Use "--morehelp mux-after-done" for more details
  --custom-hls-method <METHOD>                            Set HLS encryption method (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)
  --custom-hls-key <FILE|HEX|BASE64>                      Set the HLS decryption key. Can be file, HEX or Base64
  --custom-hls-iv <FILE|HEX|BASE64>                       Set the HLS decryption iv. Can be file, HEX or Base64
  --use-system-proxy                                      Use system default proxy [default: True]
  --custom-proxy <URL>                                    Set web request proxy, like http://127.0.0.1:8888
  -4, --ipv4                                              Use IPv4 only for connections [default: False]
  -6, --ipv6                                              Use IPv6 only for connections [default: False]
  --http1.0                                               Use HTTP/1.0 [default: False]
  --http1.1                                               Use HTTP/1.1 [default: False]
  --http2                                                 Use HTTP/2 (ALPN on HTTPS) [default: True]
  --http2-prior-knowledge                                 Use HTTP/2 with prior knowledge; cleartext HTTP uses H2C [default: False]
  --custom-range <RANGE>                                  Download only part of the segments. Use "--morehelp custom-range" for more details
  --task-start-at <yyyyMMddHHmmss>                        Task execution will not start before this time
  --live-perform-as-vod                                   Download live streams as vod [default: False]
  --live-real-time-merge                                  Real-time merge into file when recording live [default: False]
  --live-keep-segments                                    Keep segments when recording a live (liveRealTimeMerge enabled) [default: True]
  --live-pipe-mux                                         Real-time muxing to TS file through pipeline + ffmpeg (liveRealTimeMerge enabled) [default: False]
  --live-fix-vtt-by-audio                                 Correct VTT sub by reading the start time of the audio file [default: False]
  --live-host-mirror <HOST>                               Extra mirror host(s) for live recording: each segment is fetched concurrently from the primary URL and mirrors; the first successful completion wins. Repeatable. Accepts hostname, host:port, or full http(s) URL.
  --live-record-limit <HH:mm:ss>                          Recording time limit when recording live
  --live-wait-time <SEC>                                  Manually set the live playlist refresh interval
  --live-take-count <NUM>                                 Manually set the number of segments downloaded for the first time when recording live [default: 16]
  --live-from-start                                       When recording live, best-effort backfill predictable segments toward the start; immediately download each generated full segment until a backfill download fails [default: False]
  --live-keep-m3u8-updated                                Keep raw.m3u8 updated while recording live, excluding filled gap content [default: False]
  --live-fill-segments-gap                                Auto-fill missing segments by predictable numeric naming pattern when the live playlist refreshes with gaps [default: True]
  --live-fill-segments-gap-max <NUM>                      Max missing segments to fill per gap; default max(1, min(60, ceil(60/wait_time))), where wait_time is the refresh interval in seconds (overridable via --live-wait-time)
  --live-restart-on-ext-map-change                        When EXT-X-MAP changes during live recording, finish the current output and restart recording with the new init segment; disable to stop recording instead [default: True]
  --mux-import <OPTIONS>                                  When MuxAfterDone enabled, allow to import local media files. Use "--morehelp mux-import" for more details
  -sv, --select-video <OPTIONS>                           Select video streams by regular expressions. Use "--morehelp select-video" for more details
  -sa, --select-audio <OPTIONS>                           Select audio streams by regular expressions. Use "--morehelp select-audio" for more details
  -ss, --select-subtitle <OPTIONS>                        Select subtitle streams by regular expressions. Use "--morehelp select-subtitle" for more details
  -dv, --drop-video <OPTIONS>                             Drop video streams by regular expressions.
  -da, --drop-audio <OPTIONS>                             Drop audio streams by regular expressions.
  -ds, --drop-subtitle <OPTIONS>                          Drop subtitle streams by regular expressions.
  --ad-keyword <REG>                                      Set URL keywords (regular expressions) for AD segments
  --disable-update-check                                  Disable version update check [default: False]
  --allow-hls-multi-ext-map                               Allow multiple #EXT-X-MAP in HLS (experimental) [default: False]
  --morehelp <OPTION>                                     Set more help info about one option
  -?, -h, --help                                          Show help and usage information
  --version                                               Show version information
```

<details>
<summary>Click to view "More Help" section</summary>

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

When MuxAfterDone enabled, allow to import local media files. OPTIONS is a colon separated list of:

* path=PATH: set file path
* lang=CODE: set media language code (not required)
* name=NAME: set description (not required)

Examples:
# import subtitle
--mux-import path=en-US.srt:lang=eng:name="English (Original)"
# import audio and subtitle
--mux-import path="D\:\media\atmos.m4a":lang=eng:name="English Description Audio" --mux-import path="D\:\media\eng.vtt":lang=eng:name="English (Description)"
```

```
More Help:

  --select-video

Select video streams by regular expressions. OPTIONS is a colon separated list of:

id=REGEX:lang=REGEX:name=REGEX:codecs=REGEX:res=REGEX:frame=REGEX
segsMin=number:segsMax=number:ch=REGEX:range=REGEX:url=REGEX
plistDurMin=hms:plistDurMax=hms:bwMin=int:bwMax=int:role=string:for=FOR

* for=FOR: Select type. best[number], worst[number], all (Default: best)

Examples:
# select best video
-sv best
# select 4K+HEVC video
-sv res="3840*":codecs=hvc1:for=best
# Select best video with duration longer than 1 hour 20 minutes 30 seconds
-sv plistDurMin="1h20m30s":for=best
-sv role="main":for=best
# Select video with bandwidth between 800Kbps and 1Mbps
-sv bwMin=800:bwMax=1000
```

```
More Help:

  --select-audio

Select audio streams by regular expressions. ref --select-video

Examples:
# select all
-sa all
# select best eng audio
-sa lang=en:for=best
# select best 2, and language is ja or en
-sa lang="ja|en":for=best2
-sa role="main":for=best
```

```
More Help:

  --select-subtitle

Select subtitle streams by regular expressions. ref --select-video

Examples:
# select all subs
-ss all
# select all subs containing "English"
-ss name="English":for=all
```

```
More Help:

  --custom-range

Download only part of the segments when downloading vod content.

Examples:
# Download [0,10], a total of 11 segments
--custom-range 0-10
# Download subsequent segments starting from index 10
--custom-range 10-
# Download the first 100 segments
--custom-range -99
# Download content from the 05:00 to 20:00
--custom-range 05:00-20:00
```

</details>

## Screenshots

### On-demand

![RE1](img/RE.gif)

Can also download in parallel and automatically mix streams

![RE2](img/RE2.gif)

### Live

Record TS live source:

[click to show gif](http://pan.iqiyi.com/file/paopao/W0LfmaMRvuA--uCdOpZ1cldM5JCVhMfIm7KFqr4oKCz80jLn0bBb-9PWmeCFZ-qHpAaQydQ1zk-CHYT_UbRLtw.gif)

Record MPD live source:

[click to show gif](http://pan.iqiyi.com/file/paopao/nmAV5MOh0yIyHhnxdgM_6th_p2nqrFsM4k-o3cUPwUa8Eh8QOU4uyPkLa_BlBrMa3GBnKWSk8rOaUwbsjKN14g.gif)

During recording, use ffmpeg to mix audio and video in real time

```bash
ffmpeg -readrate 1 -i 2022-09-21_19-54-42_V.mp4 -i 2022-09-21_19-54-42_V.chi.m4a -c copy 2022-09-21_19-54-42_V.ts
```

From v0.1.5, you can try to enable `live-pipe-mux` instead of the above command

> [!NOTE]
> If the network environment is not stable, do not enable `live-pipe-mux`. The data read in the pipeline is handled by ffmpeg, and it is easy to lose live data in some environments.

From v0.1.8, you can set the environment variable `RE_LIVE_PIPE_OPTIONS` to change some options of ffmpeg when `live-pipe-mux` is enabled: <https://github.com/nilaoda/N_m3u8DL-RE/issues/162#issuecomment-1592462532>

## Donate

Thanks to upstream author nilaoda for the original project.

<a href="https://www.buymeacoffee.com/nilaoda" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/default-orange.png" alt="Buy Me A Coffee" height="41" width="174"></a>
