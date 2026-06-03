using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Util;
using Shouldly;
using static N_m3u8DL_RE.Common.Util.LiveSegmentUrlUtil;

namespace N_m3u8DL_RE.Tests.Common.Util;

public class LiveSegmentUrlUtilTests
{
    [Theory]
    [InlineData("123", 123L, true)]
    [InlineData("000123", 123L, true)]
    [InlineData("", 0L, false)]
    [InlineData("12a", 0L, false)]
    public void TryParseSegmentNumber_OnlyAcceptsUnsignedDecimalDigits(string value, long expected, bool expectedResult)
    {
        var result = TryParseSegmentNumber(value, out var number);

        result.ShouldBe(expectedResult);
        number.ShouldBe(expected);
    }

    [Fact]
    public void ParseAndReplaceSegmentUrl_PreservesExtensionAndQuery()
    {
        var parts = ParseSegmentUrl("https://example.test/live/000123.m4s?token=abc");

        parts.Path.ShouldBe("https://example.test/live/000123.m4s");
        parts.Query.ShouldBe("?token=abc");
        parts.FileNameWithoutExtension.ShouldBe("000123");
        parts.Extension.ShouldBe(".m4s");
        ReplaceUrlFileName(parts, "000124").ShouldBe("https://example.test/live/000124.m4s?token=abc");
    }

    [Fact]
    public void CreateFilledSegment_UsesTemplateMetadataAndClonesIv()
    {
        var template = new MediaSegment
        {
            Index = 124,
            Duration = 2,
            Title = "video",
            DateTime = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc),
            StartRange = 100,
            ExpectLength = 200,
            Url = "https://example.test/live/00124.m4s?token=abc",
            NameFromVar = "from-var",
            EncryptInfo = new EncryptInfo
            {
                Method = EncryptMethod.AES_128,
                Key = [1, 2, 3],
                IV = [4, 5, 6],
                HasExplicitIV = true,
            },
        };
        var parts = ParseSegmentUrl(template.Url);

        var filled = CreateFilledSegment(template, parts, index: 120, templateNumber: 124);

        filled.ShouldNotBeNull();
        filled.Index.ShouldBe(120);
        filled.Duration.ShouldBe(2);
        filled.Title.ShouldBe("video");
        filled.DateTime.ShouldBe(template.DateTime.Value.AddSeconds(-8));
        filled.StartRange.ShouldBe(100);
        filled.ExpectLength.ShouldBe(200);
        filled.Url.ShouldBe("https://example.test/live/00120.m4s?token=abc");
        filled.NameFromVar.ShouldBeNull();
        filled.EncryptInfo.Method.ShouldBe(EncryptMethod.AES_128);
        filled.EncryptInfo.Key.ShouldBe(template.EncryptInfo.Key);
        filled.EncryptInfo.IV.ShouldBe([4, 5, 6]);
        ReferenceEquals(filled.EncryptInfo.IV, template.EncryptInfo.IV).ShouldBeFalse();
        filled.EncryptInfo.HasExplicitIV.ShouldBeTrue();
    }

    [Fact]
    public void CheckSegmentUrlPattern_RequiresSameQueryMatchingIndexAndStrictIncrease()
    {
        var segments = new[]
        {
            new MediaSegment { Index = 10, Url = "https://example.test/live/10.ts?k=v" },
            new MediaSegment { Index = 11, Url = "https://example.test/live/11.ts?k=v" },
        };

        var pattern = CheckSegmentUrlPattern(segments);

        pattern.SameQuery.ShouldBeTrue();
        pattern.NumericFileNameMatchesIndex.ShouldBeTrue();
        pattern.StrictlyIncreasing.ShouldBeTrue();
    }
}
