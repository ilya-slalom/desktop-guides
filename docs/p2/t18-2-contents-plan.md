# T18.2 PR a (Contents: Contract, TXT Detection and the Pane) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** TXT guides with sections show a contents tree in a left pane.
Opening it selects the current section, and following an entry goes there
and saves the place. A guide with no sections says so and offers search.
On narrow windows the pane covers the guide and closes after an entry is
followed.

**Architecture:**
- **The contract (Core).** `IOutlineReader` and the `Outline` data, with
  `OutlineTree` building the nested tree from a flat list.
- **TXT detection (Core).** `TextSectionDetector` finds sections from the
  guide's own markup: search codes first, then underlined or boxed
  headings, then numbered ones.
- **The reader.** `TextReaderSession` turns sections into ordinary TXT
  locations.
- **The shell.** It builds the outline in the background once tracking
  starts. A `ContentsPane` control holds a `TreeView`, opened from the
  toolbar's overflow and with Ctrl+Shift+C.

**Tech Stack:** C# / .NET 10, WinUI 3, xUnit, PowerShell 5.1 and 7, UI
Automation, GitHub Actions.

**Spec:** `docs/p2/t18-2-contents-design.md`, and
`docs/p2-technical-design.md` section 4.

**Target:** T18.2, PR a of two (a: the contract, TXT and the pane; b: HTML
and PDF), on `feat/p2-t18-2-contents`.
**Prerequisites:** T18.1, complete (#113, #116 and #117, merged).

## Global Constraints

- **Detection:**
  - search codes, then underlined or boxed headings, then numbered
    headings;
  - the first signal that finds at least three sections, not all in the
    guide's first tenth, wins;
  - otherwise there is no outline (`None`);
  - a 2 s budget, and running out means `None`, never a partial tree.
- **Entries:**
  - titles trimmed to 120 characters, with an ellipsis;
  - a jump in levels nests one level down;
  - an entry's location is an ordinary TXT locator for its heading's line.
- **Failure never blocks reading.** The outline builds off the UI thread
  after the guide opens, and a failure gives `None`.
- **Text:**
  - the pane's heading is "Contents";
  - while building: "Finding sections…";
  - when empty: "No sections found in this guide.", with the link "Search
    in the guide";
  - the close button is "Close contents";
  - the toolbar command is "Contents", and the keyboard map entry is
    "Show contents", Ctrl+Shift+C, in the Reader context.
- **The pane:**
  - 300 px wide, beside the guide;
  - below 720 px it covers the guide, and closes after an entry is
    followed;
  - its open state lasts for the app session and isn't stored.
- **The saved place:** following an entry is a deliberate move, so it
  saves. Opening and closing the pane doesn't.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.
  There is no `cd` in local shell commands.

## Review Focus

1. **A search code used as a cross-reference** ("see [WLK02]") before its
   heading. The entry must land on the heading, not the reference. Pinned
   by Task 2's `ACrossReferenceBeforeTheHeadingIsSkipped`.
2. **A huge TXT guide** (10 MB) must not freeze the UI, or block opening
   when detection is slow. Pinned by Task 2's cancellation test (the
   budget gives `None`), and by Task 3, which runs detection with
   `Task.Run`.
3. **Leaving the guide while its outline builds.** A late outline must not
   appear in the next guide's pane. Pinned by Task 4's session check in
   `BuildOutlineAsync`, and by the review.
4. **Focus after following an entry,** especially when the narrow overlay
   closes itself. Pinned by Task 5's focus checks in `contents-txt` and
   `contents-narrow`.
5. **Numbered prose and contents-only guides** must not become outlines.
   This covers lines such as "1. Go to the door." and a contents block with
   no body. Pinned by Task 2's must-find-nothing tests.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t18-2a) { Remove-Item -Recurse -Force E:\work\desktop-guides\t18-2a }; New-Item -ItemType Directory E:\work\desktop-guides\t18-2a | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t18-2a'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t18-2a && dotnet test tests\DesktopGuides.Core.Tests --filter "<filter>" -nologo > E:\work\t18-2a-core.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" /c:"[FAIL]" E:\work\t18-2a-core.log'`
- **Production build:** `s 'cd /d E:\work\desktop-guides\t18-2a && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t18-2a-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t18-2a-build.log'`
- **Copying files to the host:** use `tar`, never a PowerShell `$input` pipe, which re-encodes non-ASCII text.

---
### Task 1: The outline contract and `OutlineTree`

**Files:**
- Create: `src/DesktopGuides.Core/Reading/OutlineContract.cs`
- Create: `src/DesktopGuides.Core/Reading/OutlineTree.cs`
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs`
  (`ReaderCapabilities.Outline = 256`, `ReaderCommand.Contents`, and the
  policy row `(ReaderCommand.Contents, ReaderCapabilities.Outline)`)
- Test: `tests/DesktopGuides.Core.Tests/OutlineTreeTests.cs`

**Interfaces:**
- Produces:
  - `enum OutlineSource { Document, Detected, None }`;
  - `sealed record OutlineEntry(string Title, int Level, ReaderLocation Location, long Order, IReadOnlyList<OutlineEntry> Children)`,
    where `Level` counts from 1 at the top of the tree;
  - `sealed record Outline(OutlineSource Source, IReadOnlyList<OutlineEntry> Entries)`,
    with `static Outline Empty` (`None`, no entries);
  - `readonly record struct OutlineItem(string Title, int Level, ReaderLocation Location, long Order)`,
    a flat input item whose `Level` is the source's own level (heading
    depth, `=`/`-`, number depth);
  - `interface IOutlineReader { Task<Outline> GetOutlineAsync(CancellationToken token); long OrderOf(ReaderLocation location); }`;
  - `OutlineTree.Build(OutlineSource source, IEnumerable<OutlineItem> items)`,
    which returns `Outline.Empty` when there are no items;
  - `OutlineTree.EntryAt(Outline outline, long order)`, which returns
    `OutlineEntry?`: the last entry, in reading order, whose `Order` is at
    or before `order`;
  - `OutlineTree.Flatten(IEnumerable<OutlineEntry> entries)`, in reading
    order;
  - `OutlineTree.MaxTitleLength = 120`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/OutlineTreeTests.cs`:

```csharp
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

    [Fact]
    public void ContentsNeedsTheOutlineCapability()
    {
        Assert.Contains(ReaderCommand.Contents, ReaderCommandPolicy.VisibleCommands(new CapabilitySession(ReaderCapabilities.Outline)));
        Assert.DoesNotContain(ReaderCommand.Contents, ReaderCommandPolicy.VisibleCommands(new CapabilitySession(ReaderCapabilities.Find)));
    }
}
```

`CapabilitySession` is a minimal `IReaderSession` with fixed
capabilities. Read the existing `ReaderCommandPolicy` tests first. If they
already have such a stub, reuse it; otherwise add a private nested class
here, implementing `IReaderSession` with `NotSupportedException` bodies.
Ledger which you used.

- [ ] **Step 2: Run them to see them fail**

Run: the Core tests, with filter `FullyQualifiedName~OutlineTreeTests`.
Expected: the build fails: `OutlineTree`, `OutlineItem` and
`ReaderCommand.Contents` don't exist.

- [ ] **Step 3: Implement**

In `ReaderContract.cs`:
- `ReaderCapabilities` gains `Outline = 256`;
- `ReaderCommand` gains `Contents` after `Find`;
- the policy table gains `(ReaderCommand.Contents, ReaderCapabilities.Outline)`.

Create `src/DesktopGuides.Core/Reading/OutlineContract.cs`:

```csharp
namespace DesktopGuides.Core.Reading;

// T18.2: a guide's table of contents. A session that implements
// IOutlineReader declares ReaderCapabilities.Outline.
public enum OutlineSource { Document, Detected, None }

// Level counts from 1 at the top of the tree; Order is the place in
// reading order (the TXT offset, the HTML walk offset, the PDF page).
public sealed record OutlineEntry(
    string Title, int Level, ReaderLocation Location, long Order, IReadOnlyList<OutlineEntry> Children);

public sealed record Outline(OutlineSource Source, IReadOnlyList<OutlineEntry> Entries)
{
    public static Outline Empty { get; } = new(OutlineSource.None, []);
}

// A flat entry before nesting; Level is the source's own level.
public readonly record struct OutlineItem(string Title, int Level, ReaderLocation Location, long Order);

public interface IOutlineReader
{
    Task<Outline> GetOutlineAsync(CancellationToken token);

    // The current place's Order, to select the entry it falls in.
    long OrderOf(ReaderLocation location);
}
```

