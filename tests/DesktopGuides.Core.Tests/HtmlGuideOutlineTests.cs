using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlGuideOutlineTests
{
    private static readonly string Sha = new('a', 64);

    private static Outline OpenPage(HtmlGuidePages pages, int page) =>
        OutlineTree.Build(OutlineSource.Document,
        [
            new OutlineItem("Ford", 1, HtmlLocationRules.Capture(Sha, pages[page].RequestPath, new HtmlCapture(10, "Ford", null, 0.2), page, pages.Count), 10),
            new OutlineItem("Road", 1, HtmlLocationRules.Capture(Sha, pages[page].RequestPath, new HtmlCapture(40, "Road", null, 0.6), page, pages.Count), 40),
        ]);

    [Fact]
    public void EveryPageIsAnEntryWithTheOpenPagesOutlineBeneath()
    {
        HtmlGuidePages pages = HtmlGuidePagesTests.Pages();

        Outline outline = HtmlGuideOutline.Build(pages, 1, OpenPage(pages, 1), Sha);

        Assert.Equal(["Sample Quest", "Chapter 1", "Chapter 2", "Chapter 3"], outline.Entries.Select(entry => entry.Title));
        Assert.Equal(["Ford", "Road"], outline.Entries[1].Children.Select(entry => entry.Title));
        Assert.All(outline.Entries[1].Children, entry => Assert.Equal(2, entry.Level));
        Assert.Equal([pages.OrderOf(1, 10), pages.OrderOf(1, 40)], outline.Entries[1].Children.Select(entry => entry.Order));
        Assert.All(outline.Entries.Where((_, index) => index != 1), entry => Assert.Empty(entry.Children));
        for (int index = 0; index < pages.Count; index++)
        {
            OutlineEntry entry = outline.Entries[index];
            Assert.Equal(pages.OrderOf(index, 0), entry.Order);
            LocationDecodeResult decoded = HtmlLocationRules.Decode(entry.Location, Sha, pages.DocumentPaths);
            Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
            HtmlPosition position = Assert.IsType<HtmlPosition>(decoded.Location!.Payload);
            Assert.Equal((pages[index].RequestPath, 0), (position.DocumentPath, position.TextOffset));
        }
    }

    [Fact]
    public void AnOpenPageWithNoOutlineStillListsThePages()
    {
        Outline outline = HtmlGuideOutline.Build(HtmlGuidePagesTests.Pages(), 2, Outline.Empty, Sha);

        Assert.Equal(4, outline.Entries.Count);
        Assert.All(outline.Entries, entry => Assert.Empty(entry.Children));
    }

    // Review Focus 5.
    [Fact]
    public void AOnePageGuideKeepsItsOwnOutline()
    {
        HtmlGuidePages one = new(HtmlGuidePagesTests.Id, [new GuidePage(0, "guide.html", null, "Guide")]);
        Outline own = OpenPage(one, 0);

        Assert.Same(own, HtmlGuideOutline.Build(one, 0, own, Sha));
    }
}
