# T21.2 PR a (Multi-page HTML: import, storage and backup) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A reader picks one saved web page, and the import brings in the
guide's other saved pages from the same folder, in reading order. Each page
keeps its own stylesheets, including MediaWiki's `load.php` ones. The
library stores the pages, and backup and restore carry them. In this PR a
multi-page guide opens at its first page; PR b makes its links work.

**Architecture:**
- **Core** gets `SavedPageUrl` (a saved page's recorded address, and the
  key two addresses of one page share), `PageTitles` (the shared title
  suffix removed), `GuidePage`, and `GuideAssetKind.Page`.
  `HtmlRequestPolicy` serves files by kind, not extension.
- **The scanner** (`StaticHtmlDependencyScanner`):
  - recognises stylesheets by role;
  - applies its budgets per page;
  - finds the guide's other pages through a new
    `IStaticHtmlPageDirectory`, which `RootedStaticHtmlAssetSource`
    implements.
- **Storage** moves to schema 7: `GuideAssets.Kind` gains `'Page'`, and a
  new `GuidePages` table holds the order, address and title. The archive
  plan checks the pages.
- **Production** shows the pages in the import preview.

**Tech Stack:** .NET 10, AngleSharp, Microsoft.Data.Sqlite, xUnit, WinUI 3,
PowerShell UI Automation.

**Spec:** [t21-2-multipage-html-design.md](t21-2-multipage-html-design.md)
(approved 18 October 2026).

## Global Constraints

- **A page's recorded address:**
  - first, the `<!-- saved from url=(NNNN)URL -->` comment within the
    file's first 1,024 characters, where NNNN is the address's length;
  - otherwise the first `<link rel="canonical">` within the first
    256 KiB;
  - only absolute `http` and `https` addresses count.
- **The page key:** host, non-default port, path and query, unescaped
  (`UriFormat.Unescaped`). The scheme, the host's case and the fragment
  don't count.
- **Candidates:**
  - top-level `.html` and `.htm` files in the selected page's folder,
    other than the selected page, whose names are safe managed paths;
  - indexed in ordinal name order, at most 2,000 of them, with the first
    claim on a key winning;
  - only addresses on the first page's host are indexed or followed.
- **Order:** breadth-first from the first page. Each page's `<a href>`
  links are taken in document order. A relative link resolves against the
  page's address, or matches a candidate's file name.
- **Stylesheets by role:** a file reached through
  `<link rel="stylesheet">` or `@import`:
  - **named `.css`:** behaves exactly as today;
  - **any other name:** must contain no NUL byte, its first
    non-whitespace character must not be `<`, and it must decode with
    `CssTextDecoder`. Otherwise it's an `Invalid` reference, skipped with
    the warning "A linked style sheet isn't readable CSS and will be
    skipped."
- **Limits:**
  - **per page:** `MaxEntryBytes` for the page file, and `MaxAssets`,
    `MaxReferences`, `MaxCssRules`, the CSS variable budget and
    `MaxTotalBytes` for the page's files;
  - **per guide:** `MaxPages = 200`, `MaxGuideBytes = 1 GiB` and
    `MaxGuideAssets = 16384`;
  - `<a href>` links never count as references.
- **Serving by kind:** `EntryHtml` and `Page` are `text/html`, and
  `StyleSheet` is `text/css`. An `Image` keeps today's extension rule.
- **Schema 7:**

```sql
CREATE TABLE GuideAssets7 (
    GuideId TEXT NOT NULL REFERENCES Guides(Id) ON DELETE CASCADE,
    RequestPath TEXT NOT NULL,
    RelativePath TEXT NOT NULL,
    Kind TEXT NOT NULL CHECK (Kind IN ('EntryHtml', 'Page', 'StyleSheet', 'Image')),
    ByteCount INTEGER NOT NULL CHECK (ByteCount >= 0),
    Sha256 TEXT NOT NULL CHECK (length(Sha256) = 64),
    PRIMARY KEY (GuideId, RequestPath)
);
INSERT INTO GuideAssets7 SELECT GuideId, RequestPath, RelativePath, Kind, ByteCount, Sha256 FROM GuideAssets;
DROP TABLE GuideAssets;
ALTER TABLE GuideAssets7 RENAME TO GuideAssets;
CREATE TABLE GuidePages (
    GuideId TEXT NOT NULL REFERENCES Guides(Id) ON DELETE CASCADE,
    PageOrder INTEGER NOT NULL CHECK (PageOrder >= 0),
    RequestPath TEXT NOT NULL,
    OriginalUrl TEXT CHECK (OriginalUrl IS NULL OR length(OriginalUrl) BETWEEN 1 AND 2048),
    Title TEXT CHECK (Title IS NULL OR length(Title) BETWEEN 1 AND 512),
    PRIMARY KEY (GuideId, PageOrder),
    UNIQUE (GuideId, RequestPath),
    FOREIGN KEY (GuideId, RequestPath) REFERENCES GuideAssets(GuideId, RequestPath) ON DELETE CASCADE
);
INSERT INTO GuidePages (GuideId, PageOrder, RequestPath)
    SELECT GuideId, 0, RequestPath FROM GuideAssets WHERE Kind = 'EntryHtml';
PRAGMA user_version = 7;
```

- **Every HTML guide has at least one `GuidePages` row.** Order 0 is the
  `EntryHtml` asset at `PrimaryRelativePath`; later orders are `Page`
  assets.
- **Titles and addresses are stored as recorded,** with the shared suffix
  removed only for display. A missing title shows the file name without
  its extension.
- **The fixture is authored:** made-up text and a made-up host,
  `guides.example`. No real page is copied (P2 rule 5).
- **Shell rules:** no `cd` in local shell commands. PowerShell stays
  ASCII-only and runs under 5.1 and 7.

## Review Focus

