# T21.2 PR b (Multi-page HTML: reading) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A multi-page HTML guide reads as one guide.
- **Links:** a link to one of its pages opens the imported page.
- **Page commands:** the toolbar's page commands move through the
  reading order.
- **Place:** the saved place remembers its page.
- **Progress:** progress counts across pages.

**Architecture:**
- **Core:**
  - `HtmlGuidePages` maps a guide's pages to their guide-origin addresses
    and their original website addresses;
  - `HtmlNavigationPolicy` classifies a click on one of them as a `Page`
    navigation;
  - `HtmlLocationRules` captures and decodes a place on any page, with
    progress across pages;
  - `ReaderCapabilities.GuidePages` shows the page commands, with keys
    that don't steal Page Up/Down.
- **Infrastructure:** `GetGuidePagesAsync`, and the loader hands the
  pages to the session.
- **Production:**
  - `HtmlReaderSession` keeps a current page and opens another with a new
    top-level navigation, then writes the appearance and restores;
  - the toolbar routes Ctrl+Page Up/Down;
  - the shell checks saved places and bookmarks against the guide's pages.

**Tech Stack:** .NET 10, WinUI 3, WebView2, Microsoft.Data.Sqlite, xUnit,
PowerShell UI Automation.

**Spec:** [t21-2-multipage-html-design.md](t21-2-multipage-html-design.md)
(approved 18 October 2026; PR a merged as #133).

## Global Constraints

- **Pages:** the guide's `GuidePages` rows, in `PageOrder`. Page 0 is the
  entry. A guide with one page behaves exactly as today: no page commands
  and the same keys.
- **A page's guide address:** `GuideWebOrigin.EntryUri(guideId, page.RequestPath)`.
- **Link clicks** (`HtmlNavigationPolicy.Classify`), checked in this order:
  1. the open page's own document: today's rules (first load, then
     fragment-only);
  2. a person's click on a guide page, by its guide address (a relative
     link) or by `SavedPageUrl.Key` of its recorded original address:
     `Page`, with the page's index and the link's fragment;
  3. any other website address: today's `External`;
  4. another path on the guide's origin: today's `Unavailable`.
- **Moving to a page:**
  - the guide's one saved place moves to the new page;
  - the session navigates the WebView to the page's guide address;
  - it waits for the load (the open's 45 s budget), then writes the
    appearance;
  - with a fragment, it navigates to the fragment within the page;
  - finally it raises `LocationChanged`.

  A click on the open page's own address with a fragment only scrolls.
- **The page commands** (the user's choice, 18 October 2026):
  - Previous page and Next page step through the reading order, and stop
    at the first and last pages;
  - Go to start and Go to end open the first and last pages;
  - Ctrl+Page Up and Ctrl+Page Down run Previous page and Next page;
  - plain Page Up/Down and Ctrl+Home/End stay with the page, which
    scrolls.

  The tooltips are "Previous page (Ctrl+Page Up)", "Next page (Ctrl+Page
  Down)", "Go to the first page" and "Go to the last page".
- **The locator:** `documentPath` is the open page's request path, and
  the locator version is unchanged. Decoding accepts any page of the
  guide.
- **Progress:** `EstimatedFraction = (page index + scroll fraction) ÷ page count`,
  for example halfway through page 3 of 4 is (2 + 0.5) ÷ 4 = 0.625.
  `HtmlPosition.ScrollFraction` stays within the page.
- **Restoring:** the session opens the locator's page first if it isn't
  open, then uses today's steps. A page the guide doesn't have is
  `Invalid`, and the guide opens at its first page.
- **Left for PR c:** contents, search and bookmarks across pages. In PR b
  they work on the open page, as today.
- **Shell rules:** no `cd` in local shell commands. PowerShell stays
  ASCII-only and runs under 5.1 and 7.

## Review Focus

1. **A link to the open page by its own website address, with a
   fragment,** such as the first page's "Back to top", must scroll, not
   reload. Pinned by Task 1's `TheOpenPageByItsAddressIsThatPage` and the
   session's same-page rule.
2. **A website link to a page that wasn't saved** keeps the website bar,
   not "not imported". Pinned by Task 1's `AnUnsavedPageOnTheSiteIsExternal`.
3. **Ctrl+Page Down on the last page,** or Ctrl+Page Up on the first, does
   nothing: there's no wrapping. Pinned by Task 1's `StepsStopAtTheEnds`.
4. **A one-page guide** keeps today's toolbar and keys. Pinned by Task 3's
   `OnePageGuidesShowNoPageCommands`.
5. **A saved place on a page the guide doesn't have** is invalid, not a
   crash. Pinned by Task 2's `APlaceOnAnotherPageSetIsInvalid`.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t21-2b) { Remove-Item -Recurse -Force E:\work\desktop-guides\t21-2b }; New-Item -ItemType Directory E:\work\desktop-guides\t21-2b | Out-Null"'
  git -C /Users/ilya.lissoboi/work/desktop-guides ls-files -z -co --exclude-standard | grep -z -v -E '^(\.claude/|az2aws)' | COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --null -cf - -T - | s 'tar -xf - -C E:\work\desktop-guides\t21-2b'
  s 'cd /d E:\work\desktop-guides\t21-2b && python tools\p0\make_fixtures.py > E:\work\t21-2b-fixtures.log 2>&1'
}
t(){ f=""; [ -n "$2" ] && f="--filter \"$2\""; s "cd /d E:\\work\\desktop-guides\\t21-2b && dotnet test tests\\$1 $f -nologo > E:\\work\\t21-2b-test.log 2>&1 & findstr /c:\"Passed!\" /c:\"Failed!\" /c:\"error CS\" /c:\"[FAIL]\" E:\\work\\t21-2b-test.log"; }
build(){ s 'cd /d E:\work\desktop-guides\t21-2b && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t21-2b-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t21-2b-build.log'; }
```

---

### Task 1: Core: the page map and link clicks between pages

**Files:**
- Create: `src/DesktopGuides.Core/Html/HtmlGuidePages.cs`
- Modify: `src/DesktopGuides.Core/Html/HtmlNavigationPolicy.cs` (`Page`, `PageIndex`, `Fragment`, the `pages` parameter)
- Test: `tests/DesktopGuides.Core.Tests/HtmlGuidePagesTests.cs`, `HtmlNavigationPolicyTests.cs`

**Interfaces:**
- Consumes: PR a's `GuidePage` and `SavedPageUrl`.
- Produces:
  - `sealed class HtmlGuidePages(Guid guideId, IReadOnlyList<GuidePage> pages)`, with:
    - `int Count`, `GuidePage this[int index]` and `Uri AddressOf(int index)`;
    - `int? IndexOf(Uri target)`: by guide address or original address;
    - `int? IndexOfDocument(string documentPath)`;
    - `IReadOnlyList<string> DocumentPaths`;
    - `int Step(int current, int delta)`, which clamps;
  - `HtmlNavigationKind.Page`
  - `HtmlNavigation(HtmlNavigationKind Kind, Uri? ExternalUri = null, int? PageIndex = null, string? Fragment = null)`
  - `Classify(string uri, Uri document, bool documentNavigated, bool userInitiated, HtmlGuidePages? pages = null)`

- [ ] **Step 1: Write the failing tests.** Create `HtmlGuidePagesTests.cs`:

```csharp
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
```

In `HtmlNavigationPolicyTests`:

```csharp
    // T21.2: clicks between a guide's pages.
    private static readonly HtmlGuidePages GuidePages = HtmlGuidePagesTests.Pages();
    private static readonly Uri Start = GuidePages.AddressOf(0);

    [Fact]
    public void AClickOnAnotherPageByItsWebsiteAddressOpensIt()
    {
        HtmlNavigation navigation = HtmlNavigationPolicy.Classify(
            "https://guides.example/wiki/Sample_Quest:Chapter_2#top", Start, documentNavigated: true,
            userInitiated: true, GuidePages);

        Assert.Equal((HtmlNavigationKind.Page, 2, "#top"), (navigation.Kind, navigation.PageIndex, navigation.Fragment));
    }

    [Fact]
    public void AClickOnAnotherPageByItsGuideAddressOpensIt() =>
        Assert.Equal(3, HtmlNavigationPolicy.Classify(
            GuidePages.AddressOf(3).AbsoluteUri, Start, true, true, GuidePages).PageIndex);

    // Review Focus 1: the open page's own website address is that page; the session only scrolls.
    [Fact]
    public void TheOpenPageByItsAddressIsThatPage()
    {
        HtmlNavigation navigation = HtmlNavigationPolicy.Classify(
            "https://guides.example/wiki/Sample_Quest#contents", Start, true, true, GuidePages);

        Assert.Equal((HtmlNavigationKind.Page, 0, "#contents"), (navigation.Kind, navigation.PageIndex, navigation.Fragment));
    }

    // Review Focus 2.
    [Fact]
    public void AnUnsavedPageOnTheSiteIsExternal() =>
        Assert.Equal(HtmlNavigationKind.External, HtmlNavigationPolicy.Classify(
            "https://guides.example/wiki/Sample_Quest/Chapter_4", Start, true, true, GuidePages).Kind);

    [Fact]
    public void OnlyAPersonsClickOpensAnotherPage() =>
        Assert.Equal(HtmlNavigationKind.Deny, HtmlNavigationPolicy.Classify(
            GuidePages.AddressOf(1).AbsoluteUri, Start, true, userInitiated: false, GuidePages).Kind);

    [Fact]
    public void TheSessionsOwnMoveToAPageIsItsFirstLoad() =>
        Assert.Equal(HtmlNavigationKind.Entry, HtmlNavigationPolicy.Classify(
            GuidePages.AddressOf(2).AbsoluteUri, GuidePages.AddressOf(2), documentNavigated: false,
            userInitiated: false, GuidePages).Kind);
