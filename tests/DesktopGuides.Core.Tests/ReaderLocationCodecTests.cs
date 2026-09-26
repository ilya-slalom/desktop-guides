using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ReaderLocationCodecTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly string ChangedHash = new('b', 64);

    [Theory]
    [MemberData(nameof(Locations))]
    public void RoundTripsEachFormat(ReaderLocation original, string? htmlDocument)
    {
        string json = ReaderLocationCodec.Serialize(original);

        LocationDecodeResult result = ReaderLocationCodec.Deserialize(
            json, original.Format, Hash, htmlDocument);

        Assert.Equal(LocationDecodeStatus.Valid, result.Status);
        Assert.Equal(original, result.Location);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= 4096);
    }

    [Fact]
    public void ChangedContentRetainsContextAndApproximateFallback()
    {
        ReaderLocation original = new(GuideFormat.Txt, 1, Hash,
            new TextPosition(504, "tower ladder"), 0.42);

        LocationDecodeResult result = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(original), GuideFormat.Txt, ChangedHash);

        Assert.Equal(LocationDecodeStatus.ContentChanged, result.Status);
        Assert.Equal(original, result.Location);
        Assert.Equal([RestoreKind.Context, RestoreKind.Approximate],
            ReaderLocationCodec.RestoreCandidates(result));
    }

    [Fact]
    public void ChangedPdfKeepsPageAsApproximateFallbackWithoutEstimate()
    {
        ReaderLocation original = new(GuideFormat.Pdf, 1, Hash,
            new PdfPosition(4, 0.25), null);

        LocationDecodeResult result = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(original), GuideFormat.Pdf, ChangedHash);

        Assert.Equal(LocationDecodeStatus.ContentChanged, result.Status);
        Assert.Equal([RestoreKind.Approximate],
            ReaderLocationCodec.RestoreCandidates(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnknownLocatorVersionsAreUnavailable(int version)
    {
        ReaderLocation original = new(GuideFormat.Pdf, 1, Hash,
            new PdfPosition(4, 0.25), 0.5);
        string json = ReaderLocationCodec.Serialize(original)
            .Replace("\"schemaVersion\":1", $"\"schemaVersion\":{version}");

        LocationDecodeResult result = ReaderLocationCodec.Deserialize(
            json, GuideFormat.Pdf, Hash);

        Assert.Equal(LocationDecodeStatus.UnsupportedVersion, result.Status);
        Assert.Null(result.Location);
        Assert.Empty(ReaderLocationCodec.RestoreCandidates(result));
    }

    [Fact]
    public void RejectsWrongFormatAndAnotherGuideDocument()
    {
        ReaderLocation html = new(GuideFormat.Html, 1, Hash,
            new HtmlPosition("guide.html", "boss", "Final boss", 4, 0.7), 0.7);
        string json = ReaderLocationCodec.Serialize(html);

        Assert.Equal(LocationDecodeStatus.Invalid,
            ReaderLocationCodec.Deserialize(json, GuideFormat.Txt, Hash).Status);
        Assert.Equal(LocationDecodeStatus.Invalid,
            ReaderLocationCodec.Deserialize(json, GuideFormat.Html, Hash, "other.html").Status);
    }

    [Fact]
    public void RejectsNumericFormatNamesAndDuplicateFields()
    {
        ReaderLocation pdf = new(GuideFormat.Pdf, 1, Hash,
            new PdfPosition(0, 0.2), 0.2);
        string json = ReaderLocationCodec.Serialize(pdf);

        Assert.Equal(LocationDecodeStatus.Invalid,
            ReaderLocationCodec.Deserialize(
                json.Replace("\"Pdf\"", "\"2\""), GuideFormat.Pdf, Hash).Status);
        Assert.Equal(LocationDecodeStatus.Invalid,
            ReaderLocationCodec.Deserialize(
                json.Replace("\"schemaVersion\":1",
                    "\"schemaVersion\":1,\"schemaVersion\":1"),
                GuideFormat.Pdf, Hash).Status);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"Pdf\",\"schemaVersion\":1,\"contentSha256\":\"bad\"}")]
    public void RejectsMalformedOrIncompleteJson(string json)
    {
        Assert.Equal(LocationDecodeStatus.Invalid,
            ReaderLocationCodec.Deserialize(json, GuideFormat.Pdf, Hash).Status);
    }

    [Fact]
    public void RejectsInvalidPageAndOversizedQuote()
    {
        Assert.Throws<InvalidDataException>(() => ReaderLocationCodec.Serialize(
            new ReaderLocation(GuideFormat.Pdf, 1, Hash,
                new PdfPosition(-1, 2), null)));
        Assert.Throws<InvalidDataException>(() => ReaderLocationCodec.Serialize(
            new ReaderLocation(GuideFormat.Txt, 1, Hash,
                new TextPosition(1, new string('x', 5000)), 0.2)));
    }

    public static TheoryData<ReaderLocation, string?> Locations => new()
    {
        { new(GuideFormat.Txt, 1, Hash, new TextPosition(204, "checkpoint"), 0.25), null },
        { new(GuideFormat.Html, 1, Hash,
            new HtmlPosition("guide.html", "chapter-2", "bridge", 12, 0.6), 0.6), "guide.html" },
        { new(GuideFormat.Pdf, 1, Hash, new PdfPosition(8, 0.37), 0.37), null }
    };
}