Create `src/DesktopGuides.Core/Reading/OutlineTree.cs`:

```csharp
namespace DesktopGuides.Core.Reading;

public static class OutlineTree
{
    public const int MaxTitleLength = 120;

    // Each item nests under the nearest earlier item of a lower level, so a
    // jump of levels (h1 then h4) nests one down, never into empty levels.
    public static Outline Build(OutlineSource source, IEnumerable<OutlineItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        List<Node> roots = [];
        Stack<Node> path = new();
        foreach (OutlineItem item in items)
        {
            while (path.Count > 0 && path.Peek().Item.Level >= item.Level) path.Pop();
            Node node = new(item, path.Count + 1);
            (path.Count == 0 ? roots : path.Peek().Children).Add(node);
            path.Push(node);
        }
        return roots.Count == 0 ? Outline.Empty : new Outline(source, roots.Select(Freeze).ToArray());
    }

    public static OutlineEntry? EntryAt(Outline outline, long order)
    {
        ArgumentNullException.ThrowIfNull(outline);
        OutlineEntry? found = null;
        foreach (OutlineEntry entry in Flatten(outline.Entries))
        {
            if (entry.Order > order) break;
            found = entry;
        }
        return found;
    }

    public static IEnumerable<OutlineEntry> Flatten(IEnumerable<OutlineEntry> entries)
    {
        foreach (OutlineEntry entry in entries)
        {
            yield return entry;
            foreach (OutlineEntry child in Flatten(entry.Children)) yield return child;
        }
    }

    private static OutlineEntry Freeze(Node node) =>
        new(Trim(node.Item.Title), node.Depth, node.Item.Location, node.Item.Order,
            node.Children.Select(Freeze).ToArray());

    private static string Trim(string title)
    {
        string trimmed = title.Trim();
        if (trimmed.Length <= MaxTitleLength) return trimmed;
        int length = MaxTitleLength - 1;
        if (char.IsHighSurrogate(trimmed[length - 1])) length--;
        return trimmed[..length] + "…";
    }

    private sealed record Node(OutlineItem Item, int Depth)
    {
        public List<Node> Children { get; } = [];
    }
}
```

`EntryAt` relies on reading order, so it stops at the first entry past
`order`. An entry whose high surrogate is trimmed is one character
shorter than 120. The trim test uses ASCII, so it reads exactly 120.

- [ ] **Step 4: Run them to see them pass, then the whole suite and the build**

Expected: the filtered run passes (8 tests); the full Core suite passes
(1221 before, plus 8); the Production build has 0 warnings. A `switch`
over `ReaderCommand` that now misses `Contents` fails the build; add the
case it needs (the toolbar gets its button in Task 4), and ledger it.

- [ ] **Step 5: Commit**

```bash
git add src tests/DesktopGuides.Core.Tests
git commit -m "feat(p2): add the outline contract and its tree"
```

---
### Task 2: `TextSectionDetector`

**Files:**
- Create: `src/DesktopGuides.Core/Text/TextSectionDetector.cs`
- Test: `tests/DesktopGuides.Core.Tests/TextSectionDetectorTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct DetectedSection(int Offset, int Level, string Title)`,
    where `Offset` is the start of the heading's line in the normalized
    text;
  - `TextSectionDetector.Detect(string text, CancellationToken token)`,
    which returns `IReadOnlyList<DetectedSection>` in offset order, or an
    empty list for no outline;
  - `TextSectionDetector.Budget` (2 s) and `MinimumSections` (3).

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/TextSectionDetectorTests.cs`:

```csharp
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextSectionDetectorTests
{
    private static string Filler(int lines) =>
        string.Concat(Enumerable.Range(1, lines).Select(n => $"Step {n:000} - keep walking along the path.\n"));

    private static IReadOnlyList<DetectedSection> Detect(string text) =>
        TextSectionDetector.Detect(text, CancellationToken.None);

    private static string Boxed(string title, string code) =>
        $"==========================================\n {title,-30} [{code}]\n==========================================\n";

    private const string Contents =
        "GUIDE TITLE\n\nCONTENTS\n" +
        "  Introduction ............ [INT00]\n" +
        "  Forest .................. [WLK01]\n" +
        "  Castle .................. [WLK02]\n" +
        "  Bosses .................. [BOS00]\n\n";

    private static string CodedGuide() =>
        Contents +
        Boxed("Introduction", "INT00") + Filler(40) +
        Boxed("Forest", "WLK01") + Filler(40) +
        Boxed("Castle", "WLK02") + Filler(40) +
        Boxed("Bosses", "BOS00") + Filler(40);

    [Fact]
    public void SearchCodesFindTheSectionsAfterTheContents()
    {
        string text = CodedGuide();

        IReadOnlyList<DetectedSection> sections = Detect(text);

        Assert.Equal(["Introduction", "Forest", "Castle", "Bosses"], sections.Select(s => s.Title));
        Assert.All(sections, s => Assert.Equal(1, s.Level));
        Assert.Equal(text.IndexOf(" Castle", text.IndexOf("[WLK02]", StringComparison.Ordinal) + 1, StringComparison.Ordinal),
            sections[2].Offset);
    }

    [Fact]
    public void ACrossReferenceBeforeTheHeadingIsSkipped()
    {
        string text = Contents +
            Boxed("Introduction", "INT00") + "If you get lost, see [WLK02] for the castle.\n" + Filler(40) +
            Boxed("Forest", "WLK01") + Filler(40) +
            Boxed("Castle", "WLK02") + Filler(40) +
            Boxed("Bosses", "BOS00") + Filler(40);

        DetectedSection castle = Detect(text).Single(s => s.Title == "Castle");

        Assert.Equal(text.LastIndexOf(" Castle", StringComparison.Ordinal), castle.Offset);
    }

    [Fact]
    public void DottedSearchCodesNestByDepth()
    {
        string text = "CONTENTS\n  Basics [1.0]\n  Controls [1.1]\n  Walkthrough [2.0]\n\n" +
            Boxed("Basics", "1.0") + Filler(30) + Boxed("Controls", "1.1") + Filler(30) +
            Boxed("Walkthrough", "2.0") + Filler(30);

        Assert.Equal([1, 2, 1], Detect(text).Select(s => s.Level));
    }

    [Fact]
    public void UnderlinedHeadingsAreFound()
    {
        string text = "Intro text.\n" + Filler(5) +
            "Walkthrough\n===========\n" + Filler(30) +
            "The forest\n----------\n" + Filler(30) +
            "Bosses\n======\n" + Filler(30);

        IReadOnlyList<DetectedSection> sections = Detect(text);

        Assert.Equal(["Walkthrough", "The forest", "Bosses"], sections.Select(s => s.Title));
        Assert.Equal([1, 2, 1], sections.Select(s => s.Level));
        Assert.Equal(text.IndexOf("The forest", StringComparison.Ordinal), sections[1].Offset);
    }

    [Fact]
    public void BoxedHeadingsAreFoundWhateverTheRuleLength()
    {
        string rule = new('=', 70) + "\n";
        string text = Filler(5) + rule + " WALKTHROUGH\n" + rule + Filler(30) +
            rule + " BOSSES\n" + rule + Filler(30) + rule + " SECRETS\n" + rule + Filler(30);

        Assert.Equal(["WALKTHROUGH", "BOSSES", "SECRETS"], Detect(text).Select(s => s.Title));
    }

    [Fact]
    public void NumberedHeadingsNestAndSkipTheContentsCopy()
    {
        string text = "CONTENTS\n1.0 Basics ........ 3\n1.1 Controls ...... 4\n2.0 Walkthrough ... 9\n\n" +
            Filler(10) + "1.0 Basics\n" + Filler(30) + "1.1 Controls\n" + Filler(30) + "2.0 Walkthrough\n" + Filler(30);

        IReadOnlyList<DetectedSection> sections = Detect(text);

        Assert.Equal(["Basics", "Controls", "Walkthrough"], sections.Select(s => s.Title));
        Assert.Equal([1, 2, 1], sections.Select(s => s.Level));
        Assert.Equal(text.LastIndexOf("1.0 Basics", StringComparison.Ordinal), sections[0].Offset);
    }

    [Fact]
    public void SearchCodesWinOverHeadings()
    {
        string text = CodedGuide() + "Appendix\n========\n" + Filler(5);

        Assert.DoesNotContain(Detect(text), s => s.Title == "Appendix");
    }

    [Fact]
    public void AsciiArtIsNotAnOutline() =>
        Assert.Empty(Detect(Filler(20) +
            "+-------+-------+\n|  Hall |  Keep |\n+-------+-------+\n|  Well |  Gate |\n+-------+-------+\n" +
            "   /\\\n  /  \\\n /____\\\n" + Filler(40)));

    [Fact]
    public void TwoSectionsAreNotEnough() =>
        Assert.Empty(Detect("Walkthrough\n===========\n" + Filler(30) + "Bosses\n======\n" + Filler(30)));

    [Fact]
    public void NumberedProseIsNotAnOutline() =>
        Assert.Empty(Detect(Filler(10) +
            "1. Go to the door and open it.\n" + Filler(20) +
            "2. Take the key from the table.\n" + Filler(20) +
            "3. Return to the hall and use it.\n" + Filler(20)));

    [Fact]
    public void AContentsBlockWithNoBodyIsNotAnOutline() =>
        Assert.Empty(Detect(Contents + Filler(200)));

    [Fact]
    public void SectionsOnlyInTheFirstTenthAreNotAnOutline() =>
        Assert.Empty(Detect("Aaa\n===\n" + "Bbb\n===\n" + "Ccc\n===\n" + "Ddd\n===\n" + Filler(400)));

    [Fact]
    public void ACancelledDetectionFindsNothing()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Empty(TextSectionDetector.Detect(CodedGuide(), cancelled.Token));
    }
}
```

Read before running:
- **The first-tenth case.** `SectionsOnlyInTheFirstTenthAreNotAnOutline`'s
  headings ("Aaa" to "Ddd") are underlined with three-character rules, the
  shortest that count. So the only reason they're rejected is that they all
  sit in the first tenth.
- **The cancelled token.** `CancellationToken` is checked only every 256
  lines, so the pre-cancelled token must also be checked between the
  signals, as the implementation below does.

- [ ] **Step 2: Run them to see them fail**

Run: the Core tests, with filter `FullyQualifiedName~TextSectionDetectorTests`.
Expected: the build fails: `TextSectionDetector` doesn't exist.

- [ ] **Step 3: Implement**

Create `src/DesktopGuides.Core/Text/TextSectionDetector.cs`:

```csharp
using System.Text.RegularExpressions;