```

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Core.Tests "FullyQualifiedName~HtmlGuidePages|FullyQualifiedName~HtmlNavigationPolicy"`
Expected: build errors (`HtmlGuidePages` and `HtmlNavigationKind.Page` don't exist).

- [ ] **Step 3: Implement.** Create `HtmlGuidePages.cs`:

```csharp
namespace DesktopGuides.Core.Html;

/// <summary>
/// T21.2: a guide's pages in reading order, found by the address the reader
/// serves them at or by the website address each page recorded.
/// </summary>
public sealed class HtmlGuidePages
{
    private readonly GuidePage[] pages;
    private readonly Uri[] addresses;
    private readonly Dictionary<string, int> byOriginal = new(StringComparer.Ordinal);

    public HtmlGuidePages(Guid guideId, IReadOnlyList<GuidePage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0 || pages.Where((page, index) => page.Order != index).Any())
        {
            throw new ArgumentException("A guide's pages run from its entry, page 0, in order.", nameof(pages));
        }
        this.pages = [.. pages];
        addresses = [.. pages.Select(page => GuideWebOrigin.EntryUri(guideId, page.RequestPath))];
        foreach (GuidePage page in pages)
        {
            if (SavedPageUrl.Parse(page.OriginalUrl) is Uri original)
            {
                byOriginal.TryAdd(SavedPageUrl.Key(original), page.Order);
            }
        }
    }

    public int Count => pages.Length;

    public GuidePage this[int index] => pages[index];

    public IReadOnlyList<string> DocumentPaths => [.. pages.Select(page => page.RequestPath)];

    public Uri AddressOf(int index) => addresses[index];

    public int? IndexOf(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (int index = 0; index < addresses.Length; index++)
        {
            if (HtmlNavigationPolicy.IsEntryDocument(target, addresses[index])) return index;
        }
        return SavedPageUrl.Parse(target.AbsoluteUri) is Uri web &&
            byOriginal.TryGetValue(SavedPageUrl.Key(web), out int found)
                ? found
                : null;
    }

    public int? IndexOfDocument(string documentPath)
    {
        int index = Array.FindIndex(pages, page => string.Equals(page.RequestPath, documentPath, StringComparison.Ordinal));
        return index >= 0 ? index : null;
    }

    // Previous and Next page stop at the first and last pages.
    public int Step(int current, int delta) => Math.Clamp(current + delta, 0, pages.Length - 1);
}
```

In `HtmlNavigationPolicy.cs`:
- the enum becomes `{ Entry, SameDocument, External, Deny, Unavailable, Page }`;
- the record becomes
  `HtmlNavigation(HtmlNavigationKind Kind, Uri? ExternalUri = null, int? PageIndex = null, string? Fragment = null)`;
- `Classify` becomes:

```csharp
    // T21.2: document is the page the view shows, or is about to show. A
    // person's click on another of the guide's pages, by either address,
    // is a Page move the session makes itself.
    public static HtmlNavigation Classify(
        string uri, Uri document, bool documentNavigated, bool userInitiated, HtmlGuidePages? pages = null)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? target)) return Denied;
        if (IsEntryDocument(target, document))
        {
            bool fragment = target.Fragment.Length > 0;
            if (!documentNavigated && !fragment) return new(HtmlNavigationKind.Entry);
            if (documentNavigated && fragment) return new(HtmlNavigationKind.SameDocument);
            return Denied;
        }
        if (pages is not null && userInitiated && target.UserInfo.Length == 0 && pages.IndexOf(target) is int page)
        {
            return new(HtmlNavigationKind.Page, PageIndex: page, Fragment: target.Fragment.Length > 0 ? target.Fragment : null);
        }
        // Only a person's click may raise the bar; redirects and refreshes can't.
        if (target.Scheme is "http" or "https" && userInitiated &&
            target.UserInfo.Length == 0 && !GuideWebOrigin.IsGuideHost(target.Host))
        {
            return new(HtmlNavigationKind.External, target);
        }
        // Another page of this guide's origin wasn't imported. A person is
        // told; anything else is cancelled silently.
        if (target.Scheme is "http" or "https" && userInitiated && target.UserInfo.Length == 0 &&
            string.Equals(target.Host, document.Host, StringComparison.OrdinalIgnoreCase))
        {
            return new(HtmlNavigationKind.Unavailable);
        }
        return Denied;
    }
```

Existing callers pass `entry` and `entryNavigated` positionally, so they
compile unchanged; Task 5 moves the session to the current page.

- [ ] **Step 4: Run them, then the suite**

Run: the Step 2 command. Expected: PASS. Then `t DesktopGuides.Core.Tests`.
Expected: all pass, 1344 + 11.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): a guide's page map, and link clicks between its pages"
```

---
### Task 2: Core: places on any page, and progress across pages

**Files:**
- Modify: `src/DesktopGuides.Core/Reading/ReaderLocationCodec.cs` (a `Deserialize` overload taking the guide's documents)
- Modify: `src/DesktopGuides.Core/Html/HtmlLocationRules.cs` (`Capture` with the page's index and count; `Decode` with the documents)
- Test: `tests/DesktopGuides.Core.Tests/HtmlLocationRulesTests.cs`, `ReaderLocationCodecTests.cs`

**Interfaces:**
- Produces:
  - `ReaderLocationCodec.Deserialize(string? json, GuideFormat expectedFormat, string expectedContentSha256, IReadOnlyCollection<string>? htmlDocuments)`.
    The existing `string? expectedHtmlDocument` overload calls it with
    that one document.
  - `HtmlLocationRules.Capture(string contentSha256, string documentPath, HtmlCapture capture, int pageIndex = 0, int pageCount = 1)`
  - `HtmlLocationRules.Decode(ReaderLocation location, string contentSha256, IReadOnlyCollection<string> documents)`.
    The existing single-document overload calls it.

- [ ] **Step 1: Write the failing tests.** In `HtmlLocationRulesTests`:

```csharp
    // T21.2: progress counts the pages before the open one.
    [Fact]
    public void ProgressCountsAcrossPages()
    {
        ReaderLocation location = HtmlLocationRules.Capture(
            new string('a', 64), "Chapter 2.html", new HtmlCapture(40, "The tower key", null, 0.5), 2, 4);

        Assert.Equal(0.625, location.EstimatedFraction);
        HtmlPosition position = Assert.IsType<HtmlPosition>(location.Payload);
        Assert.Equal(("Chapter 2.html", 0.5), (position.DocumentPath, position.ScrollFraction));
    }

    [Fact]
    public void APlaceOnAnyPageOfTheGuideDecodes()
    {
        string sha = new('a', 64);
        ReaderLocation location = HtmlLocationRules.Capture(sha, "Chapter 2.html", new HtmlCapture(40, "q", null, 0.5), 2, 4);

        LocationDecodeResult decoded = HtmlLocationRules.Decode(location, sha, ["Start.html", "Chapter 1.html", "Chapter 2.html"]);

        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
    }

    // Review Focus 5.
    [Fact]
    public void APlaceOnAnotherPageSetIsInvalid()
    {
        string sha = new('a', 64);
        ReaderLocation location = HtmlLocationRules.Capture(sha, "Elsewhere.html", new HtmlCapture(0, null, null, 0));

        Assert.Equal(LocationDecodeStatus.Invalid,
            HtmlLocationRules.Decode(location, sha, ["Start.html", "Chapter 1.html"]).Status);
    }