1. **A page with thousands of links** (Neoseeker's has 9,350) must import.
   Links are kept apart from the reference budget. Pinned by Task 2's
   `ManyLinksDontUseTheReferenceBudget`.
2. **A page saved twice** ("Chapter 1.html" and "Chapter 1 (1).html")
   joins once: the first name in ordinal order wins. Pinned by Task 2's
   `TwoSavesOfOnePageJoinOnce`.
3. **Forty pages that each carry the site's 3,000-rule stylesheet** must
   not trip the 10,000-rule budget. Pinned by Task 2's
   `BudgetsApplyPerPage`.
4. **A saved page whose name has `%`** never breaks the import. It isn't
   a candidate, so a link to it acts as unsaved. Pinned by Task 2's
   `AnUnsafePageNameIsNotACandidate` and by the fixture in Task 4.
5. **A single saved page** keeps today's preview and publish exactly,
   plus one page row. Pinned by Task 2's `ASinglePageHasOnePage` and by
   the existing publisher tests running unchanged.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t21-2a) { Remove-Item -Recurse -Force E:\work\desktop-guides\t21-2a }; New-Item -ItemType Directory E:\work\desktop-guides\t21-2a | Out-Null"'
  git -C /Users/ilya.lissoboi/work/desktop-guides ls-files -z -co --exclude-standard | grep -z -v -E '^(\.claude/|az2aws)' | COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --null -cf - -T - | s 'tar -xf - -C E:\work\desktop-guides\t21-2a'
  s 'cd /d E:\work\desktop-guides\t21-2a && python tools\p0\make_fixtures.py > E:\work\t21-2a-fixtures.log 2>&1'
}
t(){ f=""; [ -n "$2" ] && f="--filter \"$2\""; s "cd /d E:\\work\\desktop-guides\\t21-2a && dotnet test tests\\$1 $f -nologo > E:\\work\\t21-2a-test.log 2>&1 & findstr /c:\"Passed!\" /c:\"Failed!\" /c:\"error CS\" /c:\"[FAIL]\" E:\\work\\t21-2a-test.log"; }
build(){ s 'cd /d E:\work\desktop-guides\t21-2a && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t21-2a-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t21-2a-build.log'; }
```

`t DesktopGuides.Core.Tests "FullyQualifiedName~SavedPageUrl"` runs one
class; `t DesktopGuides.Core.Tests` runs the suite. Stage before each run.

---

### Task 1: Core: addresses, titles, pages and serving by kind

**Files:**
- Create: `src/DesktopGuides.Core/Html/SavedPageUrl.cs`
- Create: `src/DesktopGuides.Core/Html/GuidePage.cs` (`GuidePage`, `PageTitles`)
- Modify: `src/DesktopGuides.Core/Html/GuideAsset.cs` (`GuideAssetKind.Page`)
- Modify: `src/DesktopGuides.Core/Html/HtmlRequestPolicy.cs` (`ContentTypeFor`)
- Modify: `src/DesktopGuides.Core/Import/ImportPresentation.cs` (`PageCount`, `PageList`)
- Test: `tests/DesktopGuides.Core.Tests/SavedPageUrlTests.cs`, `PageTitlesTests.cs`, `HtmlRequestPolicyTests.cs`, `ImportPresentationTests.cs`

**Interfaces:**
- Produces:
  - `static Uri? SavedPageUrl.FromComment(string prefix)`,
    `static Uri? SavedPageUrl.Parse(string? value)`,
    `static string SavedPageUrl.Key(Uri uri)` and
    `static bool SavedPageUrl.SameHost(Uri a, Uri b)`
  - `sealed record GuidePage(int Order, string RequestPath, string? OriginalUrl, string? Title)`
  - `static IReadOnlyList<string> PageTitles.WithoutSharedSuffix(IReadOnlyList<string> titles)`
  - `GuideAssetKind.Page`
  - `static string ImportPresentation.PageCount(int count)` and
    `static string ImportPresentation.PageList(IReadOnlyList<string> titles)`

- [ ] **Step 1: Write the failing tests.** Create `SavedPageUrlTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class SavedPageUrlTests
{
    [Fact]
    public void TheCommentGivesTheAddressItsLengthNames() =>
        Assert.Equal(new Uri("https://guides.example/wiki/Sample_Quest"), SavedPageUrl.FromComment(
            "<!DOCTYPE html>\n<!-- saved from url=(0040)https://guides.example/wiki/Sample_Quest -->\n<html>"));

    [Theory]
    [InlineData("<html><head>")]
    [InlineData("<!-- saved from url=(0099)https://guides.example/short -->")]
    [InlineData("<!-- saved from url=(abcd)https://guides.example/ -->")]
    [InlineData("<!-- saved from url=(0016)file:///C:/x.htm -->")]
    public void ABrokenOrMissingCommentGivesNoAddress(string prefix) =>
        Assert.Null(SavedPageUrl.FromComment(prefix));

    [Theory]
    [InlineData("mailto:someone@guides.example")]
    [InlineData("/wiki/Relative")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyWebAddressesParse(string? value) => Assert.Null(SavedPageUrl.Parse(value));

    [Fact]
    public void AddressesOfOnePageShareAKey()
    {
        string key = SavedPageUrl.Key(new Uri("https://guides.example/wiki/Sample_Quest:Chapter_2"));

        Assert.Equal(key, SavedPageUrl.Key(new Uri("http://Guides.Example/wiki/Sample_Quest%3AChapter_2#top")));
        Assert.Equal(key, SavedPageUrl.Key(new Uri("https://guides.example:443/wiki/Sample_Quest:Chapter_2")));
    }

    [Fact]
    public void ThePathCaseAndTheQueryStillCount()
    {
        string key = SavedPageUrl.Key(new Uri("https://guides.example/faqs/1?page=2"));

        Assert.NotEqual(key, SavedPageUrl.Key(new Uri("https://guides.example/faqs/1?page=3")));
        Assert.NotEqual(key, SavedPageUrl.Key(new Uri("https://guides.example/FAQS/1?page=2")));
        Assert.NotEqual(key, SavedPageUrl.Key(new Uri("https://guides.example:8443/faqs/1?page=2")));
    }

    [Fact]
    public void HostsCompareWithoutCase()
    {
        Assert.True(SavedPageUrl.SameHost(new Uri("https://Guides.Example/a"), new Uri("http://guides.example/b")));
        Assert.False(SavedPageUrl.SameHost(new Uri("https://guides.example/a"), new Uri("https://other.example/a")));
    }
}
```

Create `PageTitlesTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PageTitlesTests
{
    [Fact]
    public void TheSuffixEveryPageSharesIsRemoved() =>
        Assert.Equal(["Sample Quest", "Chapter 1", "Chapter 2"], PageTitles.WithoutSharedSuffix(
            ["Sample Quest — Guides Example", "Chapter 1 — Guides Example", "Chapter 2 — Guides Example"]));

    [Fact]
    public void TheCutStartsAtASeparatorNotInsideAWord() =>
        Assert.Equal(["Maps", "Bosses"], PageTitles.WithoutSharedSuffix(
            ["Maps — StrategyWiki | Strategy guide", "Bosses — StrategyWiki | Strategy guide"]));

    [Fact]
    public void WithoutASharedSeparatorTitlesStayWhole() =>
        Assert.Equal(["Walkthrough", "Breakthrough"], PageTitles.WithoutSharedSuffix(["Walkthrough", "Breakthrough"]));

    [Fact]
    public void OnePageKeepsItsWholeTitle() =>
        Assert.Equal(["Sample Quest — Guides Example"], PageTitles.WithoutSharedSuffix(["Sample Quest — Guides Example"]));

    [Fact]
    public void ACutThatWouldEmptyATitleIsntMade() =>
        Assert.Equal([" | Site", "Map | Site"], PageTitles.WithoutSharedSuffix([" | Site", "Map | Site"]));
}
```

In `HtmlRequestPolicyTests`, beside `UnsupportedFileTypesAreDenied`:

```csharp
    // T21.2: what the import recorded decides the type, not the file name.
    [Fact]
    public void FilesAreServedByTheirKind()
    {
        HtmlRequestPolicy policy = new(Id,
        [
            .. Assets,
            new GuideAsset("Guide_files/load.php", "Guide_files/load.php", GuideAssetKind.StyleSheet, 10, Hash),
            new GuideAsset("Chapter 1.htm", "Chapter 1.htm", GuideAssetKind.Page, 10, Hash),
            new GuideAsset("Guide_files/icon.php", "Guide_files/icon.php", GuideAssetKind.Image, 10, Hash),
        ]);

        Assert.Equal("text/css", Assert.IsType<HtmlServe>(policy.Decide("GET", Origin + "/Guide_files/load.php")).ContentType);
        Assert.Equal("text/html", Assert.IsType<HtmlServe>(policy.Decide("GET", Origin + "/Chapter%201.htm")).ContentType);
        Assert.Equal(HtmlDenyReason.UnsupportedType,
            Assert.IsType<HtmlDeny>(policy.Decide("GET", Origin + "/Guide_files/icon.php")).Reason);
    }
```

In `ImportPresentationTests`:

```csharp
    [Fact]
    public void PagesAreCountedAndListedInOrder()
    {
        Assert.Equal("1 page", ImportPresentation.PageCount(1));
        Assert.Equal("4 pages", ImportPresentation.PageCount(4));
        Assert.Equal("1. Sample Quest\n2. Chapter 1", ImportPresentation.PageList(["Sample Quest", "Chapter 1"]));
    }

    [Fact]
    public void ALongPageListShowsTwentyAndCountsTheRest()
    {
        string[] titles = Enumerable.Range(1, 23).Select(n => $"Page {n}").ToArray();

        string[] lines = ImportPresentation.PageList(titles).Split('\n');

        Assert.Equal(21, lines.Length);
        Assert.Equal("20. Page 20", lines[19]);
        Assert.Equal("and 3 more", lines[20]);
    }
```

Check `HtmlServe`'s and `HtmlDeny`'s member names (`ContentType`,
`Reason`) in `HtmlRequestPolicy.cs` and match them.

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Core.Tests "FullyQualifiedName~SavedPageUrl|FullyQualifiedName~PageTitles|FullyQualifiedName~FilesAreServedByTheirKind|FullyQualifiedName~Pages"`
Expected: build errors (`SavedPageUrl`, `PageTitles`, `GuideAssetKind.Page`, `PageCount` don't exist).

- [ ] **Step 3: Implement.** Create `SavedPageUrl.cs`:

```csharp
using System.Globalization;

namespace DesktopGuides.Core.Html;

// T21.2: the address a browser recorded when it saved a page, and the key
// two addresses of one page share.
public static class SavedPageUrl
{
    private const string Marker = "<!-- saved from url=(";

    // Edge and Chrome write "<!-- saved from url=(NNNN)URL -->" at the top
    // of a saved page; NNNN is the address's length.
    public static Uri? FromComment(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        int start = prefix.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        int digits = start + Marker.Length;
        int close = prefix.IndexOf(')', digits);
        if (close - digits is < 1 or > 5 ||
            !int.TryParse(prefix.AsSpan(digits, close - digits), NumberStyles.None, CultureInfo.InvariantCulture, out int length) ||
            length < 1 || close + 1 + length > prefix.Length)
        {
            return null;
        }
        return Parse(prefix.Substring(close + 1, length));
    }

    // Only an absolute http or https address names a page.
    public static Uri? Parse(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" && uri.Host.Length > 0
            ? uri
            : null;

    // The scheme, the host's case, the fragment and escaping don't count.
    public static string Key(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.GetComponents(
            UriComponents.Host | UriComponents.Port | UriComponents.PathAndQuery, UriFormat.Unescaped);
    }

    public static bool SameHost(Uri a, Uri b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase);
    }
}
```

`UriComponents.Port` omits a scheme's default port, so `https://x:443/a`
and `http://x/a` share a key. Create `GuidePage.cs`:

```csharp
namespace DesktopGuides.Core.Html;

/// <summary>
/// T21.2: one page of an HTML guide, in reading order. Order 0 is the entry
/// page; a one-page guide has only it. The address and title are what the
/// saved page recorded, when it did.
/// </summary>
public sealed record GuidePage(int Order, string RequestPath, string? OriginalUrl, string? Title);

public static class PageTitles
{
    private static readonly string[] Separators = [" - ", " – ", " — ", " | ", " · ", " _ ", " :: "];

    // Removes the suffix every title shares, such as " — StrategyWiki | …".
    // The cut starts at a separator, so a word is never split; one page, or
    // a cut that would empty a title, keeps the titles whole.
    public static IReadOnlyList<string> WithoutSharedSuffix(IReadOnlyList<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        if (titles.Count < 2) return titles;
        string first = titles[0];
        int common = first.Length;
        foreach (string title in titles.Skip(1))
        {
            int length = 0;
            while (length < common && length < title.Length && first[^(length + 1)] == title[^(length + 1)])
            {
                length++;
            }
            common = length;
        }
        string shared = first[^common..];
        int cut = Separators
            .Select(separator => shared.IndexOf(separator, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (cut < 0) return titles;
        int remove = common - cut;
        string[] trimmed = titles.Select(title => title[..^remove].TrimEnd()).ToArray();
        return trimmed.Any(title => title.Length == 0) ? titles : trimmed;
    }
}
```

In `GuideAsset.cs`, add `Page` after `EntryHtml` with the comment
`// T21.2: a guide page after the first.` In `HtmlRequestPolicy.cs`, change
`ContentTypeFor(asset.RelativePath)` to `ContentTypeFor(asset)` and replace
the method:

```csharp
    // T21.2: the kind the import recorded decides the type, so a MediaWiki
    // load.php stylesheet is CSS. An image still needs a known image type.
    // No charset: legacy guides keep their own meta charset or BOM.
    private static string? ContentTypeFor(GuideAsset asset) => asset.Kind switch
    {
        GuideAssetKind.EntryHtml or GuideAssetKind.Page => "text/html",
        GuideAssetKind.StyleSheet => "text/css",
        GuideAssetKind.Image => Path.GetExtension(asset.RelativePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null
        },
        _ => null
    };
```

In `ImportPresentation.cs`:

```csharp
    // T21.2: a multi-page guide's pages, counted and listed in reading order.
    public const int ShownPages = 20;

    public static string PageCount(int count) =>
        count == 1 ? "1 page" : string.Create(CultureInfo.CurrentCulture, $"{count:N0} pages");

    public static string PageList(IReadOnlyList<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        List<string> lines = titles.Take(ShownPages).Select((title, index) => $"{index + 1}. {title}").ToList();
        if (titles.Count > ShownPages)
        {
            lines.Add(string.Create(CultureInfo.CurrentCulture, $"and {titles.Count - ShownPages:N0} more"));
        }
        return string.Join('\n', lines);
    }
```

Add `using System.Globalization;` if the file lacks it.

- [ ] **Step 4: Run them, then the suite**

Run: the Step 2 command. Expected: PASS. Then `t DesktopGuides.Core.Tests`.
Expected: all pass, 1324 + 20.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): saved page addresses, page titles, and serving by kind"
```

---
### Task 2: The scanner: stylesheets by role, budgets per page, linked pages

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Import/StaticHtmlManifest.cs`
- Modify: `src/DesktopGuides.Infrastructure/Import/StaticHtmlDependencyScanner.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/StaticHtmlDependencyScannerTests.cs`

**Interfaces:**
- Consumes: Task 1's `SavedPageUrl`.
- Produces:
  - `StaticAssetKind.Page` and `StaticReferenceStatus.Invalid`
  - `StaticScanLimit.PageCount`, `GuideBytes` and `GuideAssetCount`
  - `StaticHtmlScanLimits(..., int MaxPages = 200, long MaxGuideBytes = 1024L * 1024 * 1024, int MaxGuideAssets = 16384)`
  - `sealed record StaticPage(string RelativePath, string RequestRelativePath, string? OriginalUrl, string? Title)`
  - `StaticHtmlManifest(Assets, References, TotalBytes, IReadOnlyList<StaticPage> Pages)`, with `Pages[0]` the entry
  - `public interface IStaticHtmlPageDirectory { IReadOnlyList<string> ListPages(); }`

- [ ] **Step 1: Write the failing tests.** In
  `StaticHtmlDependencyScannerTests`, add a folder source beside
  `MemorySource`:

```csharp
    // A source that also lists its top-level pages, as a saved folder does.
    private sealed class MemoryFolder(params (string Path, string Text)[] files)
        : IStaticHtmlAssetSource, IStaticHtmlPageDirectory
    {
        private readonly MemorySource source = new(files);
        private readonly string[] pages = files
            .Select(file => file.Path)
            .Where(path => !path.Contains('/') &&
                (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        public List<string> Reads => source.Reads;

        public IReadOnlyList<string> ListPages() => pages;

        public ValueTask<Stream?> OpenReadAsync(string safeRelativePath, CancellationToken cancellationToken = default) =>
            source.OpenReadAsync(safeRelativePath, cancellationToken);
    }

    private static string SavedPage(string url, string title, string body) =>
        $"<!DOCTYPE html>\n<!-- saved from url=({url.Length:0000}){url} -->\n" +
        $"<html><head><title>{title}</title></head><body>{body}</body></html>";
```

Then the tests:

```csharp
    [Fact]
    public async Task AStyleSheetLinkedByRoleIsIncludedWhateverItsName()
    {
        MemorySource source = new(
            ("guide.html", """<link rel="stylesheet" href="Guide_files/load.php?lang=en">"""),
            ("Guide_files/load.php", """@import "load(2).php"; .title { background: url(icon.png) }"""),
            ("Guide_files/load(2).php", ".body { color: #333 }"),
            ("Guide_files/icon.png", "icon"));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner().ScanAsync("guide.html", source);

        Assert.Equal(
            [("Guide_files/icon.png", StaticAssetKind.Image), ("Guide_files/load(2).php", StaticAssetKind.StyleSheet),
                ("Guide_files/load.php", StaticAssetKind.StyleSheet), ("guide.html", StaticAssetKind.EntryHtml)],
            manifest.Assets.Select(asset => (asset.RelativePath, asset.Kind)));
        Assert.All(manifest.References, reference => Assert.Equal(StaticReferenceStatus.Included, reference.Status));
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>Not found</body></html>")]
    [InlineData("  \n<html>")]
    [InlineData("body { color: red }\0\0")]
    public async Task AStyleSheetThatIsntCssIsSkipped(string content)
    {
        MemorySource source = new(
            ("guide.html", """<link rel="stylesheet" href="load.php">"""),
            ("load.php", content));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner().ScanAsync("guide.html", source);

        Assert.Equal(["guide.html"], manifest.Assets.Select(asset => asset.RelativePath));
        StaticAssetReference reference = Assert.Single(manifest.References);
        Assert.Equal((StaticReferenceStatus.Invalid, StaticAssetKind.StyleSheet), (reference.Status, reference.ExpectedKind));
        Assert.Equal(manifest.Assets.Sum(asset => asset.ByteCount), manifest.TotalBytes);
    }

    [Fact]
    public async Task SavedPagesJoinInBreadthFirstLinkOrder()
    {
        MemoryFolder folder = new(
            ("Start.html", SavedPage("https://guides.example/wiki/Quest", "Quest — Guides", """
                <a href="https://guides.example/wiki/Quest/One">One</a>
                <a href="https://Guides.Example/wiki/Quest%3ATwo#top">Two</a>
                <a href="https://guides.example/wiki/Quest/Unsaved">Unsaved</a>
                <a href="https://other.example/wiki/Elsewhere">Elsewhere</a>
                """)),
            ("One.html", SavedPage("https://guides.example/wiki/Quest/One", "One — Guides", """
                <a href="https://guides.example/wiki/Quest/Three">Three</a>
                <a href="https://guides.example/wiki/Quest">Back</a>
                <link rel="stylesheet" href="One_files/load.php">
                """)),
            ("One_files/load.php", ".one { color: blue }"),
            ("Two.html", """
                <html><head><title>Two — Guides</title>
                <link rel="canonical" href="https://guides.example/wiki/Quest:Two"></head>
                <body><a href="https://guides.example/wiki/Quest">Back</a></body></html>
                """),
            ("Three.html", SavedPage("https://guides.example/wiki/Quest/Three", "Three — Guides",
                """<a href="One.html">Relative</a>""")),
            ("Elsewhere.html", SavedPage("https://other.example/wiki/Elsewhere", "Elsewhere", "")),
            ("Loose.html", SavedPage("https://guides.example/wiki/Loose", "Loose", "")));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner().ScanAsync("Start.html", folder);

        Assert.Equal(
            [("Start.html", "https://guides.example/wiki/Quest", "Quest — Guides"),
                ("One.html", "https://guides.example/wiki/Quest/One", "One — Guides"),
                ("Two.html", "https://guides.example/wiki/Quest:Two", "Two — Guides"),
                ("Three.html", "https://guides.example/wiki/Quest/Three", "Three — Guides")],
            manifest.Pages.Select(page => (page.RelativePath, page.OriginalUrl, page.Title)));
        Assert.Equal(
            [StaticAssetKind.Page, StaticAssetKind.StyleSheet, StaticAssetKind.EntryHtml, StaticAssetKind.Page, StaticAssetKind.Page],
            manifest.Assets.Select(asset => asset.Kind));
        Assert.DoesNotContain(manifest.Assets, asset => asset.RelativePath is "Elsewhere.html" or "Loose.html");
    }

    [Fact]
    public async Task WithoutARecordedAddressThePageStandsAlone()
    {
        MemoryFolder folder = new(
            ("Start.html", """<a href="https://guides.example/wiki/Quest/One">One</a>"""),
            ("One.html", SavedPage("https://guides.example/wiki/Quest/One", "One", "")));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner().ScanAsync("Start.html", folder);

        Assert.Equal(["Start.html"], manifest.Pages.Select(page => page.RelativePath));
        Assert.DoesNotContain("One.html", folder.Reads);
    }

    // Review Focus 5: a plain source stays one page, with its title.
    [Fact]
    public async Task ASinglePageHasOnePage()
    {
        MemorySource source = new(("guide.html", "<title>Lone Guide</title><p>Text</p>"));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner().ScanAsync("guide.html", source);

        StaticPage page = Assert.Single(manifest.Pages);
        Assert.Equal(("guide.html", "guide.html", null, "Lone Guide"),
            (page.RelativePath, page.RequestRelativePath, page.OriginalUrl, page.Title));
    }

    // Review Focus 2.
    [Fact]
    public async Task TwoSavesOfOnePageJoinOnce()
    {
        MemoryFolder folder = new(
            ("Start.html", SavedPage("https://guides.example/q", "Start", """<a href="https://guides.example/q/1">1</a>""")),
            ("One (1).html", SavedPage("https://guides.example/q/1", "One again", "")),
            ("One.html", SavedPage("https://guides.example/q/1", "One", "")));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner().ScanAsync("Start.html", folder);

        Assert.Equal(["Start.html", "One (1).html"], manifest.Pages.Select(page => page.RelativePath));
    }

    // Review Focus 4: '%' isn't a safe managed name, so the page is never opened.
    [Fact]
    public async Task AnUnsafePageNameIsNotACandidate()
    {
        MemoryFolder folder = new(
            ("Start.html", SavedPage("https://guides.example/q", "Start", """<a href="https://guides.example/q/half">Half</a>""")),
            ("50% Done.html", SavedPage("https://guides.example/q/half", "Half", "")));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner().ScanAsync("Start.html", folder);

        Assert.Equal(["Start.html"], manifest.Pages.Select(page => page.RelativePath));
        Assert.DoesNotContain("50% Done.html", folder.Reads);
    }

    // Review Focus 1.
    [Fact]
    public async Task ManyLinksDontUseTheReferenceBudget()
    {
        string links = string.Concat(Enumerable.Range(0, 50).Select(n => $"<a href=\"https://guides.example/q/{n}\">{n}</a>"));
        MemoryFolder folder = new(("Start.html", SavedPage("https://guides.example/q", "Start", links)));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner(new StaticHtmlScanLimits(MaxReferences: 5))
            .ScanAsync("Start.html", folder);

        Assert.Single(manifest.Pages);
    }

    // Review Focus 3: each page's stylesheet fits the rule budget on its own.
    [Fact]
    public async Task BudgetsApplyPerPage()
    {
        string rules = string.Concat(Enumerable.Range(0, 8).Select(n => $".r{n} {{ color: red }}"));
        MemoryFolder folder = new(
            ("Start.html", SavedPage("https://guides.example/q", "Start",
                """<link rel="stylesheet" href="a.css"><a href="https://guides.example/q/1">1</a><a href="https://guides.example/q/2">2</a>""")),
            ("a.css", rules),
            ("One.html", SavedPage("https://guides.example/q/1", "One", """<link rel="stylesheet" href="b.css">""")),
            ("b.css", rules),
            ("Two.html", SavedPage("https://guides.example/q/2", "Two", """<link rel="stylesheet" href="c.css">""")),
            ("c.css", rules));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner(new StaticHtmlScanLimits(MaxCssRules: 10))
            .ScanAsync("Start.html", folder);

        Assert.Equal(3, manifest.Pages.Count);
    }

    [Theory]
    [InlineData(StaticScanLimit.PageCount)]
    [InlineData(StaticScanLimit.GuideBytes)]
    [InlineData(StaticScanLimit.GuideAssetCount)]
    public async Task GuideLimitsStopTheScan(StaticScanLimit limit)
    {
        MemoryFolder folder = new(
            ("Start.html", SavedPage("https://guides.example/q", "Start",
                """<a href="https://guides.example/q/1">1</a><a href="https://guides.example/q/2">2</a>""")),
            ("One.html", SavedPage("https://guides.example/q/1", "One", "")),
            ("Two.html", SavedPage("https://guides.example/q/2", "Two", "")));
        StaticHtmlScanLimits limits = limit switch
        {
            StaticScanLimit.PageCount => new(MaxPages: 2),
            StaticScanLimit.GuideBytes => new(MaxGuideBytes: 250),
            _ => new(MaxGuideAssets: 2),
        };

        StaticHtmlScanException error = await Assert.ThrowsAsync<StaticHtmlScanException>(
            () => new StaticHtmlDependencyScanner(limits).ScanAsync("Start.html", folder));

        Assert.Equal(limit, error.Limit);
    }
```

The `GuideBytes` case relies on each saved page being about 100 bytes, so
three pages pass 250. Check the sizes when the test first runs, and adjust
the limit if needed.

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Infrastructure.Tests "FullyQualifiedName~StaticHtmlDependencyScannerTests"`
Expected: build errors (`IStaticHtmlPageDirectory`, `StaticPage`, `Pages` and the new enum values don't exist).

- [ ] **Step 3: The manifest types.** In `StaticHtmlManifest.cs`:
  - add `Page` after `EntryHtml` in `StaticAssetKind`;
  - add `Invalid` to `StaticReferenceStatus`;
  - add `PageCount, GuideBytes, GuideAssetCount` to `StaticScanLimit`;
  - append `int MaxPages = 200, long MaxGuideBytes = 1024L * 1024 * 1024, int MaxGuideAssets = 16384` to `StaticHtmlScanLimits`;
  - add `IReadOnlyList<StaticPage> Pages` as `StaticHtmlManifest`'s last parameter;
  - then add:

```csharp
/// <summary>
/// T21.2: one page of the guide, in reading order. Pages[0] is the entry.
/// OriginalUrl is the address the saved page recorded, when it did.
/// </summary>
public sealed record StaticPage(
    string RelativePath, string RequestRelativePath, string? OriginalUrl, string? Title);

/// <summary>
/// T21.2: a source that can list the saved pages beside the entry, by
/// top-level file name. Names are candidates only; every read still goes
/// through OpenReadAsync.
/// </summary>
public interface IStaticHtmlPageDirectory
{
    IReadOnlyList<string> ListPages();
}
```

In the scanner's constructor check, add `this.limits.MaxPages < 1 ||
this.limits.MaxGuideBytes < 1 || this.limits.MaxGuideAssets < 1 ||`.

- [ ] **Step 4: The scanner.** Add `using DesktopGuides.Core.Html;` to
  the file. In `ScanSession`:
  - **Fields:** add the page budgets and the candidate cap:

```csharp
        // T21.2: these budgets restart for each page; totalBytes and assets
        // cover the whole guide.
        private int pageReferenceStart;
        private int pageAssetStart;
        private long pageBytes;
        private const int PrefixChars = 256 * 1024;
        private const int CommentChars = 1024;
        private const int MaxCandidates = 2000;

        private sealed record PageScan(StaticPage Page, Uri? Url, IReadOnlyList<string> Links);
```

  - **`ScanAsync(string entryPath)`:** replace the body. The old element
    loop moves into `ScanPageAsync`.

```csharp
        public async Task<StaticHtmlManifest> ScanAsync(string entryPath)
        {
            PageScan entry = await ScanPageAsync(entryPath, StaticAssetKind.EntryHtml)
                ?? throw new FileNotFoundException("HTML entry was not found.");
            List<StaticPage> pages = [entry.Page];
            if (source is IStaticHtmlPageDirectory directory && entry.Url is Uri entryUrl)
            {
                await AddLinkedPagesAsync(directory, entryPath, entry, entryUrl, pages);
            }
            return new StaticHtmlManifest(
                assets.Values.OrderBy(asset => asset.RelativePath, StringComparer.Ordinal).ToArray(),
                references.ToArray(),
                totalBytes,
                pages);
        }

        // Scans one page and its files under fresh per-page budgets. Null
        // when the page can't be opened.
        private async Task<PageScan?> ScanPageAsync(string pagePath, StaticAssetKind kind)
        {
            pageReferenceStart = references.Count;
            pageAssetStart = assets.Count;
            pageBytes = 0;
            cssRuleCount = 0;
            cssRuleCandidates = 0;
            variableTokenCount = 0;
            customPropertyImages.Clear();
            customPropertyVariables.Clear();
            seenVariableDefinitions.Clear();
            variableUses.Clear();
            seenVariableUses.Clear();

            byte[]? bytes = await ReadAssetAsync(pagePath, kind);
            if (bytes is null) return null;
            using MemoryStream stream = new(bytes, writable: false);
            IDocument document = htmlParser.ParseDocument(stream);
            List<string> links = [];
            foreach (IElement element in document.QuerySelectorAll("*"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element.LocalName == "a" && element.GetAttribute("href") is string href)
                {
                    links.Add(href);
                }
                await ScanElementAsync(pagePath, element);
            }
            await ScanCustomPropertyUrlsAsync();
            Uri? url = SavedPageUrl.FromComment(Prefix(bytes, CommentChars)) ?? CanonicalOf(document);
            string? title = string.IsNullOrWhiteSpace(document.Title) ? null : document.Title.Trim();
            StaticAsset asset = assets[pagePath];
            return new PageScan(
                new StaticPage(asset.RelativePath, asset.RequestRelativePath, url?.AbsoluteUri, title), url, links);
        }
```

  - **`ScanElementAsync(string pagePath, IElement element)`:** holds the old
    loop body (the `img`, `source`, `link`, `base`, `src`, `style` and
    `style=` branches), with `entryPath` renamed `pagePath`.
  - **The helpers:**

```csharp
        private static string Prefix(byte[] bytes, int chars) =>
            System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, chars));

        private static Uri? CanonicalOf(IDocument document) => SavedPageUrl.Parse(
            document.QuerySelectorAll("link")
                .FirstOrDefault(link => HasRel(link.GetAttribute("rel"), "canonical"))
                ?.GetAttribute("href"));

        // Indexes the folder's other saved pages by their recorded addresses,
        // then follows links breadth-first from the entry, in document order.
        private async Task AddLinkedPagesAsync(
            IStaticHtmlPageDirectory directory, string entryPath, PageScan entry, Uri entryUrl, List<StaticPage> pages)
        {
            string[] names = directory.ListPages()
                .Where(name => !string.Equals(name, entryPath, StringComparison.Ordinal) && IsSafeName(name))
                .Order(StringComparer.Ordinal)
                .Take(MaxCandidates)
                .ToArray();
            Dictionary<string, string> byKey = new(StringComparer.Ordinal);
            foreach (string name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (Uri url in await ReadPageUrlsAsync(name))
                {
                    if (SavedPageUrl.SameHost(url, entryUrl)) byKey.TryAdd(SavedPageUrl.Key(url), name);
                }
            }
            HashSet<string> byName = new(names, StringComparer.Ordinal);
            HashSet<string> added = new(StringComparer.Ordinal) { entryPath };
            Queue<(Uri Base, IReadOnlyList<string> Links)> pending = new([(entryUrl, entry.Links)]);
            while (pending.TryDequeue(out (Uri Base, IReadOnlyList<string> Links) next))
            {
                foreach (string href in next.Links)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (PageFor(href, next.Base, entryUrl, byKey, byName) is not string name || !added.Add(name))
                    {
                        continue;
                    }
                    if (pages.Count >= limits.MaxPages)
                    {
                        throw Limit(StaticScanLimit.PageCount, "The guide has too many pages.");
                    }
                    if (await ScanPageAsync(name, StaticAssetKind.Page) is not PageScan page) continue;
                    pages.Add(page.Page);
                    pending.Enqueue((page.Url ?? next.Base, page.Links));
                }
            }
        }

        // A link names a candidate by its address or, when relative, by the
        // file name it points at.
        private static string? PageFor(
            string href, Uri baseUrl, Uri entryUrl, Dictionary<string, string> byKey, HashSet<string> byName)
        {
            string target = href.Trim();
            if (!Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                int cut = target.IndexOfAny(['?', '#']);
                string file = Uri.UnescapeDataString(cut >= 0 ? target[..cut] : target);
                if (file.StartsWith("./", StringComparison.Ordinal)) file = file[2..];
                if (byName.Contains(file)) return file;
            }
            if (!Uri.TryCreate(baseUrl, target, out Uri? resolved) ||
                SavedPageUrl.Parse(resolved.AbsoluteUri) is not Uri url ||
                !SavedPageUrl.SameHost(url, entryUrl))
            {
                return null;
            }
            return byKey.GetValueOrDefault(SavedPageUrl.Key(url));
        }

        // Reads only the start of a candidate; it isn't part of the guide yet.
        private async Task<IReadOnlyList<Uri>> ReadPageUrlsAsync(string name)
        {
            await using Stream? input = await source.OpenReadAsync(name, cancellationToken);
            if (input is null) return [];
            byte[] buffer = new byte[PrefixChars];
            int length = 0;
            while (length < buffer.Length)
            {
                int count = await input.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (count == 0) break;
                length += count;
            }
            string prefix = System.Text.Encoding.UTF8.GetString(buffer, 0, length);
            List<Uri> urls = [];
            if (SavedPageUrl.FromComment(prefix[..Math.Min(prefix.Length, CommentChars)]) is Uri comment) urls.Add(comment);
            if (CanonicalOf(htmlParser.ParseDocument(prefix)) is Uri canonical) urls.Add(canonical);
            return urls;
        }

        private static bool IsSafeName(string name)
        {
            try
            {
                ManagedRelativePath.Parse(name);
                return !name.Contains('/') && IsExtension(name, ".html", ".htm");
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
```

  - **`EnsureReferenceBudget`:** compare against this page:
    `if (references.Count - pageReferenceStart >= limits.MaxReferences)`.
  - **`ReadAssetAsync`:**
    - the count check becomes `if (assets.Count - pageAssetStart - 1 >= limits.MaxAssets && kind is not (StaticAssetKind.EntryHtml or StaticAssetKind.Page))`;
    - after it, add `if (assets.Count >= limits.MaxGuideAssets) throw Limit(StaticScanLimit.GuideAssetCount, "The guide has too many files.");`;
    - `maximum` and the size `Limit` treat `Page` like `EntryHtml`;
    - the total check uses `pageBytes` against `MaxTotalBytes`;
    - add `if (totalBytes + output.Length + count > limits.MaxGuideBytes) throw Limit(StaticScanLimit.GuideBytes, "The guide is too large.");`;
    - after `totalBytes += bytes.Length;` add `pageBytes += bytes.Length;`.
  - **`ReadHtmlReferenceAsync`:**
    - `supported` for a stylesheet is now `true`;
    - add a stylesheet check before the asset is recorded: read the bytes
      first, then for a name other than `.css`, call
      `IsReadableCss(bytes)`. When it's false, record an `Invalid`
      reference and keep the bytes out of `assets` and `totalBytes`.

    The block below goes after the existing-asset and CSS depth checks.
    It replaces the code from `byte[]? bytes = await ReadAssetAsync(path, kind);`
    to the end, for stylesheets only; images keep that code.

    Split `ReadAssetAsync` so the size checks and the read happen in
    `ReadBytesAsync(path, kind)`, and the recording happens in
    `Record(path, kind, bytes)`. `ReadAssetAsync` is
    `ReadBytesAsync` then `Record`, used for pages and images.

```csharp
            if (kind == StaticAssetKind.StyleSheet)
            {
                byte[]? sheet = await ReadBytesAsync(path, kind);
                if (sheet is null)
                {
                    references.Add(new StaticAssetReference(sourcePath, raw, path, kind, StaticReferenceStatus.Missing));
                    return;
                }
                bool named = IsExtension(path, ".css");
                string? css = named ? CssTextDecoder.Decode(sheet) : ReadableCss(sheet);
                if (css is null)
                {
                    references.Add(new StaticAssetReference(sourcePath, raw, path, kind, StaticReferenceStatus.Invalid));
                    return;
                }
                Record(path, kind, sheet);
                references.Add(new StaticAssetReference(sourcePath, raw, path, kind, StaticReferenceStatus.Included));
                await ScanCssAsync(path, css, depth);
                return;
            }
```

```csharp
        // T21.2: a stylesheet found by role but not named .css must be text,
        // not a saved error page or binary data.
        private static string? ReadableCss(byte[] bytes)
        {
            if (Array.IndexOf(bytes, (byte)0) >= 0) return null;
            string css;
            try
            {
                css = CssTextDecoder.Decode(bytes);
            }
            catch (InvalidDataException)
            {
                return null;
            }
            return css.TrimStart().StartsWith('<') ? null : css;
        }
```

  Keep `.css` sheets exactly as before: `CssTextDecoder.Decode` throws for
  an unknown declared charset, and
  `UnsupportedDeclaredCssEncodingFailsPreview` still expects that.

- [ ] **Step 5: Run them, then the suite**

Run: the Step 2 command. Expected: every scanner test passes, the new ones
included (14 new, counting the theory rows). Then
`t DesktopGuides.Infrastructure.Tests`.
Expected: all pass. The validator, publisher and backup tests run unchanged
here: `Pages` isn't used outside the scanner yet.

- [ ] **Step 6: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Infrastructure/Import tests/DesktopGuides.Infrastructure.Tests/StaticHtmlDependencyScannerTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): find a saved guide's other pages, and stylesheets by role"
```

---
### Task 3: Schema 7 and the page rows

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibrarySchema.cs` (`CurrentVersion = 7`, `Version7`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/ImportJournal.cs` (`NewImportedGuide.Pages`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (the publish insert)
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`, `ImportJournalTests.cs`

**Interfaces:**
- Consumes: Task 1's `GuidePage` and `GuideAssetKind.Page`.
- Produces:
  - schema 7, exactly the Global Constraints SQL;
  - `NewImportedGuide(..., IReadOnlyList<GuideAsset>? Assets = null, IReadOnlyList<GuidePage>? Pages = null)`.
    An HTML guide published without `Pages` gets its `EntryHtml` asset as
    page 0, so test helpers and older callers keep the invariant.

- [ ] **Step 1: Write the failing tests.** In `SqliteLibraryRepositoryTests`,
  beside the version 6 migration tests:

```csharp
    [Fact]
    public async Task UpgradesPopulatedVersionSixWithOnePagePerHtmlGuide()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionSix(directory, gameId, guideId);
        InsertAsset(directory.Paths.DatabasePath, guideId, "guide.html", "EntryHtml");
        InsertAsset(directory.Paths.DatabasePath, guideId, "styles/main.css", "StyleSheet");

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Assert.Equal(7L, ReadUserVersion(directory.Paths.DatabasePath));
        Assert.Equal("0|guide.html||", ScalarText(directory.Paths.DatabasePath,
            "SELECT PageOrder || '|' || RequestPath || '|' || IFNULL(OriginalUrl, '') || '|' || IFNULL(Title, '') FROM GuidePages"));
        Assert.Equal("EntryHtml,StyleSheet", ScalarText(directory.Paths.DatabasePath,
            "SELECT group_concat(Kind, ',') FROM (SELECT Kind FROM GuideAssets ORDER BY RequestPath)"));
    }

    [Fact]
    public async Task FailedVersionSevenMigrationStaysAtVersionSix()
    {
        using TestLibrary directory = new();
        CreatePopulatedVersionSix(directory, Guid.NewGuid(), Guid.NewGuid());

        await using (SqliteLibraryRepository failing = new(directory.Paths, null, version =>
        {
            if (version == 7) throw new IOException("Injected after the v7 tables were rebuilt.");
        }))
        {
            LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(() => failing.InitializeAsync());
            Assert.Equal(LibraryOpenIssue.MigrationFailed, error.Issue);
        }

        Assert.Equal(6L, ReadUserVersion(directory.Paths.DatabasePath));
    }

    [Fact]
    public async Task APageRowNeedsItsAssetAndLeavesWithItsGuide()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Pages", null, null);
        Guid guide = Guid.NewGuid();
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);
        InsertAsset(directory.Paths.DatabasePath, guide, "Chapter 1.html", "Page");

        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO GuidePages (GuideId, PageOrder, RequestPath) VALUES ($g, $o, $r)";
        insert.Parameters.AddWithValue("$g", guide.ToString("N"));
        insert.Parameters.AddWithValue("$o", 1);
        insert.Parameters.AddWithValue("$r", "Chapter 1.html");
        insert.ExecuteNonQuery();
        insert.Parameters["$o"].Value = 2;
        insert.Parameters["$r"].Value = "Missing.html";
        Assert.Throws<SqliteException>(() => insert.ExecuteNonQuery());
        insert.Parameters["$o"].Value = -1;
        insert.Parameters["$r"].Value = "Chapter 1.html";
        Assert.Throws<SqliteException>(() => insert.ExecuteNonQuery());

        using SqliteCommand remove = connection.CreateCommand();
        remove.CommandText = $"DELETE FROM Guides WHERE Id = '{guide:N}'";
        remove.ExecuteNonQuery();
        Assert.Equal("0", ScalarText(directory.Paths.DatabasePath, "SELECT COUNT(*) FROM GuidePages"));
    }
