# T19.1 PR b (The Bookmarks tab) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Readers add, follow, rename and delete bookmarks from a
Bookmarks tab in the left pane, with Ctrl+D, Ctrl+Shift+B and the reader
toolbar. A deleted bookmark can be undone for 10 s, and a damaged one
offers only Delete.

**Architecture:**
- **Core** gets `BookmarkList`: decoding and reading-order sorting,
  the duplicate check, default labels, screen-reader names and the
  messages. It also gets two keyboard rows, with the first key-conflict
  test, and two reader commands gated by `ReaderCapabilities.Outline`.
- **Production:**
  - **`ContentsPane`** becomes a two-tab pane. A `BoundedChoice` (Toolkit
    `Segmented`) switches between the existing contents view and a new
    `BookmarksPane` (a `ListView` with in-place rename).
  - **`ShellWindow.Bookmarks.cs`** loads, adds, follows, renames and
    deletes through PR a's `IBookmarkStore`, and shows the status line's
    Undo for 10 s.
- **Installed modes** in the `navigation` group, on both lanes, with a
  seed command for stale and damaged bookmarks.

**Tech Stack:** .NET 10, WinUI 3, CommunityToolkit.WinUI.Controls.Segmented
(through `BoundedChoice`), xUnit, PowerShell UI Automation smoke tests.

**Spec:** [t19-1-bookmarks-design.md](t19-1-bookmarks-design.md), the
section "The pane and the keys" (approved 16 October 2026). PR a merged as
#126.

## Global Constraints

- **Tabs:** Contents and Bookmarks, in the existing 300 px pane, with the
  narrow-window overlay (below 720 px) unchanged.
- **Opening:**
  - Ctrl+Shift+C or the Contents command opens the pane on Contents;
  - Ctrl+Shift+B or the Bookmarks command opens it on Bookmarks;
  - the command for the tab already showing closes the pane.
- **Adding:** Ctrl+D or "Add bookmark", on the toolbar or in the tab. The
  place is the reader's current location.
  - **One place, one bookmark:** if a bookmark already has the same
    reading-order position, it is selected instead, with "This place is
    already bookmarked."
  - **The default label** is the contents entry the place falls in.
    Without one: TXT, the first words of the text at the place; HTML, the
    first words of the quote at the top; PDF, "Page N". It's trimmed to
    whole words within 80 characters.
  - **Afterwards** the pane opens on Bookmarks with the new bookmark
    selected, and the status says "Bookmark added."