```

In `ReaderLocationCodecTests`:

```csharp
    [Fact]
    public void AnHtmlPlaceNeedsTheGuidesDocuments()
    {
        string sha = new('a', 64);
        string json = ReaderLocationCodec.Serialize(new ReaderLocation(
            GuideFormat.Html, ReaderLocationCodec.CurrentVersion, sha,
            new HtmlPosition("Chapter 1.html", null, null, 0, 0), 0));

        Assert.Equal(LocationDecodeStatus.Valid,
            ReaderLocationCodec.Deserialize(json, GuideFormat.Html, sha, (IReadOnlyCollection<string>)["Start.html", "Chapter 1.html"]).Status);
        Assert.Equal(LocationDecodeStatus.Invalid,
            ReaderLocationCodec.Deserialize(json, GuideFormat.Html, sha, (IReadOnlyCollection<string>?)null).Status);
    }
```

Match `HtmlCapture`'s constructor (offset, quote, id, fraction) and the
existing usings in each test file.

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Core.Tests "FullyQualifiedName~ProgressCountsAcrossPages|FullyQualifiedName~APlaceOn|FullyQualifiedName~TheGuidesDocuments"`
Expected: build errors (no such overloads).

- [ ] **Step 3: Implement.**
  - **`ReaderLocationCodec`:** rename the body's check. The four-argument
    method keeps its signature and calls the new overload:

```csharp
    public static LocationDecodeResult Deserialize(
        string? json,
        GuideFormat expectedFormat,
        string expectedContentSha256,
        string? expectedHtmlDocument = null) =>
        Deserialize(json, expectedFormat, expectedContentSha256,
            expectedHtmlDocument is null ? null : (IReadOnlyCollection<string>)[expectedHtmlDocument]);

    // T21.2: an HTML place is valid on any of the guide's documents.
    public static LocationDecodeResult Deserialize(
        string? json,
        GuideFormat expectedFormat,
        string expectedContentSha256,
        IReadOnlyCollection<string>? htmlDocuments)
```

    The old body moves into the new overload. Its document check becomes:

```csharp
            if (format == GuideFormat.Html &&
                (htmlDocuments is null ||
                 !htmlDocuments.Any(document =>
                     ManagedRelativePath.Parse(document) == ((HtmlPosition)position).DocumentPath)))
            {
                return new(LocationDecodeStatus.Invalid, null);
            }
```

  - **`HtmlLocationRules.Capture`:** gains `int pageIndex = 0, int pageCount = 1`.
    The estimate is
    `pageCount > 1 ? (pageIndex + capture.Fraction) / pageCount : capture.Fraction`,
    and `ScrollFraction` stays `capture.Fraction`. Throw
    `ArgumentOutOfRangeException` unless `0 <= pageIndex < pageCount`.
  - **`HtmlLocationRules.Decode`:** add the documents overload, and have
    the string overload call it with `[documentPath]`.

- [ ] **Step 4: Run them, then the suite**

Run: the Step 2 command. Expected: PASS. Then `t DesktopGuides.Core.Tests`.
Expected: all pass, Task 1's count + 4.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): an HTML place on any of a guide's pages, with progress across them"
```

---
### Task 3: Core: the page commands and their keys

**Files:**
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs` (`GuidePages`, `VisibleCommands(ReaderCapabilities)`, `PageKeyRouting`)
- Modify: `src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs` (two rows)
- Test: `tests/DesktopGuides.Core.Tests/ReaderCommandPolicyTests.cs` (create, or add to the existing contract tests), `KeyboardShortcutsTests.cs`

