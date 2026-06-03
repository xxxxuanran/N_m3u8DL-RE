using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class HLSExtractorTests
{
    private static ParserConfig CreateTestConfig() => new()
    {
        Url = "http://example.com/live/index.m3u8",
        OriginalUrl = "http://example.com/live/index.m3u8",
    };

    [Fact]
    public async Task ExtractStreamsAsync_Aes128KeyWithoutIV_MarksDefaultDerivedIVAsImplicit()
    {
        const string m3u8 = """
            #EXTM3U
            #EXT-X-TARGETDURATION:4
            #EXT-X-MEDIA-SEQUENCE:7
            #EXT-X-KEY:METHOD=AES-128
            #EXTINF:4,
            segment7.ts
            #EXT-X-KEY:METHOD=AES-128,IV=0x00000000000000000000000000000008
            #EXTINF:4,
            segment8.ts
            """;

        var extractor = new HLSExtractor(CreateTestConfig());
        var streams = await extractor.ExtractStreamsAsync(m3u8);
        var segments = streams.Single().Playlist!.MediaParts.Single().MediaSegments;

        segments[0].EncryptInfo.Method.ShouldBe(EncryptMethod.AES_128);
        segments[0].EncryptInfo.HasExplicitIV.ShouldBeFalse();
        HexUtil.BytesToHex(segments[0].EncryptInfo.IV!).ShouldBe("00000000000000000000000000000007");

        segments[1].EncryptInfo.Method.ShouldBe(EncryptMethod.AES_128);
        segments[1].EncryptInfo.HasExplicitIV.ShouldBeTrue();
        HexUtil.BytesToHex(segments[1].EncryptInfo.IV!).ShouldBe("00000000000000000000000000000008");
    }

    [Fact]
    public async Task RefreshPlayListAsync_WhenLaterStreamFails_DoesNotCommitEarlierStream()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"n_m3u8dl_re_hls_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var firstPlaylistPath = Path.Combine(tempDir, "first.m3u8");
            await File.WriteAllTextAsync(firstPlaylistPath, """
                #EXTM3U
                #EXT-X-TARGETDURATION:4
                #EXT-X-MEDIA-SEQUENCE:100
                #EXTINF:4,
                segment100.ts
                """);

            var first = CreateStreamSpec(new Uri(firstPlaylistPath).AbsoluteUri, 10, "old-first.ts");
            var second = CreateStreamSpec(new Uri(Path.Combine(tempDir, "missing.m3u8")).AbsoluteUri, 20, "old-second.ts");
            var extractor = new HLSExtractor(new ParserConfig
            {
                Url = first.Url,
                OriginalUrl = first.Url,
            });

            await Should.ThrowAsync<FileNotFoundException>(() => extractor.RefreshPlayListAsync([first, second]));

            var firstSegment = first.Playlist!.MediaParts.Single().MediaSegments.Single();
            firstSegment.Index.ShouldBe(10);
            firstSegment.Url.ShouldBe("old-first.ts");

            var secondSegment = second.Playlist!.MediaParts.Single().MediaSegments.Single();
            secondSegment.Index.ShouldBe(20);
            secondSegment.Url.ShouldBe("old-second.ts");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static StreamSpec CreateStreamSpec(string url, long segmentIndex, string segmentUrl)
    {
        return new StreamSpec
        {
            Url = url,
            Extension = "ts",
            Playlist = new Playlist
            {
                MediaParts =
                [
                    new MediaPart
                    {
                        MediaSegments =
                        [
                            new MediaSegment
                            {
                                Index = segmentIndex,
                                Duration = 4,
                                Url = segmentUrl,
                            },
                        ],
                    },
                ],
            },
        };
    }
}
