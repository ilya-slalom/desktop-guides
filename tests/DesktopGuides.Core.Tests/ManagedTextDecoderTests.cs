using System.Security.Cryptography;
using System.Text;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ManagedTextDecoderTests
{
    private static Guide TxtGuide(
        byte[] recorded, int? codePage = null, GuideFormat format = GuideFormat.Txt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Guide", format, "content/guide", "guide.txt",
            Convert.ToHexStringLower(SHA256.HashData(recorded)), recorded.LongLength,
            null, codePage, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static TextGuideLoaded Loaded(byte[] bytes, int? codePage = null) =>
        Assert.IsType<TextGuideLoaded>(ManagedTextDecoder.Decode(bytes, TxtGuide(bytes, codePage)));

    private static TextGuideLoadError Failed(byte[] bytes, Guide guide) =>
        Assert.IsType<TextGuideLoadFailed>(ManagedTextDecoder.Decode(bytes, guide)).Error;

    [Fact]
    public void NormalizesMixedNewlinesAndKeepsTabsAndTrailingSpaces()
    {
        TextGuideLoaded loaded = Loaded(Encoding.UTF8.GetBytes("a  \r\n\tb\rc\n"));

        Assert.Equal("a  \n\tb\nc\n", loaded.Document.Text);
        Assert.False(loaded.ContentChanged);
    }

    [Fact]
    public void DecodesCp437BoxDrawing()
    {
        TextGuideLoaded loaded = Loaded([0xC9, 0xCD, 0xBB, 0x0A, 0xBA, 0x20, 0xBA, 0x0A, 0xC8, 0xCD, 0xBC], 437);

        Assert.Equal("╔═╗\n║ ║\n╚═╝", loaded.Document.Text);
        Assert.Equal("ibm437", loaded.Document.EncodingName);
    }

    [Fact]
    public void DecodesWindows1252AndPinsItsUndefinedByte()
    {
        TextGuideLoaded loaded = Loaded([0x80, 0x20, 0x81], 1252);

        // 0x81 is undefined in Windows-1252; this pins what the provider returns.
        Assert.Equal("€ \u0081", loaded.Document.Text);
        Assert.Equal("windows-1252", loaded.Document.EncodingName);
    }

    [Fact]
    public void BomWinsOverAStoredCodePage()
    {
        TextGuideLoaded loaded = Loaded([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é")], 437);

        Assert.Equal("é", loaded.Document.Text);
        Assert.Equal("utf-8", loaded.Document.EncodingName);
    }

    public static TheoryData<byte[], int?> NotUtf8Bytes => new()
    {
        { new byte[] { 0x48, 0x82, 0x0A }, null },
        { new byte[] { 0x41, 0xC3 }, null },
        { new byte[] { 0xEF, 0xBB, 0xBF, 0x48, 0x82 }, null },
        { new byte[] { 0xEF, 0xBB, 0xBF, 0x48, 0x82 }, 437 },
    };

    [Theory]
    [MemberData(nameof(NotUtf8Bytes))]
    public void InvalidOrTruncatedUtf8IsNotUtf8(byte[] bytes, int? codePage)
    {
        Assert.Equal(TextGuideLoadError.NotUtf8, Failed(bytes, TxtGuide(bytes, codePage)));
    }

    [Fact]
    public void MatchingBytesAreUnchangedEvenWithAnUppercaseFingerprint()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");
        Guide guide = TxtGuide(bytes) with { ContentSha256 = Convert.ToHexString(SHA256.HashData(bytes)) };

        Assert.False(Assert.IsType<TextGuideLoaded>(ManagedTextDecoder.Decode(bytes, guide)).ContentChanged);
    }

    [Fact]
    public void DifferentBytesOpenAsChanged()
    {
        Guide guide = TxtGuide(Encoding.UTF8.GetBytes("abc"));

        TextGuideLoaded loaded = Assert.IsType<TextGuideLoaded>(
            ManagedTextDecoder.Decode(Encoding.UTF8.GetBytes("abd"), guide));

        Assert.Equal("abd", loaded.Document.Text);
        Assert.True(loaded.ContentChanged);
    }

    [Fact]
    public void ShortenedCopyOpensAsChanged()
    {
        Guide guide = TxtGuide(Encoding.UTF8.GetBytes("abc\n"));

        Assert.True(Assert.IsType<TextGuideLoaded>(
            ManagedTextDecoder.Decode(Encoding.UTF8.GetBytes("ab"), guide)).ContentChanged);
    }

    [Fact]
    public void LengthAloneMarksTheCopyChanged()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");
        Guide guide = TxtGuide(bytes) with { ContentBytes = 4 };

        Assert.True(Assert.IsType<TextGuideLoaded>(ManagedTextDecoder.Decode(bytes, guide)).ContentChanged);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF })]
    public void EmptyCopyOpensAsChanged(byte[] bytes)
    {
        Guide guide = TxtGuide(Encoding.UTF8.GetBytes("abc"));

        TextGuideLoaded loaded = Assert.IsType<TextGuideLoaded>(ManagedTextDecoder.Decode(bytes, guide));

        Assert.Equal("", loaded.Document.Text);
        Assert.True(loaded.ContentChanged);
    }

    [Fact]
    public void KeepsDosEndOfFileByte()
    {
        TextGuideLoaded loaded = Loaded([0x45, 0x6E, 0x64, 0x0D, 0x0A, 0x1A], 437);

        Assert.Equal("End\n\u001A", loaded.Document.Text);
    }

    [Fact]
    public void NormalizesNewlinesInLegacyText()
    {
        TextGuideLoaded loaded = Loaded([0x41, 0x82, 0x0D, 0x0A, 0x42, 0x0D, 0x43], 437);

        Assert.Equal("Aé\nB\nC", loaded.Document.Text);
        Assert.Equal([0, 3, 5], loaded.Document.LineStarts);
    }

    [Fact]
    public void NonTextGuideIsInvalidMetadata()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");

        Assert.Equal(TextGuideLoadError.InvalidMetadata, Failed(bytes, TxtGuide(bytes, format: GuideFormat.Html)));
    }

    [Theory]
    [InlineData(65001)]
    [InlineData(0)]
    public void UnsupportedStoredCodePageIsInvalidMetadata(int codePage)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");

        Assert.Equal(TextGuideLoadError.InvalidMetadata, Failed(bytes, TxtGuide(bytes, codePage)));
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ManagedTextDecoder.Decode(null!, TxtGuide([])));
        Assert.Throws<ArgumentNullException>(() => ManagedTextDecoder.Decode([], null!));
    }
}