namespace DesktopGuides.Core.Text;

public readonly record struct DetectedSection(int Offset, int Level, string Title);

/// <summary>
/// T18.2: a TXT guide's sections, from its own markup. Search codes are the
/// strongest signal, then underlined or boxed headings, then numbered ones.
/// The first signal that finds three sections, not all in the guide's first
/// tenth, wins; anything less, or running out of time, is no outline.
/// </summary>
public static partial class TextSectionDetector
{
    public const int MinimumSections = 3;
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);
    private const int MaxHeadingLength = 80;
    private const int MaxTitleLength = 60;
    private static readonly char[] Decoration = " \t=-*#~|_.:[]{}".ToCharArray();

    // [WLK01], {A12}, [1.0]: letters then digits, or a dotted number.
    [GeneratedRegex(@"[\[{](?<code>[A-Za-z]{1,6}[0-9]{1,4}[A-Za-z]?|[0-9]{1,2}(?:\.[0-9]{1,2}){1,3})[\]}]")]
    private static partial Regex SearchCode();

    [GeneratedRegex(@"^\s{0,3}(?<number>[0-9]{1,2}(?:\.[0-9]{1,2}){0,3})\.?\s+(?<title>\p{L}.*)$")]
    private static partial Regex NumberedHeading();

    // A dot leader and what follows it: "Basics ........ 12".
    [GeneratedRegex(@"\.{3,}.*$")]
    private static partial Regex Leader();

    private readonly record struct Line(int Start, string Text);

    private delegate List<DetectedSection>? Signal(List<Line> lines, CancellationToken token);

    public static IReadOnlyList<DetectedSection> Detect(string text, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<Line> lines = Split(text);
        foreach (Signal signal in new Signal[] { SearchCodes, Underlined, Numbered })
        {
            if (token.IsCancellationRequested) return [];
            List<DetectedSection>? found = signal(lines, token);
            // Out of time: no outline, never a partial one.
            if (found is null) return [];
            if (found.Count >= MinimumSections && found[^1].Offset >= text.Length / 10) return found;
        }
        return [];
    }

    private static List<DetectedSection>? SearchCodes(List<Line> lines, CancellationToken token)
    {
        Dictionary<string, List<int>> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < lines.Count; i++)
        {
            if ((i & 255) == 0 && token.IsCancellationRequested) return null;
            foreach (Match match in SearchCode().Matches(lines[i].Text))
            {
                string code = match.Groups["code"].Value.ToUpperInvariant();
                if (!seen.TryGetValue(code, out List<int>? at)) seen[code] = at = [];
                if (at.Count == 0 || at[^1] != i) at.Add(i);
            }
        }
        List<DetectedSection> found = [];
        foreach ((string code, List<int> at) in seen)
        {
            // Listed once: a contents line with no section behind it.
            if (at.Count < 2) continue;
            // The first occurrence is the contents block; the heading is the
            // first later line that reads like one, not a cross-reference.
            int line = at.Skip(1).Where(i => HeadingLike(SearchCode().Replace(lines[i].Text, "")))
                .DefaultIfEmpty(at[1]).First();
            found.Add(new DetectedSection(lines[line].Start, CodeLevel(code), Title(lines[line].Text)));
        }
        found.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return found;
    }

    private static List<DetectedSection>? Underlined(List<Line> lines, CancellationToken token)
    {
        List<DetectedSection> found = [];
        for (int i = 1; i < lines.Count; i++)
        {
            if ((i & 255) == 0 && token.IsCancellationRequested) return null;
            if (!IsRule(lines[i].Text, out char rule)) continue;
            string title = lines[i - 1].Text.Trim();
            if (title.Length == 0 || title.Length > MaxHeadingLength || !title.Any(char.IsLetter) ||
                IsRule(title, out _))
            {
                continue;
            }
            // Boxed: a rule above as well, of any length.
            bool boxed = i >= 2 && IsRule(lines[i - 2].Text, out _);
            int ruleLength = lines[i].Text.Trim().Length;
            if (!boxed && Math.Abs(ruleLength - title.Length) > Math.Max(3, title.Length / 4)) continue;
            found.Add(new DetectedSection(lines[i - 1].Start, boxed || rule == '=' ? 1 : 2, Title(title)));
        }
        return found;
    }

    private static List<DetectedSection>? Numbered(List<Line> lines, CancellationToken token)
    {
        Dictionary<string, List<DetectedSection>> byKey = new(StringComparer.Ordinal);
        List<string> order = [];
        for (int i = 0; i < lines.Count; i++)
        {
            if ((i & 255) == 0 && token.IsCancellationRequested) return null;
            string text = lines[i].Text;
            if (text.Trim().Length > MaxHeadingLength) continue;
            Match match = NumberedHeading().Match(text);
            if (!match.Success) continue;
            string raw = Leader().Replace(match.Groups["title"].Value, "").Trim();
            string title = Title(raw);
            if (title.Length == 0 || title.Length > MaxTitleLength || EndsSentence(raw)) continue;
            string number = match.Groups["number"].Value;
            string key = number + " " + title.ToUpperInvariant();
            if (!byKey.TryGetValue(key, out List<DetectedSection>? at))
            {
                byKey[key] = at = [];
                order.Add(key);
            }
            at.Add(new DetectedSection(lines[i].Start, NumberLevel(number), title));
        }
        // A heading listed in the contents comes twice: keep the body's.
        List<DetectedSection> found = order.Select(key => byKey[key] is { Count: > 1 } at ? at[1] : byKey[key][0]).ToList();
        found.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return found;
    }

    private static bool IsRule(string line, out char rule)
    {
        string trimmed = line.Trim();
        rule = trimmed.Length > 0 ? trimmed[0] : '\0';
        return trimmed.Length >= 3 && "=-~*#_".Contains(rule) && trimmed.All(c => c == rule);
    }

    private static bool HeadingLike(string remainder)
    {
        string trimmed = remainder.Trim();
        return trimmed.Length <= MaxHeadingLength && trimmed.Any(char.IsLetter) && !EndsSentence(trimmed);
    }

    // "for the castle." is a sentence; a dot leader ("....") isn't.
    private static bool EndsSentence(string text) =>
        text.Length >= 2 && text[^1] == '.' && char.IsLetterOrDigit(text[^2]);

    private static int CodeLevel(string code) => char.IsDigit(code[0]) ? NumberLevel(code) : 1;

    // "2" and "2.0" are level 1, "2.1" level 2, "2.1.3" level 3.
    private static int NumberLevel(string number)
    {
        string[] parts = number.Split('.');
        int depth = parts.Length;
        if (depth > 1 && parts[^1].TrimStart('0').Length == 0) depth--;
        return depth;
    }

    private static string Title(string line)
    {
        string text = Leader().Replace(SearchCode().Replace(line, " "), "");
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(Decoration);
    }

    private static List<Line> Split(string text)
    {
        List<Line> lines = [];
        int start = 0;
        while (true)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0)
            {
                lines.Add(new Line(start, text[start..]));
                return lines;
            }
            lines.Add(new Line(start, text[start..end]));
            start = end + 1;
        }
    }
}
```

Watch for:
- **The underlined pass:** in the cancelled-token and contents-only
  tests it runs on the contents block's lines. They have no rules, so it
  finds nothing.
- **A boxed heading:** the box's top rule, line `i - 2`, can itself look
  like an underline of the line above it. The rule must be of about that
  line's length, and a filler line is much longer, so the test texts
  don't trigger it.

- [ ] **Step 4: Run them to see them pass, then the whole suite**

Expected: 13 `TextSectionDetectorTests` pass, and then the full Core
suite. If a must-find-nothing case finds sections, use
superpowers:systematic-debugging. Never loosen a rejection test to make it
pass.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Text/TextSectionDetector.cs tests/DesktopGuides.Core.Tests/TextSectionDetectorTests.cs
git commit -m "feat(p2): detect a TXT guide's sections from its own markup"
```