```

Add the helpers beside `CreatePopulatedVersionFive`:

```csharp
    private static void CreatePopulatedVersionSix(TestLibrary directory, Guid gameId, Guid guideId)
    {
        CreatePopulatedVersionFive(directory, gameId, guideId);
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = LibrarySchema.Version6;
        command.ExecuteNonQuery();
    }

    private static string ScalarText(string databasePath, string sql)
    {
        using SqliteConnection connection = OpenWithForeignKeys(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }
```

If the file already has an equivalent scalar helper, use it instead of
adding `ScalarText`.

In `ImportJournalTests`:

```csharp
    [Fact]
    public async Task AnHtmlGuideKeepsItsPagesInOrder()
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        GuideAsset[] assets =
        [
            new("Start.html", "Start.html", GuideAssetKind.EntryHtml, 10, Hash),
            new("One.html", "One.html", GuideAssetKind.Page, 10, Hash),
        ];
        await Run(journal =>
        {
            journal.Prepare(operation, id);
            journal.Publish(new NewImportedGuide(operation, id, game.Id, "Pages", GuideFormat.Html, "Start.html",
                Hash, 20, "Start.html", null, assets,
                [new GuidePage(0, "Start.html", "https://guides.example/q", "Start"), new GuidePage(1, "One.html", null, null)]),
                () => { });
        });

        Assert.Equal("0:Start.html:https://guides.example/q:Start;1:One.html::", Scalar(
            $"SELECT group_concat(PageOrder || ':' || RequestPath || ':' || IFNULL(OriginalUrl, '') || ':' || IFNULL(Title, ''), ';') " +
            $"FROM (SELECT * FROM GuidePages WHERE GuideId = '{id:N}' ORDER BY PageOrder)"));
    }

    [Fact]
    public async Task AnHtmlGuideWithoutAPageListHasItsEntry()
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        await Run(journal =>
        {
            journal.Prepare(operation, id);
            journal.Publish(new NewImportedGuide(operation, id, game.Id, "One", GuideFormat.Html, "guide.html",
                Hash, 10, "guide.html", null, [new("guide.html", "guide.html", GuideAssetKind.EntryHtml, 10, Hash)]),
                () => { });
        });

        Assert.Equal("0:guide.html", Scalar($"SELECT PageOrder || ':' || RequestPath FROM GuidePages WHERE GuideId = '{id:N}'"));
    }