- **The list:** reading order, with ties going to the older bookmark, and
  damaged bookmarks last.
  - **Screen readers** hear "label, page 42" (PDF), "label, 37% through"
    (TXT and HTML, from the locator's estimate), or the label alone with
    no estimate.
- **Following a bookmark** (Enter or a click) is a deliberate move, so the
  place is saved:
  - `Exact` and `Context` restores go straight there;
  - `Approximate` shows "This bookmark's place may have shifted.";
  - `Unavailable` shows "This bookmark's place couldn't be found.", and
    the bookmark is kept.

  The narrow overlay closes after the move.
- **F2** edits the label in place: Enter saves, Escape cancels, and an
  empty label keeps the old one.
- **Delete** removes the bookmark, with "Bookmark deleted." and "Undo" in
  the status line for 10 s.
- **A damaged bookmark** (its locator doesn't decode) is listed as
  "Damaged bookmark" and offers only Delete.
- **Failures:**
  - a full guide: "This guide has 500 bookmarks. Delete one to add
    another.";
  - a failed write: "Couldn't save the bookmark.", and nothing changes.
- **The keyboard map:** Ctrl+D ("Add bookmark") and Ctrl+Shift+B ("Show
  bookmarks") in the Reader context, covered by a conflict test. F2 and
  Delete act only inside the list.
- **The Contents fallback** ("No sections found in this guide.") gains a
  "Bookmarks" link beside "Search in the guide".
- **#115:** keys pressed inside an HTML page don't reach the app, so
  "Add bookmark" is also a toolbar command, and the installed HTML mode
  uses the toolbar.
- **Statuses that the pane already shows** are announced without the bar
  (T14.4): "Bookmark added." and "This place is already bookmarked.". The
  smoke's quiet patterns gain them, and "This bookmark's place may have
  shifted.", which goes through the reader notice.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.
  There is no `cd` in local shell commands.

## Review Focus

1. **Undo clicked twice, or after another status replaced it.** It must
   restore once, and must not resurrect a bookmark after a later status.
   PR a's restore is idempotent. Task 3's undo clears its action before
   awaiting, and the timer checks that the action is still its own.
2. **Ctrl+D right after opening, before the outline is built.** The label
   falls back to the place's own words; it must not fail. Pinned by Task
   1's `WithNoContentsTheTextGivesTheLabel`, because the shell passes
   `Outline.Empty` while the outline is still building.
3. **Escape while renaming.** It must cancel the rename, not close the
   pane. Task 3's rename box marks Escape handled. Pinned by Task 4's
   `bookmark-txt` step that presses Escape in the editor and finds the
   list still visible.
4. **A stale bookmark (the guide's content changed) at the same offset as
   the current place.** It must not count as "already bookmarked". Pinned
   by Task 1's `AStaleBookmarkIsNotThisPlace`.
5. **Leaving the guide while an Undo is showing.** The Undo must not put
   the bookmark into another guide's list. The bookmark keeps its own
   `GuideId`, so a restore goes back to its guide, and Task 3 reloads the
   list only when that guide is open. Checked by the review.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t19-1b) { Remove-Item -Recurse -Force E:\work\desktop-guides\t19-1b }; New-Item -ItemType Directory E:\work\desktop-guides\t19-1b | Out-Null"'
  git -C /Users/ilya.lissoboi/work/desktop-guides ls-files -z -co --exclude-standard | grep -z -v -E '^(\.claude/|az2aws)' | COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --null -cf - -T - | s 'tar -xf - -C E:\work\desktop-guides\t19-1b'
  s 'cd /d E:\work\desktop-guides\t19-1b && python tools\p0\make_fixtures.py > E:\work\t19-1b-fixtures.log 2>&1'
}
```

- **Tests:** `stage && s 'cd /d E:\work\desktop-guides\t19-1b && dotnet test tests\<project> --filter "<filter>" -nologo > E:\work\t19-1b-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" /c:"[FAIL]" E:\work\t19-1b-test.log'`
- **Production build:** `s 'cd /d E:\work\desktop-guides\t19-1b && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t19-1b-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t19-1b-build.log'`
- **Script parse check:** `s 'powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\t19-1b\tools\p1\test_windows_scripts_parse.ps1'`
- **Installed modes:** `gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p2-t19-1-bookmarks-tab -f shell-scope=navigation -f shell-lane=<msix|portable>`

---
### Task 1: `BookmarkList` (Core)

**Files:**
- Create: `src/DesktopGuides.Core/Reading/BookmarkList.cs`
- Test: `tests/DesktopGuides.Core.Tests/BookmarkListTests.cs`

**Interfaces:**
- Consumes: PR a's `Bookmark` and `BookmarkRules`; `ReaderLocationCodec`,
  `LocationDecodeResult`, `Outline`, `OutlineTree.EntryAt`,
  `RestoreKind`.
- Produces:
  - `sealed record BookmarkItem(Bookmark Bookmark, ReaderLocation? Location, long Order, bool Stale)` with `bool IsDamaged`
  - `static class BookmarkList` with:
    - the message constants `DamagedLabel`, `FallbackLabel`, `Added`, `AlreadyBookmarked`, `Deleted`, `UndoLabel`, `Full`, `SaveFailed`, `Shifted`, `NotFound`, and `TimeSpan UndoWindow`
    - `IReadOnlyList<BookmarkItem> Arrange(IEnumerable<Bookmark>, Func<string, LocationDecodeResult> decode, Func<ReaderLocation, long> orderOf)`
    - `BookmarkItem? At(IReadOnlyList<BookmarkItem>, long order)`
    - `string DefaultLabel(ReaderLocation place, Outline outline, long order)`
    - `string FirstWords(string text, int limit = 80)`
    - `string DisplayLabel(BookmarkItem)` and `string AccessibleName(BookmarkItem)`
    - `string? MessageFor(RestoreKind)`

- [ ] **Step 1: Write the failing tests**

```csharp
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
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-1b && dotnet test tests\DesktopGuides.Core.Tests --filter "FullyQualifiedName~BookmarkListTests" -nologo > E:\work\t19-1b-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" E:\work\t19-1b-test.log'`
Expected: FAIL to compile, with `error CS0246` for `BookmarkItem` or
`CS0103` for `BookmarkList`.

- [ ] **Step 3: Write `BookmarkList`**

```csharp
using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Reading;

// T19.1: a stored bookmark with its decoded place. A locator that doesn't
// decode leaves Location null: a damaged bookmark, offered only Delete.
// Stale: saved against other content, so it is followed but isn't "here".
public sealed record BookmarkItem(Bookmark Bookmark, ReaderLocation? Location, long Order, bool Stale)
{
    public bool IsDamaged => Location is null;
}

public static class BookmarkList
{
    public const string DamagedLabel = "Damaged bookmark";
    public const string FallbackLabel = "Bookmark";
    public const string Added = "Bookmark added.";
    public const string AlreadyBookmarked = "This place is already bookmarked.";
    public const string Deleted = "Bookmark deleted.";
    public const string UndoLabel = "Undo";
    public const string Full = "This guide has 500 bookmarks. Delete one to add another.";
    public const string SaveFailed = "Couldn't save the bookmark.";
    public const string Shifted = "This bookmark's place may have shifted.";
    public const string NotFound = "This bookmark's place couldn't be found.";
    public static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(10);

    // Reading order, the older first at one place; damaged bookmarks last.
    public static IReadOnlyList<BookmarkItem> Arrange(
        IEnumerable<Bookmark> bookmarks,
        Func<string, LocationDecodeResult> decode,
        Func<ReaderLocation, long> orderOf)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        ArgumentNullException.ThrowIfNull(decode);
        ArgumentNullException.ThrowIfNull(orderOf);
        List<BookmarkItem> items = [];
        foreach (Bookmark bookmark in bookmarks)
        {
            LocationDecodeResult decoded = decode(bookmark.LocatorJson);
            ReaderLocation? location =
                decoded.Status is LocationDecodeStatus.Valid or LocationDecodeStatus.ContentChanged
                    ? decoded.Location
                    : null;
            long order = long.MaxValue;
            if (location is not null)
            {
                try
                {
                    order = orderOf(location);
                }
                catch (Exception)
                {
                    // A place the reader can't order is as good as damaged.
                    location = null;
                }
            }
            items.Add(new BookmarkItem(
                bookmark, location, order, decoded.Status == LocationDecodeStatus.ContentChanged));
        }
        return items
            .OrderBy(item => item.IsDamaged)
            .ThenBy(item => item.Order)
            .ThenBy(item => item.Bookmark.CreatedUtc)
            .ThenBy(item => item.Bookmark.Id)
            .ToArray();
    }

    // The bookmark already at this place, if one is.
    public static BookmarkItem? At(IReadOnlyList<BookmarkItem> items, long order) =>
        items.FirstOrDefault(item => !item.IsDamaged && !item.Stale && item.Order == order);

    public static string DefaultLabel(ReaderLocation place, Outline outline, long order)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(outline);
        if (OutlineTree.EntryAt(outline, order) is OutlineEntry entry &&
            FirstWords(entry.Title) is { Length: > 0 } title)
        {
            return title;
        }
        string words = place.Payload switch
        {
            PdfPosition pdf => string.Create(CultureInfo.InvariantCulture, $"Page {pdf.PageIndex + 1}"),
            TextPosition text => FirstWords(text.Context),
            HtmlPosition html => FirstWords(html.TextQuote ?? string.Empty),
            _ => string.Empty
        };
        return words.Length > 0 ? words : FallbackLabel;
    }

    // Whitespace collapsed, then cut at the last word that fits.
    public static string FirstWords(string text, int limit = BookmarkRules.MaxLabelLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        string collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= limit) return collapsed;
        int space = collapsed.LastIndexOf(' ', limit);
        string cut = space > 0 ? collapsed[..space] : collapsed[..limit];
        return char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut;
    }

    public static string DisplayLabel(BookmarkItem item) =>
        item.IsDamaged ? DamagedLabel : item.Bookmark.Label;

    public static string AccessibleName(BookmarkItem item)
    {
        if (item.Location is not ReaderLocation place) return DamagedLabel;
        if (place.Payload is PdfPosition pdf)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{item.Bookmark.Label}, page {pdf.PageIndex + 1}");
        }
        if (place.EstimatedFraction is double fraction && double.IsFinite(fraction))
        {
            int percent = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100, MidpointRounding.AwayFromZero);
            return string.Create(CultureInfo.InvariantCulture, $"{item.Bookmark.Label}, {percent}% through");
        }
        return item.Bookmark.Label;
    }

    public static string? MessageFor(RestoreKind kind) => kind switch
    {
        RestoreKind.Approximate => Shifted,
        RestoreKind.Unavailable => NotFound,
        _ => null
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Core suite.
Expected: `BookmarkListTests` 14/14 (10 facts and a 4-row theory, with
`EachRestoreOutcomeHasItsMessage` counted 4 times) and Core passes in full
(1275 + 14 = 1289).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core/Reading/BookmarkList.cs tests/DesktopGuides.Core.Tests/BookmarkListTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): order, label and name bookmarks"
```

---
### Task 2: The keys, the commands and the conflict test (Core)

**Files:**
- Modify: `src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs`
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs`
- Test: `tests/DesktopGuides.Core.Tests/KeyboardShortcutsTests.cs`,
  `ShortcutRoutingTests.cs`, `ReaderContractTests.cs`

**Interfaces:**
- Produces:
  - `ShortcutCommand.AddBookmark` and `ShortcutCommand.ShowBookmarks`;
  - the table rows `(Reader, "Ctrl+D", AddBookmark, "Add bookmark")` and
    `(Reader, "Ctrl+Shift+B", ShowBookmarks, "Show bookmarks")`;
  - `ReaderCommand.Bookmarks` and `ReaderCommand.AddBookmark`, both
    needing `ReaderCapabilities.Outline`.

- [ ] **Step 1: Write the failing tests**
  - In `KeyboardShortcutsTests.TheTableMatchesTheSpec`, after the
    `Ctrl+Shift+C` row:

```csharp
                (ShortcutContext.Reader, "Ctrl+D", ShortcutCommand.AddBookmark, "Add bookmark"),
                (ShortcutContext.Reader, "Ctrl+Shift+B", ShortcutCommand.ShowBookmarks, "Show bookmarks"),
```

  - And a new fact in the same class:

```csharp
    // T19.1: two shortcuts that can run at the same moment never share keys.
    [Fact]
    public void NoTwoShortcutsShareKeysWhereBothCanRun()
    {
        static bool Overlap(ShortcutContext a, ShortcutContext b) =>
            a == b || a == ShortcutContext.Everywhere || b == ShortcutContext.Everywhere ||
            (a, b) is (ShortcutContext.Library, ShortcutContext.LibrarySearch)
                or (ShortcutContext.LibrarySearch, ShortcutContext.Library);

        string[] clashes =
        [
            .. from first in KeyboardShortcuts.All
               from second in KeyboardShortcuts.All
               where first.Command < second.Command && first.Keys == second.Keys &&
                     Overlap(first.Context, second.Context)
               select $"{first.Command} and {second.Command} on {first.Keys}"
        ];

        Assert.Empty(clashes);
    }
```

  - In `ShortcutRoutingTests`, after `ShowContentsRunsOnlyInTheReader`:

```csharp
    [Theory]
    [InlineData(ShortcutCommand.AddBookmark)]
    [InlineData(ShortcutCommand.ShowBookmarks)]
    public void BookmarkKeysRunOnlyInTheReader(ShortcutCommand command)
    {
        Assert.Equal(ShortcutDecision.Run,
            ShortcutRouting.Resolve(command, Reader, textInputFocused: false, dialogOpen: false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(command, Library, textInputFocused: false, dialogOpen: false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(command, Reader, textInputFocused: false, dialogOpen: true));
    }
```

  - In `ReaderContractTests`, after `ContentsNeedsTheOutlineCapability`:

```csharp
    // T19.1: bookmarks order their places with the Outline capability's OrderOf.
    [Fact]
    public void BookmarksNeedTheOutlineCapability()
    {
        using FakeReader outlined = new(GuideFormat.Txt, ReaderCapabilities.Outline);
        using FakeReader findable = new(GuideFormat.Txt, ReaderCapabilities.Find);

        Assert.Contains(ReaderCommand.Bookmarks, ReaderCommandPolicy.VisibleCommands(outlined));
        Assert.Contains(ReaderCommand.AddBookmark, ReaderCommandPolicy.VisibleCommands(outlined));
        Assert.DoesNotContain(ReaderCommand.Bookmarks, ReaderCommandPolicy.VisibleCommands(findable));
        Assert.DoesNotContain(ReaderCommand.AddBookmark, ReaderCommandPolicy.VisibleCommands(findable));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-1b && dotnet test tests\DesktopGuides.Core.Tests -nologo > E:\work\t19-1b-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" E:\work\t19-1b-test.log'`
Expected: FAIL to compile, with `CS0117` for `ShortcutCommand.AddBookmark`
and `ReaderCommand.Bookmarks`.

- [ ] **Step 3: Add the keys and commands**
  - `KeyboardShortcuts.cs`: the enum gains `AddBookmark, ShowBookmarks`
    between `ShowContents` and `CloseDialog`. `All` gains, after the
    `Ctrl+Shift+C` row:

```csharp
        new(ShortcutContext.Reader, "Ctrl+D", ShortcutCommand.AddBookmark, "Add bookmark"),
        new(ShortcutContext.Reader, "Ctrl+Shift+B", ShortcutCommand.ShowBookmarks, "Show bookmarks"),
```

  - `ReaderContract.cs`: `ReaderCommand` gains `Bookmarks, AddBookmark`
    after `Contents`, and `ReaderCommandPolicy.Commands` gains:

```csharp
        (ReaderCommand.Bookmarks, ReaderCapabilities.Outline),
        (ReaderCommand.AddBookmark, ReaderCapabilities.Outline)
```

  Ctrl+D isn't a typing key, so `ShortcutRouting.TypingKeys` is
  unchanged: Ctrl+D adds a bookmark even while the rename box has focus.

- [ ] **Step 4: Run the tests to verify they pass, and that the conflict
  test can fail**

Run: the Step 2 command.
Expected: Core passes in full (1289 + 1 + 2 + 1 = 1293).

The conflict test passes on today's table, so prove it bites: change the
new row's keys to `"Ctrl+Shift+C"` in `KeyboardShortcuts.cs`, run
`--filter "FullyQualifiedName~NoTwoShortcutsShareKeys"`, and expect FAIL
naming `ShowContents and ShowBookmarks on Ctrl+Shift+C`. Then restore
`"Ctrl+Shift+B"`.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs src/DesktopGuides.Core/Reading/ReaderContract.cs tests/DesktopGuides.Core.Tests/KeyboardShortcutsTests.cs tests/DesktopGuides.Core.Tests/ShortcutRoutingTests.cs tests/DesktopGuides.Core.Tests/ReaderContractTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): bookmark keys and commands, and a key-conflict test"
```

---
### Task 3: The Bookmarks tab, the toolbar commands and the shell (Production)

**Files:**
- Create: `src/DesktopGuides.Production/BookmarksPane.xaml` and `.xaml.cs`
- Create: `src/DesktopGuides.Production/ShellWindow.Bookmarks.cs`
- Modify: `src/DesktopGuides.Production/ContentsPane.xaml` and `.xaml.cs` (tabs, the Bookmarks link)
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml` and `.xaml.cs` (two commands)
- Modify: `src/DesktopGuides.Production/ShellWindow.Contents.cs` (tab-aware toggling; `StartContents` takes the guide)
- Modify: `src/DesktopGuides.Production/ShellWindow.Keyboard.cs`, `ShellWindow.Progress.cs:210`, `ShellWindow.xaml.cs:164`
- Test: `tests/DesktopGuides.Core.Tests/ReaderToolbarTooltipTests.cs` (two rows)

**Interfaces:**
- Consumes: Task 1's `BookmarkItem` and `BookmarkList`; Task 2's
  `ReaderCommand.Bookmarks`, `ReaderCommand.AddBookmark`,
  `ShortcutCommand.AddBookmark` and `ShortcutCommand.ShowBookmarks`; PR
  a's `IBookmarkStore` on `repository`.
- Produces (for Task 4's smoke): these AutomationIds:
  - `ReaderPaneTabs`, with the items `ReaderPaneContentsTab` and `ReaderPaneBookmarksTab`;
  - `ReaderBookmarksPane`, `ReaderBookmarksAdd`, `ReaderBookmarksEmpty` and `ReaderBookmarksList`, whose list items are named by `BookmarkList.AccessibleName`;
  - `ReaderBookmarkEditor`, `ReaderContentsBookmarks`, and `ShellStatusAction` for Undo;
  - the toolbar commands "Bookmarks" and "Add bookmark".

Production has no unit-test project. This task's checks are the tooltip
test, a warning-free build, and Task 4's installed modes.

- [ ] **Step 1: Write the failing tooltip rows.** In
  `ReaderToolbarTooltipTests.EachToolbarTooltipNamesItsKeys`, add:

```csharp
    [InlineData("ShowBookmarks", ShortcutCommand.ShowBookmarks)]
    [InlineData("AddBookmark", ShortcutCommand.AddBookmark)]
```

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-1b && dotnet test tests\DesktopGuides.Core.Tests --filter "FullyQualifiedName~ReaderToolbarTooltipTests" -nologo > E:\work\t19-1b-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" E:\work\t19-1b-test.log'`
Expected: the two new rows FAIL, because the toolbar has no such buttons.

- [ ] **Step 2: The toolbar commands.** In `ReaderToolbar.xaml`, after
  the `ShowContents` button in `SecondaryCommands`:

```xml
            <AppBarButton x:Name="ShowBookmarks"
                          Label="Bookmarks"
                          ToolTipService.ToolTip="Bookmarks (Ctrl+Shift+B)"
                          AutomationProperties.AcceleratorKey="Ctrl+Shift+B"
                          Visibility="Collapsed"
                          Click="ShowBookmarksClicked">
                <AppBarButton.Icon>
                    <!-- Bookmarks. -->
                    <FontIcon Glyph="&#xE8A4;" />
                </AppBarButton.Icon>
            </AppBarButton>
            <AppBarButton x:Name="AddBookmark"
                          Label="Add bookmark"
                          ToolTipService.ToolTip="Add bookmark (Ctrl+D)"
                          AutomationProperties.AcceleratorKey="Ctrl+D"
                          Visibility="Collapsed"
                          Click="AddBookmarkClicked">
                <AppBarButton.Icon>
                    <!-- Add. -->
                    <FontIcon Glyph="&#xE710;" />
                </AppBarButton.Icon>
            </AppBarButton>
```

  In `ReaderToolbar.xaml.cs`:
  - in `RefreshCommands`, after `bool contents = …`:
    `bool bookmarks = supported.Contains(ReaderCommand.Bookmarks);` and
    `bool addBookmark = supported.Contains(ReaderCommand.AddBookmark);`;
  - after `ShowContents.Visibility = Show(contents);`:
    `ShowBookmarks.Visibility = Show(bookmarks);` and
    `AddBookmark.Visibility = Show(addBookmark);`;
  - add `|| bookmarks || addBookmark` to the `Commands.Visibility` list;
  - in the focus-fallback array (line 415), add `ShowBookmarks, AddBookmark`
    after `ShowContents`;
  - after `ShowContentsClicked`:

```csharp
    // T19.1: the shell opens the Bookmarks tab, or adds a bookmark here.
    public event EventHandler? BookmarksRequested;
    public event EventHandler? AddBookmarkRequested;

    private void ShowBookmarksClicked(object sender, RoutedEventArgs args) =>
        BookmarksRequested?.Invoke(this, EventArgs.Empty);

    private void AddBookmarkClicked(object sender, RoutedEventArgs args) =>
        AddBookmarkRequested?.Invoke(this, EventArgs.Empty);
```

  `tools/p1/DesktopGuides.ReaderToolbarSmoke` compiles these two files
  directly. They use only WinUI and Core types, so it still builds.

- [ ] **Step 3: `BookmarksPane`.** `BookmarksPane.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl x:Class="DesktopGuides.Production.BookmarksPane"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="using:DesktopGuides.Production"
             AutomationProperties.AutomationId="ReaderBookmarksPane"
             AutomationProperties.Name="Bookmarks">
    <Grid RowSpacing="8">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <Button x:Name="AddButton" Content="Add bookmark" Click="AddClicked"
                ToolTipService.ToolTip="Add bookmark (Ctrl+D)"
                AutomationProperties.AutomationId="ReaderBookmarksAdd" />
        <TextBlock x:Name="Empty" Grid.Row="1" Visibility="Collapsed" TextWrapping="Wrap"
                   Text="No bookmarks in this guide yet."
                   AutomationProperties.AutomationId="ReaderBookmarksEmpty"
                   Style="{StaticResource DesktopGuidesBodyStyle}" />
        <ListView x:Name="List" Grid.Row="2" Visibility="Collapsed"
                  SelectionMode="Single" IsItemClickEnabled="True"
                  ItemClick="ItemClicked" KeyDown="ListKeyDown"
                  ContainerContentChanging="ListContainerChanging"
                  AutomationProperties.AutomationId="ReaderBookmarksList"
                  AutomationProperties.Name="Bookmarks">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="local:BookmarkRow">
                    <Grid>
                        <TextBlock Text="{x:Bind Label}" VerticalAlignment="Center"
                                   TextTrimming="CharacterEllipsis"
                                   Visibility="{x:Bind LabelVisibility, Mode=OneWay}" />
                        <TextBox MaxLength="80"
                                 Visibility="{x:Bind EditorVisibility, Mode=OneWay}"
                                 KeyDown="EditorKeyDown" LostFocus="EditorLostFocus"
                                 AutomationProperties.AutomationId="ReaderBookmarkEditor"
                                 AutomationProperties.Name="Bookmark name" />
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</UserControl>
```

  `BookmarksPane.xaml.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.WinUI;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace DesktopGuides.Production;

// T19.1: one row of the Bookmarks tab. Only the editor's visibility changes;
// a rename reloads the list from the library.
public sealed partial class BookmarkRow(BookmarkItem item) : INotifyPropertyChanged
{
    private bool editing;

    public BookmarkItem Item { get; } = item;
    public string Label => BookmarkList.DisplayLabel(Item);
    public string AccessibleName => BookmarkList.AccessibleName(Item);
    public Visibility LabelVisibility => editing ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditorVisibility => editing ? Visibility.Visible : Visibility.Collapsed;

    public bool IsEditing
    {
        get => editing;
        set
        {
            if (editing == value) return;
            editing = value;
            PropertyChanged?.Invoke(this, new(nameof(LabelVisibility)));
            PropertyChanged?.Invoke(this, new(nameof(EditorVisibility)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

// T19.1: the Bookmarks tab. It lists and reports; the shell stores and moves.
public sealed partial class BookmarksPane : UserControl
{
    private readonly ObservableCollection<BookmarkRow> rows = [];

    public BookmarksPane()
    {
        InitializeComponent();
        List.ItemsSource = rows;
    }

    public event EventHandler? AddRequested;
    public event EventHandler<BookmarkItem>? ItemInvoked;
    public event EventHandler<(BookmarkItem Item, string Label)>? RenameRequested;
    public event EventHandler<BookmarkItem>? DeleteRequested;

    // The bookmark to select, or the first.
    public void ShowItems(IReadOnlyList<BookmarkItem> items, Guid? select)
    {
        ArgumentNullException.ThrowIfNull(items);
        rows.Clear();
        foreach (BookmarkItem item in items) rows.Add(new BookmarkRow(item));
        Empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        List.SelectedItem = rows.FirstOrDefault(row => row.Item.Bookmark.Id == select) ?? rows.FirstOrDefault();
    }

    public void Clear()
    {
        rows.Clear();
        Empty.Visibility = Visibility.Collapsed;
        List.Visibility = Visibility.Collapsed;
    }

    // After layout, so the selected item's container exists.
    public void FocusContent() =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (List.SelectedItem is BookmarkRow row && List.ContainerFromItem(row) is ListViewItem item &&
                item.Focus(FocusState.Programmatic))
            {
                return;
            }
            AddButton.Focus(FocusState.Programmatic);
        });

    private void AddClicked(object sender, RoutedEventArgs args) => AddRequested?.Invoke(this, EventArgs.Empty);

    // Enter, Space or a click. A damaged bookmark has no place to go to.
    private void ItemClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is BookmarkRow { IsEditing: false } row && !row.Item.IsDamaged)
        {
            ItemInvoked?.Invoke(this, row.Item);
        }
    }

    private void ListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (List.SelectedItem is not BookmarkRow row || row.IsEditing) return;
        if (args.Key == VirtualKey.F2 && !row.Item.IsDamaged)
        {
            args.Handled = true;
            BeginRename(row);
        }
        else if (args.Key == VirtualKey.Delete)
        {
            args.Handled = true;
            DeleteRequested?.Invoke(this, row.Item);
        }
    }

    private void ListContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is BookmarkRow row) AutomationProperties.SetName(args.ItemContainer, row.AccessibleName);
    }

    private void BeginRename(BookmarkRow row)
    {
        row.IsEditing = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (List.ContainerFromItem(row) is ListViewItem item && item.FindDescendant<TextBox>() is TextBox editor)
            {
                editor.Text = row.Item.Bookmark.Label;
                editor.SelectAll();
                editor.Focus(FocusState.Programmatic);
            }
        });
    }

    // Escape is handled here, so the pane doesn't take it as "close".
    private void EditorKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (sender is not TextBox { DataContext: BookmarkRow row } editor) return;
        if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            EndRename(row, editor.Text);
        }
        else if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            EndRename(row, null);
        }
    }

    private void EditorLostFocus(object sender, RoutedEventArgs args)
    {
        if (sender is TextBox { DataContext: BookmarkRow { IsEditing: true } row }) EndRename(row, null);
    }

    // An empty or unchanged label keeps the old one.
    private void EndRename(BookmarkRow row, string? label)
    {
        row.IsEditing = false;
        if (List.ContainerFromItem(row) is ListViewItem item) item.Focus(FocusState.Programmatic);
        if (label?.Trim() is { Length: > 0 } trimmed && trimmed != row.Item.Bookmark.Label)
        {
            RenameRequested?.Invoke(this, (row.Item, trimmed));
        }
    }
}
```

- [ ] **Step 4: Tabs in `ContentsPane`.** Replace `ContentsPane.xaml`'s
  root `Grid` with:

```xml
    <Grid RowSpacing="8" Padding="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <Grid ColumnSpacing="8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <local:BoundedChoice x:Name="Tabs" HorizontalAlignment="Left"
                                 AutomationProperties.AutomationId="ReaderPaneTabs"
                                 AutomationProperties.Name="Reader pane" />
            <Button x:Name="CloseButton" Grid.Column="1" Click="CloseClicked"
                    AutomationProperties.AutomationId="ReaderContentsClose"
                    AutomationProperties.Name="Close pane"
                    ToolTipService.ToolTip="Close pane (Esc)">
                <SymbolIcon Symbol="Cancel" />
            </Button>
        </Grid>
        <Grid x:Name="ContentsView" Grid.Row="1" RowSpacing="8">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <StackPanel Spacing="8">
                <TextBlock x:Name="Building" Text="Finding sections…" Visibility="Collapsed"
                           AutomationProperties.AutomationId="ReaderContentsStatus"
                           Style="{StaticResource DesktopGuidesSecondaryBodyStyle}" />
                <TextBlock x:Name="Empty" Text="No sections found in this guide." Visibility="Collapsed"
                           TextWrapping="Wrap"
                           AutomationProperties.AutomationId="ReaderContentsEmpty"
                           Style="{StaticResource DesktopGuidesBodyStyle}" />
                <HyperlinkButton x:Name="SearchLink" Content="Search in the guide" Visibility="Collapsed"
                                 Click="SearchClicked"
                                 AutomationProperties.AutomationId="ReaderContentsSearch" />
                <HyperlinkButton x:Name="BookmarksLink" Content="Bookmarks" Visibility="Collapsed"
                                 Click="BookmarksClicked"
                                 AutomationProperties.AutomationId="ReaderContentsBookmarks" />
            </StackPanel>
            <TreeView x:Name="Tree" Grid.Row="1" Visibility="Collapsed" SelectionMode="Single"
                      ItemInvoked="TreeItemInvoked"
                      AutomationProperties.AutomationId="ReaderContentsTree"
                      AutomationProperties.Name="Contents" />
        </Grid>
        <local:BookmarksPane x:Name="BookmarksView" Grid.Row="1" Visibility="Collapsed" />
    </Grid>
```

  Add `xmlns:local="using:DesktopGuides.Production"` to the root, and set
  its `AutomationProperties.Name` to "Reader pane". In
  `ContentsPane.xaml.cs`:

```csharp
public enum ReaderPaneTab { Contents, Bookmarks }
```

  The constructor gains, after `InitializeComponent();`:

```csharp
        Tabs.SetOptions(
        [
            new BoundedChoiceOption("Contents", "ReaderPaneContentsTab"),
            new BoundedChoiceOption("Bookmarks", "ReaderPaneBookmarksTab")
        ]);
        Tabs.SelectedIndex = 0;
        Tabs.ChoiceChanged += (_, _) =>
        {
            ApplyTab();
            // Only the user's choice: the shell sets the tab while it loads.
            if (!settingTab) TabChanged?.Invoke(this, EventArgs.Empty);
        };
```

  And the members:

```csharp
    private bool settingTab;

    public event EventHandler? TabChanged;
    public event EventHandler? BookmarksRequested;

    public BookmarksPane BookmarksTab => BookmarksView;

    public ReaderPaneTab Tab => Tabs.SelectedIndex == 1 ? ReaderPaneTab.Bookmarks : ReaderPaneTab.Contents;

    public void ShowTab(ReaderPaneTab tab)
    {
        settingTab = true;
        try
        {
            Tabs.SelectedIndex = tab == ReaderPaneTab.Bookmarks ? 1 : 0;
        }
        finally
        {
            settingTab = false;
        }
        ApplyTab();
    }

    private void ApplyTab()
    {
        ContentsView.Visibility = Tab == ReaderPaneTab.Contents ? Visibility.Visible : Visibility.Collapsed;
        BookmarksView.Visibility = Tab == ReaderPaneTab.Bookmarks ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BookmarksClicked(object sender, RoutedEventArgs args) =>
        BookmarksRequested?.Invoke(this, EventArgs.Empty);
```

  The existing members change in three places:
  - `ShowOutline`'s empty branch also sets
    `BookmarksLink.Visibility = Visibility.Visible;`.
  - `Clear()` also collapses `BookmarksLink`.
  - `FocusContent` starts with
    `if (Tab == ReaderPaneTab.Bookmarks) { BookmarksView.FocusContent(); return; }`.

- [ ] **Step 5: The shell, `ShellWindow.Bookmarks.cs`**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T19.1: the open guide's bookmarks, in the pane's Bookmarks tab. The tab
    // last chosen lasts for the app session, like the pane's open state.
    private const string BookmarksLoadFailed = "Couldn't load this guide's bookmarks.";
    private ReaderPaneTab paneTab = ReaderPaneTab.Contents;
    private Guid? bookmarkGuide;
    private GuideFormat bookmarkFormat;
    private string bookmarkSha = string.Empty;
    private string? bookmarkEntry;
    private IReadOnlyList<BookmarkItem> bookmarks = [];
    private Func<Task>? undoAction;
    private DispatcherQueueTimer? undoTimer;

    private void InitializeBookmarks()
    {
        BookmarksPane tab = ReaderContents.BookmarksTab;
        tab.AddRequested += (_, _) => _ = AddBookmarkAsync();
        tab.ItemInvoked += (_, item) => _ = FollowBookmarkAsync(item);
        tab.RenameRequested += (_, change) => _ = RenameBookmarkAsync(change.Item, change.Label);
        tab.DeleteRequested += (_, item) => _ = DeleteBookmarkAsync(item);
        ReaderContents.BookmarksRequested += (_, _) => _ = OpenPaneAsync(ReaderPaneTab.Bookmarks);
        ReaderContents.TabChanged += (_, _) =>
        {
            paneTab = ReaderContents.Tab;
            if (paneTab == ReaderPaneTab.Bookmarks) _ = RefreshBookmarksAsync(select: null, focus: false);
        };
        ReaderActions.BookmarksRequested += (_, _) => TogglePane(ReaderPaneTab.Bookmarks);
        ReaderActions.AddBookmarkRequested += (_, _) => _ = AddBookmarkAsync();
        undoTimer = DispatcherQueue.CreateTimer();
        undoTimer.Interval = BookmarkList.UndoWindow;
        undoTimer.IsRepeating = false;
        // Another status may have replaced the Undo meanwhile; that one stays.
        undoTimer.Tick += (_, _) =>
        {
            if (undoAction is not null && ReferenceEquals(statusAction, undoAction)) HideStatus();
            undoAction = null;
        };
    }

    // Called by StartContents, once the guide's tracking has started.
    private void StartBookmarks(Guide guide, string contentSha256, string? htmlEntry)
    {
        bookmarkGuide = guide.Id;
        bookmarkFormat = guide.Format;
        bookmarkSha = contentSha256;
        bookmarkEntry = htmlEntry;
        bookmarks = [];
        ReaderContents.BookmarksTab.Clear();
    }

    // A pending Undo is kept: it restores into its own guide.
    private void StopBookmarks()
    {
        bookmarkGuide = null;
        bookmarks = [];
        ReaderContents.BookmarksTab.Clear();
    }

    private bool AddBookmarkFromKeyboard()
    {
        if (bookmarkGuide is null || readerSession is not IOutlineReader) return false;
        _ = AddBookmarkAsync();
        return true;
    }

    private bool ToggleBookmarksFromKeyboard()
    {
        if (bookmarkGuide is null || readerSession is not IOutlineReader) return false;
        TogglePane(ReaderPaneTab.Bookmarks);
        return true;
    }

    private async Task OpenBookmarksAsync(Guid? select)
    {
        if (bookmarkGuide is null || readerSession is not IOutlineReader) return;
        paneTab = ReaderPaneTab.Bookmarks;
        contentsOpen = true;
        ReaderContents.ShowTab(ReaderPaneTab.Bookmarks);
        ShowContentsHost();
        await RefreshBookmarksAsync(select, focus: true);
    }

    private async Task<IReadOnlyList<BookmarkItem>?> LoadBookmarksAsync()
    {
        if (bookmarkGuide is not Guid guide || readerSession is not IOutlineReader reader ||
            repository is not SqliteLibraryRepository store)
        {
            return null;
        }
        IReadOnlyList<Bookmark> stored;
        try
        {
            stored = await store.ListBookmarksAsync(guide);
        }
        catch (Exception)
        {
            ShowErrorStatus(BookmarksLoadFailed);
            return null;
        }
        // The guide was left, or another opened, while the list loaded.
        if (bookmarkGuide != guide || !ReferenceEquals(reader, readerSession)) return null;
        bookmarks = BookmarkList.Arrange(stored,
            json => ReaderLocationCodec.Deserialize(json, bookmarkFormat, bookmarkSha, bookmarkEntry),
            reader.OrderOf);
        return bookmarks;
    }

    private async Task RefreshBookmarksAsync(Guid? select, bool focus)
    {
        if (await LoadBookmarksAsync() is not IReadOnlyList<BookmarkItem> items) return;
        ReaderContents.BookmarksTab.ShowItems(items, select);
        if (focus) ReaderContents.BookmarksTab.FocusContent();
    }

    private async Task AddBookmarkAsync()
    {
        if (bookmarkGuide is not Guid guide || readerSession is not IReaderSession session ||
            session is not IOutlineReader reader || repository is not SqliteLibraryRepository store)
        {
            return;
        }
        ReaderLocation place;
        try
        {
            place = await session.GetLocationAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            ShowErrorStatus(BookmarkList.SaveFailed);
            return;
        }
        if (await LoadBookmarksAsync() is not IReadOnlyList<BookmarkItem> items) return;
        long order = reader.OrderOf(place);
        if (BookmarkList.At(items, order) is BookmarkItem existing)
        {
            await OpenBookmarksAsync(existing.Bookmark.Id);
            AnnounceStatus(BookmarkList.AlreadyBookmarked);
            return;
        }
        // While the outline builds there are no entries yet: the place's own words.
        string label = BookmarkList.DefaultLabel(place, outline ?? Outline.Empty, order);
        Bookmark added;
        try
        {
            added = await store.AddBookmarkAsync(guide, ReaderLocationCodec.Serialize(place), label);
        }
        catch (BookmarkLimitException)
        {
            ShowWarningStatus(BookmarkList.Full);
            return;
        }
        catch (Exception)
        {
            ShowErrorStatus(BookmarkList.SaveFailed);
            return;
        }
        await OpenBookmarksAsync(added.Id);
        AnnounceStatus(BookmarkList.Added);
    }

    // A deliberate move: the restore raises LocationChanged, so the place saves.
    private async Task FollowBookmarkAsync(BookmarkItem item)
    {
        if (item.Location is not ReaderLocation place || readerSession is not IReaderSession session) return;
        RestoreOutcome outcome;
        try
        {
            outcome = await session.RestoreLocationAsync(place, CancellationToken.None);
        }
        catch (Exception)
        {
            outcome = new RestoreOutcome(RestoreKind.Unavailable, null);
        }
        if (!ReferenceEquals(session, readerSession)) return;
        if (outcome.Kind == RestoreKind.Approximate) ShowReaderNotice(BookmarkList.Shifted);
        else if (outcome.Kind == RestoreKind.Unavailable) ShowWarningStatus(BookmarkList.NotFound);
        if (ContentsNarrow) CloseContents(focusGuide: false);
        FocusReaderContent();
    }

    private async Task RenameBookmarkAsync(BookmarkItem item, string label)
    {
        if (repository is not SqliteLibraryRepository store) return;
        try
        {
            await store.RenameBookmarkAsync(item.Bookmark.Id, label);
        }
        catch (Exception)
        {
            ShowErrorStatus(BookmarkList.SaveFailed);
            return;
        }
        await RefreshBookmarksAsync(item.Bookmark.Id, focus: true);
    }

    private async Task DeleteBookmarkAsync(BookmarkItem item)
    {
        if (repository is not SqliteLibraryRepository store) return;
        Guid id = item.Bookmark.Id;
        BookmarkItem? next = bookmarks.SkipWhile(b => b.Bookmark.Id != id).Skip(1).FirstOrDefault()
            ?? bookmarks.TakeWhile(b => b.Bookmark.Id != id).LastOrDefault();
        try
        {
            await store.DeleteBookmarkAsync(id);
        }
        catch (Exception)
        {
            ShowErrorStatus(BookmarkList.SaveFailed);
            return;
        }
        await RefreshBookmarksAsync(next?.Bookmark.Id, focus: true);
        Bookmark deleted = item.Bookmark;
        Func<Task> undo = null!;
        undo = async () =>
        {
            // A second click, or one after the window closed, does nothing.
            if (!ReferenceEquals(undoAction, undo)) return;
            undoAction = null;
            undoTimer?.Stop();
            HideStatus();
            try
            {
                await store.RestoreBookmarkAsync(deleted);
            }
            catch (BookmarkLimitException)
            {
                ShowWarningStatus(BookmarkList.Full);
                return;
            }
            catch (Exception)
            {
                ShowErrorStatus(BookmarkList.SaveFailed);
                return;
            }
            if (bookmarkGuide == deleted.GuideId) await RefreshBookmarksAsync(deleted.Id, focus: true);
        };
        ShowStatus(BookmarkList.Deleted, InfoBarSeverity.Informational, isClosable: true, autoDismiss: false);
        ShowStatusAction(BookmarkList.UndoLabel, undo);
        undoAction = undo;
        undoTimer?.Stop();
        undoTimer?.Start();
    }
}
```

  Check the field types before building: `repository` is declared
  `SqliteLibraryRepository?` (ShellWindow.xaml.cs:44), and `statusAction`
  is `Func<Task>?` (line 64). If `ShowStatus`'s parameter names differ
  from `isClosable` and `autoDismiss`, pass them positionally.

- [ ] **Step 6: The tab-aware pane in `ShellWindow.Contents.cs`**
  - `InitializeContents`: replace
    `ReaderActions.ContentsRequested += (_, _) => ToggleContents();` with
    `ReaderActions.ContentsRequested += (_, _) => TogglePane(ReaderPaneTab.Contents);`.
  - Replace `ToggleContents` with:

```csharp
    // The command for the tab already showing closes the pane.
    private void TogglePane(ReaderPaneTab tab)
    {
        if (ReaderContentsHost.Visibility == Visibility.Visible && ReaderContents.Tab == tab)
        {
            CloseContents(focusGuide: true);
        }
        else
        {
            _ = OpenPaneAsync(tab);
        }
    }

    private Task OpenPaneAsync(ReaderPaneTab tab) =>
        tab == ReaderPaneTab.Bookmarks ? OpenBookmarksAsync(select: null) : OpenContentsAsync();
```

  - `ToggleContentsFromKeyboard`: call `TogglePane(ReaderPaneTab.Contents);`
    instead of `ToggleContents();`.
  - `OpenContentsAsync`: after `contentsOpen = true;`, add
    `paneTab = ReaderPaneTab.Contents;` and
    `ReaderContents.ShowTab(ReaderPaneTab.Contents);`.
  - `StartContents` takes the guide, so bookmarks know whose they are:

```csharp
    // Called once a session's tracking starts, so a restore has run.
    private void StartContents(IReaderSession session, Guide guide, string contentSha256, string? htmlEntry)
    {
        StopContents();
        if (session is not IOutlineReader reader) return;
        StartBookmarks(guide, contentSha256, htmlEntry);
        CancellationTokenSource cancel = new();
        outlineCancel = cancel;
        if (contentsOpen)
        {
            ShowContentsHost();
            ReaderContents.ShowTab(paneTab);
            ReaderContents.ShowBuilding();
            if (paneTab == ReaderPaneTab.Bookmarks) _ = RefreshBookmarksAsync(select: null, focus: false);
        }
        _ = BuildOutlineAsync(session, reader, cancel.Token);
    }
```

  - `StopContents`: add `StopBookmarks();` after `ReaderContents.Clear();`.
  - Add `using DesktopGuides.Core.Library;` for `Guide`.
- [ ] **Step 7: Wire the rest.**
  - `ShellWindow.Progress.cs:210`: `StartContents(session);` becomes
    `StartContents(session, guide, contentSha256, htmlEntry);`.
  - `ShellWindow.xaml.cs:164`: after `InitializeContents();`, add
    `InitializeBookmarks();`.
  - `ShellWindow.Keyboard.cs` `InitializeKeyboard`: after the Ctrl+Shift+C
    line, add:

```csharp
        AddShortcut(VirtualKey.D, ShortcutCommand.AddBookmark, AddBookmarkFromKeyboard);
        AddShortcut(VirtualKey.B, ShortcutCommand.ShowBookmarks, ToggleBookmarksFromKeyboard,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift);
```

- [ ] **Step 8: Build and run the tests**

Run: the Production build (expected: 0 warnings, 0 errors), then the
Step 1 tooltip command (expected: PASS) and the whole Core suite
(expected: 1293 + 2 = 1295).
If the XAML compiler rejects event handlers inside the `DataTemplate`,
wire them in `ListContainerChanging` with `FindDescendant<TextBox>()`
instead, and ledger the ruling.

- [ ] **Step 9: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production tests/DesktopGuides.Core.Tests/ReaderToolbarTooltipTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): the Bookmarks tab, its keys and the toolbar commands"
```

---
### Task 4: The installed bookmark modes

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`clear-bookmarks`, `seed-bookmarks`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the modes, the quiet patterns, the Settings rows)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-NavigationScenarios`)

**Interfaces:**
- Consumes: Task 3's AutomationIds and toolbar labels; Task 1's names
  ("label, N% through", "Page N, page N") and messages.
- Produces: `bookmark-txt`, `bookmark-restart`, `bookmark-html`,
  `bookmark-pdf` and `bookmark-changed` in the `navigation` group, with the
  screenshots `bookmark-txt`, `bookmark-html` and `bookmark-changed`.

- [ ] **Step 1: The seed commands.** In `ShellSeed/Program.cs`, beside
  `clear-reading-locations`:

```csharp
if (args.Length == 2 && args[0] == "clear-bookmarks")
{
    // T19.1: each bookmark pass that adds its own starts from none.
    ExecuteSql(new ManagedPathResolver(args[1]), "DELETE FROM Bookmarks");
    return 0;
}

if (args.Length == 2 && args[0] == "seed-bookmarks")
{
    // T19.1: Sections Guide gets a bookmark saved against older content,
    // whose words aren't in the guide and which has no estimate (so it can't
    // be found), and one whose locator doesn't parse.
    string stale = ReaderLocationCodec.Serialize(new ReaderLocation(
        GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, new string('0', 64),
        new TextPosition(0, "These words were never in the sections guide."), null));
    ExecuteSql(new ManagedPathResolver(args[1]), $$"""
        DELETE FROM Bookmarks;
        INSERT INTO Bookmarks (Id, GuideId, LocatorJson, Label, CreatedUtcMs, UpdatedUtcMs)
        SELECT '{{Guid.NewGuid():N}}', Id, '{{stale}}', 'Old place', 1000, 1000
        FROM Guides WHERE Title = 'Sections Guide';
        INSERT INTO Bookmarks (Id, GuideId, LocatorJson, Label, CreatedUtcMs, UpdatedUtcMs)
        SELECT '{{Guid.NewGuid():N}}', Id, '{not json', 'Broken', 2000, 2000
        FROM Guides WHERE Title = 'Sections Guide';
        """);
    return 0;
}
```

  Add `using DesktopGuides.Core.Reading;` and `using DesktopGuides.Core.Library;`
  if they're missing (for `ReaderLocation` and `GuideFormat`).

- [ ] **Step 2: Register the modes in the smoke.** In
  `windows_shell_ui_smoke.ps1`:
  - **the `ValidateSet` (line 29) and the reader-mode `-in` list (line
    1568):** add `'bookmark-txt', 'bookmark-restart', 'bookmark-html',
    'bookmark-pdf', 'bookmark-changed'` after `'contents-pdf'`;
  - **the game mapping:** in `elseif ($Mode -like 'progress-*' -or … -or
    $Mode -like 'contents-*')`, add `-or $Mode -like 'bookmark-*'`, so
    they use 'Progress Game';
  - **the dispatch block:** `elseif ($Mode -like 'find-*' -or $Mode -like
    'contents-*')` becomes `… -or $Mode -like 'bookmark-*')`;
  - **`$quietStatusPatterns`** gains:

```powershell
        '^Bookmark added\.$',
        '^This place is already bookmarked\.$',
        "^This bookmark's place may have shifted\.$",
```

  - **the keyboard-settings `$expected` table** gains:

```powershell
            ShortcutAddBookmark = 'Add bookmark, Ctrl+D'
            ShortcutShowBookmarks = 'Show bookmarks, Ctrl+Shift+B'
```

- [ ] **Step 3: The helpers.** In the find, contents and bookmark block,
  after `Wait-TopRowLike`:

```powershell
            $bookmarkItem = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)
            function Get-BookmarkItems {
                $list = Find-ById 'ReaderBookmarksList'
                if (-not $list -or $list.Current.IsOffscreen) { return @() }
                return @($list.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bookmarkItem))
            }
            # The list's names, in order, each matching its -like pattern.
            function Wait-BookmarkNames([string[]] $patterns, [string] $step) {
                $deadline = (Get-Date).AddSeconds(10)
                $names = @()
                do {
                    $names = @(Get-BookmarkItems | ForEach-Object { $_.Current.Name })
                    if ($names.Count -eq $patterns.Count) {
                        $matched = $true
                        for ($i = 0; $i -lt $names.Count; $i++) {
                            if ($names[$i] -notlike $patterns[$i]) { $matched = $false }
                        }
                        if ($matched) { return $names }
                    }
                    Start-Sleep -Milliseconds 250
                } while ((Get-Date) -lt $deadline)
                [void](Save-WindowScreenshot 'bookmark-names')
                throw "After $step the bookmarks read '$($names -join ' | ')'; expected '$($patterns -join ' | ')'."
            }
            function Select-Bookmark([string] $pattern) {
                $item = @(Get-BookmarkItems | Where-Object { $_.Current.Name -like $pattern })[0]
                if (-not $item) { throw "No bookmark is named like '$pattern'." }
                $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                $item.SetFocus()
            }
            function Wait-SavedAfter([int] $baseline, [string] $step) {
                $deadline = (Get-Date).AddSeconds(8)
                do {
                    Start-Sleep -Milliseconds 250
                    $saves = Read-Saves
                } while ($saves -le $baseline -and (Get-Date) -lt $deadline)
                if ($saves -le $baseline) { throw "$step saved nothing." }
            }