**Interfaces:**
- Produces:
  - `ReaderCapabilities.GuidePages = 1024`
  - `ReaderCommandPolicy.VisibleCommands(ReaderCapabilities capabilities)`.
    The session overload calls it. `PageTurn` and `PageEdge` show for
    `PageNavigation` or `GuidePages`.
  - `enum PageKey { PageUp, PageDown, Home, End }` and
    `static PageKeyCommand? PageKeyRouting.For(ReaderCapabilities capabilities, PageKey key, bool control)`,
    with `enum PageKeyCommand { PreviousPage, NextPage, PageStart, PageEnd }`
  - `ShortcutCommand.PreviousGuidePage` and `NextGuidePage`

- [ ] **Step 1: Write the failing tests.** Create `ReaderCommandPolicyTests.cs`
  (if a test class for `ReaderCommandPolicy` already exists, add these to
  it instead):

```csharp
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ReaderCommandPolicyTests
{
    private const ReaderCapabilities Html =
        ReaderCapabilities.Scroll | ReaderCapabilities.TextSize | ReaderCapabilities.Find | ReaderCapabilities.Outline;

    // Review Focus 4.
    [Fact]
    public void OnePageGuidesShowNoPageCommands()
    {
        IReadOnlyList<ReaderCommand> commands = ReaderCommandPolicy.VisibleCommands(Html);

        Assert.DoesNotContain(ReaderCommand.PageTurn, commands);
        Assert.DoesNotContain(ReaderCommand.PageEdge, commands);
    }

    [Fact]
    public void AGuideOfSeveralPagesShowsThemOnce()
    {
        IReadOnlyList<ReaderCommand> commands = ReaderCommandPolicy.VisibleCommands(Html | ReaderCapabilities.GuidePages);

        Assert.Single(commands, command => command == ReaderCommand.PageTurn);
        Assert.Single(commands, command => command == ReaderCommand.PageEdge);
    }

    [Theory]
    [InlineData(PageKey.PageUp, false, PageKeyCommand.PreviousPage)]
    [InlineData(PageKey.PageDown, false, PageKeyCommand.NextPage)]
    [InlineData(PageKey.Home, true, PageKeyCommand.PageStart)]
    [InlineData(PageKey.End, true, PageKeyCommand.PageEnd)]
    [InlineData(PageKey.PageDown, true, null)]
    public void TxtAndPdfPageWithPageKeys(PageKey key, bool control, PageKeyCommand? expected) =>
        Assert.Equal(expected, PageKeyRouting.For(ReaderCapabilities.PageNavigation, key, control));

    // The user's choice (18 October 2026): the page scrolls with its own keys.
    [Theory]
    [InlineData(PageKey.PageUp, true, PageKeyCommand.PreviousPage)]
    [InlineData(PageKey.PageDown, true, PageKeyCommand.NextPage)]
    [InlineData(PageKey.PageUp, false, null)]
    [InlineData(PageKey.PageDown, false, null)]
    [InlineData(PageKey.Home, true, null)]
    [InlineData(PageKey.End, true, null)]
    public void AGuideOfSeveralPagesLeavesPageKeysToThePage(PageKey key, bool control, PageKeyCommand? expected) =>
        Assert.Equal(expected, PageKeyRouting.For(Html | ReaderCapabilities.GuidePages, key, control));

    [Fact]
    public void WithoutPagesNoKeyPages() =>
        Assert.Null(PageKeyRouting.For(Html, PageKey.PageDown, false));
}
```

`KeyboardShortcutsTests.EveryCommandAppearsOnce` and
`NoTwoShortcutsShareKeysWhereBothCanRun` cover the two new rows. They fail
once the enum has the values and the table doesn't.

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Core.Tests "FullyQualifiedName~ReaderCommandPolicyTests|FullyQualifiedName~KeyboardShortcutsTests"`
Expected: build errors (`GuidePages`, `PageKeyRouting` don't exist).

- [ ] **Step 3: Implement.** In `ReaderContract.cs`:
  - add `// T21.2: a web guide of several pages; its page commands move between them.`
    and `GuidePages = 1024` to `ReaderCapabilities`;
  - add `(ReaderCommand.PageTurn, ReaderCapabilities.GuidePages)` and
    `(ReaderCommand.PageEdge, ReaderCapabilities.GuidePages)` to
    `Commands`;
  - add the overloads:

```csharp
    public static IReadOnlyList<ReaderCommand> VisibleCommands(IReaderSession session) =>
        VisibleCommands(session.Capabilities);

    public static IReadOnlyList<ReaderCommand> VisibleCommands(ReaderCapabilities capabilities) =>
        Commands
            .Where(entry => (capabilities & entry.Required) == entry.Required)
            .Select(entry => entry.Command)
            .Distinct()
            .ToArray();
```

and, below `ReaderCommandPolicy`:

```csharp
public enum PageKey { PageUp, PageDown, Home, End }

public enum PageKeyCommand { PreviousPage, NextPage, PageStart, PageEnd }

/// <summary>
/// T21.2: TXT and PDF page with Page Up/Down and Ctrl+Home/End. A web guide
/// of several pages leaves those to the page, which scrolls, and moves
/// between its pages with Ctrl+Page Up/Down (the user's choice).
/// </summary>
public static class PageKeyRouting
{
    public static PageKeyCommand? For(ReaderCapabilities capabilities, PageKey key, bool control)
    {
        if ((capabilities & ReaderCapabilities.GuidePages) != 0)
        {
            return (key, control) switch
            {
                (PageKey.PageUp, true) => PageKeyCommand.PreviousPage,
                (PageKey.PageDown, true) => PageKeyCommand.NextPage,
                _ => null
            };
        }
        if ((capabilities & ReaderCapabilities.PageNavigation) == 0) return null;
        return (key, control) switch
        {
            (PageKey.PageUp, false) => PageKeyCommand.PreviousPage,
            (PageKey.PageDown, false) => PageKeyCommand.NextPage,
            (PageKey.Home, true) => PageKeyCommand.PageStart,
            (PageKey.End, true) => PageKeyCommand.PageEnd,
            _ => null
        };
    }
}
```

In `KeyboardShortcuts.cs`:
- add `PreviousGuidePage, NextGuidePage` to `ShortcutCommand`, after `PageEnd`;
- add these rows after `Ctrl+End`:

```csharp
        new(ShortcutContext.Reader, "Ctrl+Page Up", ShortcutCommand.PreviousGuidePage, "Previous page (web guides of several pages)"),
        new(ShortcutContext.Reader, "Ctrl+Page Down", ShortcutCommand.NextGuidePage, "Next page (web guides of several pages)"),
```

- [ ] **Step 4: Run them, then the suite**

Run: the Step 2 command. Expected: PASS. Then `t DesktopGuides.Core.Tests`.
Expected: all pass, Task 2's count + 14.
- **If a Settings or shortcuts test pins the number of rows**, it now
  counts 2 more. Update the literal and ledger it as a ruling.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): page commands for a web guide of several pages, on Ctrl+Page Up/Down"