```

Check the existing `Scalar(string sql)` helper returns the value as text,
and add `using DesktopGuides.Core.Html;` if needed.

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Infrastructure.Tests "FullyQualifiedName~VersionSix|FullyQualifiedName~VersionSeven|FullyQualifiedName~APageRow|FullyQualifiedName~Pages|FullyQualifiedName~HasItsEntry"`
Expected: build errors (`NewImportedGuide` has no `Pages`); after the
record change, failures because there's no `GuidePages` table.

- [ ] **Step 3: Implement.**
  - **`LibrarySchema.cs`:**
    - set `CurrentVersion = 7`;
    - add `Version7`, exactly the Global Constraints SQL, with the comment
      `// T21.2: page assets, and each HTML guide's pages in reading order. SQLite can't change a CHECK, so GuideAssets is rebuilt; nothing references it yet.`;
    - append `(7, Version7)` to `Migrations`.
  - **`ImportJournal.cs`:** append
    `IReadOnlyList<GuidePage>? Pages = null` to `NewImportedGuide`.
  - **`SqliteLibraryRepository.cs`:** after the `foreach (GuideAsset asset ...)`
    insert loop:

```csharp
            // T21.2: every HTML guide has its pages; without a list, the entry
            // is the only page.
            IReadOnlyList<GuidePage> pages = guide.Pages ?? (guide.Format == GuideFormat.Html
                ? (guide.Assets ?? [])
                    .Where(asset => asset.Kind == GuideAssetKind.EntryHtml)
                    .Select(asset => new GuidePage(0, asset.RequestPath, null, null))
                    .ToArray()
                : []);
            foreach (GuidePage page in pages)
            {
                using SqliteCommand row = connection.CreateCommand();
                row.Transaction = transaction;
                row.CommandText = """
                    INSERT INTO GuidePages (GuideId, PageOrder, RequestPath, OriginalUrl, Title)
                    VALUES ($id, $order, $request, $url, $title)
                    """;
                row.Parameters.AddWithValue("$id", id);
                row.Parameters.AddWithValue("$order", page.Order);
                row.Parameters.AddWithValue("$request", page.RequestPath);
                row.Parameters.AddWithValue("$url", (object?)page.OriginalUrl ?? DBNull.Value);
                row.Parameters.AddWithValue("$title", (object?)page.Title ?? DBNull.Value);
                row.ExecuteNonQuery();
            }
```

  Titles over 512 characters and addresses over 2,048 would fail the
  `CHECK`. The publisher trims them in Task 4.