---
### Task 3: Ctrl+Shift+C in the keyboard map

**Files:**
- Modify: `src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs`
- Test: `tests/DesktopGuides.Core.Tests/KeyboardShortcutsTests.cs`
  and `tests/DesktopGuides.Core.Tests/ShortcutRoutingTests.cs`

**Interfaces:**
- Produces: `ShortcutCommand.ShowContents`, listed as
  `(ShortcutContext.Reader, "Ctrl+Shift+C", ShortcutCommand.ShowContents, "Show contents")`.

- [ ] **Step 1: Write the failing tests**

In `KeyboardShortcutsTests.TheTableMatchesTheSpec`, after the `Shift+F3`
row, add:

```csharp
                (ShortcutContext.Reader, "Ctrl+Shift+C", ShortcutCommand.ShowContents, "Show contents"),
```

In `ShortcutRoutingTests`, add:

```csharp
    [Fact]
    public void ShowContentsRunsOnlyInTheReader()
    {
        Assert.Equal(ShortcutDecision.Run,
            ShortcutRouting.Resolve(ShortcutCommand.ShowContents, Reader, textInputFocused: true, dialogOpen: false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(ShortcutCommand.ShowContents, Library, textInputFocused: false, dialogOpen: false));
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(ShortcutCommand.ShowContents, Reader, textInputFocused: false, dialogOpen: true));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: the Core tests, with filter
`FullyQualifiedName~KeyboardShortcutsTests|FullyQualifiedName~ShortcutRoutingTests`.
Expected: the build fails: `ShortcutCommand.ShowContents` doesn't exist.

- [ ] **Step 3: Implement**

In `KeyboardShortcuts.cs`:
- `ShortcutCommand` gains `ShowContents` after `FindPrevious`;
- `All` gains, after the `Shift+F3` row:

```csharp
        new(ShortcutContext.Reader, "Ctrl+Shift+C", ShortcutCommand.ShowContents, "Show contents"),
```

`ShortcutRouting` needs no change. `ShowContents` isn't a typing key, so it
runs from the find box too.

- [ ] **Step 4: Run them to see them pass, then the whole suite**

Expected: the filtered tests pass, including `EveryCommandAppearsOnce`
and `TheSettingsCopyIsVerbatim`, and then the full Core suite. If the
Settings copy test lists the entries' text, add "Show contents, Ctrl+Shift+C"
there in table order, and ledger it.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs tests/DesktopGuides.Core.Tests
git commit -m "feat(p2): add Ctrl+Shift+C for the contents to the keyboard map"
```

---
### Task 4: TXT contents, the `ContentsPane` control, and the toolbar's Contents command

**Files:**
- Modify: `src/DesktopGuides.Production/TextReaderSession.cs`
  (`IOutlineReader`, `ReaderCapabilities.Outline`)
- Modify: `src/DesktopGuides.Production/TextReaderView.xaml.cs` (`FocusLines`)
- Create: `src/DesktopGuides.Production/ContentsPane.xaml` and `ContentsPane.xaml.cs`
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml` and
  `ReaderToolbar.xaml.cs` (the Contents command)

**Interfaces:**
- Consumes: Task 1's contract and `OutlineTree`; Task 2's
  `TextSectionDetector`.
- Produces:
  - `TextReaderSession : IReaderSession, IFindableReader, IOutlineReader`;
  - `TextReaderView.FocusLines()`, which returns `bool`;
  - `ContentsPane` with:
    - `ShowBuilding()`, `ShowOutline(Outline outline, bool canSearch)`,
      `Select(OutlineEntry? entry)` (the first entry when `null`),
      `FocusContent()` and `Clear()`;
    - the events `EntryInvoked` (`EventHandler<OutlineEntry>`),
      `SearchRequested` and `CloseRequested`;
    - the AutomationIds `ReaderContentsPane`, `ReaderContentsTree`,
      `ReaderContentsStatus` (building), `ReaderContentsEmpty`,
      `ReaderContentsSearch` and `ReaderContentsClose`;
  - `ReaderToolbar.ContentsRequested`, and an overflow command labelled
    "Contents", shown only with `ReaderCommand.Contents`.

The Production project has no unit tests. This task's gate is a clean
build. Task 6's installed modes verify the behaviour.

- [ ] **Step 1: The TXT session's outline**

In `TextReaderSession`:
- declare `internal sealed class TextReaderSession : IReaderSession, IFindableReader, IOutlineReader`;
- add `| ReaderCapabilities.Outline` to `Capabilities`;
- add, after `ClearFind`:

```csharp
    // T18.2: sections from the guide's own markup, found off the UI thread
    // within the detector's budget. None found, or out of time, is no outline.
    public async Task<Outline> GetOutlineAsync(CancellationToken token)
    {
        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TextSectionDetector.Budget);
        TextGuideDocument source = document;
        IReadOnlyList<DetectedSection> sections =
            await Task.Run(() => TextSectionDetector.Detect(source.Text, budget.Token), CancellationToken.None);
        token.ThrowIfCancellationRequested();
        return OutlineTree.Build(OutlineSource.Detected, sections.Select(section =>
        {
            int line = source.LineAtOffset(section.Offset);
            return new OutlineItem(section.Title, section.Level, TextLocator.Capture(source, line), source.LineStarts[line]);
        }));
    }

    public long OrderOf(ReaderLocation location) =>
        location.Payload is TextPosition position ? position.CharacterOffset : 0;