```

- [ ] **Step 4: The modes.** After `contents-pdf`'s block:

```powershell
            elseif ($Mode -eq 'bookmark-txt') {
                Open-TextGuide 'Sections Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                # Contents first, so the outline is built and the bookmark takes its entry's name.
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-VisibleById 'ReaderContentsTree')
                Wait-ContentsSelected 'Introduction'
                [System.Windows.Forms.SendKeys]::SendWait('{DOWN}{DOWN}{ENTER}')
                [void](Wait-TopRowLike '*Walkthrough: Castle*' 'following Walkthrough: Castle')
                Assert-FocusInGuide 'following an entry'
                [System.Windows.Forms.SendKeys]::SendWait('^d')
                [void](Wait-Status 'Bookmark added.')
                [void](Wait-BookmarkNames @('Walkthrough: Castle, *% through') 'adding a bookmark')
                # One place, one bookmark.
                [System.Windows.Forms.SendKeys]::SendWait('^d')
                [void](Wait-Status 'This place is already bookmarked.')
                [void](Wait-BookmarkNames @('Walkthrough: Castle, *% through') 'adding at the same place')
                # F2 edits in place; Escape cancels without closing the pane.
                [System.Windows.Forms.SendKeys]::SendWait('{F2}')
                [void](Wait-VisibleById 'ReaderBookmarkEditor')
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Wait-HiddenById 'ReaderBookmarkEditor'
                [void](Wait-VisibleById 'ReaderBookmarksList')
                [System.Windows.Forms.SendKeys]::SendWait('{F2}')
                [void](Wait-VisibleById 'ReaderBookmarkEditor')
                [System.Windows.Forms.SendKeys]::SendWait('Castle gate{ENTER}')
                [void](Wait-BookmarkNames @('Castle gate, *% through') 'renaming')
                # Leave the place, then follow the bookmark back: a deliberate move saves.
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Wait-HiddenById 'ReaderBookmarksList'
                Assert-FocusInGuide 'closing the pane'
                [System.Windows.Forms.SendKeys]::SendWait('^{HOME}')
                $deadline = (Get-Date).AddSeconds(10)
                while ((Get-TopRowName) -like '*Walkthrough: Castle*' -and (Get-Date) -lt $deadline) {
                    Start-Sleep -Milliseconds 200
                }
                Start-Sleep -Seconds 2
                $baseline = Read-Saves
                [System.Windows.Forms.SendKeys]::SendWait('^+b')
                [void](Wait-BookmarkNames @('Castle gate, *% through') 'opening the Bookmarks tab')
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                $report.bookmarkTop = Wait-TopRowLike '*Walkthrough: Castle*' 'following a bookmark'
                Assert-FocusInGuide 'following a bookmark'
                Wait-SavedAfter $baseline 'Following a bookmark'
                # Delete, then Undo within 10 s. Wide: the pane stays open.
                Select-Bookmark 'Castle gate*'
                [System.Windows.Forms.SendKeys]::SendWait('{DELETE}')
                [void](Wait-Status 'Bookmark deleted.')
                [void](Wait-VisibleById 'ReaderBookmarksEmpty')
                Invoke-Element (Wait-VisibleById 'ShellStatusAction')
                [void](Wait-BookmarkNames @('Castle gate, *% through') 'undoing the delete')
                $report.bookmarkTxtScreenshot = Save-WindowScreenshot 'bookmark-txt'
                $report.phases += 'bookmark-txt'
            }
            elseif ($Mode -eq 'bookmark-restart') {
                # A new launch on the same data: bookmark-txt's bookmark is still there.
                Open-TextGuide 'Sections Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                [System.Windows.Forms.SendKeys]::SendWait('^+b')
                [void](Wait-BookmarkNames @('Castle gate, *% through') 'reopening after a restart')
                $report.phases += 'bookmark-restart'
            }
            elseif ($Mode -eq 'bookmark-html') {
                # The keyboard is in the page (#115): the toolbar's commands drive the pane.
                Open-TextGuide 'Contents Web Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-PageName 'Contents Web Guide')
                [void](Wait-HtmlPosition { param($p) $p.locator } 'a first capture')
                Invoke-OverflowCommand 'Contents'
                [void](Wait-VisibleById 'ReaderContentsTree')
                Wait-ContentsSelected 'Getting started'
                [System.Windows.Forms.SendKeys]::SendWait('{DOWN}{DOWN}{DOWN}{ENTER}')
                [void](Wait-HtmlPosition { param($p) $p.quote -like 'Castle*' } 'the Castle heading on top')
                Invoke-OverflowCommand 'Add bookmark'
                [void](Wait-Status 'Bookmark added.')
                [void](Wait-BookmarkNames @('The castle*') 'adding from the toolbar')
                # Away to the first section through Contents, then back through Bookmarks.
                Invoke-OverflowCommand 'Contents'
                [void](Wait-VisibleById 'ReaderContentsTree')
                Wait-ContentsSelected 'The castle'
                [System.Windows.Forms.SendKeys]::SendWait('{UP}{UP}{UP}{ENTER}')
                [void](Wait-HtmlPosition { param($p) $p.quote -and $p.quote -notlike 'Castle*' } 'leaving the Castle heading')
                Invoke-OverflowCommand 'Bookmarks'
                [void](Wait-BookmarkNames @('The castle*') 'opening the Bookmarks tab')
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-HtmlPosition { param($p) $p.quote -like 'Castle*' } 'following the bookmark')
                $report.bookmarkHtmlScreenshot = Save-WindowScreenshot 'bookmark-html'
                $report.phases += 'bookmark-html'
            }
            elseif ($Mode -eq 'bookmark-pdf') {
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-PdfPage 1 200 'page 1 of 200')
                [System.Windows.Forms.SendKeys]::SendWait('{PGDN}{PGDN}')
                [void](Wait-PdfPage 3 200 'page 3 of 200')
                # No outline in this guide: the page names the bookmark.
                [System.Windows.Forms.SendKeys]::SendWait('^d')
                [void](Wait-Status 'Bookmark added.')
                [void](Wait-BookmarkNames @('Page 3, page 3') 'adding in a PDF')
                $report.phases += 'bookmark-pdf'
            }
            elseif ($Mode -eq 'bookmark-changed') {
                # Seeded: one bookmark saved against older content, and one damaged.
                Open-TextGuide 'Sections Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                [System.Windows.Forms.SendKeys]::SendWait('^+b')
                [void](Wait-BookmarkNames @('Old place', 'Damaged bookmark') 'opening seeded bookmarks')
                Select-Bookmark 'Old place'
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-Status "This bookmark's place couldn't be found.")
                [void](Wait-BookmarkNames @('Old place', 'Damaged bookmark') 'a place that can''t be found')
                # A damaged bookmark offers only Delete: no rename.
                Select-Bookmark 'Damaged bookmark'
                [System.Windows.Forms.SendKeys]::SendWait('{F2}')
                Start-Sleep -Milliseconds 500
                $editor = Find-ById 'ReaderBookmarkEditor'
                if ($editor -and -not $editor.Current.IsOffscreen) { throw 'A damaged bookmark offered a rename.' }
                $report.bookmarkChangedScreenshot = Save-WindowScreenshot 'bookmark-changed'
                [System.Windows.Forms.SendKeys]::SendWait('{DELETE}')
                [void](Wait-Status 'Bookmark deleted.')
                [void](Wait-BookmarkNames @('Old place') 'deleting the damaged bookmark')
                $report.phases += 'bookmark-changed'
            }