- [ ] **Step 4: Run them, then the suite**

Run: the Step 2 command. Expected: PASS (5 new). Then
`t DesktopGuides.Infrastructure.Tests`. Expected: all pass.
- **If a schema test pins the version 6 schema or the table list** (for
  example "UpgradesToCurrentVersion"), it now expects version 7. Update a
  literal `6L` to `LibrarySchema.CurrentVersion`, as T19.2 did for `5L`.
- **If a restore test fails** because a schema 6 backup now migrates,
  read it before changing it: Task 5 covers restore.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Infrastructure/Storage tests/DesktopGuides.Infrastructure.Tests
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): schema 7 stores each HTML guide's pages"
```

---
### Task 4: The fixture, the folder listing, the preview manifest and publishing pages

**Files:**
- Create: `tests/fixtures/p2/html-multipage/` (the files below)
- Modify: `src/DesktopGuides.Infrastructure/Import/RootedStaticHtmlAssetSource.cs` (`IStaticHtmlPageDirectory`)
- Modify: `src/DesktopGuides.Infrastructure/Import/StaticHtmlImportValidator.cs` (the `Invalid` warning)
- Modify: `src/DesktopGuides.Core/Import/ImportContracts.cs` (`HtmlImportManifest.PageTitles`)
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs` (`InspectHtmlAsync`, `HtmlLimitMessage`)
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs` (`ToGuideAsset`, the pages)
- Create: `tests/DesktopGuides.Infrastructure.Tests/Import/P2Fixtures.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/MultipageImportTests.cs`, `GuideImportValidatorHtmlPdfTests.cs`

**Interfaces:**
- Consumes: Task 2's `Pages`, `IStaticHtmlPageDirectory` and new limits;
  Task 3's `NewImportedGuide.Pages`.
- Produces:
  - `HtmlImportManifest(..., string Fingerprint, IReadOnlyList<string> PageTitles)`.
    The titles are in reading order, with the shared suffix removed; a
    missing title is the file name without its extension.
  - `AssetCount` counts linked files only, not pages.
  - The fixture's entry is `tests/fixtures/p2/html-multipage/Sample Quest - Guides Example.html`
    (Task 6 imports it).

- [ ] **Step 1: The fixture.** Create these files, exactly. Every page saves
  its stylesheet as `load.php`, as MediaWiki's pages do.

`Sample Quest - Guides Example.html`:

```html
<!DOCTYPE html>
<!-- saved from url=(0040)https://guides.example/wiki/Sample_Quest -->
<html lang="en"><head><meta charset="utf-8">
<title>Sample Quest — Guides Example</title>
<link rel="canonical" href="https://guides.example/wiki/Sample_Quest">
<link rel="stylesheet" href="./Sample Quest - Guides Example_files/load.php">
</head><body>
<!-- An authored T21.2 fixture: four saved pages of a made-up guide. -->
<h1 class="guide-title">Sample Quest</h1>
<p>Start here, then read each chapter in turn.</p>
<ul>
<li><a href="https://guides.example/wiki/Sample_Quest/Chapter_1">Chapter 1</a></li>
<li><a href="https://Guides.Example/wiki/Sample_Quest:Chapter_2#top">Chapter 2</a></li>
<li><a href="https://guides.example/wiki/Sample_Quest/Chapter_4">Chapter 4, not saved</a></li>
<li><a href="https://guides.example/wiki/Sample_Quest/Fifty_Percent">Halfway, a page whose file name has a percent sign</a></li>
<li><a href="https://other.example/wiki/Unrelated">A page on another site</a></li>
</ul>
</body></html>
```

`Chapter 1 - Guides Example.html`:

```html
<!DOCTYPE html>
<!-- saved from url=(0050)https://guides.example/wiki/Sample_Quest/Chapter_1 -->
<html lang="en"><head><meta charset="utf-8">
<title>Chapter 1 — Guides Example</title>
<link rel="stylesheet" href="./Chapter 1 - Guides Example_files/load.php">
</head><body>
<h1 class="guide-title">Chapter 1</h1>
<p>Cross the river at the ford, then follow the north road.</p>
<p><a href="https://guides.example/wiki/Sample_Quest/Chapter_3">On to chapter 3</a></p>
<p><a href="https://guides.example/wiki/Sample_Quest">Back to the start</a></p>
</body></html>
```

`Chapter 2 - Guides Example.html` (no comment: its address is the canonical one):

```html
<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<title>Chapter 2 — Guides Example</title>
<link rel="canonical" href="https://guides.example/wiki/Sample_Quest%3AChapter_2">
<link rel="stylesheet" href="./Chapter 2 - Guides Example_files/load.php">
</head><body>
<h1 class="guide-title" id="top">Chapter 2</h1>
<p>The tower key is under the third step.</p>
<p><a href="https://guides.example/wiki/Sample_Quest">Back to the start</a></p>
</body></html>
```

`Chapter 3 - Guides Example.html`:

```html
<!DOCTYPE html>
<!-- saved from url=(0050)https://guides.example/wiki/Sample_Quest/Chapter_3 -->
<html lang="en"><head><meta charset="utf-8">
<title>Chapter 3 — Guides Example</title>
<link rel="stylesheet" href="./Chapter 3 - Guides Example_files/load.php">
</head><body>
<h1 class="guide-title">Chapter 3</h1>
<p>The last chapter. The boss is weak to fire.</p>
<p><a href="https://guides.example/wiki/Sample_Quest/Chapter_1">Back to chapter 1</a></p>
</body></html>
```

`Unrelated - Other Example.html`:

```html
<!DOCTYPE html>
<!-- saved from url=(0036)https://other.example/wiki/Unrelated -->
<html lang="en"><head><meta charset="utf-8"><title>Unrelated — Other Example</title></head>
<body><p>A page from another site, saved into the same folder.</p></body></html>
```

`50% Done - Guides Example.html`:

```html
<!DOCTYPE html>
<!-- saved from url=(0054)https://guides.example/wiki/Sample_Quest/Fifty_Percent -->
<html lang="en"><head><meta charset="utf-8"><title>Halfway — Guides Example</title></head>
<body><p>A page whose file name the app can't use.</p></body></html>
```

Each of `Sample Quest - Guides Example_files/load.php`,
`Chapter 1 - Guides Example_files/load.php`,
`Chapter 2 - Guides Example_files/load.php` and
`Chapter 3 - Guides Example_files/load.php` holds:

```css
/* An authored stylesheet, saved under MediaWiki's load.php name. */
.guide-title { color: #1f4e8c; border-bottom: 2px solid #1f4e8c; }
body { font-family: Georgia, serif; line-height: 1.5; }
```

Each comment's number must equal its address's length (40, 50, 50, 36
and 54). Check them with
`python3 -c "print(len('https://guides.example/wiki/Sample_Quest'))"`.

- [ ] **Step 2: Write the failing tests.** Create `P2Fixtures.cs`:

```csharp
namespace DesktopGuides.Infrastructure.Tests.Import;

internal static class P2Fixtures
{
    public static string Resolve(string relative) => Path.GetFullPath(
        Path.Combine(P0Fixtures.Root, "..", "p2", relative.Replace('/', Path.DirectorySeparatorChar)));

    // Copies a fixture folder's files, keeping their relative paths.
    public static void CopyTree(string fixture, string destination)
    {
        string source = Resolve(fixture);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
```

Create `MultipageImportTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class MultipageImportTests
{
    private const string Entry = "Sample Quest - Guides Example.html";

    [Fact]
    public async Task ThePreviewListsFourPagesInReadingOrder()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory files = new();
        P2Fixtures.CopyTree("html-multipage", files.Root);

        ImportInspection inspection = await new GuideImportValidator()
            .InspectAsync(Path.Combine(files.Root, Entry), CancellationToken.None);

        HtmlImportManifest manifest = Assert.IsType<HtmlImportManifest>(Assert.IsType<ImportReady>(inspection).Manifest);
        Assert.Equal(["Sample Quest", "Chapter 1", "Chapter 2", "Chapter 3"], manifest.PageTitles);
        Assert.Equal(4, manifest.AssetCount);
        Assert.DoesNotContain(manifest.Warnings, warning => warning.Message.Contains("readable CSS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishingStoresEveryPageWithItsOrderAndAddress()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        P2Fixtures.CopyTree("html-multipage", harness.Sources.Root);
        string entry = Path.Combine(harness.Sources.Root, Entry);

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

        IReadOnlyList<GuideAsset> assets = await harness.Repository.GetGuideAssetsAsync(id);
        Assert.Equal(
            [("Chapter 1 - Guides Example.html", GuideAssetKind.Page),
                ("Chapter 1 - Guides Example_files/load.php", GuideAssetKind.StyleSheet),
                ("Chapter 2 - Guides Example.html", GuideAssetKind.Page),
                ("Chapter 2 - Guides Example_files/load.php", GuideAssetKind.StyleSheet),
                ("Chapter 3 - Guides Example.html", GuideAssetKind.Page),
                ("Chapter 3 - Guides Example_files/load.php", GuideAssetKind.StyleSheet),
                ("Sample Quest - Guides Example.html", GuideAssetKind.EntryHtml),
                ("Sample Quest - Guides Example_files/load.php", GuideAssetKind.StyleSheet)],
            assets.Select(asset => (asset.RequestPath, asset.Kind)));
        foreach (GuideAsset asset in assets)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(harness.Sources.Root, asset.RelativePath)),
                File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, asset.RelativePath)));
        }

        using SqliteConnection connection = new($"Data Source={harness.Paths.DatabasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using SqliteCommand pages = connection.CreateCommand();
        pages.CommandText = $"SELECT PageOrder, RequestPath, OriginalUrl, Title FROM GuidePages WHERE GuideId = '{id:N}' ORDER BY PageOrder";
        using SqliteDataReader reader = pages.ExecuteReader();
        List<(long, string, string?, string)> rows = [];
        while (reader.Read())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
        }
        Assert.Equal(
            [(0L, "Sample Quest - Guides Example.html", "https://guides.example/wiki/Sample_Quest", "Sample Quest — Guides Example"),
                (1L, "Chapter 1 - Guides Example.html", "https://guides.example/wiki/Sample_Quest/Chapter_1", "Chapter 1 — Guides Example")],
            rows.Take(2));
        Assert.Equal(["Chapter 2 - Guides Example.html", "Chapter 3 - Guides Example.html"],
            rows.Skip(2).Select(row => row.Item2));
    }
}
```

Check `PublisherHarness.PublishAsync`'s signature against
`PublishesStaticHtmlGuide` and match it.

In `GuideImportValidatorHtmlPdfTests`, beside `HtmlLimitsMapToTooLarge`:

```csharp
    [Theory]
    [InlineData(StaticScanLimit.PageCount, "The guide links more than 2 saved pages.")]
    [InlineData(StaticScanLimit.GuideBytes, "The guide's pages and their linked files are larger than the 5 bytes limit.")]
    [InlineData(StaticScanLimit.GuideAssetCount, "The guide's pages link more than 3 files.")]
    public void GuideLimitsHaveTheirMessages(StaticScanLimit limit, string message) =>
        Assert.Equal(message, new GuideImportValidator(new GuideImportLimits(
                Html: new StaticHtmlScanLimits(MaxPages: 2, MaxGuideBytes: 5, MaxGuideAssets: 3)))
            .HtmlLimitMessage(limit));
