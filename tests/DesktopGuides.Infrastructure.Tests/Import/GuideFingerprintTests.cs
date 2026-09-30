using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class GuideFingerprintTests
{
    private static readonly (string, string)[] Files =
    [
        ("guide.html", new string('a', 64)),
        ("styles/site.css", new string('b', 64)),
    ];

    [Fact]
    public void HtmlFingerprintHashesSortedPathAndHashLines() =>
        Assert.Equal(
            "4124251947a2ecaab5072f8a3d6c7e914af2b8ab1bf2920001d050d131f3759a",
            GuideFingerprint.OfHtml(Files));

    [Fact]
    public void HtmlFingerprintIgnoresInputOrder() =>
        Assert.Equal(GuideFingerprint.OfHtml(Files), GuideFingerprint.OfHtml(Files.Reverse()));
}
