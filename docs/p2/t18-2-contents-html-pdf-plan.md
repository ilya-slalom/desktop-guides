# T18.2 PR b (Contents for HTML and PDF) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HTML guides show their headings, or their own in-page contents
list, and PDF guides show their own outline, in PR a's Contents pane.
Following an entry restores its place and saves it.

**Architecture:**
- **HTML.** A fixed script, like T18.1's find scripts, reports each
  visible `h1`–`h6` and each same-page `#id` link, with walk offsets,
  quotes and ids. Core's `HtmlOutlineRules` decides: a container holding
  three or more links whose targets exist is the page's own contents list,
  and it wins over the headings. Each entry's location is the HTML
  position the reader already restores exactly.
- **PDF.** The search text source (T18.1 PR c) reads PdfPig's bookmarks
  under its gate. Entries that open a page of this document become page
  locations. Website and other-file links are left out, and a grouping
  entry with no page passes its children up.

**Tech Stack:** C# / .NET 10, WinUI 3, WebView2 (DevTools
`Runtime.evaluate`), PdfPig 0.1.16, xUnit, PowerShell 5.1 and 7, UI
Automation, GitHub Actions.

**Spec:** `docs/p2/t18-2-contents-design.md`, and
`docs/p2-technical-design.md` section 4.