```

  `Find-ById` returns the first match, so a collapsed editor in another
  row may come back. If the damaged-rename check flakes on that, search
  for any `ReaderBookmarkEditor` that is not offscreen, and ledger it.

- [ ] **Step 5: The installer passes.** At the end of
  `Run-NavigationScenarios`:

```powershell
    # T19.1: bookmarks.
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    Invoke-ShellSeed @('clear-bookmarks', $dataRoot) | Out-Null
    $report.bookmarkTxt = Invoke-ProgressPass 'bookmark-txt' 0
    if ($null -eq (& $estimateOf 'Sections Guide')) {
        throw 'Following a bookmark stored no place.'
    }
    $report.bookmarkRestart = Invoke-ProgressPass 'bookmark-restart' 0
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.bookmarkHtml = Invoke-ProgressPass 'bookmark-html' 0 -HtmlPosition
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.bookmarkPdf = Invoke-ProgressPass 'bookmark-pdf' 0
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    Invoke-ShellSeed @('seed-bookmarks', $dataRoot) | Out-Null
    $report.bookmarkChanged = Invoke-ProgressPass 'bookmark-changed' 0
```

- [ ] **Step 6: Check the scripts and the seed tool on the host**

Run: `stage`, then the script parse check (expected: 0 errors for
every script), then
`s 'cd /d E:\work\desktop-guides\t19-1b && dotnet build tools\p1\DesktopGuides.ShellSeed -nologo -warnaserror > E:\work\t19-1b-seed.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" E:\work\t19-1b-seed.log'`
(expected: 0 warnings, 0 errors). Check that the two smoke files stay
ASCII-only:
`LC_ALL=C grep -c -P '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1`
(expected: 0 for both).

