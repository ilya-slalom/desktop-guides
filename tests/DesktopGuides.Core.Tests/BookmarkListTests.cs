using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class BookmarkListTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Then = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

    private static ReaderLocation Text(int offset, string context = "Text", double? fraction = null, string sha = Sha) =>
        new(GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, sha, new TextPosition(offset, context), fraction);

    private static Bookmark Stored(string label, string locator, int minutes = 0) =>
        new(Guid.NewGuid(), Guid.NewGuid(), locator, label, Then.AddMinutes(minutes), Then.AddMinutes(minutes));

    private static Bookmark At(string label, ReaderLocation place, int minutes = 0) =>
        Stored(label, ReaderLocationCodec.Serialize(place), minutes);

    private static IReadOnlyList<BookmarkItem> Arrange(params Bookmark[] bookmarks) =>
        BookmarkList.Arrange(bookmarks,
            json => ReaderLocationCodec.Deserialize(json, GuideFormat.Txt, Sha),
            place => ((TextPosition)place.Payload).CharacterOffset);

    [Fact]
    public void BookmarksListInReadingOrderTheOlderFirstAndDamagedLast()
    {
        Bookmark damaged = Stored("Broken", "{not json");
        Bookmark late = At("Late", Text(900));
        Bookmark newer = At("Newer", Text(100), minutes: 5);
        Bookmark older = At("Older", Text(100), minutes: 1);

        IReadOnlyList<BookmarkItem> items = Arrange(damaged, late, newer, older);

        Assert.Equal(["Older", "Newer", "Late", "Broken"], items.Select(i => i.Bookmark.Label));
        Assert.True(items[3].IsDamaged);
        Assert.Null(items[3].Location);
    }

    [Fact]
    public void AtFindsTheBookmarkAtThatPlace()
    {
        IReadOnlyList<BookmarkItem> items = Arrange(At("Here", Text(100)), At("There", Text(200)));

        Assert.Equal("There", BookmarkList.At(items, 200)?.Bookmark.Label);
        Assert.Null(BookmarkList.At(items, 150));
    }

    // Review Focus 4: a bookmark for older content is listed, and followed,
    // but it isn't this place.
    [Fact]
    public void AStaleBookmarkIsNotThisPlace()
    {
        IReadOnlyList<BookmarkItem> items = Arrange(At("Old", Text(100, sha: new string('b', 64))));

        BookmarkItem stale = Assert.Single(items);
        Assert.False(stale.IsDamaged);
        Assert.True(stale.Stale);
        Assert.Null(BookmarkList.At(items, 100));
    }

    [Fact]
    public void TheContentsEntryGivesTheLabel()
    {
        Outline outline = new(OutlineSource.Detected,
        [
            new OutlineEntry("Introduction", 1, Text(0), 0, []),
            new OutlineEntry("Walkthrough: Castle", 1, Text(500), 500, [])
        ]);

        Assert.Equal("Walkthrough: Castle", BookmarkList.DefaultLabel(Text(640, "Gate"), outline, 640));
    }

    // Review Focus 2: before the outline is built the shell passes Outline.Empty.
    [Fact]
    public void WithNoContentsTheTextGivesTheLabel() =>
        Assert.Equal("Cross the bridge to the keep.",
            BookmarkList.DefaultLabel(Text(640, "  Cross the bridge\nto the keep.  "), Outline.Empty, 640));

    [Fact]
    public void APlaceBeforeEveryEntryUsesItsOwnWords()
    {
        Outline outline = new(OutlineSource.Detected, [new OutlineEntry("Bosses", 1, Text(500), 500, [])]);

        Assert.Equal("Opening words", BookmarkList.DefaultLabel(Text(10, "Opening words"), outline, 10));
    }

    [Fact]
    public void HtmlUsesItsQuoteAndPdfItsPage()
    {
        ReaderLocation html = new(GuideFormat.Html, ReaderLocationCodec.CurrentVersion, Sha,
            new HtmlPosition("guide.html", null, "The castle gate opens at dawn", 40, 0.2), 0.2);
        ReaderLocation pdf = new(GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, Sha,
            new PdfPosition(41, 0), 0.4);

        Assert.Equal("The castle gate opens at dawn", BookmarkList.DefaultLabel(html, Outline.Empty, 40));
        Assert.Equal("Page 42", BookmarkList.DefaultLabel(pdf, Outline.Empty, 41));
    }

    [Fact]
    public void APlaceWithNoWordsIsABookmark() =>
        Assert.Equal(BookmarkList.FallbackLabel, BookmarkList.DefaultLabel(Text(0, "   "), Outline.Empty, 0));

    [Fact]
    public void FirstWordsCutAtAWordWithinTheLimit()
    {
        string words = string.Join(' ', Enumerable.Repeat("castle", 20));

        string cut = BookmarkList.FirstWords(words);

        Assert.True(cut.Length <= 80);
        Assert.EndsWith("castle", cut);
        Assert.Equal(new string('x', 80), BookmarkList.FirstWords(new string('x', 120)));
        // A cut never leaves half a surrogate pair.
        Assert.Equal(new string('x', 79), BookmarkList.FirstWords(new string('x', 79) + "\U0001F5FA"));
    }

    [Fact]
    public void ScreenReadersHearTheLabelAndWhere()
    {
        ReaderLocation pdf = new(GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, Sha, new PdfPosition(41, 0), 0.4);
        Bookmark page = At("Maps", pdf);
        BookmarkItem paged = new(page, pdf, 41, false);
        BookmarkItem through = Arrange(At("Castle", Text(5, fraction: 0.374)))[0];
        BookmarkItem plain = Arrange(At("Start", Text(0)))[0];
        BookmarkItem damaged = Arrange(Stored("Broken", "{"))[0];

        Assert.Equal("Maps, page 42", BookmarkList.AccessibleName(paged));
        Assert.Equal("Castle, 37% through", BookmarkList.AccessibleName(through));
        Assert.Equal("Start", BookmarkList.AccessibleName(plain));
        Assert.Equal("Damaged bookmark", BookmarkList.AccessibleName(damaged));
        Assert.Equal("Damaged bookmark", BookmarkList.DisplayLabel(damaged));
        Assert.Equal("Start", BookmarkList.DisplayLabel(plain));
    }

    [Theory]
    [InlineData(RestoreKind.Exact, null)]
    [InlineData(RestoreKind.Context, null)]
    [InlineData(RestoreKind.Approximate, "This bookmark's place may have shifted.")]
    [InlineData(RestoreKind.Unavailable, "This bookmark's place couldn't be found.")]
    public void EachRestoreOutcomeHasItsMessage(RestoreKind kind, string? message) =>
        Assert.Equal(message, BookmarkList.MessageFor(kind));


    // T21.2: a bookmark in a guide of several pages names its page.
    [Fact]
    public void AMultipageBookmarkNamesItsPage()
    {
        BookmarkItem through = Arrange(At("Ford", Text(5, fraction: 0.375)))[0];

        Assert.Equal("Ford, Chapter 1, 38% through", BookmarkList.AccessibleName(through, "Chapter 1"));
        Assert.Equal("Ford, 38% through", BookmarkList.AccessibleName(through));
    }
}