**Target:** T18.2, PR b of two, on `feat/p2-t18-2-contents-html-pdf`.
**Prerequisites:** PR a (#118), merged.

## Global Constraints

- **HTML:**
  - headings `h1`–`h6`, visible (with boxes) and with text, nested by
    level;
  - a contents list is a container (`ul`, `ol`, `nav`, `dl` or `table`)
    with three or more same-page links whose targets exist. The largest
    such list wins over the headings, and nested lists nest;
  - each entry's location is `HtmlLocationRules.Capture` of its target's
    walk offset, quote, id and fraction;
  - `Order` is the walk offset.
- **PDF:**
  - the document's own outline, via `PdfDocument.TryGetBookmarks`;
  - `DocumentBookmarkNode`s that aren't `ExternalBookmarkNode` or
    `EmbeddedBookmarkNode` become entries at their page, with fraction 0;
  - `UriBookmarkNode`, `ExternalBookmarkNode` and `EmbeddedBookmarkNode`
    are left out, and their children move up;
  - a `ContainerBookmarkNode` takes its first child's page;
  - `Order` is the page index;
  - no outline, or a broken one, gives `Outline.Empty`.
- **Scripts:** host values enter only as `JsonSerializer` literals. DOM
  members are reached through prototypes, because named elements can
  shadow them.
- **Failure never blocks reading.** Either source failing gives `None`,
  and the pane says "No sections found in this guide.".
- **Keys:** with the keyboard inside an HTML page, shell keys don't
  arrive (#115). The installed HTML modes reopen the pane from the
  toolbar's *Contents* command.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.
  There is no `cd` in local shell commands.

## Review Focus

1. **A saved site page with a nav menu of `#` links** (for example "Back
   to top", or a footnote list) beside real headings. Pinned by Task 1's
   `LinksWithoutTargetsDontCount` and `AShortListLeavesTheHeadings`.
2. **Hostile names** (`form name="querySelectorAll"`) break the collect
   script. Pinned by Task 4's `contents-html-headings` on Long Web Guide,
   whose fixture has them.
3. **A PDF whose outline points at a page outside the document,** or is
   malformed. Pinned by Task 2's page-range check, and by the catch that
   returns no outline.
4. **A huge HTML page** (tens of thousands of nodes) within the 5 s script
   timeout. The script walks the nodes once, building a first-text-offset
   map, never `contains` per heading. Checked by the review.
5. **Following an HTML entry, then using the pane again,** while the
   keyboard is in the page (#115). Pinned by Task 4's modes, which reopen
   the pane from the toolbar.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t18-2b) { Remove-Item -Recurse -Force E:\work\desktop-guides\t18-2b }; New-Item -ItemType Directory E:\work\desktop-guides\t18-2b | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t18-2b'
}
```

- **Core and Infrastructure tests:** `stage && s 'cd /d E:\work\desktop-guides\t18-2b && dotnet test tests\<project> --filter "<filter>" -nologo > E:\work\t18-2b-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" /c:"[FAIL]" E:\work\t18-2b-test.log'`
- **Production build:** `s 'cd /d E:\work\desktop-guides\t18-2b && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t18-2b-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t18-2b-build.log'`
- **Generated fixtures:** run `python3 -I tools/p0/make_fixtures.py` locally. Commit the manifest; `generated/` is ignored and CI regenerates it.

---
### Task 1: `HtmlOutlineRules`

**Files:**
- Create: `src/DesktopGuides.Core/Html/HtmlOutlineRules.cs`
- Test: `tests/DesktopGuides.Core.Tests/HtmlOutlineRulesTests.cs`

**Interfaces:**
- Consumes: PR a's `OutlineTree`, `OutlineItem` and `OutlineSource`;
  `HtmlLocationRules.Capture` and `HtmlCapture`.
- Produces: `HtmlOutlineRules.Build(string? json, string contentSha256, string documentPath)`,
  which returns an `Outline`. The reply's shape is:

```json
{ "headings": [ { "level": 2, "text": "Forest", "offset": 120, "quote": "Forest ...", "id": "forest", "fraction": 0.1 } ],
  "links": [ { "text": "Forest", "container": 0, "depth": 1,
               "target": { "offset": 120, "quote": "Forest ...", "id": "forest", "fraction": 0.1 } } ] }
```

A link whose target doesn't exist has `"target": null`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/HtmlOutlineRulesTests.cs`:

```csharp
using System.Text.Json;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlOutlineRulesTests
{
    private const string Sha = "ABCDEF";
    private const string Path = "guide.html";

    private static object Place(int offset, string id) =>
        new { offset, quote = $"text at {offset}", id, fraction = offset / 10_000.0 };

    private static object Heading(int level, string text, int offset) =>
        new { level, text, offset, quote = $"{text} text", id = text.ToLowerInvariant(), fraction = offset / 10_000.0 };

    private static object Link(string text, int container, int depth, int? target) =>
        new { text, container, depth, target = target is int at ? Place(at, text.ToLowerInvariant()) : null };

    private static Outline Build(object[] headings, object[] links) =>
        HtmlOutlineRules.Build(JsonSerializer.Serialize(new { headings, links }), Sha, Path);

    [Fact]
    public void HeadingsNestByLevel()
    {
        Outline outline = Build([Heading(1, "Guide", 0), Heading(2, "Forest", 100), Heading(2, "Castle", 200)], []);

        Assert.Equal(OutlineSource.Document, outline.Source);
        OutlineEntry guide = Assert.Single(outline.Entries);
        Assert.Equal(["Forest", "Castle"], guide.Children.Select(e => e.Title));
        Assert.Equal(200, guide.Children[1].Order);
        HtmlPosition position = Assert.IsType<HtmlPosition>(guide.Children[1].Location.Payload);
        Assert.Equal(200, position.TextOffset);
        Assert.Equal("castle", position.ElementId);
        Assert.Equal(Path, position.DocumentPath);
    }

    [Fact]
    public void ThePagesOwnContentsListWins()
    {
        Outline outline = Build([Heading(2, "Forest", 100), Heading(2, "Castle", 200)],
            [Link("Start here", 0, 1, 50), Link("The forest", 0, 1, 100), Link("North path", 0, 2, 150), Link("The castle", 0, 1, 200)]);

        Assert.Equal(["Start here", "The forest", "The castle"], outline.Entries.Select(e => e.Title));
        Assert.Equal(["North path"], outline.Entries[1].Children.Select(e => e.Title));
        Assert.Equal(150, outline.Entries[1].Children[0].Order);
    }

    [Fact]
    public void AShortListLeavesTheHeadings() =>
        Assert.Equal(["Forest", "Castle"], Build([Heading(2, "Forest", 100), Heading(2, "Castle", 200)],
            [Link("Top", 0, 1, 0), Link("Forest", 0, 1, 100)]).Entries.Select(e => e.Title));

    [Fact]
    public void LinksWithoutTargetsDontCount() =>
        Assert.Equal(["Forest", "Castle"], Build([Heading(2, "Forest", 100), Heading(2, "Castle", 200)],
            [Link("A", 0, 1, 10), Link("B", 0, 1, null), Link("C", 0, 1, 30)]).Entries.Select(e => e.Title));

    [Fact]
    public void TheLargestListWins() =>
        Assert.Equal(["One", "Two", "Three", "Four"], Build([],
            [Link("x", 0, 1, 1), Link("y", 0, 1, 2), Link("z", 0, 1, 3),
             Link("One", 1, 1, 10), Link("Two", 1, 1, 20), Link("Three", 1, 1, 30), Link("Four", 1, 1, 40)])
            .Entries.Select(e => e.Title));

    [Fact]
    public void TitlesCollapseWhitespaceAndEmptyOnesAreSkipped() =>
        Assert.Equal(["The forest"], Build([Heading(2, "The\n   forest", 100), Heading(2, "   ", 200)], [])
            .Entries.Select(e => e.Title));

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"headings\":[]}")]
    [InlineData("{\"headings\":[{\"level\":\"two\"}],\"links\":[]}")]
    public void ARepliesThatCantBeReadIsNoOutline(string? json) =>
        Assert.Same(Outline.Empty, HtmlOutlineRules.Build(json, Sha, Path));

    [Fact]
    public void NothingFoundIsNoOutline() => Assert.Same(Outline.Empty, Build([], []));
}
```

A note on the theory: **the malformed reply** (`"level": "two"`) makes the whole
  reply unreadable. The reply comes from our own script, so a wrong
  shape means a broken script, and no outline is the honest answer.

- [ ] **Step 2: Run them to see them fail**

Run: the Core tests, with filter `FullyQualifiedName~HtmlOutlineRulesTests`.
Expected: the build fails: `HtmlOutlineRules` doesn't exist.

- [ ] **Step 3: Implement**

Create `src/DesktopGuides.Core/Html/HtmlOutlineRules.cs`:

```csharp
using System.Text.Json;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

/// <summary>
/// T18.2: an HTML page's contents from the collect script's reply. The
/// page's own contents list (three or more same-page links with real
/// targets in one container) wins over its headings; each entry is the
/// HTML position the reader already restores.
/// </summary>
public static class HtmlOutlineRules
{
    public const int MinimumListLinks = 3;

    private sealed record Place(int Offset, string? Quote, string? Id, double Fraction);
    private sealed record Heading(int Level, string Text, Place Place);
    private sealed record Link(string Text, int Container, int Depth, Place? Target);

    public static Outline Build(string? json, string contentSha256, string documentPath)
    {
        if (json is null) return Outline.Empty;
        List<Heading> headings = [];
        List<Link> links = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("headings", out JsonElement headingList) || headingList.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("links", out JsonElement linkList) || linkList.ValueKind != JsonValueKind.Array)
            {
                return Outline.Empty;
            }
            foreach (JsonElement item in headingList.EnumerateArray())
            {
                headings.Add(new Heading(item.GetProperty("level").GetInt32(), item.GetProperty("text").GetString() ?? "",
                    ReadPlace(item)));
            }
            foreach (JsonElement item in linkList.EnumerateArray())
            {
                JsonElement target = item.GetProperty("target");
                links.Add(new Link(item.GetProperty("text").GetString() ?? "", item.GetProperty("container").GetInt32(),
                    item.GetProperty("depth").GetInt32(), target.ValueKind == JsonValueKind.Null ? null : ReadPlace(target)));
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // Our own script's reply in the wrong shape: no outline.
            return Outline.Empty;
        }

        List<Link>? list = links.Where(link => link.Target is not null)
            .GroupBy(link => link.Container)
            .Where(group => group.Count() >= MinimumListLinks)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault()?.ToList();
        IEnumerable<OutlineItem> items = list is not null
            ? list.Select(link => Item(link.Text, link.Depth, link.Target!, contentSha256, documentPath))
            : headings.Select(heading => Item(heading.Text, heading.Level, heading.Place, contentSha256, documentPath));
        return OutlineTree.Build(OutlineSource.Document, items.Where(item => item.Title.Length > 0).ToList());
    }

    private static Place ReadPlace(JsonElement item) => new(
        item.GetProperty("offset").GetInt32(),
        item.TryGetProperty("quote", out JsonElement quote) && quote.ValueKind == JsonValueKind.String ? quote.GetString() : null,
        item.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
        Math.Clamp(item.GetProperty("fraction").GetDouble(), 0, 1));

    private static OutlineItem Item(string text, int level, Place place, string contentSha256, string documentPath) =>
        new(string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), level,
            HtmlLocationRules.Capture(contentSha256, documentPath,
                new HtmlCapture(place.Offset, place.Quote, string.IsNullOrEmpty(place.Id) ? null : place.Id, place.Fraction)),
            place.Offset);
}
```

`OrderByDescending` is stable, so between two lists of the same size, the
earlier one in the reply (in document order) wins.

- [ ] **Step 4: Run them to see them pass, then the whole suite**

Expected: 12 `HtmlOutlineRulesTests` pass (7 facts and a 5-case theory),
then the full Core suite (1250 before, plus 12).

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Html/HtmlOutlineRules.cs tests/DesktopGuides.Core.Tests/HtmlOutlineRulesTests.cs
git commit -m "feat(p2): build an HTML page's contents from its headings or its own contents list"
```

---
### Task 2: The PDF outline: a fixture and `PdfPageTextSource.GetOutlineAsync`

**Files:**
- Modify: `tools/p0/make_fixtures.py` (`outline_pdf()`, as
  `generated/pdf-outline.pdf`) and `tests/fixtures/p0/manifest.json`
  (regenerated)
- Modify: `src/DesktopGuides.Infrastructure/Reading/PdfPageTextSource.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/PdfPageTextSourceTests.cs`

**Interfaces:**
- Produces:
  - `sealed record PdfOutlineNode(string Title, int PageIndex, IReadOnlyList<PdfOutlineNode> Children)`,
    where `PageIndex` is 0-based;
  - `PdfPageTextSource.GetOutlineAsync(CancellationToken token)`, which
    returns `IReadOnlyList<PdfOutlineNode>`, empty for no outline.

- [ ] **Step 1: The fixture**

In `make_fixtures.py`, before `def tagged_pdf()`:

```python
def outline_pdf() -> bytes:
    """T18.2: six text pages and a nested outline, with a website link and
    a link to another file that the contents must leave out."""
    page_count = 6
    first_page = 12  # object number of page 1; each page is followed by its content
    page_ref = lambda n: first_page + 2 * (n - 1)
    def dest(n: int) -> str:
        return f"/Dest [{page_ref(n)} 0 R /XYZ 0 792 0]"
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R /Outlines 3 0 R /PageMode /UseOutlines >>",
        ("<< /Type /Pages /Count 6 /Kids [" + " ".join(f"{page_ref(n)} 0 R" for n in range(1, 7)) + "] >>").encode(),
        b"<< /Type /Outlines /First 4 0 R /Last 9 0 R /Count 7 >>",
        f"<< /Title (Part One) /Parent 3 0 R /First 5 0 R /Last 6 0 R /Count 2 /Next 7 0 R {dest(1)} >>".encode(),
        f"<< /Title (Chapter 1) /Parent 4 0 R /Next 6 0 R {dest(2)} >>".encode(),
        f"<< /Title (Chapter 2) /Parent 4 0 R /Prev 5 0 R {dest(3)} >>".encode(),
        f"<< /Title (Part Two) /Parent 3 0 R /Prev 4 0 R /Next 8 0 R /First 10 0 R /Last 10 0 R /Count 1 {dest(4)} >>".encode(),
        b"<< /Title (Website) /Parent 3 0 R /Prev 7 0 R /Next 9 0 R /A << /S /URI /URI (https://example.com/guide) >> >>",
        b"<< /Title (Other guide) /Parent 3 0 R /Prev 8 0 R /A << /S /GoToR /F (other.pdf) /D [0 /Fit] >> >>",
        f"<< /Title (Chapter 3) /Parent 7 0 R {dest(5)} >>".encode(),
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ]
    for n in range(1, page_count + 1):
        objects.append(
            f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 11 0 R >> >> /Contents {page_ref(n) + 1} 0 R >>".encode())
        commands = f"BT /F1 20 Tf 72 720 Td (Outline guide - page {n} of {page_count}) Tj ET".encode()
        objects.append(f"<< /Length {len(commands)} >>\nstream\n".encode() + commands + b"\nendstream")
    return write_pdf(objects)
```

Next to the other `add(...)` calls:

```python
    add(
        "generated/pdf-outline.pdf",
        outline_pdf(),
        "pdf",
        "Contents: a nested outline, with website and other-file links left out",
    )
```

Then run `python3 -I tools/p0/make_fixtures.py`. Check that `git diff`
shows only the script and one new manifest entry.

- [ ] **Step 2: Write the failing tests**

In `PdfPageTextSourceTests`, add:

```csharp
    [Fact]
    public async Task TheOutlineKeepsThisDocumentsPagesAndLeavesOutLinks()
    {
        using PdfPageTextSource source = Open("generated/pdf-outline.pdf");

        IReadOnlyList<PdfOutlineNode> outline = await source.GetOutlineAsync(CancellationToken.None);

        Assert.Equal(["Part One", "Part Two"], outline.Select(n => n.Title));
        Assert.Equal([0, 3], outline.Select(n => n.PageIndex));
        Assert.Equal(["Chapter 1", "Chapter 2"], outline[0].Children.Select(n => n.Title));
        Assert.Equal([1, 2], outline[0].Children.Select(n => n.PageIndex));
        Assert.Equal(4, Assert.Single(outline[1].Children).PageIndex);
    }

    [Fact]
    public async Task ADocumentWithoutAnOutlineHasNone()
    {
        using PdfPageTextSource source = Open(Long);

        Assert.Empty(await source.GetOutlineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ACancelledOutlineReadStops()
    {
        using PdfPageTextSource source = Open("generated/pdf-outline.pdf");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.GetOutlineAsync(new CancellationToken(canceled: true)));
    }
```

- [ ] **Step 3: Run them to see them fail**

Run: the Infrastructure tests, with filter `FullyQualifiedName~PdfPageTextSourceTests`.
Expected: the build fails: `GetOutlineAsync` and `PdfOutlineNode` don't
exist.

- [ ] **Step 4: Implement**

In `PdfPageTextSource.cs`, add `using UglyToad.PdfPig.Outline;`, and
after `PdfPageText` add:

```csharp
// T18.2: an outline entry that opens a page of this document (0-based).
public sealed record PdfOutlineNode(string Title, int PageIndex, IReadOnlyList<PdfOutlineNode> Children);
```

In the class, after `GetPageTextAsync`:

```csharp
    // T18.2: the document's own outline, read under the gate like a page.
    // No outline, or one PdfPig can't read, is an empty list.
    public async Task<IReadOnlyList<PdfOutlineNode>> GetOutlineAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed || closing, this);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed || closing, this);
            return await Task.Run(() => ReadOutline(token), token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private IReadOnlyList<PdfOutlineNode> ReadOutline(CancellationToken token)
    {
        reader.Token = token;
        try
        {
            return document.TryGetBookmarks(out Bookmarks? bookmarks, false) && bookmarks is not null
                ? Map(bookmarks.Roots)
                : [];
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return [];
        }
        finally
        {
            reader.Token = CancellationToken.None;
        }
    }

    // Entries that open a page here. A link to a website or another file is
    // left out, and its children move up; a grouping entry takes its first
    // child's page.
    private List<PdfOutlineNode> Map(IEnumerable<BookmarkNode> nodes)
    {
        List<PdfOutlineNode> mapped = [];
        foreach (BookmarkNode node in nodes)
        {
            List<PdfOutlineNode> children = Map(node.Children);
            int? page = node switch
            {
                ExternalBookmarkNode or EmbeddedBookmarkNode => null,
                DocumentBookmarkNode inDocument => inDocument.PageNumber - 1,
                ContainerBookmarkNode when children.Count > 0 => children[0].PageIndex,
                _ => null,
            };
            if (page is int index && index >= 0 && index < PageCount && !string.IsNullOrWhiteSpace(node.Title))
            {
                mapped.Add(new PdfOutlineNode(node.Title.Trim(), index, children));
            }
            else
            {
                mapped.AddRange(children);
            }
        }
        return mapped;
    }
```

Two things to check against the build:
- **`TryGetBookmarks`'s second parameter** is a `bool`, which reflection
  showed. If its name shows it means something other than lenient
  parsing, pass the value that keeps parsing strict, and ledger it.
- **The `switch` order** matters: `ExternalBookmarkNode` and
  `EmbeddedBookmarkNode` derive from `DocumentBookmarkNode`, so they must
  come first.

- [ ] **Step 5: Run them to see them pass, then the whole suite**

Expected: the three new tests pass, then the full Infrastructure suite
(803 before, plus 3).

- [ ] **Step 6: Commit**

```bash
git add tools/p0/make_fixtures.py tests/fixtures/p0/manifest.json src/DesktopGuides.Infrastructure tests/DesktopGuides.Infrastructure.Tests
git commit -m "feat(p2): read a PDF's own outline for its contents"
```

---
### Task 3: HTML and PDF contents in the readers

**Files:**
- Create: `src/DesktopGuides.Production/HtmlOutlineScripts.cs`
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs` and
  `src/DesktopGuides.Production/PdfReaderSession.cs` (`IOutlineReader`,
  `ReaderCapabilities.Outline`)

**Interfaces:**
- Consumes: Task 1's `HtmlOutlineRules.Build`; Task 2's
  `PdfPageTextSource.GetOutlineAsync` and `PdfOutlineNode`; PR a's
  contract; `HtmlPositionScripts.Prelude`.
- Produces: `HtmlOutlineScripts.Collect`, and both sessions as
  `IOutlineReader`.

The Production project has no unit tests. This task's gate is a clean
build. Task 4's installed modes verify the behaviour.

- [ ] **Step 1: The collect script**

Create `src/DesktopGuides.Production/HtmlOutlineScripts.cs`:

```csharp
namespace DesktopGuides.Production;

// T18.2: the collect script. It reports the page's visible headings and
// its same-page links, each with the walk position it leads to; Core's
// HtmlOutlineRules decides the contents. One pass over the walk finds each
// element's first text, so a long page stays inside the script timeout.
internal static class HtmlOutlineScripts
{
    public const string Collect = "(() => {" + HtmlPositionScripts.Prelude + """
        const w = walk();
        const body = bodyOf.call(document);
        const textOf = getter(Node.prototype, 'textContent');
        const attributeOf = Element.prototype.getAttribute;
        const positionOf = Node.prototype.compareDocumentPosition;
        const all = (selector) => Array.from(Document.prototype.querySelectorAll.call(document, selector));
        const tagOf = (element) => String(nameOf.call(element)).toUpperCase();
        // Each element's first non-blank text offset. A node's chain stops at
        // the first ancestor already set: an earlier node set the rest.
        const firstText = new Map();
        for (let i = 0; i < w.nodes.length; i++) {
          const lead = dataOf.call(w.nodes[i]).search(/\S/);
          if (lead < 0) continue;
          for (let p = parentOf.call(w.nodes[i]); p && p !== body; p = parentOf.call(p)) {
            if (firstText.has(p)) break;
            firstText.set(p, w.starts[i] + lead);
          }
        }
        // An empty anchor (<a id="forest"></a>) leads to the text after it.
        const followingText = (element) => {
          for (let i = 0; i < w.nodes.length; i++) {
            const lead = dataOf.call(w.nodes[i]).search(/\S/);
            if (lead >= 0 && (positionOf.call(element, w.nodes[i]) & Node.DOCUMENT_POSITION_FOLLOWING)) {
              return w.starts[i] + lead;
            }
          }
          return -1;
        };
        const idNear = (element) => {
          for (let p = element; p && p instanceof Element; p = parentOf.call(p)) {
            const value = idOf.call(p);
            if (value) return value.length > 128 ? null : value;
          }
          return null;
        };
        const fractionOf = (element) => {
          const height = heightOf.call(scrollingOf.call(document));
          return height > 0 ? Math.min(1, Math.max(0, (elementRect.call(element).top + scrollY) / height)) : 0;
        };
        const place = (element) => {
          const offset = firstText.has(element) ? firstText.get(element) : followingText(element);
          return offset < 0 ? null : { offset, quote: slice(w, offset, 160), id: idNear(element), fraction: fractionOf(element) };
        };
        const headings = [];
        for (const heading of all('h1,h2,h3,h4,h5,h6')) {
          if (elementRects.call(heading).length === 0) continue;
          const at = place(heading);
          if (at !== null) headings.push({ level: Number(tagOf(heading)[1]), text: String(textOf.call(heading)), ...at });
        }
        // The outermost list holds a contents list's nested lists too.
        const containers = new Map();
        const links = [];
        for (const link of all('a[href^="#"]')) {
          if (elementRects.call(link).length === 0) continue;
          const href = String(attributeOf.call(link, 'href') || '');
          let id = href.slice(1);
          try { id = decodeURIComponent(id); } catch (e) { }
          if (!id) continue;
          let container = null, depth = 0;
          for (let p = parentOf.call(link); p && p !== body && p instanceof Element; p = parentOf.call(p)) {
            const tag = tagOf(p);
            if (tag === 'UL' || tag === 'OL') { depth++; container = p; }
            else if (container === null && (tag === 'NAV' || tag === 'DL' || tag === 'TABLE')) container = p;
          }
          if (container === null) continue;
          if (!containers.has(container)) containers.set(container, containers.size);
          const target = Document.prototype.getElementById.call(document, id) ||
            all('a[name="' + CSS.escape(id) + '"]')[0] || null;
          links.push({ text: String(textOf.call(link)), container: containers.get(container),
            depth: Math.max(1, depth), target: target ? place(target) : null });
        }
        return { headings, links };
        })()
        """;
}
```

- [ ] **Step 2: The HTML session**

In `HtmlReaderSession`:
- add `IOutlineReader` to the class's interfaces;
- add `| ReaderCapabilities.Outline` to `Capabilities`;
- add, after `ClearFind`'s helpers:

```csharp
    // T18.2: the page's headings, or its own contents list. Core decides;
    // a script failure is no outline.
    public async Task<Outline> GetOutlineAsync(CancellationToken token)
    {
        if (!await entryLoad.Task.WaitAsync(token) || disposed || contentSha256 is null) return Outline.Empty;
        string? reply = await RunScriptAsync(HtmlOutlineScripts.Collect);
        token.ThrowIfCancellationRequested();
        return HtmlOutlineRules.Build(reply, contentSha256, policy.Entry.RequestPath);
    }

    public long OrderOf(ReaderLocation location) =>
        location.Payload is HtmlPosition position ? position.TextOffset : 0;
```

- [ ] **Step 3: The PDF session**

In `PdfReaderSession`:
- add `IOutlineReader` to the class's interfaces;
- add `| ReaderCapabilities.Outline` to `Capabilities`;
- add, after `ClearFind`:

```csharp
    // T18.2: the file's own outline, from the search document, so reading
    // never waits on it. Each entry opens its page from the top.
    public async Task<Outline> GetOutlineAsync(CancellationToken token)
    {
        if (document is null || disposed) return Outline.Empty;
        IReadOnlyList<PdfOutlineNode> nodes = await search.GetOutlineAsync(token);
        List<OutlineItem> items = [];
        void Add(IReadOnlyList<PdfOutlineNode> level, int depth)
        {
            foreach (PdfOutlineNode node in level)
            {
                items.Add(new OutlineItem(node.Title, depth,
                    PdfLocationRules.Capture(contentSha256, node.PageIndex, 0, pageCount), node.PageIndex));
                Add(node.Children, depth + 1);
            }
        }
        Add(nodes, 1);
        return OutlineTree.Build(OutlineSource.Document, items);
    }

    public long OrderOf(ReaderLocation location) =>
        location.Payload is PdfPosition position ? position.PageIndex : 0;
```

`search.GetOutlineAsync` throws `ObjectDisposedException` if the source
has closed. The shell's `BuildOutlineAsync` catches any exception as no
outline.

- [ ] **Step 4: Build**

Run: the Production build (after `stage`).
Expected: 0 warnings and 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(p2): give HTML and PDF guides their contents"
```

---
### Task 4: The `contents-html`, `contents-html-headings` and `contents-pdf` modes

**Files:**
- Create: `tests/fixtures/p2/html-contents/guide.html` (authored)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`seed-find` adds
  "Contents Web Guide" and "Outline PDF Guide")
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (three modes, and the mode
  lists)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-NavigationScenarios`)
- Modify: `docs/p1/e2e-testing.md` (the "Table of contents" row)

**Interfaces:**
- Consumes:
  - Task 3's sessions;
  - PR a's pane and helpers (`Get-ContentsItems`, `Wait-ContentsSelected`);
  - the smoke's `Wait-PageName`, `Wait-HtmlPosition`, `Invoke-OverflowCommand`,
    `Wait-PdfPage` and `Read-Saves`;
  - Long Web Guide, whose fixture has hostile names, for the headings mode.

- [ ] **Step 1: Author the fixture**

Create `tests/fixtures/p2/html-contents/guide.html` with this one-off
script. Commit the file, not the script:

```bash
python3 - <<'EOF'
def section(tag, id_, title, words, count):
    return [f"<{tag} id=\"{id_}\">{title}</{tag}>"] + [f"<p>{words} step {n:02}: keep to the path.</p>" for n in range(1, count + 1)]
lines = ["<!doctype html>", "<html lang=\"en\">", "<head>", "<meta charset=\"utf-8\">", "<title>Contents Web Guide</title>", "</head>", "<body>",
         "<h1>Contents Web Guide</h1>",
         "<p>An authored fixture: the page's own contents list wins over its headings.</p>",
         "<nav><ul>",
         "<li><a href=\"#basics\">Getting started</a></li>",
         "<li><a href=\"#forest\">The forest</a><ul><li><a href=\"#north\">North path</a></li></ul></li>",
         "<li><a href=\"#castle\">The castle</a></li>",
         "<li><a href=\"#nowhere\">A link with no target</a></li>",
         "</ul></nav>"]
lines += section("h2", "basics", "Basics", "Basics", 40)
lines += section("h2", "forest", "Forest", "Forest", 40)
lines += section("h3", "north", "The north path", "North", 40)
lines += section("h2", "castle", "Castle", "Castle", 40)
lines += ["</body>", "</html>"]
import os
os.makedirs("tests/fixtures/p2/html-contents", exist_ok=True)
open("tests/fixtures/p2/html-contents/guide.html", "w", newline="\n").write("\n".join(lines) + "\n")
EOF
```

- [ ] **Step 2: Seed it**

In `seed-find`, after "Sections Guide", publish
`tests/fixtures/p2/html-contents/guide.html` as "Contents Web Guide", and
`tests/fixtures/p0/generated/pdf-outline.pdf` as "Outline PDF Guide".
Add `contentsWeb` and `outlinePdf` to the JSON it prints.

- [ ] **Step 3: Write the modes**

Add `'contents-html', 'contents-html-headings', 'contents-pdf'` to both
mode lists, beside the other contents modes. Then, after
`contents-narrow`:

```powershell
            elseif ($Mode -eq 'contents-html') {
                Open-TextGuide 'Contents Web Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-PageName 'Contents Web Guide')
                [void](Wait-HtmlPosition { param($p) $p.locator } 'a first capture')
                Start-Sleep -Seconds 2
                $baseline = Read-Saves
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-VisibleById 'ReaderContentsTree')
                $names = @(Get-ContentsItems | ForEach-Object { $_.Current.Name })
                $report.contentsHtmlNames = $names
                # The page's own list, not its headings; the link with no target is left out.
                if (($names -join '|') -ne 'Getting started|The forest|North path|The castle') {
                    throw "The contents read '$($names -join ', ')'."
                }
                Wait-ContentsSelected 'Getting started'
                [System.Windows.Forms.SendKeys]::SendWait('{DOWN}{DOWN}{DOWN}{ENTER}')
                [void](Wait-HtmlPosition { param($p) $p.quote -like 'Castle*' } 'the Castle heading on top')
                $deadline = (Get-Date).AddSeconds(8)
                do {
                    Start-Sleep -Milliseconds 250
                    $afterFollow = Read-Saves
                } while ($afterFollow -le $baseline -and (Get-Date) -lt $deadline)
                if ($afterFollow -le $baseline) { throw 'Following an HTML contents entry saved nothing.' }
                # The keyboard is in the page now (#115): the toolbar's command toggles the pane.
                Invoke-OverflowCommand 'Contents'
                Wait-HiddenById 'ReaderContentsTree'
                Invoke-OverflowCommand 'Contents'
                [void](Wait-VisibleById 'ReaderContentsTree')
                Wait-ContentsSelected 'The castle'
                $report.contentsHtmlScreenshot = Save-WindowScreenshot 'contents-html'
                Invoke-Element (Wait-VisibleById 'ReaderContentsClose')
                Wait-HiddenById 'ReaderContentsTree'
                $report.phases += 'contents-html'
            }
            elseif ($Mode -eq 'contents-html-headings') {
                # Long Web Guide has no contents list, and names that shadow DOM members.
                Open-TextGuide 'Long Web Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-PageName 'Long Web Guide')
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-VisibleById 'ReaderContentsTree')
                $names = @(Get-ContentsItems | ForEach-Object { $_.Current.Name })
                $report.contentsHeadingNames = $names
                $walkthrough = [array]::IndexOf($names, 'Walkthrough')
                if ($names[0] -ne 'Long Web Guide' -or $walkthrough -lt 1) {
                    throw "The headings read '$($names -join ', ')'."
                }
                [System.Windows.Forms.SendKeys]::SendWait(('{DOWN}' * $walkthrough) + '{ENTER}')
                [void](Wait-HtmlPosition { param($p) $p.quote -like 'Walkthrough*' } 'the Walkthrough heading on top')
                $report.phases += 'contents-html-headings'
            }
            elseif ($Mode -eq 'contents-pdf') {
                Open-TextGuide 'Outline PDF Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-PdfPage 1 6 'page 1 of 6')
                Start-Sleep -Seconds 2
                $baseline = Read-Saves
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-VisibleById 'ReaderContentsTree')
                $names = @(Get-ContentsItems | ForEach-Object { $_.Current.Name })
                $report.contentsPdfNames = $names
                # The website and other-file links are left out.
                if (($names -join '|') -ne 'Part One|Chapter 1|Chapter 2|Part Two|Chapter 3') {
                    throw "The outline read '$($names -join ', ')'."
                }
                Wait-ContentsSelected 'Part One'
                [System.Windows.Forms.SendKeys]::SendWait('{DOWN}{DOWN}{DOWN}{DOWN}{ENTER}')
                [void](Wait-PdfPage 5 6 'page 5 of 6')
                $deadline = (Get-Date).AddSeconds(8)
                do {
                    Start-Sleep -Milliseconds 250
                    $afterFollow = Read-Saves
                } while ($afterFollow -le $baseline -and (Get-Date) -lt $deadline)
                if ($afterFollow -le $baseline) { throw 'Following a PDF outline entry saved nothing.' }
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                Wait-HiddenById 'ReaderContentsTree'
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-VisibleById 'ReaderContentsTree')
                Wait-ContentsSelected 'Chapter 3'
                $report.contentsPdfScreenshot = Save-WindowScreenshot 'contents-pdf'
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Wait-HiddenById 'ReaderContentsTree'
                # A PDF with no outline of its own: the fallback, never detection.
                [System.Windows.Forms.SendKeys]::SendWait('%{LEFT}')
                [void](Wait-Name 'GameHeading' $textGame)
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status 'Guide ready.')
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-Name 'ReaderContentsEmpty' 'No sections found in this guide.')
                $report.phases += 'contents-pdf'
            }
```

Read first, and adapt:
- **Shortcuts from the PDF preview.** After an entry is followed, the
  shell focuses the PDF preview. If Ctrl+Shift+C doesn't reach the shell
  from there, use `Invoke-OverflowCommand 'Contents'` instead, as the HTML
  mode does, and ledger it.
- **Long Web Guide's headings.** If its heading list has another order,
  keep the check to "the first is *Long Web Guide*, and *Walkthrough*
  follows".

- [ ] **Step 4: The installer**

In `Run-NavigationScenarios`, after `contents-narrow`:

```powershell
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsHtml = Invoke-ProgressPass 'contents-html' 0 -HtmlPosition
    if ($null -eq (& $estimateOf 'Contents Web Guide')) {
        throw 'Following an HTML contents entry stored no place.'
    }
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsHtmlHeadings = Invoke-ProgressPass 'contents-html-headings' 0 -HtmlPosition
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsPdf = Invoke-ProgressPass 'contents-pdf' 0
    if ($null -eq (& $estimateOf 'Outline PDF Guide')) {
        throw 'Following a PDF outline entry stored no place.'
    }
```

- [ ] **Step 5: Document**

In `docs/p1/e2e-testing.md`'s "Table of contents" row:
- the seed list adds Contents Web Guide and Outline PDF Guide;
- before "Screenshots", add:

  > `contents-html`: the page's own list (*Getting started*, *The forest*
  > with *North path*, *The castle*) wins over its headings, and the link
  > with no target is left out; following *The castle* puts its heading on
  > top and saves; the toolbar's *Contents* toggles the pane, because keys
  > typed in the page don't reach the shell (#115). `contents-html-headings`:
  > Long Web Guide lists its headings, and *Walkthrough* goes there.
  > `contents-pdf`: the PDF's own outline, without its website and
  > other-file links; *Chapter 3* opens page 5 and saves; reopening selects
  > it; a PDF with no outline shows the fallback.