- [ ] **Step 7: Commit, push, and run the navigation group on both lanes**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tools/p1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(p2): the installed bookmark modes"
git -C /Users/ilya.lissoboi/work/desktop-guides push -u origin feat/p2-t19-1-bookmarks-tab
```

Then dispatch `shell-scope=navigation` on `msix` and `portable` (see Host
commands).
Expected: both pass, with `bookmark-txt`, `bookmark-restart`,
`bookmark-html`, `bookmark-pdf` and `bookmark-changed` beside the
existing modes. Download the artifacts and look at the three bookmark
screenshots: the tab is selected, the list is readable, and the
narrow-overlay rules don't apply in this 1500 px window. A failure is
debugged under superpowers:systematic-debugging. Never weaken a check to
pass it.

---
### Task 5: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t19-1-bookmarks-design.md` (Status; a "PR b
  verification record")
- Modify: `docs/p2/results.md` (the #126 row)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md`
- Create: `docs/p2/evidence/ci/t19-1/bookmark-txt.png`, `bookmark-html.png`
  and `bookmark-changed.png`, from Task 4's run

- [ ] **Step 1: The full suites and the build.** Run Core (expected:
  1295), Infrastructure (expected: 828), the Production build (expected:
  0 warnings) and the script parse check on the host.
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **The design's Status:** "Approved in conversation, 16 October 2026.
    PR a (storage and backup) merged as #126. PR b (the Bookmarks tab)
    implemented on `feat/p2-t19-1-bookmarks-tab`."
  - **The "PR b verification record":**
    - the tests per task and the suite counts;
    - the navigation dispatches on both lanes;
    - the screenshots;
    - the review, and every ruling.

    It also adds one deviation: the tab has its own "Add bookmark"
    button, beside the toolbar's.
  - **`results.md`:** #126's row: 2026-10-16, `ca7a0e9`, T19.1 (PR a),
    run 38861787184, Core 1275, Infra 828, linking the design's "PR a
    verification record". The note: "Bookmark storage and backup. One
    re-run: the MSIX `design` `completion-reader-light` saw a bar open
    above the Reader (#127), outside this change."
  - **The status pages:** "M9 in progress: T19.1 PR a merged (#126); PR b
    (the Bookmarks tab) in review." The `progress.md` "Next" becomes "T19.2
    TXT reflow".
- [ ] **Step 4: Commit, push, open the PR, and wait for its run.**
  - **Commit:** "docs(p2): record T19.1 PR b".
  - **The PR:** "T19.1 PR b: the Bookmarks tab", based on `main`. It names
    the task, the prerequisite (PR a, merged as #126) and the outcome. It
    includes the three screenshots (this is a UI change), the navigation
    dispatches, every ruling and the deferred minors.
  - **Expected:** every job passes. Record the run in the PR, then stop
    for the user's merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| Contents and Bookmarks tabs in the 300 px pane, with the narrow overlay | 3 |
| Ctrl+Shift+B, and the Bookmarks command; the showing tab's command closes | 2, 3, 4 |
| Ctrl+D, and "Add bookmark" on the toolbar (#115) | 2, 3, 4 |
| One place, one bookmark | 1, 3, 4 |
| The default label: contents entry, the words at the place, "Page N" | 1, 3, 4 |
| Reading order, ties to the older, damaged last | 1, 4 |
| "label, location" for screen readers | 1, 3, 4 |
| Follow: Exact and Context go there; Approximate and Unavailable say so | 1, 3, 4 |
| F2 in place; Escape cancels; an empty label keeps the old one | 3, 4 |
| Delete with a 10 s Undo | 1, 3, 4 |
| A damaged bookmark offers only Delete | 1, 3, 4 |
| The 500 limit and a failed write | 1, 3 |
| The Contents fallback's Bookmarks link | 3 |
| The keyboard map rows and the conflict test | 2 |
| `bookmark-*` modes on both lanes, including a restart and a content change | 4 |
