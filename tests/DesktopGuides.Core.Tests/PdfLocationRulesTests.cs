using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfLocationRulesTests
{
    private const string Sha = "ab12";

    private static ReaderLocation At(int page, string sha = Sha) =>
        new(GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, sha, new PdfPosition(page, 0), null);

    [Fact]
    public void CaptureRecordsThePageAndAnEstimate()
    {
        ReaderLocation location = PdfLocationRules.Capture("AB12", 4, 200);

        Assert.Equal(GuideFormat.Pdf, location.Format);
        Assert.Equal(ReaderLocationCodec.CurrentVersion, location.SchemaVersion);
        Assert.Equal("ab12", location.ContentSha256);
        Assert.Equal(new PdfPosition(4, 0), location.Payload);
        Assert.Equal(5.0 / 200, location.EstimatedFraction);
    }

    [Fact]
    public void CapturedLocationsSurviveTheCodec()
    {
        string sha = new('a', 64);
        ReaderLocation location = PdfLocationRules.Capture(sha, 199, 200);

        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(location), GuideFormat.Pdf, sha);

        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Equal(new PdfPosition(199, 0), decoded.Location!.Payload);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(57)]
    [InlineData(199)]
    public void AnInRangePageIsExact(int page)
    {
        PdfRestore restore = PdfLocationRules.Restore(At(page), Sha, 200);

        Assert.Equal(page, restore.PageIndex);
        Assert.Equal(RestoreKind.Exact, restore.Outcome.Kind);
    }

    [Fact]
    public void ShaComparisonIgnoresCase() =>
        Assert.Equal(RestoreKind.Exact, PdfLocationRules.Restore(At(3, "AB12"), Sha, 200).Outcome.Kind);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(200, 199)]
    [InlineData(int.MaxValue, 199)]
    public void AnOutOfRangePageIsClampedAndApproximate(int page, int expected)
    {
        PdfRestore restore = PdfLocationRules.Restore(At(page), Sha, 200);

        Assert.Equal(expected, restore.PageIndex);
        Assert.Equal(RestoreKind.Approximate, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.ClampedReason, restore.Outcome.Reason);
    }

    [Fact]
    public void AnotherGuideVersionIsUnavailable() =>
        AssertUnavailable(At(3, "ff00"));

    [Fact]
    public void AnotherFormatIsUnavailable() =>
        AssertUnavailable(new ReaderLocation(GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, Sha,
            new TextPosition(0, "x"), null));

    [Fact]
    public void APdfLocationWithATextPayloadIsUnavailable() =>
        AssertUnavailable(new ReaderLocation(GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, Sha,
            new TextPosition(0, "x"), null));

    [Fact]
    public void AnotherSchemaVersionIsUnavailable() =>
        AssertUnavailable(At(3) with { SchemaVersion = ReaderLocationCodec.CurrentVersion + 1 });

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonFinitePageFractionIsUnavailable(double fraction) =>
        AssertUnavailable(At(3) with { Payload = new PdfPosition(3, fraction) });

    [Fact]
    public void NoPagesIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfLocationRules.Restore(At(0), Sha, 0));

    private static void AssertUnavailable(ReaderLocation location)
    {
        PdfRestore restore = PdfLocationRules.Restore(location, Sha, 200);

        Assert.Equal(RestoreKind.Unavailable, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.UnavailableReason, restore.Outcome.Reason);
    }
}