```

Match `GuideImportValidator`'s constructor; the existing `Rejected` helper
shows how it takes limits.

- [ ] **Step 3: Run them and watch them fail**

Run: `stage && t DesktopGuides.Infrastructure.Tests "FullyQualifiedName~MultipageImportTests|FullyQualifiedName~GuideLimitsHaveTheirMessages"`
Expected: build errors (`PageTitles` doesn't exist).

- [ ] **Step 4: Implement.**
  - **`RootedStaticHtmlAssetSource`:** add `IStaticHtmlPageDirectory` to
    its interfaces, and:

```csharp
    // T21.2: the saved pages beside the selected one, by name. Every read
    // still goes through OpenReadAsync and its checks.
    public IReadOnlyList<string> ListPages() =>
        Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name =>
                (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                 name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)) &&
                !string.Equals(name, sourceEntryName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, entryAlias, StringComparison.OrdinalIgnoreCase))
            .ToArray();
```

  - **`StaticHtmlImportValidator.ToWarning`:** add
    `StaticReferenceStatus.Invalid => "A linked style sheet isn't readable CSS and will be skipped.",`.
  - **`ImportContracts.cs`:** append `IReadOnlyList<string> PageTitles` to
    `HtmlImportManifest`, after `Fingerprint`.
  - **`GuideImportValidator.InspectHtmlAsync`** (add
    `using DesktopGuides.Core.Html;` for `PageTitles`):

```csharp
        StaticHtmlManifest scanned = preview.Manifest;
        string[] titles = scanned.Pages
            .Select((page, index) => page.Title ?? Path.GetFileNameWithoutExtension(
                index == 0 ? source.FileName : page.RelativePath))
            .ToArray();
        return new ImportReady(new HtmlImportManifest(
            source, title, preview.EntryRelativePath,
            scanned.Assets.Count(asset => asset.Kind is not (StaticAssetKind.EntryHtml or StaticAssetKind.Page)),
            scanned.TotalBytes, warnings,
            GuideFingerprint.OfHtml(scanned.Assets.Select(asset => (asset.RelativePath, asset.Sha256))),
            PageTitles.WithoutSharedSuffix(titles)));