- the screenshots add `contents-html` and `contents-pdf`.

- [ ] **Step 6: Check, commit, push and dispatch**

```bash
stage
s 'cd /d E:\work\desktop-guides\t18-2b && (powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_start_installed_task.ps1 & dotnet build tools\p1\DesktopGuides.ShellSeed -c Release -nologo) > E:\work\t18-2b-scripts.log 2>&1 & findstr /c:"passed" /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t18-2b-scripts.log'
LC_ALL=C grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git add tests/fixtures/p2 tools/p1 docs/p1/e2e-testing.md
git commit -m "test(p2): add the HTML and PDF contents scenarios to the navigation group"
git push -u origin feat/p2-t18-2-contents-html-pdf
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p2-t18-2-contents-html-pdf -f shell-scope=navigation -f shell-lane=both
```

Expected: the checks pass, and the dispatch's `navigation` shard passes on
both lanes with all the contents and find modes. Look at each screenshot.
A test can pass while the screen is wrong, as PR a's see-through overlay
showed.

---

### Task 5: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t18-2-contents-design.md` (Status; a "PR b
  verification record")
- Modify: `docs/p2/results.md` (the #118 row)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md` ("M8 in
  progress: T18.2 PR b in review"; once it merges, T18.2 and M8 are
  complete)