```

In `TextReaderView.xaml.cs`, after `Clear`:

```csharp
    // T18.2: following a contents entry gives the keyboard back to the text.
    public bool FocusLines() => Lines.Focus(FocusState.Programmatic);
```

- [ ] **Step 2: The pane control**

Create `src/DesktopGuides.Production/ContentsPane.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl x:Class="DesktopGuides.Production.ContentsPane"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             AutomationProperties.AutomationId="ReaderContentsPane"
             AutomationProperties.Name="Contents">
    <Grid RowSpacing="8" Padding="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <Grid>
            <TextBlock Text="Contents" VerticalAlignment="Center"
                       AutomationProperties.HeadingLevel="Level2"
                       Style="{StaticResource DesktopGuidesSectionTitleStyle}" />
            <Button x:Name="CloseButton" HorizontalAlignment="Right" Click="CloseClicked"
                    AutomationProperties.AutomationId="ReaderContentsClose"
                    AutomationProperties.Name="Close contents"
                    ToolTipService.ToolTip="Close contents (Esc)">
                <SymbolIcon Symbol="Cancel" />
            </Button>
        </Grid>
        <StackPanel Grid.Row="1" Spacing="8">
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
        </StackPanel>
        <TreeView x:Name="Tree" Grid.Row="2" Visibility="Collapsed" SelectionMode="Single"
                  ItemInvoked="TreeItemInvoked"
                  AutomationProperties.AutomationId="ReaderContentsTree"
                  AutomationProperties.Name="Contents" />
    </Grid>
</UserControl>
```

Create `src/DesktopGuides.Production/ContentsPane.xaml.cs`:

```csharp
using DesktopGuides.Core.Reading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DesktopGuides.Production;

// T18.2: the reader's Contents pane. It shows an outline and reports what
// the user picked; the shell does the moving.
public sealed partial class ContentsPane : UserControl
{
    private readonly Dictionary<TreeViewNode, OutlineEntry> entries = [];

    public ContentsPane()
    {
        InitializeComponent();
        KeyDown += PaneKeyDown;
    }

    public event EventHandler<OutlineEntry>? EntryInvoked;
    public event EventHandler? SearchRequested;
    public event EventHandler? CloseRequested;

    public void ShowBuilding()
    {
        Clear();
        Building.Visibility = Visibility.Visible;
    }