```

  - **`HtmlLimitMessage`:**

```csharp
            StaticScanLimit.PageCount => $"The guide links more than {html.MaxPages:N0} saved pages.",
            StaticScanLimit.GuideBytes =>
                $"The guide's pages and their linked files are larger than the {ImportPresentation.FormatSize(html.MaxGuideBytes)} limit.",
            StaticScanLimit.GuideAssetCount => $"The guide's pages link more than {html.MaxGuideAssets:N0} files.",
```

  - **`GuideImportPublisher`:**
    - `ToGuideAsset` maps `StaticAssetKind.Page => GuideAssetKind.Page`;
    - `NewImportedGuide` receives
      `plan.Html?.Manifest.Pages.Select(ToGuidePage).ToArray()` as `Pages`,
      with:

```csharp
    // An address too long to store is left out; a long title is shortened.
    private static GuidePage ToGuidePage(StaticPage page, int order) => new(
        order, page.RequestRelativePath,
        page.OriginalUrl is { Length: <= 2048 } url ? url : null,
        page.Title is { Length: > 512 } title ? title[..512] : page.Title);
```

- [ ] **Step 5: Run them, then the suite**

Run: the Step 3 command. Expected: PASS (5 new). Then
`t DesktopGuides.Infrastructure.Tests`. Expected: all pass. The existing
HTML tests still see one page: `html-static` has no recorded address.

- [ ] **Step 6: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tests/fixtures/p2/html-multipage src tests/DesktopGuides.Infrastructure.Tests
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): import a saved guide's pages, with the html-multipage fixture"
```

---
### Task 5: Backup and restore check the pages

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibraryArchivePlan.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/LibraryExporterTests.cs`, `Restore/LibraryRestorerValidationTests.cs`, `Restore/LibraryRestorerSwapTests.cs`

**Interfaces:**
- Consumes: Task 3's schema 7 and page rows.
- Produces: `LibraryArchivePlan.Read` treats an HTML guide as damaged when
  any of these fail:
  - its pages aren't numbered 0…n−1;
  - they don't match its `EntryHtml` and `Page` assets one to one;
  - page 0 isn't its one `EntryHtml` asset at `PrimaryRelativePath`.

  A snapshot below schema 7 has no pages to check. Export reports a
  damaged guide as `ManagedFilesDamaged`, and restore as
  `ReferencesInvalid`, through the existing mapping.

- [ ] **Step 1: Write the failing tests.** In `LibraryExporterTests.cs`:

```csharp
    [Fact]
    public async Task TheSnapshotHoldsThePages()
    {
        string zip = (await Export()).Path;

        Assert.Equal("1", Scalar(zip, $"SELECT COUNT(*) FROM GuidePages WHERE GuideId = '{fixture.HtmlGuide:N}' AND PageOrder = 0"));
    }

    [Theory]
    [InlineData("DELETE FROM GuidePages WHERE GuideId = '{0}'")]
    [InlineData("UPDATE GuidePages SET PageOrder = 1 WHERE GuideId = '{0}'")]
    [InlineData("UPDATE GuideAssets SET Kind = 'Page' WHERE GuideId = '{0}' AND Kind = 'EntryHtml'")]
    public async Task BrokenPageRowsDamageTheGuide(string sql)
    {
        fixture.Library.Execute(string.Format(CultureInfo.InvariantCulture, sql, fixture.HtmlGuide.ToString("N")));

        LibraryExportException error = await Fails(LibraryExportIssue.ManagedFilesDamaged);

        Assert.Equal(new[] { fixture.HtmlGuide }, error.GuideIds);
        AssertNothingLeft();
    }
```

In `LibraryRestorerValidationTests.cs`:

```csharp
    [Fact]
    public async Task AGuideWithoutItsPagesIsRefused()
    {
        string backup = fixture.RewriteDatabase(
            $"DELETE FROM GuidePages WHERE GuideId = '{fixture.Source.HtmlGuide:N}';");

        LibraryRestoreException error = await Refused(backup, LibraryRestoreIssue.ReferencesInvalid);

        Assert.Equal([fixture.Source.HtmlGuide], error.GuideIds);
    }
```

In `LibraryRestorerSwapTests.cs`:

```csharp
    // A backup from before schema 7 has no pages; it restores, and migration gives each HTML guide its entry page.
    [Fact]
    public async Task AVersionSixBackupRestoresWithOnePagePerHtmlGuide()
    {
        string backup = fixture.RewriteDatabase(
            RestoreFixture.GuideAssetsBeforeVersionSeven() + " PRAGMA user_version = 6;",
            manifest => manifest with { SchemaVersion = 6 });
        LibraryRestoreStage stage = await fixture.StageAsync(backup);

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        await fixture.Target.RestartAsync();

        Assert.Equal(LibrarySchema.CurrentVersion.ToString(CultureInfo.InvariantCulture), fixture.Target.Scalar("PRAGMA user_version"));
        Assert.Equal("1", fixture.Target.Scalar(
            $"SELECT COUNT(*) FROM GuidePages WHERE GuideId = '{fixture.Source.HtmlGuide:N}' AND PageOrder = 0"));
    }
```

In `RestoreFixture.cs`, beside `ReaderPreferencesBeforeVersionSix()`:

```csharp
    // T21.2: GuideAssets exactly as versions 4 to 6 had it, without GuidePages.
    // Version 4's own statement rebuilds it, so the stored schema text
    // matches what migrations 1 to 6 produce. A rename would store
    // CREATE TABLE "GuideAssets" instead, and validation would refuse it.
    public static string GuideAssetsBeforeVersionSeven() =>
        "DROP TABLE GuidePages; ALTER TABLE GuideAssets RENAME TO GuideAssetsOld; " +
        LibrarySchema.Version4 +
        " INSERT INTO GuideAssets SELECT * FROM GuideAssetsOld; DROP TABLE GuideAssetsOld;";
```

`Version4` ends with `PRAGMA user_version = 4;`; the test's
`PRAGMA user_version = 6;`, which comes after it, wins. This follows the
T19.1 ruling "rebuild with version 1's exact statement".

- [ ] **Step 2: Run them and watch them fail**

Run: `stage && t DesktopGuides.Infrastructure.Tests "FullyQualifiedName~ThePages|FullyQualifiedName~BrokenPageRows|FullyQualifiedName~WithoutItsPages|FullyQualifiedName~VersionSixBackup"`
Expected:
- `TheSnapshotHoldsThePages` passes; the snapshot is a database copy;
- the three `BrokenPageRowsDamageTheGuide` cases and
  `AGuideWithoutItsPagesIsRefused` fail: export and restore succeed;
- `AVersionSixBackupRestoresWithOnePagePerHtmlGuide` fails until the plan
  skips pages below schema 7, because `GuidePages` doesn't exist there.

- [ ] **Step 3: Implement.** In `LibraryArchivePlan.cs`:
  - **Assets:** read the request path and kind too. The query becomes
    `SELECT GuideId, RequestPath, RelativePath, Kind, ByteCount, Sha256 FROM GuideAssets ORDER BY GuideId, RequestPath`,
    and `AssetRow` becomes
    `(string Request, string RelativePath, string Kind, long Bytes, string Sha256)`.
    Update its other uses.
  - **Pages, from schema 7 on:**

```csharp
        // T21.2: pages exist from schema 7; an older snapshot has none to check.
        bool hasPages = Query(snapshot, "PRAGMA user_version", reader => reader.GetInt64(0)).Single() >= 7;
        ILookup<Guid, PageRow> pages = (hasPages
                ? Query(snapshot, "SELECT GuideId, PageOrder, RequestPath FROM GuidePages ORDER BY GuideId, PageOrder",
                    reader => (Guid.ParseExact(reader.GetString(0), "N"), new PageRow(reader.GetInt64(1), reader.GetString(2))))
                : [])
            .ToLookup(row => row.Item1, row => row.Item2);
```

    Pass `hasPages ? pages[guide.Id].ToArray() : null` to `TryPlanGuide`.
    Add `private sealed record PageRow(long Order, string Request);`.
  - **In `TryPlanGuide`'s HTML branch,** after the fingerprint checks:

```csharp
            if (pageRows is not null && !PagesMatch(guide, assets, pageRows))
            {
                return false;
            }
```

```csharp
    // T21.2: the pages run 0…n−1, match the page assets one to one, and
    // start at the guide's one entry.
    private static bool PagesMatch(GuideRow guide, AssetRow[] assets, PageRow[] pages)
    {
        if (pages.Length == 0 || pages.Where((page, index) => page.Order != index).Any())
        {
            return false;
        }
        string[] pageAssets = assets
            .Where(asset => asset.Kind is "EntryHtml" or "Page")
            .Select(asset => asset.Request)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!pageAssets.SequenceEqual(pages.Select(page => page.Request).Order(StringComparer.Ordinal)))
        {
            return false;
        }
        AssetRow? first = assets.FirstOrDefault(asset => asset.Request == pages[0].Request);
        return first is { Kind: "EntryHtml" } &&
            first.RelativePath == guide.Primary &&
            assets.Count(asset => asset.Kind == "EntryHtml") == 1;
    }
```

  If `Query` is generic over a row reader, `PRAGMA user_version` fits it.
  Otherwise read the version with a plain `SqliteCommand`.

- [ ] **Step 4: Run them, then the suite**

Run: the Step 2 command. Expected: PASS (6 new, counting the theory rows).
Then `t DesktopGuides.Infrastructure.Tests`. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Infrastructure/Storage tests/DesktopGuides.Infrastructure.Tests
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): backups check each HTML guide's pages"
```

---
### Task 6: The import preview's pages, and the installed `import-html-pages` mode