- [ ] **Step 1: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 2: The records.**
  - **The design's Status:** "PR a merged (#118). PR b (HTML and PDF)
    implemented on `feat/p2-t18-2-contents-html-pdf`."
  - **The "PR b verification record":** the runs, the screenshots, the
    review, and every ruling.
  - **`results.md`:** #118's row (2026-10-15, `ba76195`, T18.2 (PR a), run
    38736057394, Core 1250, Infra 803). The caveat is the re-run: the MSIX
    `html` `provider-settings` status.
  - **The status pages:** "M8 in progress: T18.2 PR a merged (#118); PR b
    (HTML and PDF contents) in review."
- [ ] **Step 3: Commit, push, open the PR, and wait for its run.**
  - **Commit:** the records and the `contents-html` and `contents-pdf`
    screenshots, under `docs/p2/evidence/ci/t18-2/`.
  - **The PR:** "T18.2 PR b: contents for HTML and PDF guides", based on
    `main`, with the task, the prerequisite (#118, merged), the outcome,
    the screenshots, the runs, every ruling and the deferred minors.
  - **Expected:** every job passes. Record the run in the PR, then stop
    for the user's merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| HTML headings, nested by level | 1, 3, 4 |
| The page's own contents list wins (three or more links with targets) | 1, 3, 4 |
| HTML entries as exact HTML positions | 1, 3, 4 |
| The PDF's own outline; website and other-file links left out | 2, 3, 4 |
| A PDF without an outline gets no detection, and shows the fallback | 2, 4 |
| A failure is no outline and never blocks reading | 1, 2, 3 |
| `contents-html`, `contents-html-headings` and `contents-pdf` on both lanes | 4 |
