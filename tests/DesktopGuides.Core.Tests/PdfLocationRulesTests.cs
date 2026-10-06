using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfLocationRulesTests
{
    private const string Sha = "ab12";

    private static ReaderLocation At(int page, double fraction = 0, string sha = Sha) =>
        new(GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, sha, new PdfPosition(page, fraction), null);

    [Fact]
    public void CaptureRecordsThePagePointAndAnEstimate()
    {
        ReaderLocation location = PdfLocationRules.Capture("AB12", 4, 0.5, 200);

        Assert.Equal(GuideFormat.Pdf, location.Format);
        Assert.Equal(ReaderLocationCodec.CurrentVersion, location.SchemaVersion);
        Assert.Equal("ab12", location.ContentSha256);
        Assert.Equal(new PdfPosition(4, 0.5), location.Payload);
        Assert.Equal(4.5 / 200, location.EstimatedFraction);
    }

    [Fact]
    public void TheBottomOfTheLastPageEstimatesTheEnd() =>
        Assert.Equal(1.0, PdfLocationRules.Capture(Sha, 199, 1, 200).EstimatedFraction);

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CaptureRejectsABadFraction(double fraction) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfLocationRules.Capture(Sha, 3, fraction, 200));

    [Fact]
    public void CapturedLocationsSurviveTheCodec()
    {
        string sha = new('a', 64);
        ReaderLocation location = PdfLocationRules.Capture(sha, 199, 0.25, 200);

        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(location), GuideFormat.Pdf, sha);

        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Equal(new PdfPosition(199, 0.25), decoded.Location!.Payload);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(57, 0.4)]
    [InlineData(199, 1.0)]
    public void AnInRangePageIsExact(int page, double fraction)
    {
        PdfRestore restore = PdfLocationRules.Restore(At(page, fraction), Sha, 200);

        Assert.Equal(page, restore.PageIndex);
        Assert.Equal(fraction, restore.PageFraction);
        Assert.Equal(RestoreKind.Exact, restore.Outcome.Kind);
        Assert.Null(restore.Outcome.Reason);
    }

    [Fact]
    public void ShaComparisonIgnoresCase() =>
        Assert.Equal(RestoreKind.Exact, PdfLocationRules.Restore(At(3, sha: "AB12"), Sha, 200).Outcome.Kind);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(200, 199)]
    [InlineData(int.MaxValue, 199)]
    public void AnOutOfRangePageIsClampedToItsTop(int page, int expected)
    {
        PdfRestore restore = PdfLocationRules.Restore(At(page, 0.6), Sha, 200);

        Assert.Equal(expected, restore.PageIndex);
        Assert.Equal(0, restore.PageFraction);
        Assert.Equal(RestoreKind.Approximate, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.ClampedReason, restore.Outcome.Reason);
    }

    [Fact]
    public void ChangedBytesKeepThePointButAreApproximate()
    {
        PdfRestore restore = PdfLocationRules.Restore(At(57, 0.4, "ff00"), Sha, 200);

        Assert.Equal(57, restore.PageIndex);
        Assert.Equal(0.4, restore.PageFraction);
        Assert.Equal(RestoreKind.Approximate, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.ChangedReason, restore.Outcome.Reason);
    }

    [Fact]
    public void ChangedBytesWithAMissingPageGoToTheNearestPageTop()
    {
        PdfRestore restore = PdfLocationRules.Restore(At(250, 0.4, "ff00"), Sha, 200);

        Assert.Equal(199, restore.PageIndex);
        Assert.Equal(0, restore.PageFraction);
        Assert.Equal(RestoreKind.Approximate, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.ChangedReason, restore.Outcome.Reason);
    }

    [Fact]
    public void TheChangedReasonMatchesText() =>
        Assert.Equal(TextLocator.ApproximateReason, PdfLocationRules.ChangedReason);

    [Fact]
    public void ADecodedChangedLocationRestoresApproximately()
    {
        string saved = new('a', 64);
        string current = new('b', 64);
        string json = ReaderLocationCodec.Serialize(PdfLocationRules.Capture(saved, 10, 0.3, 200));

        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(json, GuideFormat.Pdf, current);
        PdfRestore restore = PdfLocationRules.Restore(decoded.Location!, current, 200);

        Assert.Equal(LocationDecodeStatus.ContentChanged, decoded.Status);
        Assert.Equal(10, restore.PageIndex);
        Assert.Equal(0.3, restore.PageFraction);
        Assert.Equal(PdfLocationRules.ChangedReason, restore.Outcome.Reason);
    }

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
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ABadPageFractionIsUnavailable(double fraction) =>
        AssertUnavailable(At(3, fraction));

    [Fact]
    public void NoPagesIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfLocationRules.Restore(At(0), Sha, 0));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    [InlineData(-1, false)]
    public void IsPageInRangeIsOneBased(int pageNumber, bool expected) =>
        Assert.Equal(expected, PdfLocationRules.IsPageInRange(pageNumber, 200));

    private static void AssertUnavailable(ReaderLocation location)
    {
        PdfRestore restore = PdfLocationRules.Restore(location, Sha, 200);

        Assert.Equal(0, restore.PageIndex);
        Assert.Equal(0, restore.PageFraction);
        Assert.Equal(RestoreKind.Unavailable, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.UnavailableReason, restore.Outcome.Reason);
    }
}