**Files:**
- Modify: `src/DesktopGuides.Production/ImportGuideDialog.xaml` (an `ImportPageListRow`)
- Modify: `src/DesktopGuides.Production/ImportGuideDialog.xaml.cs` (`ShowManifest`, the reset)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`describe-import` reports `MostGuidePages`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the `import-html-pages` mode)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-ImportScenarios`)

**Interfaces:**
- Consumes: Task 1's `ImportPresentation.PageCount` and `PageList`;
  Task 4's `HtmlImportManifest.PageTitles` and the fixture.
- Produces:
  - **The page count:** for an HTML guide of two or more pages, the
    dialog's existing `ImportPages` row shows "4 pages".
  - **The page list:** the new `ImportPageList` text shows the numbered
    titles, one per line. A one-page guide shows neither, as today.
  - **`describe-import`'s `MostGuidePages`:** the largest page count of
    any guide.

- [ ] **Step 1: The dialog.** In `ImportGuideDialog.xaml`, after
  `ImportPagesRow`, add:

```xml
                        <Grid x:Name="ImportPageListRow"
                              Visibility="Collapsed"
                              ColumnSpacing="{StaticResource DesktopGuidesSpacing12}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="120" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Reading order" Style="{StaticResource DesktopGuidesMetadataStyle}" />
                            <TextBlock x:Name="ImportPageList" Grid.Column="1"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       TextWrapping="Wrap"
                                       AutomationProperties.AutomationId="ImportPageList" />
                        </Grid>
```

In `ImportGuideDialog.xaml.cs`:
- add `ImportPageListRow.Visibility = Visibility.Collapsed;` beside the
  `ImportPagesRow` reset;
- in `ShowManifest`'s `HtmlImportManifest` case, after `ImportAssetsRow`
  is shown:

```csharp
                // T21.2: a guide of several saved pages lists them in reading order.
                if (html.PageTitles.Count > 1)
                {
                    ImportPages.Text = ImportPresentation.PageCount(html.PageTitles.Count);
                    ImportPagesRow.Visibility = Visibility.Visible;
                    ImportPageList.Text = ImportPresentation.PageList(html.PageTitles);
                    ImportPageListRow.Visibility = Visibility.Visible;
                }
```

Run: `stage && build`. Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 2: `describe-import`.** In `Program.cs`'s `describe-import`
  object, add:

```csharp
        MostGuidePages = Scalar("SELECT COALESCE(MAX(Pages), 0) FROM (SELECT COUNT(*) AS Pages FROM GuidePages GROUP BY GuideId)"),
```

Run: `s 'cd /d E:\work\desktop-guides\t21-2a && dotnet build tools\p1\DesktopGuides.ShellSeed -nologo -warnaserror > E:\work\t21-2a-seed.log 2>&1 & findstr /c:"Error(s)" E:\work\t21-2a-seed.log'`
Expected: `0 Error(s)`.

- [ ] **Step 3: The mode.**
  - In `windows_shell_ui_smoke.ps1`, add `'import-html-pages'` to the
    `ValidateSet` beside `'import-preview'`.
  - Inside the `elseif ($Mode -like 'import-*')` block, after
    `Cancel-Picker`, add a picker for the p2 fixtures:

```powershell
        $p2FixtureRoot = (Resolve-Path -LiteralPath (
            Join-Path $PSScriptRoot '..\..\tests\fixtures\p2')).Path

        function Choose-P2PickerFile([string] $relativePath) {
            $picker = Wait-FilePicker
            $path = Join-Path $p2FixtureRoot $relativePath
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                Send-PickerCommand $picker 2
                Wait-PickerClosed $picker
                throw "The fixture '$relativePath' does not exist."
            }
            [DesktopGuidesForegroundProbe]::SetText((Find-InPicker $picker '1148' 'Edit'), $path)
            Send-PickerCommand $picker 1
            Wait-PickerClosed $picker
        }
```

  - Then a branch beside `if ($Mode -eq 'import-preview')`:

```powershell
        if ($Mode -eq 'import-html-pages') {
            # T21.2: one picked page brings the guide's other saved pages, in reading order.
            Select-Element 'Import Test Game'
            [void](Wait-Name 'GameHeading' 'Import Test Game')
            [void](Wait-Status 'Game ready.')
            Click-Element (Wait-EnabledById 'ImportGuideButton')
            Choose-P2PickerFile 'html-multipage\Sample Quest - Guides Example.html'
            [void](Wait-VisibleById 'ImportGuideDialog')
            [void](Wait-Text 'ImportFormat' 'Web page (HTML)')
            [void](Wait-Text 'ImportPages' '4 pages')
            $order = ((Wait-PresentById 'ImportPageList').Current.Name -replace "`r", '')
            $expectedOrder = "1. Sample Quest`n2. Chapter 1`n3. Chapter 2`n4. Chapter 3"
            if ($order -ne $expectedOrder) { throw "The reading order was '$order'." }
            $assets = (Wait-PresentById 'ImportAssets').Current.Name
            if ($assets -notlike '4 linked files, * in total') { throw "The linked files row was '$assets'." }
            $report.importHtmlPagesScreenshot = Save-WindowScreenshot 'import-html-pages'
            $report.phases += 'import-html-pages-preview'

            $pagesTitle = 'Sample Quest ' + [guid]::NewGuid().ToString('N').Substring(0, 8)
            Set-Text 'GuideTitleInput' $pagesTitle
            Invoke-Element (Wait-EnabledById 'PrimaryButton')
            [void](Wait-HiddenById 'ImportGuideDialog')
            [void](Wait-SelectedGuide $pagesTitle)
            $report.importedPagesTitle = $pagesTitle
            $report.phases += 'import-html-pages-published'
        }
```

  Check `Wait-Text`, `Wait-PresentById`, `Set-Text` and `Wait-SelectedGuide`
  are in scope in this block, as `import-publish` uses them. Make the
  existing `if ($Mode -eq 'import-preview')` an `elseif` if the block's
  branches chain.

  - In `windows_shell_install.ps1`'s `Run-ImportScenarios`, after the final
    `foreach ($name in $expected.Keys)` check:

```powershell
    # T21.2: a multi-page guide imports with its four pages.
    Start-InstalledShell
    $report.importHtmlPages = Run-ShellSmoke 'import-html-pages' -ResultName 'import-html-pages'
    Close-InstalledShell
    $pagesState = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
    $report.importHtmlPagesState = $pagesState
    if ($pagesState.MostGuidePages -ne 4) {
        throw "The multi-page guide stored $($pagesState.MostGuidePages) pages; expected 4."
    }
```

Run: `LC_ALL=C grep -c -P '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1` (with `-C` paths, no `cd`).
Expected: `0` for each. Then
`stage && s 'powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\t21-2a\tools\p1\test_windows_scripts_parse.ps1' | tail -1`.
Expected: `windows_scripts_parse: all scripts passed.`

- [ ] **Step 4: Commit, push, and run the import group on both lanes**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Production tools/p1
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): the import preview lists a guide's pages; import-html-pages"
git -C /Users/ilya.lissoboi/work/desktop-guides push -u origin feat/p2-t21-2-multipage-import
for lane in msix portable; do gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p2-t21-2-multipage-import -f shell-scope=import -f shell-lane=$lane; sleep 3; done
```

Find the runs by head SHA, and wait with `gh run watch <id> --exit-status`.
Expected: both pass, with the `import-html-pages` report showing both
phases. Copy `import-html-pages.png` from the MSIX run's
`production-shell-ui-msix-import` artifact to
`docs/p2/evidence/ci/t21-2/import-html-pages.png`.

The core shard's `normal` mode and the HTML reader modes aren't in this
group; the PR's own run covers them. Those modes import single pages from
`tests/fixtures/p0` and `p1`, which record no address, so they should be
unchanged. A failure there points at serving by kind (Task 1) or the
per-page budgets (Task 2).

---
### Task 7: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t21-2-multipage-html-design.md` (Status; a "PR a verification record")
- Modify: `docs/p2/results.md` (T21.1's row, #132)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md`
- Create: `docs/p2/evidence/ci/t21-2/import-html-pages.png`, from Task 6's run

- [ ] **Step 1: The full suites and the build.** On the host, run:
  - Core: 1324 + 20;
  - Infrastructure: 837 + the new tests;
  - the Production build: 0 warnings;
  - the script parse check.
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **The design's Status:** "Approved in conversation, 18 October 2026.
    PR a (import, storage and backup) implemented on
    `feat/p2-t21-2-multipage-import`."
  - **The "PR a verification record":** the tests, the import dispatches
    on both lanes, the screenshot, the review, and every ruling. That
    includes the plan's deviation: pages with unsafe names aren't
    candidates.
  - **`results.md`:** T21.1's row: #132, 2026-10-17, `e3c9a71`, T21.1,
    no CI run (docs only), no suite counts. The note: "The format survey;
    T21.2 limited to multi-page HTML and image maps."
  - **The status pages:** "T21.1 complete (#132). T21.2 PR a (multi-page
    HTML import) in review." The `progress.md` "Next" becomes "T21.2
    PR b (reading)".
- [ ] **Step 4: Commit, push, open the PR, and wait for its run.**
  - **Commit:** "docs(p2): record T21.2 PR a".
  - **The PR:** "T21.2 PR a: import a multi-page HTML guide", based on
    `main`. It names the task, the prerequisite (T21.1, merged as #132)
    and the outcome. It includes the screenshot (this is a UI change),
    the dispatches, every ruling and the deferred minors.
  - **Expected:** every job passes. Record the run in the PR, then stop
    for the user's merge decision.

## Deviations from the design

1. **Saved pages whose file names aren't safe managed paths,** such as a
   name with `%`, are not candidates. A link to one behaves like a link to
   an unsaved page. The design applied the entry's `%` renaming to every
   page. That would need a renaming map per page in
   `RootedStaticHtmlAssetSource`, for a name browsers rarely produce.
2. **The installed mode is `import-html-pages`,** not `html-pages-import`,
   so it shares the import group's picker helpers (`$Mode -like 'import-*'`).

## Traceability

| Design item | Task |
| --- | --- |
| A page's original address from `saved from url=` or `<link rel="canonical">` | 1, 2 |
| Finding the other pages: same folder, same host, breadth-first in document order | 2, 4 |
| Each page's files under today's rules, per page | 2 |
| Stylesheets by role, checked as CSS (TR21.2) | 2, 4 |
| Served by kind (`load.php` as `text/css`) | 1 |
| Limits: 200 pages, 1 GiB, per-file limits unchanged | 2, 4 |
| The preview: "N pages", the titles in reading order, the shared suffix removed | 1, 4, 6 |
| The input shaped for an S22 downloader (pages with their addresses) | 2, 3 |
| Schema 7: `Page` assets and `GuidePages` | 3 |
| Backup: the snapshot, the archive plan's checks, restore refusal, a schema 6 backup | 5 |
| The authored fixture | 4 |
| `html-pages-import` (as `import-html-pages`) | 6 |