```

---
### Task 4: Infrastructure: the pages for the reader

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (`GetGuidePagesAsync`)
- Modify: `src/DesktopGuides.Infrastructure/Reading/ManagedHtmlGuideLoader.cs` (`HtmlGuideLoaded.Pages`, the checks)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedHtmlGuideLoaderTests.cs`

**Interfaces:**
- Consumes: Task 1's `HtmlGuidePages`.
- Produces:
  - `Task<IReadOnlyList<GuidePage>> GetGuidePagesAsync(Guid guideId, CancellationToken token = default)`,
    in `PageOrder`;
  - `HtmlGuideLoaded(HtmlRequestPolicy Policy, ManagedHtmlAssetReader Reader, string EntryFilePath, HtmlGuidePages Pages)`.
- **The loader's checks:** the guide has page rows; page 0 is the
  `EntryHtml` asset; and every page names an `EntryHtml` or `Page`
  asset. Anything else is `HtmlGuideLoadError.Changed`, as a damaged
  entry is today.

- [ ] **Step 1: Write the failing tests.** In `ManagedHtmlGuideLoaderTests`:

```csharp
    [Fact]
    public async Task LoadsAMultipageGuideWithItsPagesInOrder()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        P2Fixtures.CopyTree("html-multipage", harness.Sources.Root);
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(
            Path.Combine(harness.Sources.Root, "Sample Quest - Guides Example.html")));
        Guide guide = (await harness.Repository.GetGuideAsync(id))!;

        HtmlGuideLoaded loaded = Assert.IsType<HtmlGuideLoaded>(await LoadAsync(harness, guide));

        Assert.Equal(
            ["Sample Quest - Guides Example.html", "Chapter 1 - Guides Example.html",
                "Chapter 2 - Guides Example.html", "Chapter 3 - Guides Example.html"],
            loaded.Pages.DocumentPaths);
        Assert.IsType<HtmlServe>(loaded.Policy.Decide("GET", loaded.Pages.AddressOf(2).AbsoluteUri));
    }

    [Fact]
    public async Task AOnePageGuideHasItsEntryAsItsPage()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);

        HtmlGuideLoaded loaded = Assert.IsType<HtmlGuideLoaded>(await LoadAsync(harness, guide));

        Assert.Equal(["guide.html"], loaded.Pages.DocumentPaths);
    }

    [Fact]
    public async Task APageRowOnAStylesheetIsChanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishAsync(harness);
        using (SqliteConnection connection = new($"Data Source={harness.Paths.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText =
                $"INSERT INTO GuidePages (GuideId, PageOrder, RequestPath) VALUES ('{guide.Id:N}', 1, 'styles/main.css')";
            insert.ExecuteNonQuery();
        }

        Assert.Equal(HtmlGuideLoadError.Changed, await FailedAsync(harness, guide));
    }
```

