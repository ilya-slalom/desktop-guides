using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class OutlineTreeTests
{
    private static ReaderLocation At(int offset) =>
        new(GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, "aa", new TextPosition(offset, "x"), 0);

    private static OutlineItem Item(string title, int level, int order) => new(title, level, At(order), order);

    [Fact]
    public void LowerLevelsNestUnderTheEntryBefore()
    {
        Outline outline = OutlineTree.Build(OutlineSource.Detected,
            [Item("One", 1, 0), Item("One A", 2, 10), Item("One B", 2, 20), Item("Two", 1, 30)]);

        Assert.Equal(OutlineSource.Detected, outline.Source);
        Assert.Equal(["One", "Two"], outline.Entries.Select(e => e.Title));
        Assert.Equal(["One A", "One B"], outline.Entries[0].Children.Select(e => e.Title));
        Assert.Equal(2, outline.Entries[0].Children[0].Level);
        Assert.Empty(outline.Entries[1].Children);
    }

    [Fact]
    public void AJumpInLevelsNestsOneDown()
    {
        Outline outline = OutlineTree.Build(OutlineSource.Document,
            [Item("Top", 1, 0), Item("Deep", 4, 10), Item("Middle", 2, 20)]);

        OutlineEntry top = Assert.Single(outline.Entries);
        Assert.Equal(["Deep", "Middle"], top.Children.Select(e => e.Title));
        Assert.All(top.Children, child => Assert.Equal(2, child.Level));
    }

    [Fact]
    public void AnOutlineThatStartsDeepStartsAtTheTop()
    {
        Outline outline = OutlineTree.Build(OutlineSource.Document, [Item("Deep", 3, 0), Item("Deeper", 4, 10)]);

        Assert.Equal(1, Assert.Single(outline.Entries).Level);
        Assert.Equal(2, Assert.Single(outline.Entries[0].Children).Level);
    }

    [Fact]
    public void LongTitlesAreTrimmedWithAnEllipsis()
    {
        Outline outline = OutlineTree.Build(OutlineSource.Detected, [Item(new string('a', 200), 1, 0)]);

        string title = Assert.Single(outline.Entries).Title;
        Assert.Equal(OutlineTree.MaxTitleLength, title.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void NoItemsIsTheEmptyOutline()
    {
        Outline outline = OutlineTree.Build(OutlineSource.Detected, []);

        Assert.Same(Outline.Empty, outline);
        Assert.Equal(OutlineSource.None, outline.Source);
    }

    [Fact]
    public void EntryAtIsTheLastEntryAtOrBeforeThePlace()
    {
        Outline outline = OutlineTree.Build(OutlineSource.Detected,
            [Item("One", 1, 100), Item("One A", 2, 200), Item("Two", 1, 300)]);

        Assert.Null(OutlineTree.EntryAt(outline, 50));
        Assert.Equal("One", OutlineTree.EntryAt(outline, 100)?.Title);
        Assert.Equal("One A", OutlineTree.EntryAt(outline, 250)?.Title);
        Assert.Equal("Two", OutlineTree.EntryAt(outline, 10_000)?.Title);
    }

    [Fact]
    public void FlattenIsReadingOrder() =>
        Assert.Equal(["One", "One A", "Two"], OutlineTree.Flatten(OutlineTree.Build(OutlineSource.Detected,
            [Item("One", 1, 100), Item("One A", 2, 200), Item("Two", 1, 300)]).Entries).Select(e => e.Title));
}