    public void ShowOutline(Outline outline, bool canSearch)
    {
        ArgumentNullException.ThrowIfNull(outline);
        Clear();
        if (outline.Entries.Count == 0)
        {
            Empty.Visibility = Visibility.Visible;
            SearchLink.Visibility = canSearch ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        foreach (OutlineEntry entry in outline.Entries) Tree.RootNodes.Add(NodeFor(entry));
        Tree.Visibility = Visibility.Visible;
    }

    // The entry that holds the place, or the first when the place comes
    // before every entry.
    public void Select(OutlineEntry? entry)
    {
        TreeViewNode? node = entry is null
            ? Tree.RootNodes.FirstOrDefault()
            : entries.FirstOrDefault(pair => ReferenceEquals(pair.Value, entry)).Key;
        if (node is not null) Tree.SelectedNode = node;
    }

    // After layout, so the selected item's container exists.
    public void FocusContent() =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (Tree.Visibility == Visibility.Visible &&
                Tree.SelectedNode is TreeViewNode node && Tree.ContainerFromNode(node) is Control item &&
                item.Focus(FocusState.Programmatic))
            {
                return;
            }
            if (SearchLink.Visibility == Visibility.Visible && SearchLink.Focus(FocusState.Programmatic)) return;
            CloseButton.Focus(FocusState.Programmatic);
        });

    public void Clear()
    {
        Tree.RootNodes.Clear();
        entries.Clear();
        Tree.Visibility = Visibility.Collapsed;
        Building.Visibility = Visibility.Collapsed;
        Empty.Visibility = Visibility.Collapsed;
        SearchLink.Visibility = Visibility.Collapsed;
    }

    private TreeViewNode NodeFor(OutlineEntry entry)
    {
        TreeViewNode node = new() { Content = entry.Title, IsExpanded = true };
        entries[node] = entry;
        foreach (OutlineEntry child in entry.Children) node.Children.Add(NodeFor(child));
        return node;
    }

    // Enter, Space or a click.
    private void TreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node && entries.TryGetValue(node, out OutlineEntry? entry))
        {
            args.Handled = true;
            EntryInvoked?.Invoke(this, entry);
        }
    }

    private void PaneKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        args.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SearchClicked(object sender, RoutedEventArgs args) => SearchRequested?.Invoke(this, EventArgs.Empty);

    private void CloseClicked(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
```

`TreeViewItemInvokedEventArgs.Handled` exists in WinUI 3. If the build
says otherwise, drop that line, and ledger it.

- [ ] **Step 3: The toolbar's Contents command**

In `ReaderToolbar.xaml`, in `SecondaryCommands`, before `FindInGuide`:

```xml
            <AppBarButton x:Name="ShowContents"
                          Label="Contents"
                          ToolTipService.ToolTip="Contents (Ctrl+Shift+C)"
                          AutomationProperties.AcceleratorKey="Ctrl+Shift+C"
                          Visibility="Collapsed"
                          Click="ShowContentsClicked">
                <AppBarButton.Icon>
                    <!-- BulletedList. -->
                    <FontIcon Glyph="&#xE8FD;" />
                </AppBarButton.Icon>
            </AppBarButton>
```

In `ReaderToolbar.xaml.cs`:
- In `RefreshCommands`, add
  `bool contents = supported.Contains(ReaderCommand.Contents);`, then
  `ShowContents.Visibility = Show(contents);`, and add `|| contents` to the
  `Commands.Visibility` condition.
- In the focus fallback list (the array ending `FindInGuide`), add
  `ShowContents` before `FindInGuide`.
- After `FindInGuideClicked`:

```csharp
    // T18.2: the shell opens or closes the Contents pane.
    public event EventHandler? ContentsRequested;

    private void ShowContentsClicked(object sender, RoutedEventArgs args) =>
        ContentsRequested?.Invoke(this, EventArgs.Empty);
```

- [ ] **Step 4: Build**

Run: the Production build (after `stage`).
Expected: 0 warnings and 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(p2): build TXT contents and add the Contents pane and command"
```

---
### Task 5: The shell: the pane's layout, opening, following and Ctrl+Shift+C

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (the reader body
  grid and the pane's host)
- Create: `src/DesktopGuides.Production/ShellWindow.Contents.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`
  (`InitializeContents()`, and `StopContents()` in `CloseReaderSessionAsync`)
- Modify: `src/DesktopGuides.Production/ShellWindow.Progress.cs` (start
  contents once tracking starts)
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` and
  `ShellWindow.PdfReader.cs` (`StopContents()` beside `CloseFind()` in the
  abandon paths)
- Modify: `src/DesktopGuides.Production/ShellWindow.Keyboard.cs`
  (Ctrl+Shift+C)

**Interfaces:**
- Consumes: Task 1's contract; Task 3's `ShortcutCommand.ShowContents`;
  Task 4's `ContentsPane`, `ReaderToolbar.ContentsRequested` and
  `TextReaderView.FocusLines`.
- Produces: the pane in the reader, with the host's AutomationId
  `ReaderContentsHost`.

- [ ] **Step 1: The layout**

In `ShellWindow.xaml`, replace

```xml
                <Border x:Name="ReaderSurfaceCard"
                        Grid.Row="2"
```

with

```xml
                <!-- T18.2: the Contents pane beside the guide; below 720 px it
                     covers the guide instead (ShellWindow.Contents.cs). -->
                <Grid x:Name="ReaderBody" Grid.Row="2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                <Border x:Name="ReaderContentsHost"
                        Width="300"
                        Margin="0,0,12,0"
                        HorizontalAlignment="Left"
                        Visibility="Collapsed"
                        Style="{StaticResource DesktopGuidesElevatedSurfaceStyle}"
                        AutomationProperties.AutomationId="ReaderContentsHost">
                    <local:ContentsPane x:Name="ReaderContents" />
                </Border>
                <Border x:Name="ReaderSurfaceCard"
                        Grid.Column="1"
```

and replace the reader panel's end (unique, just before `SettingsPanel`)

```xml
                    </Grid>
                </Border>
            </Grid>

            <Grid x:Name="SettingsPanel"
```

with

```xml
                    </Grid>
                </Border>
                </Grid>
            </Grid>

            <Grid x:Name="SettingsPanel"
```

The indentation inside `ReaderBody` stays as it was, so the diff stays
small.

- [ ] **Step 2: The pane's logic**

Create `src/DesktopGuides.Production/ShellWindow.Contents.cs`:

```csharp
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T18.2: the Contents pane. Its open state lasts for the app session;
    // each guide's outline builds in the background once tracking starts.
    private const double ContentsOverlayBelow = 720;
    private const double ContentsGap = 12;
    private bool contentsOpen;
    private Outline? outline;
    private CancellationTokenSource? outlineCancel;

    private bool ContentsNarrow => ReaderBody.ActualWidth < ContentsOverlayBelow;

    private void InitializeContents()
    {
        ReaderContents.EntryInvoked += (_, entry) => _ = FollowContentsEntryAsync(entry);
        ReaderContents.CloseRequested += (_, _) => CloseContents(focusGuide: true);
        ReaderContents.SearchRequested += (_, _) =>
        {
            if (ContentsNarrow) CloseContents(focusGuide: false);
            _ = OpenFindAsync();
        };
        ReaderActions.ContentsRequested += (_, _) => ToggleContents();
        ReaderBody.SizeChanged += (_, _) => UpdateContentsLayout();
    }

    // Called once a session's tracking starts, so a restore has run.
    private void StartContents(IReaderSession session)
    {
        StopContents();
        if (session is not IOutlineReader reader) return;
        CancellationTokenSource cancel = new();
        outlineCancel = cancel;
        if (contentsOpen)
        {
            ShowContentsHost();
            ReaderContents.ShowBuilding();
        }
        _ = BuildOutlineAsync(session, reader, cancel.Token);
    }

    private async Task BuildOutlineAsync(IReaderSession session, IOutlineReader reader, CancellationToken token)
    {
        Outline built;
        try
        {
            built = await reader.GetOutlineAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // A failure is no outline; reading goes on.
            built = Outline.Empty;
        }
        // The guide was left, or another opened, while this one built.
        if (token.IsCancellationRequested || !ReferenceEquals(session, readerSession)) return;
        outline = built;
        if (ReaderContentsHost.Visibility == Visibility.Visible) await ShowOutlineAsync(focus: false);
    }

    private void ToggleContents()
    {
        if (ReaderContentsHost.Visibility == Visibility.Visible) CloseContents(focusGuide: true);
        else _ = OpenContentsAsync();
    }

    private bool ToggleContentsFromKeyboard()
    {
        // Before tracking starts there's no outline to show yet.
        if (readerSession is not IOutlineReader || outlineCancel is null) return false;
        ToggleContents();
        return true;
    }

    private async Task OpenContentsAsync()
    {
        if (readerSession is not IOutlineReader || outlineCancel is null) return;
        contentsOpen = true;
        ShowContentsHost();
        if (outline is null)
        {
            ReaderContents.ShowBuilding();
            ReaderContents.FocusContent();
            return;
        }
        await ShowOutlineAsync(focus: true);
    }

    private async Task ShowOutlineAsync(bool focus)
    {
        IReaderSession? session = readerSession;
        if (outline is not Outline shown || session is not IOutlineReader reader) return;
        ReaderContents.ShowOutline(shown, (session.Capabilities & ReaderCapabilities.Find) != 0);
        OutlineEntry? current = null;
        try
        {
            ReaderLocation place = await session.GetLocationAsync(CancellationToken.None);
            current = OutlineTree.EntryAt(shown, reader.OrderOf(place));
        }
        catch (Exception)
        {
            // No place yet: the first entry is selected.
        }
        if (!ReferenceEquals(session, readerSession) || !ReferenceEquals(shown, outline)) return;
        ReaderContents.Select(current);
        if (focus) ReaderContents.FocusContent();
        if (focus && shown.Entries.Count == 0) AnnounceStatus("No sections found in this guide.");
    }

    // A deliberate move: the restore raises LocationChanged, so the place saves.
    private async Task FollowContentsEntryAsync(OutlineEntry entry)
    {
        if (readerSession is not IReaderSession session) return;
        RestoreOutcome outcome;
        try
        {
            outcome = await session.RestoreLocationAsync(entry.Location, CancellationToken.None);
        }
        catch (Exception)
        {
            ShowErrorStatus("Couldn't go to that section.");
            return;
        }
        if (!ReferenceEquals(session, readerSession)) return;
        if (outcome.Kind == RestoreKind.Approximate) ShowReaderNotice(ApproximateRestoreMessage);
        else if (outcome.Kind == RestoreKind.Unavailable) ShowErrorStatus("Couldn't go to that section.");
        if (ContentsNarrow) CloseContents(focusGuide: false);
        FocusReaderContent();
    }

    private void CloseContents(bool focusGuide)
    {
        contentsOpen = false;
        HideContentsHost();
        if (focusGuide) FocusReaderContent();
    }

    // Also called when the reader closes. The open state is kept, so the
    // next guide opens with the pane as the user left it.
    private void StopContents()
    {
        outlineCancel?.Cancel();
        outlineCancel?.Dispose();
        outlineCancel = null;
        outline = null;
        ReaderContents.Clear();
        HideContentsHost();
    }

    private void ShowContentsHost()
    {
        ReaderContentsHost.Visibility = Visibility.Visible;
        UpdateContentsLayout();
    }

    private void HideContentsHost() => ReaderContentsHost.Visibility = Visibility.Collapsed;

    // Beside the guide when there's room; over it, without the gap, when not.
    private void UpdateContentsLayout()
    {
        bool narrow = ContentsNarrow;
        Grid.SetColumn(ReaderContentsHost, narrow ? 1 : 0);
        Canvas.SetZIndex(ReaderContentsHost, narrow ? 1 : 0);
        ReaderContentsHost.Margin = new Thickness(0, 0, narrow ? 0 : ContentsGap, 0);
    }

    private void FocusReaderContent()
    {
        bool focused = readerSession switch
        {
            TextReaderSession text => text.View.FocusLines(),
            PdfReaderSession pdf => pdf.View.FocusPreview(),
            HtmlReaderSession html => html.View.Focus(FocusState.Programmatic),
            _ => false
        };
        if (!focused) ReaderActions.Focus(FocusState.Programmatic);
    }
}
```

Read first:
- **The notice text:** `ApproximateRestoreMessage` is the constant
  `ShowRestoreStatus` uses.
- **The status methods:** `ShowErrorStatus` and `AnnounceStatus` are the
  shell's existing ones. Use them as they are, and adapt only if their
  signatures differ. Ledger any change.

- [ ] **Step 3: Wire it in**

- **`ShellWindow.xaml.cs`:**
  - after `InitializeFind();`, add `InitializeContents();`;
  - in `CloseReaderSessionAsync`, after `CloseFind();`, add `StopContents();`.
- **`ShellWindow.HtmlReader.cs` and `ShellWindow.PdfReader.cs`:** in each
  abandon path, after `CloseFind();`, add `StopContents();`.
- **`ShellWindow.Progress.cs`:** after
  `progressTracking = progress!.Track(guide.Id, session, baseline);`, add
  `StartContents(session);`.
- **`ShellWindow.Keyboard.cs`:**
  - give `AddShortcut` a modifiers parameter,
    `VirtualKeyModifiers modifiers = VirtualKeyModifiers.Control`, and use
    it in `new KeyboardAccelerator { Key = key, Modifiers = modifiers }`;
  - in `InitializeKeyboard`, add:

```csharp
        AddShortcut(VirtualKey.C, ShortcutCommand.ShowContents, ToggleContentsFromKeyboard,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift);
```

- [ ] **Step 4: Build**

Run: the Production build (after `stage`).
Expected: 0 warnings and 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(p2): open the Contents pane in the reader and follow its entries"
```

---
### Task 6: The `contents-txt`, `contents-none` and `contents-narrow` modes

**Files:**
- Create: `tests/fixtures/p2/txt-sections.txt` (authored)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`seed-find` also
  adds "Sections Guide")
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (three modes, the mode
  lists, and the `find-*` branch's condition and game choice)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-NavigationScenarios`)
- Modify: `tools/p1/DesktopGuides.ReaderToolbarSmoke/ToolbarWindow.cs` and
  `tools/p1/windows_reader_toolbar_ui_smoke.ps1` (the Contents command)
- Modify: `docs/p1/e2e-testing.md` (a "Table of contents" row)

**Interfaces:**
- Consumes: Task 4's AutomationIds and the "Contents" command; Task 5's
  pane; the smoke's `Open-TextGuide`, `Wait-FirstTextRow`, `Get-TopRowName`,
  `Read-Saves` and `Resize-ShellWindow`.

- [ ] **Step 1: Author the fixture**

Create `tests/fixtures/p2/txt-sections.txt` with this one-off script. Run
it once, commit the file, and don't commit the script:

```bash
python3 - <<'EOF'
sections = [("Introduction", "INT00"), ("Walkthrough: Forest", "WLK01"), ("Walkthrough: Castle", "WLK02"),
            ("Bosses", "BOS00"), ("FAQ", "FAQ00")]
lines = ["SECTIONS GUIDE", "An authored fixture for the contents pane.", "", "CONTENTS"]
lines += [f"  {title} {'.' * (26 - len(title))} [{code}]" for title, code in sections] + [""]
for title, code in sections:
    lines += ["=" * 44, f" {title:<30} [{code}]", "=" * 44]
    lines += [f"Step {n:03} - {title.lower()} notes for this step." for n in range(1, 61)] + [""]
open("tests/fixtures/p2/txt-sections.txt", "w", newline="\n").write("\n".join(lines) + "\n")
EOF
```

Check it with the detector before going on. Core tests don't read
repository files (they build fixtures in temporary folders), so the test
generates the same text as the script. Add to `TextSectionDetectorTests`:

```csharp
    // The same text as tests/fixtures/p2/txt-sections.txt, the installed fixture.
    private static string SectionsFixture()
    {
        (string Title, string Code)[] sections =
            [("Introduction", "INT00"), ("Walkthrough: Forest", "WLK01"), ("Walkthrough: Castle", "WLK02"),
             ("Bosses", "BOS00"), ("FAQ", "FAQ00")];
        List<string> lines = ["SECTIONS GUIDE", "An authored fixture for the contents pane.", "", "CONTENTS"];
        lines.AddRange(sections.Select(s => $"  {s.Title} {new string('.', 26 - s.Title.Length)} [{s.Code}]"));
        lines.Add("");
        foreach ((string title, string code) in sections)
        {
            lines.Add(new string('=', 44));
            lines.Add($" {title,-30} [{code}]");
            lines.Add(new string('=', 44));
            lines.AddRange(Enumerable.Range(1, 60).Select(n => $"Step {n:000} - {title.ToLowerInvariant()} notes for this step."));
            lines.Add("");
        }
        return string.Join("\n", lines) + "\n";
    }

    [Fact]
    public void TheInstalledFixtureHasFiveSections() =>
        Assert.Equal(["Introduction", "Walkthrough: Forest", "Walkthrough: Castle", "Bosses", "FAQ"],
            Detect(SectionsFixture()).Select(s => s.Title));
```

The test and the script must produce the same text. To check, write the
helper's output to a scratch file in a scratch test run on the host, and
compare it with `tests/fixtures/p2/txt-sections.txt` byte for byte. Don't
commit the scratch test. Ledger the result.

- [ ] **Step 2: Seed it**

In `seed-find`, after "Lines PDF Guide", publish
`tests/fixtures/p2/txt-sections.txt` the same way, titled "Sections Guide".
Add `sections` to the JSON it prints.

- [ ] **Step 3: Write the modes**

In `windows_shell_ui_smoke.ps1`:
- add `'contents-txt', 'contents-none', 'contents-narrow'` to both mode
  lists, beside the find modes;
- the `$textGame` choice's `-like 'find-*'` becomes
  `-like 'find-*' -or $Mode -like 'contents-*'`;
- the branch `elseif ($Mode -like 'find-*')` becomes
  `elseif ($Mode -like 'find-*' -or $Mode -like 'contents-*')`.

In that branch's helpers, after `Get-PdfSelection`:

```powershell
            $treeItem = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::TreeItem)
            function Get-ContentsItems {
                $tree = Find-ById 'ReaderContentsTree'
                if (-not $tree) { return @() }
                return @($tree.FindAll([System.Windows.Automation.TreeScope]::Descendants, $treeItem))
            }
            function Wait-ContentsSelected([string] $name) {
                $deadline = (Get-Date).AddSeconds(10)
                $seen = 'nothing'
                do {
                    foreach ($item in Get-ContentsItems) {
                        try {
                            $pattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                            if ($pattern.Current.IsSelected) {
                                $seen = $item.Current.Name
                                if ($seen -eq $name) { return }
                            }
                        }
                        catch { }
                    }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $deadline)
                throw "Expected the contents entry '$name' selected; saw '$seen'."
            }
            # Focus is in the guide's text: the focused element sits inside ReaderTextLines.
            function Assert-FocusInGuide([string] $step) {
                $deadline = (Get-Date).AddSeconds(5)
                do {
                    $node = [System.Windows.Automation.AutomationElement]::FocusedElement
                    for ($depth = 0; $node -and $depth -lt 12; $depth++) {
                        if ($node.Current.AutomationId -eq 'ReaderTextLines') { return }
                        $node = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($node)
                    }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $deadline)
                throw "After $step the keyboard isn't in the guide's text."
            }
            function Wait-TopRowLike([string] $pattern, [string] $step) {
                $deadline = (Get-Date).AddSeconds(10)
                do {
                    $name = Get-TopRowName
                    if ($name -like $pattern) { return $name }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $deadline)
                throw "After $step the top line was '$name'; expected '$pattern'."
            }
```

Then the modes, after `find-pdf-lines`:

```powershell
            elseif ($Mode -eq 'contents-txt') {
                Open-TextGuide 'Sections Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                Start-Sleep -Seconds 2
                $baseline = Read-Saves
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-VisibleById 'ReaderContentsTree')
                $names = @(Get-ContentsItems | ForEach-Object { $_.Current.Name })
                $report.contentsNames = $names
                if (($names -join '|') -ne 'Introduction|Walkthrough: Forest|Walkthrough: Castle|Bosses|FAQ') {
                    throw "The contents read '$($names -join ', ')'."
                }
                # At the top of the guide, before every section: the first is selected.
                Wait-ContentsSelected 'Introduction'
                [System.Windows.Forms.SendKeys]::SendWait('{DOWN}{DOWN}{ENTER}')
                $report.contentsTop = Wait-TopRowLike '*Walkthrough: Castle*' 'following Walkthrough: Castle'
                Assert-FocusInGuide 'following an entry'
                # A deliberate move: the place saves.
                $deadline = (Get-Date).AddSeconds(8)
                do {
                    Start-Sleep -Milliseconds 250
                    $afterFollow = Read-Saves
                } while ($afterFollow -le $baseline -and (Get-Date) -lt $deadline)
                if ($afterFollow -le $baseline) { throw 'Following a contents entry saved nothing.' }
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-VisibleById 'ReaderContentsTree')
                Wait-ContentsSelected 'Walkthrough: Castle'
                $report.contentsScreenshot = Save-WindowScreenshot 'contents-txt'
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Wait-HiddenById 'ReaderContentsTree'
                Assert-FocusInGuide 'closing the pane'
                $report.phases += 'contents-txt'
            }
            elseif ($Mode -eq 'contents-none') {
                Open-TextGuide 'Numbered Lines Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-Name 'ReaderContentsEmpty' 'No sections found in this guide.')
                $report.contentsNoneScreenshot = Save-WindowScreenshot 'contents-none'
                Invoke-Element (Wait-VisibleById 'ReaderContentsSearch')
                [void](Wait-VisibleById 'ReaderFindBar')
                $report.phases += 'contents-none'
            }
            elseif ($Mode -eq 'contents-narrow') {
                Resize-ShellWindow 700 720
                Open-TextGuide 'Sections Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                Wait-FirstTextRow
                [System.Windows.Forms.SendKeys]::SendWait('^+c')
                [void](Wait-VisibleById 'ReaderContentsTree')
                # Narrow: the pane covers the guide's text rather than sitting beside it.
                $pane = (Wait-VisibleById 'ReaderContentsPane').Current.BoundingRectangle
                $text = (Find-ById 'ReaderTextLines').Current.BoundingRectangle
                if ($pane.Right -le $text.Left) { throw "The pane ($($pane.Left)..$($pane.Right)) sits beside the text ($($text.Left)..)." }
                $report.contentsNarrowScreenshot = Save-WindowScreenshot 'contents-narrow'
                Wait-ContentsSelected 'Introduction'
                [System.Windows.Forms.SendKeys]::SendWait('{DOWN}{DOWN}{ENTER}')
                Wait-HiddenById 'ReaderContentsTree'
                [void](Wait-TopRowLike '*Walkthrough: Castle*' 'following an entry in the overlay')
                Assert-FocusInGuide 'the overlay closing'
                $report.phases += 'contents-narrow'
            }
```

Read first, and adapt:
- **The branch's preamble** resizes the window to 1500 x 720 before each
  mode, so `contents-narrow` resizes after it.
- **The arrow keys:** if they move focus in the tree without moving the
  selection (so Enter invokes the focused item, not the selected one),
  this still works. If Enter invokes nothing, use the tree item's
  `InvokePattern` on 'Walkthrough: Castle' instead, and ledger it.
- **The pane's bounds:** if `ReaderContentsPane` has no automation peer,
  measure `ReaderContentsTree` instead, and ledger it.

- [ ] **Step 4: The installer**

In `Run-NavigationScenarios`, after `find-pdf-lines`:

```powershell
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.contentsTxt = Invoke-ProgressPass 'contents-txt' 0
    if ($null -eq (& $estimateOf 'Sections Guide')) {
        throw 'Following a contents entry stored no place.'
    }
    $report.contentsNone = Invoke-ProgressPass 'contents-none' 0
    $report.contentsNarrow = Invoke-ProgressPass 'contents-narrow' 0
```

- [ ] **Step 5: The toolbar smoke**

In `ToolbarWindow.cs`:
- the "All controls" set gains `ReaderCapabilities.Outline`;
- beside the `FindRequested` line, add:

```csharp
        toolbar.ContentsRequested += (_, _) => lastAction.Text = "Contents requested";
```

In `windows_reader_toolbar_ui_smoke.ps1`:
- **In the `TextControls` check** (`no-find-without-the-capability`), add
  `[void](Wait-HiddenByName 'Contents')` beside each `'Find in guide'`
  check.
- **Before the `find-requests-the-find-bar` step,** so that the later
  `Assert-ActionStays 'Find requested'` still holds:

```powershell
    # T18.2: Contents asks the shell for the pane.
    Invoke-Element (Wait-VisibleByName 'Contents')
    Wait-Action 'Contents requested'
    $report.phases += 'contents-requests-the-pane'
    Open-Overflow
```

Read the lines around `find-requests-the-find-bar` first. The overflow is
already open there, so the Contents step goes after `Open-Overflow`, and
the overflow is reopened for Find. Ledger the exact placement.

- [ ] **Step 6: Document**

In `docs/p1/e2e-testing.md`, after the "In-guide search" row:

```markdown
| Table of contents | `-NavigationOnly`, after `seed-find` (Sections Guide). `contents-txt`: Ctrl+Shift+C opens the pane with focus in `ReaderContentsTree`; the five sections are listed and the first is selected at the top of the guide; Down, Down, Enter goes to *Walkthrough: Castle* with focus in the text, and the place saves; reopening selects *Walkthrough: Castle*; Escape closes the pane and returns focus. `contents-none`: Numbered Lines Guide shows "No sections found in this guide.", and *Search in the guide* opens the find bar. `contents-narrow`: at 700 px the pane covers the text and closes after an entry is followed. Screenshots: `contents-txt`, `contents-none`, `contents-narrow`. | T18.2 |
```

- [ ] **Step 7: Check, commit, push and dispatch**

```bash
stage
s 'cd /d E:\work\desktop-guides\t18-2a && (powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_start_installed_task.ps1 & dotnet build tools\p1\DesktopGuides.ShellSeed -c Release -nologo & dotnet build tools\p1\DesktopGuides.ReaderToolbarSmoke -c Release -p:Platform=x64 -nologo) > E:\work\t18-2a-scripts.log 2>&1 & findstr /c:"passed" /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t18-2a-scripts.log'
LC_ALL=C grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1 tools/p1/windows_reader_toolbar_ui_smoke.ps1
git add tests/fixtures/p2 tests/DesktopGuides.Core.Tests tools/p1 docs/p1/e2e-testing.md
git commit -m "test(p2): add the TXT contents scenarios to the navigation group"
git push -u origin feat/p2-t18-2-contents
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p2-t18-2-contents -f shell-scope=navigation -f shell-lane=both
```

Expected:
- the script tests pass, both builds have 0 warnings, and the PowerShell
  files are ASCII-only;
- the dispatch's `navigation` shard passes on both lanes, with the three
  contents modes beside the find modes; `reader-toolbar-ui` passes too.

If a mode fails, use superpowers:systematic-debugging on its evidence.
First make the failing wait report what it saw.

---

### Task 7: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t18-2-contents-design.md` (Status; a "PR a
  verification record")
- Modify: `docs/p2/results.md` (the #117 row)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md`
  ("M7 complete; M8 in progress: T18.2 PR a in review")

- [ ] **Step 1: The final review.** Run the whole-branch review before the
  records, as in T18.1. Its fixes land first.
- [ ] **Step 2: The records.**
  - **The design's Status:** "PR a implemented on `feat/p2-t18-2-contents`;
    PR b (HTML and PDF) follows."
  - **The "PR a verification record":** the red and green runs, the
    dispatch, the screenshots, the review, and every ruling.
  - **`results.md`:** #117's row, in #116's format (2026-10-15, `97cd358`,
    T18.1 (PR c), run 38700767060, Core 1221, Infra 803). The caveat is the
    re-run: the portable `workflow` interactive launch.
  - **The status pages:** "M7 complete (T18.1); M8 in progress: T18.2 PR
    a (TXT contents and the pane) in review."
- [ ] **Step 3: Commit, push, open the PR, and wait for its run.**
  - **Commit:** the records and the `contents-txt`, `contents-none` and
    `contents-narrow` screenshots, under `docs/p2/evidence/ci/t18-2/`.
  - **The PR:** titled "T18.2 PR a: contents for TXT guides, and the
    Contents pane", based on `main`. The body names the task, the
    prerequisite (T18.1, complete) and the outcome. It shows the
    screenshots, and lists the dispatch, every ruling and the deferred
    minors.
  - **Expected:** every job passes. Record the run in the PR description,
    then stop for the user's merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| `IOutlineReader`, `Outline`, `OutlineEntry`, `ReaderCapabilities.Outline` | 1 |
| `OutlineTree`: nesting, level jumps, the trim, `EntryAt` | 1 |
| `TextSectionDetector`: search codes, underlined, boxed and numbered headings, the threshold, the spread, the budget | 2 |
| Ctrl+Shift+C in the keyboard map | 3, 5 |
| TXT contents as ordinary locations | 4 |
| The pane: the heading, the tree, the status, the fallback with Search, Escape | 4, 5 |
| The toolbar's Contents command, only with the capability | 4, 6 |
| The pane beside the guide; over it below 720 px, closing after an entry is followed | 5, 6 |
| The current entry selected on opening; following an entry saves the place | 5, 6 |
| The background build; failure is no outline; a late outline never reaches another guide | 5 |
| `contents-txt`, `contents-none` and `contents-narrow` on both lanes | 6 |
| HTML and PDF sources | PR b |