Add `using Microsoft.Data.Sqlite;` to the test file.

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Infrastructure.Tests "FullyQualifiedName~ManagedHtmlGuideLoaderTests"`
Expected: build errors (`HtmlGuideLoaded.Pages` doesn't exist).

- [ ] **Step 3: Implement.** In `SqliteLibraryRepository`, beside `GetGuideAssetsAsync`:

```csharp
    // T21.2: an HTML guide's pages in reading order; page 0 is the entry.
    public Task<IReadOnlyList<GuidePage>> GetGuidePagesAsync(Guid guideId, CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<GuidePage>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT PageOrder, RequestPath, OriginalUrl, Title
                FROM GuidePages WHERE GuideId = $id ORDER BY PageOrder
                """;
            command.Parameters.AddWithValue("$id", guideId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            List<GuidePage> pages = [];
            while (reader.Read())
            {
                pages.Add(new GuidePage(
                    reader.GetInt32(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
            return pages;
        }, token);
```

In `ManagedHtmlGuideLoader`:
- `HtmlGuideLoaded` gains `HtmlGuidePages Pages`;
- after the entry check, read and check the pages:

```csharp
        // T21.2: the pages start at the entry, and each one is a page file.
        IReadOnlyList<GuidePage> rows = await repository.GetGuidePagesAsync(guide.Id, token);
        Dictionary<string, GuideAsset> byRequest = assets.ToDictionary(asset => asset.RequestPath, StringComparer.Ordinal);
        if (rows.Count == 0 ||
            rows[0].RequestPath != entries[0].RequestPath ||
            rows.Any(row => !byRequest.TryGetValue(row.RequestPath, out GuideAsset? asset) ||
                asset.Kind is not (GuideAssetKind.EntryHtml or GuideAssetKind.Page)))
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        }
        HtmlGuidePages pages;
        try
        {
            pages = new HtmlGuidePages(guide.Id, rows);
        }
        catch (ArgumentException)
        {
            return new HtmlGuideLoadFailed(HtmlGuideLoadError.Changed);
        }
```

- the success return passes `pages`.

Update every construction of `HtmlGuideLoaded` (the Production shell and
any test fakes) to pass the pages. Search for `new HtmlGuideLoaded(`.

- [ ] **Step 4: Run them, then the suite and the build**

Run: the Step 2 command. Expected: PASS. Then
`t DesktopGuides.Infrastructure.Tests` and `build`. Expected: all pass,
869 + 3; 0 warnings. The build passes once Task 5 consumes `Pages`; if it
doesn't compile yet because a Production fake constructs
`HtmlGuideLoaded`, fix that construction here.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src tests/DesktopGuides.Infrastructure.Tests
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): the reader loads an HTML guide's pages"
```

---
### Task 5: Production: moving between pages in the reader

**Files:**
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs`
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml.cs` (keys and tooltips)
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs`, `ShellWindow.Progress.cs`, `ShellWindow.Contents.cs`, `ShellWindow.Bookmarks.cs` (the guide's documents)

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces: no new public types. This task is verified by the build here
  and by Task 6's installed modes. The WebView2 session has no unit tests.

- [ ] **Step 1: The session's current page.** In `HtmlReaderSession`:
  - **The fields:** add these, and set them in the constructor from
    `loaded.Pages`:

```csharp
    // T21.2: the guide's pages and the one the view shows. document is
    // that page's guide address; documentNavigated is entryNavigated's
    // meaning for whichever page is open.
    private readonly HtmlGuidePages pages;
    private int pageIndex;
    private Uri document;
```

```csharp
        pages = loaded.Pages;
        document = pages.AddressOf(0);
```

  - **Renames:** rename `entryNavigated` to `documentNavigated` throughout.
  - **Replacements, then:**
    - every `policy.EntryUri` becomes `document`;
    - every `policy.Entry.RequestPath` becomes `pages[pageIndex].RequestPath`;
    - in `OnWebResourceRequested`:

```csharp
                // The open page's own document decides the open's outcome.
                bool isEntry = serve.Asset.Kind is GuideAssetKind.EntryHtml or GuideAssetKind.Page &&
                    string.Equals(serve.Asset.RequestPath, pages[pageIndex].RequestPath, StringComparison.Ordinal);
```

  - **The `Classify` calls** (navigation starting, new window) pass
    `document, documentNavigated, ..., pages`.
  - **`GetLocationAsync` and `WritePositionForTest`** call
    `HtmlLocationRules.Capture(contentSha256 ?? string.Empty, pages[pageIndex].RequestPath, current ?? Start, pageIndex, pages.Count)`.
  - **`Capabilities`:**

```csharp
    public ReaderCapabilities Capabilities =>
        ReaderCapabilities.Scroll | ReaderCapabilities.TextSize | ReaderCapabilities.Find |
        ReaderCapabilities.Outline | (pages.Count > 1 ? ReaderCapabilities.GuidePages : ReaderCapabilities.None);
```

- [ ] **Step 2: Moving to a page.** Add:

```csharp
    // T21.2: opens another of the guide's pages with a new top-level
    // navigation, writes the appearance, and shows the fragment or the top.
    // It takes the restore turn, so the tracker and restores wait for it.
    // A move to the open page with a fragment only scrolls.
    private async Task<bool> GoToPageAsync(int index, string? fragment)
    {
        if (disposed || failed || core is null || index < 0 || index >= pages.Count) return false;
        if (index == pageIndex)
        {
            if (fragment is not null) core.Navigate(document.AbsoluteUri + fragment);
            return true;
        }
        await restoreTurn.WaitAsync();
        restoring = true;
        try
        {
            pageIndex = index;
            document = pages.AddressOf(index);
            documentNavigated = false;
            applied = null;
            current = Start;
            lastScroll = null;
            ResetFind();
            if (!await NavigateDocumentAsync())
            {
                if (!disposed)
                {
                    Failed?.Invoke(this, HtmlGuideLoadMessages.ForFailedOpen(entry, failed));
                }
                return false;
            }
            await WriteAppearanceAsync();
            if (fragment is not null) core.Navigate(document.AbsoluteUri + fragment);
            current = await CaptureAsync() ?? Start;
            WritePositionForTest();
            RaiseLocationChanged();
            return true;
        }
        finally
        {
            restoring = false;
            restoreTurn.Release();
        }
    }

    // The open's wait, for a later page: the view must load and serve the
    // page within the open's budget.
    private async Task<bool> NavigateDocumentAsync()
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) =>
            completion.TrySetResult(args.IsSuccess);
        core!.NavigationCompleted += Completed;
        try
        {
            pendingNavigation = completion;
            core.Navigate(document.AbsoluteUri);
            return await completion.Task.WaitAsync(EntryNavigationTimeout) && entry == HtmlEntryState.Served;
        }
        catch (Exception error) when (error is TimeoutException or COMException)
        {
            return false;
        }
        finally
        {
            pendingNavigation = null;
            if (!disposed && !failed) core.NavigationCompleted -= Completed;
        }
    }
```

Add `using System.Runtime.InteropServices;` if the file lacks it.

- [ ] **Step 3: Links, commands and restore.**
  - **In `OnNavigationStarting`'s switch:**

```csharp
            case HtmlNavigationKind.Page:
                args.Cancel = true;
                _ = GoToPageAsync(navigation.PageIndex!.Value, navigation.Fragment);
                break;
```

  - **In `OnNewWindowRequested`:** add
    `else if (navigation.Kind == HtmlNavigationKind.Page) _ = GoToPageAsync(navigation.PageIndex!.Value, navigation.Fragment);`.
  - **`ExecuteAsync`:**

```csharp
    public async Task ExecuteAsync(ReaderAction action, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action)
        {
            case TextSizeAction size:
                await ApplyAppearanceAsync(appearance with { TextScale = size.Scale }, token);
                break;
            // T21.2: in a guide of several pages, the page commands move between its pages.
            case PageTurnAction turn when pages.Count > 1:
                await GoToPageAsync(pages.Step(pageIndex, turn.Delta), null);
                break;
            case PageEdgeAction edge when pages.Count > 1:
                await GoToPageAsync(edge.Edge == ReaderEdge.Start ? 0 : pages.Count - 1, null);
                break;
            default:
                throw new NotSupportedException($"HTML guides don't support {action.Command}.");
        }
    }
```

  - **`RestoreLocationAsync`:** decode against every page, and open the
    place's page first:

```csharp
        LocationDecodeResult decoded = HtmlLocationRules.Decode(location, contentSha256, pages.DocumentPaths);
        if (decoded.Location?.Payload is HtmlPosition position &&
            pages.IndexOfDocument(position.DocumentPath) is int page && page != pageIndex &&
            !await GoToPageAsync(page, null))
        {
            return new RestoreOutcome(RestoreKind.Unavailable, HtmlLocationRules.UnavailableReason);
        }
        return await RestoreAsync(decoded);
```

- [ ] **Step 4: The toolbar.** In `ReaderToolbar.xaml.cs`:
  - add `(VirtualKey.PageUp, Ctrl)` and `(VirtualKey.PageDown, Ctrl)` to
    `KeyTable`;
  - route page keys through Core:

```csharp
    private AppBarButton? CommandFor(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        // T21.2: which page keys page depends on the session.
        PageKey? page = key switch
        {
            VirtualKey.PageUp => PageKey.PageUp,
            VirtualKey.PageDown => PageKey.PageDown,
            VirtualKey.Home => PageKey.Home,
            VirtualKey.End => PageKey.End,
            _ => null
        };
        if (page is PageKey pageKey)
        {
            if (modifiers is not (VirtualKeyModifiers.None or Ctrl)) return null;
            return PageKeyRouting.For(session?.Capabilities ?? ReaderCapabilities.None, pageKey, modifiers == Ctrl) switch
            {
                PageKeyCommand.PreviousPage => PreviousPage,
                PageKeyCommand.NextPage => NextPage,
                PageKeyCommand.PageStart => PageStart,
                PageKeyCommand.PageEnd => PageEnd,
                _ => null
            };
        }
        return (key, modifiers) switch
        {
            (VirtualKey.G, Ctrl) => GoToPage,
            (VirtualKey.Add or EqualsKey, Ctrl) => ZoomOr(ZoomIn, LargerText),
            (EqualsKey, Ctrl | VirtualKeyModifiers.Shift) => ZoomOr(ZoomIn, LargerText),
            (VirtualKey.Subtract or MinusKey, Ctrl) => ZoomOr(ZoomOut, SmallerText),
            (VirtualKey.Number0 or VirtualKey.NumberPad0, Ctrl) => ZoomOr(FitToWidth, ResetTextSize),
            _ => null
        };
    }
```

  - **The tooltips:** in the constructor, keep the four page buttons' XAML
    tooltips. In `RefreshCommands`, for a session with `GuidePages`, set:
    - `PreviousPage`: "Previous page (Ctrl+Page Up)";
    - `NextPage`: "Next page (Ctrl+Page Down)";
    - `PageStart`: "Go to the first page";
    - `PageEnd`: "Go to the last page".

    Otherwise put the kept tooltips back.

```csharp
    private readonly object?[] pageTips;
    // in the constructor, after InitializeComponent():
    pageTips = [.. new[] { PageStart, PreviousPage, NextPage, PageEnd }.Select(ToolTipService.GetToolTip)];
```

```csharp
        bool guidePages = session is not null && (session.Capabilities & ReaderCapabilities.GuidePages) != 0;
        string?[] guideTips = ["Go to the first page", "Previous page (Ctrl+Page Up)", "Next page (Ctrl+Page Down)", "Go to the last page"];
        AppBarButton[] pageButtons = [PageStart, PreviousPage, NextPage, PageEnd];
        for (int index = 0; index < pageButtons.Length; index++)
        {
            ToolTipService.SetToolTip(pageButtons[index], guidePages ? guideTips[index] : pageTips[index]);
        }
```

  If the shell sets these buttons' tooltips or accelerator keys elsewhere
  (search `ShowShortcut(` for `PreviousPage`), apply the same rule there.

- [ ] **Step 5: The shell passes the guide's documents.**
  - **The type change:** in `ShellWindow.Progress.cs`, `ShellWindow.Contents.cs`
    and `ShellWindow.Bookmarks.cs`, the `string? htmlEntry` parameters and
    the `bookmarkEntry` field become
    `IReadOnlyCollection<string>? htmlDocuments` and `bookmarkDocuments`.
    Their `ReaderLocationCodec.Deserialize` calls pass them, which picks
    Task 2's overload.
  - **The source:** in `ShellWindow.HtmlReader.cs`, the call that passed
    `loaded.Policy.Entry.RequestPath` passes `loaded.Pages.DocumentPaths`.
  - **TXT and PDF** pass `null`, as before. Cast any bare `null` argument
    to `(IReadOnlyCollection<string>?)null` where the compiler finds the
    overloads ambiguous.

- [ ] **Step 6: Build**

Run: `stage && build`. Expected: `0 Warning(s)`, `0 Error(s)`. Then
`t DesktopGuides.Core.Tests` and `t DesktopGuides.Infrastructure.Tests`.
Expected: both pass.

Also build the toolbar smoke project, which compiles `ReaderToolbar`:
`s 'cd /d E:\work\desktop-guides\t21-2b && dotnet build tools\p1\DesktopGuides.ReaderToolbarSmoke -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t21-2b-tb.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" E:\work\t21-2b-tb.log'`.
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): move between a web guide's pages by its links and page commands"
```

---
### Task 6: The installed modes

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`seed-find` adds "Pages Web Guide")
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`Read-HtmlPosition`'s `document`; four modes)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-NavigationScenarios`; `Invoke-ProgressPass -HtmlDiagnostics`)

**Interfaces:**
- Consumes: Task 5's reader and PR a's fixture.
- Produces:
  - **`html-pages-links`:**
    - a link opens the imported page;
    - the toolbar and Ctrl+Page Up/Down move through the reading order;
    - plain Page Down stays on the page;
    - an unsaved page's link raises the website bar.
  - **`html-pages-style`:** each page's `load.php` stylesheet is served,
    checked from the session diagnostics.
  - **`html-pages-restart` and `html-pages-reopen`:** a place on page 3
    returns after a restart, with progress between 0.5 and 0.75.

- [ ] **Step 1: The seed.** In `Program.cs`'s `seed-find`, after the
  html-find import:

```csharp
    // T21.2: the multi-page fixture, for the html-pages modes.
    string pagesFixture = Path.Combine(Path.GetFullPath(args[2]), "p2", "html-multipage", "Sample Quest - Guides Example.html");
    if (await new GuideImportValidator().InspectAsync(pagesFixture, CancellationToken.None) is not ImportReady pagesReady)
    {
        throw new InvalidOperationException("The html-multipage fixture failed the import preview.");
    }
    await findPublisher.PublishAsync(
        pagesReady.Manifest, findGame.Id, "Pages Web Guide", false, null, CancellationToken.None);
```

- [ ] **Step 2: The modes.** In `windows_shell_ui_smoke.ps1`:
  - **The mode list:** add `'html-pages-links', 'html-pages-style', 'html-pages-restart', 'html-pages-reopen'`
    to the `ValidateSet`, beside `'contents-html'`.
  - **`Read-HtmlPosition`:** add `document = $null` to `$position`, and
    `$position.document = [string] $payload.documentPath` beside the
    offset.
  - **The branches,** beside `contents-html`, sharing its helpers:

```powershell
            elseif ($Mode -eq 'html-pages-links') {
                # T21.2: links and the page commands move between the guide's pages.
                Open-TextGuide 'Pages Web Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Sample Quest - Guides Example.html' } 'the first page')
                # A link by the page's website address opens the imported copy.
                Click-Element (Wait-PageVisible 'Chapter 1')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Chapter 1 - Guides Example.html' } 'chapter 1 by its link')
                Click-Element (Wait-PageVisible 'On to chapter 3')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Chapter 3 - Guides Example.html' } 'chapter 3 by its link')
                # The toolbar follows the reading order, not the links.
                Invoke-Element (Find-ByName 'Previous page')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Chapter 2 - Guides Example.html' } 'chapter 2 before chapter 3')
                [System.Windows.Forms.SendKeys]::SendWait('^{PGUP}')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Chapter 1 - Guides Example.html' } 'chapter 1 by Ctrl+Page Up')
                # Plain Page Down scrolls the page; it never changes page.
                [System.Windows.Forms.SendKeys]::SendWait('{PGDN}')
                Start-Sleep -Seconds 1
                if ((Read-HtmlPosition).document -ne 'Chapter 1 - Guides Example.html') { throw 'Page Down changed page.' }
                Invoke-Element (Find-ByName 'Go to end')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Chapter 3 - Guides Example.html' } 'the last page')
                [System.Windows.Forms.SendKeys]::SendWait('^{PGDN}')
                Start-Sleep -Seconds 1
                if ((Read-HtmlPosition).document -ne 'Chapter 3 - Guides Example.html') { throw 'Ctrl+Page Down on the last page moved.' }
                Invoke-Element (Find-ByName 'Go to start')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Sample Quest - Guides Example.html' } 'the first page again')
                # A page that wasn't saved stays on the website.
                Click-Element (Wait-PageVisible 'Chapter 4, not saved')
                [void](Wait-VisibleById 'ReaderExternalLinkBar')
                $report.htmlPagesLinksScreenshot = Save-WindowScreenshot 'html-pages-links'
                $report.phases += 'html-pages-links'
            }
            elseif ($Mode -eq 'html-pages-style') {
                # T21.2: the install side checks each page's load.php was served.
                Open-TextGuide 'Pages Web Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Sample Quest - Guides Example.html' } 'the first page')
                [System.Windows.Forms.SendKeys]::SendWait('^{PGDN}')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Chapter 1 - Guides Example.html' } 'chapter 1')
                $report.htmlPagesStyleScreenshot = Save-WindowScreenshot 'html-pages-style'
                $report.phases += 'html-pages-style'
            }
            elseif ($Mode -eq 'html-pages-restart') {
                Open-TextGuide 'Pages Web Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Sample Quest - Guides Example.html' } 'the first page')
                $baseline = Read-Saves
                [System.Windows.Forms.SendKeys]::SendWait('^{PGDN}')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Chapter 1 - Guides Example.html' } 'chapter 1')
                [System.Windows.Forms.SendKeys]::SendWait('^{PGDN}')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Chapter 2 - Guides Example.html' } 'chapter 2')
                $deadline = (Get-Date).AddSeconds(8)
                do {
                    Start-Sleep -Milliseconds 250
                    $saves = Read-Saves
                } while ($saves -le $baseline -and (Get-Date) -lt $deadline)
                if ($saves -le $baseline) { throw 'Moving to page 3 saved no place.' }
                $report.phases += 'html-pages-restart'
            }
            elseif ($Mode -eq 'html-pages-reopen') {
                Open-TextGuide 'Pages Web Guide'
                [void](Wait-Status 'Guide ready.')
                [void](Wait-HtmlPosition { param($p) $p.document -eq 'Chapter 2 - Guides Example.html' -and $p.kind -eq 'Exact' } 'chapter 2 restored')
                $report.htmlPagesReopenScreenshot = Save-WindowScreenshot 'html-pages-reopen'
                $report.phases += 'html-pages-reopen'
            }
```

  Check the names this assumes against `contents-html`'s code and adjust
  them if they differ:
  - the helpers: `Open-TextGuide`, `Wait-PageVisible`, `Click-Element`,
    `Find-ByName`, `Read-Saves`;
  - the toolbar buttons' UI Automation names: "Previous page",
    "Go to end", "Go to start".

  Each mode must close the guide (`Back-ToTextGame` or as `contents-html`
  ends), so the session writes its diagnostics.

- [ ] **Step 3: The install side.** In `windows_shell_install.ps1`:
  - **The new switch:** `Invoke-ProgressPass` gains `[switch] $HtmlDiagnostics`,
    which adds `'HtmlDiagnostics'` to `$names`.
  - **In `Run-NavigationScenarios`,** after the HTML contents passes:

```powershell
    # T21.2: a web guide of several pages.
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.htmlPagesLinks = Invoke-ProgressPass 'html-pages-links' 0 -HtmlPosition
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    Remove-Item -Path (Join-Path $diagnostics 'html-session-*.json') -ErrorAction SilentlyContinue
    $report.htmlPagesStyle = Invoke-ProgressPass 'html-pages-style' 0 -HtmlPosition -HtmlDiagnostics
    $served = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'html-session-*.json' |
        ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).served })
    foreach ($sheet in @('Sample Quest - Guides Example_files/load.php', 'Chapter 1 - Guides Example_files/load.php')) {
        if ($served -notcontains $sheet) { throw "The page's stylesheet $sheet was not served." }
    }
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.htmlPagesRestart = Invoke-ProgressPass 'html-pages-restart' 0 -HtmlPosition
    $estimate = & $estimateOf 'Pages Web Guide'
    if ($null -eq $estimate -or $estimate -lt 0.5 -or $estimate -gt 0.75) {
        throw "A place on page 3 of 4 stored progress $estimate; expected 0.5 to 0.75."
    }
    $report.htmlPagesReopen = Invoke-ProgressPass 'html-pages-reopen' 0 -HtmlPosition
```

  Check the session file's served property name (`served`) against
  `HtmlSessionDiagnosticsJson`.

Run: the ASCII check
(`LC_ALL=C grep -c -P '[^\x00-\x7F]'` on both scripts, expected `0`),
the parse check, and the seed build, as in PR a.

- [ ] **Step 4: Commit, push, and run the navigation group on both lanes**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tools/p1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(p2): the html-pages reading modes"
git -C /Users/ilya.lissoboi/work/desktop-guides push -u origin feat/p2-t21-2-multipage-reading
for lane in msix portable; do gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p2-t21-2-multipage-reading -f shell-scope=navigation -f shell-lane=$lane; sleep 3; done
```

Expected: both pass, with the four `html-pages-*` reports. Copy
`html-pages-links.png` and `html-pages-reopen.png` from the MSIX run to
`docs/p2/evidence/ci/t21-2/`.

---
### Task 7: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t21-2-multipage-html-design.md` (Status; a "PR b verification record")
- Modify: `docs/p2/results.md` (#133's row)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md`

- [ ] **Step 1: The full suites and the builds.** Run Core,
  Infrastructure, the Production build, the toolbar smoke build and the
  parse check on the host.
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **The design's Status:** "PR a merged as #133. PR b (reading)
    implemented on `feat/p2-t21-2-multipage-reading`."
  - **The "PR b verification record":** the tests, the navigation runs,
    the screenshots, the review and every ruling. That includes the
    user's key choice, recorded as deviation 5.
  - **`results.md`:** #133's row: 2026-10-18, `59810e0`, T21.2 (PR a), run
    39092396556, Core 1344, Infra 869, linking the design's "PR a
    verification record". The note: "Multi-page HTML import, storage and
    backup. Passed on the first attempt."
  - **The status pages:** "T21.2 PR a merged (#133); PR b (reading) in
    review." The `progress.md` "Next" becomes "T21.2 PR c (contents,
    search and bookmarks across pages)".
- [ ] **Step 4: Commit, push, open the PR, and wait for its run.**
  - **Commit:** "docs(p2): record T21.2 PR b".
  - **The PR:** "T21.2 PR b: read a multi-page HTML guide", based on
    `main`. It names the task, the prerequisite (PR a, merged as #133) and
    the outcome. It includes the screenshots, the dispatches, every ruling
    and the deferred minors.
  - **Expected:** every job passes. Record the run, then stop for the
    user's merge decision.

## Deviations from the design

1. **The page keys** (the user's choice, 18 October 2026):
   - Page Up/Down and Ctrl+Home/End keep scrolling the open page;
   - Ctrl+Page Up/Down move between pages;
   - the four buttons follow the reading order.

   The design gave the guide "the toolbar's page commands" without saying
   which keys.
2. **The installed modes** add `html-pages-reopen`, the second launch of
   `html-pages-restart`.
3. **The page being left isn't captured first.** A guide has one saved
   place, and it moves to the new page, so capturing the old page would
   only make a save that the move overwrites at once.

## Traceability

| Design item | Task |
| --- | --- |
| Serving every page from the guide's origin | 4, 5 |
| A link to a guide page, by original address or relative path, opens it; others keep today's bar | 1, 5, 6 |
| The place moves to the new page, at its fragment or top (deviation 3) | 5, 6 |
| The page commands for a guide of several pages; one-page guides unchanged | 3, 5, 6 |
| Text size and theme on every page | 5 (the appearance is rewritten per page) |
| The locator holds the page; decode accepts any page; restore opens it | 2, 5, 6 |
| Progress: (index + fraction) ÷ count | 2, 6 |
| `html-pages-links`, `html-pages-style`, `html-pages-restart` | 6 |
