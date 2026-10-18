using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlGuidePagesTests
{
    internal static readonly Guid Id = Guid.Parse("3f2a9c0e-4b7d-1a65-f08c-2e9d3b4a7c10");

    internal static HtmlGuidePages Pages() => new(Id,
    [
        new GuidePage(0, "Sample Quest - Guides Example.html", "https://guides.example/wiki/Sample_Quest", "Sample Quest"),
        new GuidePage(1, "Chapter 1 - Guides Example.html", "https://guides.example/wiki/Sample_Quest/Chapter_1", "Chapter 1"),
        new GuidePage(2, "Chapter 2 - Guides Example.html", "https://guides.example/wiki/Sample_Quest%3AChapter_2", "Chapter 2"),
        new GuidePage(3, "Chapter 3 - Guides Example.html", null, "Chapter 3"),
    ]);

    [Fact]
    public void APageIsFoundByItsGuideAddress() =>
        Assert.Equal(3, Pages().IndexOf(new Uri(Pages().AddressOf(3).AbsoluteUri + "#end")));

    [Fact]
    public void APageIsFoundByItsWebsiteAddress()
    {
        HtmlGuidePages pages = Pages();

        Assert.Equal(1, pages.IndexOf(new Uri("https://Guides.Example/wiki/Sample_Quest/Chapter_1#top")));
        Assert.Equal(2, pages.IndexOf(new Uri("https://guides.example/wiki/Sample_Quest:Chapter_2")));
        Assert.Null(pages.IndexOf(new Uri("https://guides.example/wiki/Sample_Quest/Chapter_4")));
    }

    [Fact]
    public void DocumentsAreFoundByPath()
    {
        Assert.Equal(2, Pages().IndexOfDocument("Chapter 2 - Guides Example.html"));
        Assert.Null(Pages().IndexOfDocument("guide.html"));
    }

    // Review Focus 3.
    [Fact]
    public void StepsStopAtTheEnds()
    {
        Assert.Equal(0, Pages().Step(0, -1));
        Assert.Equal(3, Pages().Step(3, 1));
        Assert.Equal(2, Pages().Step(1, 1));
    }

    [Fact]
    public void AGuideNeedsItsEntryFirst()
    {
        Assert.Throws<ArgumentException>(() => new HtmlGuidePages(Id, []));
        Assert.Throws<ArgumentException>(() => new HtmlGuidePages(Id, [new GuidePage(1, "a.html", null, null)]));
    }
}
