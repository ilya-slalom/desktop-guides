using System.Globalization;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ImportPresentationTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void PdfPasswordCopy()
    {
        Assert.Equal("That password didn't open this PDF. Try again.", ImportPresentation.PdfPasswordIncorrect);
        Assert.Equal("Password protected", ImportPresentation.PasswordProtectedFact);
    }

    [Theory]
    [InlineData(GuideFormat.Txt, "Text (TXT)")]
    [InlineData(GuideFormat.Html, "Web page (HTML)")]
    [InlineData(GuideFormat.Pdf, "PDF")]
    public void FormatLabels(GuideFormat format, string expected) =>
        Assert.Equal(expected, ImportPresentation.FormatLabel(format));

    [Theory]
    [InlineData(null, "UTF-8")]
    [InlineData(437, "DOS (CP437)")]
    [InlineData(1252, "Western (Windows-1252)")]
    public void EncodingLabels(int? codePage, string expected) =>
        Assert.Equal(expected, ImportPresentation.EncodingLabel(codePage));

    [Fact]
    public void EncodingLabelRejectsOtherCodePages() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ImportPresentation.EncodingLabel(65001));

    [Theory]
    [InlineData(1L, "1 byte")]
    [InlineData(20L, "20 bytes")]
    [InlineData(1023L, "1023 bytes")]
    [InlineData(1024L, "1 KB")]
    [InlineData(2123L, "2.1 KB")]
    [InlineData(1048524L, "1023.9 KB")]
    [InlineData(1048575L, "1 MB")]
    [InlineData(10485760L, "10 MB")]
    [InlineData(1073741824L, "1 GB")]
    public void FormatsSizes(long bytes, string expected) =>
        Assert.Equal(expected, ImportPresentation.FormatSize(bytes, Invariant));

    [Fact]
    public void LimitWarningsKeepsTheFirstTwentyAndCountsTheRest()
    {
        ImportWarning[] warnings = Enumerable.Range(0, 250)
            .Select(i => new ImportWarning($"https://ads.example/{i}.js", "A remote asset will be blocked in the offline reader."))
            .ToArray();

        (IReadOnlyList<ImportWarning> shown, int hidden) = ImportPresentation.LimitWarnings(warnings);

        Assert.Equal(20, shown.Count);
        Assert.Equal(warnings[0], shown[0]);
        Assert.Equal(230, hidden);
        Assert.Equal("230 more warnings", ImportPresentation.MoreWarnings(hidden));
        Assert.Equal("1 more warning", ImportPresentation.MoreWarnings(1));
    }

    [Fact]
    public void LimitWarningsHidesNothingAtTheCap()
    {
        ImportWarning[] warnings = Enumerable.Range(0, 20)
            .Select(i => new ImportWarning($"{i}.png", "A local asset is missing and will not appear offline."))
            .ToArray();

        Assert.Equal(0, ImportPresentation.LimitWarnings(warnings).Hidden);
    }

    [Fact]
    public void ShortenTargetKeepsBothEnds()
    {
        string target = "data:image/png;base64," + new string('Q', 5000) + "END";

        string shortened = ImportPresentation.ShortenTarget(target);

        Assert.Equal(ImportPresentation.MaxTargetLength, shortened.Length);
        Assert.StartsWith("data:image/png;base64,", shortened);
        Assert.EndsWith("END", shortened);
        Assert.Contains('…', shortened);
        Assert.Equal("images/map.png", ImportPresentation.ShortenTarget("images/map.png"));
    }

    [Fact]
    public void WarningLineJoinsTargetAndReason() =>
        Assert.Equal("images/x.png: A local asset is missing and will not appear offline.",
            ImportPresentation.WarningLine(new ImportWarning(
                "images/x.png", "A local asset is missing and will not appear offline.")));
}
