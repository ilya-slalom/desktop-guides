# T19.2 PR a (The reflow planner and the view preference) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Core can split a TXT guide into prose and preserved blocks that
map exactly to source offsets. The library stores each guide's choice of
Original or Reflow view, and backup and restore carry it. There's no UI
yet; PR b adds the reflow view.

**Architecture:**
- **Core** gets `TextReflowPlanner`, which returns a `ReflowPlan` (blocks
  with source ranges, display text and segment maps) or `null` for
  "no prose".
- **`TextLocator.CaptureAt`** captures at any offset; the line-based
  `Capture` delegates to it.
- **Infrastructure** adds `LibrarySchema.Version6`, a
  `ReaderPreferences.TextView` column, and `SaveTextViewAsync`.
- **Backup needs no production change.** Tests pin the export, restore's
  `CHECK` validation, and version 5 migration.

**Tech Stack:** .NET 10, Microsoft.Data.Sqlite, xUnit.

**Spec:** [t19-2-reflow-design.md](t19-2-reflow-design.md) (approved 16
October 2026) and
[section 6 of the P2 design](../p2-technical-design.md#6-s19-txt-reflow-t192).

## Global Constraints

- **TR19.1:** planning only reads. The document's `Text`, `ContentSha256`
  and `LineStarts`, and every locator, are unchanged.
- **The blocks** split the whole normalized text into consecutive pieces:
  - block 0 starts at 0, each block ends where the next begins, and the
    last ends at `Text.Length`;
  - blank lines belong to the block before them.
- **Prose rules,** checked per paragraph (a run of non-blank lines):
  - no line is indented;
  - every line is "texty" (letters, digits, spaces and `.,;:'"!?()-`
    only, with letters and digits at least 60% of the trimmed line);
  - no line has an interior run of two or more spaces;
  - the first line holds no detector heading;
  - every line but the last is at least 60% of the guide's typical width;
  - a one-line paragraph needs that width too.

  Anything else is preserved.
- **The typical width** is the 75th percentile of the trimmed lengths of
  texty, unindented lines of 30 or more characters. With fewer than three
  such lines, there's no plan.
- **Joining prose:** trailing spaces are dropped and lines are joined with
  one space. A line-end hyphen is kept.
- **The budget** is 2 s (`TextReflowPlanner.Budget`), checked every 256
  lines. Running out gives `null`, never a partial plan.
- **The view column:** `TextView TEXT CHECK (TextView IS NULL OR TextView
  IN ('Original', 'Reflow'))` at schema 6. `NULL` reads as Original.
- **Shell rules:** no `cd` in local shell commands.

## Review Focus

1. **Prose with numbers and quotes** ("Level 12 is 'hard', isn't it?")
   must still be prose. Pinned by Task 2's
   `ProseMayHoldNumbersAndQuotes`.
2. **An empty guide, or one of blank lines only,** has no plan, and must
   not throw. Pinned by Task 2's `AnEmptyGuideHasNoPlan`.
3. **Leading blank lines before the first paragraph:** block 0 must still
   start at offset 0, and its display text must skip them. Pinned by Task
   2's `LeadingBlankLinesBelongToTheFirstBlock`.
4. **A version 5 backup with a text size set** keeps that text size after
   it migrates to schema 6. Pinned by Task 4's
   `AVersionFiveBackupRestoresAndMigrates`.
5. **A text-size save after a view save** (or the other way round) must
   not overwrite the other value. Pinned by Task 3's
   `TheViewAndTheTextSizeSaveApart`.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t19-2a) { Remove-Item -Recurse -Force E:\work\desktop-guides\t19-2a }; New-Item -ItemType Directory E:\work\desktop-guides\t19-2a | Out-Null"'
  git -C /Users/ilya.lissoboi/work/desktop-guides ls-files -z -co --exclude-standard | grep -z -v -E '^(\.claude/|az2aws)' | COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --null -cf - -T - | s 'tar -xf - -C E:\work\desktop-guides\t19-2a'
  s 'cd /d E:\work\desktop-guides\t19-2a && python tools\p0\make_fixtures.py > E:\work\t19-2a-fixtures.log 2>&1'
}
```

- **Tests:** `stage && s 'cd /d E:\work\desktop-guides\t19-2a && dotnet test tests\<project> --filter "<filter>" -nologo > E:\work\t19-2a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" /c:"[FAIL]" E:\work\t19-2a-test.log'`
- **Production build:** `s 'cd /d E:\work\desktop-guides\t19-2a && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t19-2a-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t19-2a-build.log'`

---
### Task 1: `TextLocator.CaptureAt` (Core)

**Files:**
- Modify: `src/DesktopGuides.Core/Text/TextLocator.cs`
- Test: `tests/DesktopGuides.Core.Tests/TextLocatorTests.cs`

**Interfaces:**
- Produces: `static ReaderLocation TextLocator.CaptureAt(TextGuideDocument document, int offset)`.
  The offset is clamped to `[0, Text.Length]`. `Capture(document, line)`
  becomes `CaptureAt(document, LineStarts[line])`. PR b's reflow view
  captures at a block's start offset.

- [ ] **Step 1: Write the failing tests.** In `TextLocatorTests`, after
  `CaptureUsesLineStartAndFollowingText`:

```csharp
    // T19.2: the reflow view captures at a block's start, which may be mid-line.
    [Fact]
    public void CaptureAtUsesTheOffsetAndFollowingText()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");

        ReaderLocation location = TextLocator.CaptureAt(document, 8);

        Assert.Equal(new TextPosition(8, "ta\ngamma\n"), location.Payload);
        Assert.Equal(8.0 / 17, location.EstimatedFraction);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99, 17)]
    public void CaptureAtClampsToTheText(int offset, int expected)
    {
        ReaderLocation location = TextLocator.CaptureAt(Doc("alpha\nbeta\ngamma\n"), offset);

        Assert.Equal(expected, ((TextPosition)location.Payload).CharacterOffset);
    }

    [Fact]
    public void CaptureByLineIsCaptureAtItsStart()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");

        Assert.Equal(TextLocator.CaptureAt(document, 6), TextLocator.Capture(document, 1));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-2a && dotnet test tests\DesktopGuides.Core.Tests --filter "FullyQualifiedName~TextLocatorTests" -nologo > E:\work\t19-2a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" E:\work\t19-2a-test.log'`
Expected: FAIL to compile, with `CS0117` for `TextLocator.CaptureAt`.

- [ ] **Step 3: Split `Capture`.** Replace the body of
  `Capture(TextGuideDocument document, int line)` with a delegation, and
  move the existing body into `CaptureAt`:

```csharp
    public static ReaderLocation Capture(TextGuideDocument document, int line)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<int> starts = document.LineStarts;
        return CaptureAt(document, starts[Math.Clamp(line, 0, starts.Count - 1)]);
    }

    // T19.2: any offset, not only a line's start, for the reflow view's blocks.
    public static ReaderLocation CaptureAt(TextGuideDocument document, int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        string text = document.Text;
        offset = Math.Clamp(offset, 0, text.Length);
        int length = Math.Min(ContextLength, text.Length - offset);
        // The codec rejects NUL, and the JSON writer rejects a lone high surrogate.
        int nul = text.IndexOf('\0', offset, length);
        if (nul >= 0)
        {
            length = nul - offset;
        }
        if (length > 0 && char.IsHighSurrogate(text[offset + length - 1]))
        {
            length--;
        }
        return new ReaderLocation(
            GuideFormat.Txt,
            ReaderLocationCodec.CurrentVersion,
            document.ContentSha256.ToLowerInvariant(),
            new TextPosition(offset, text.Substring(offset, length)),
            text.Length == 0 ? 0 : (double)offset / text.Length);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Core suite.
Expected: `TextLocatorTests` passes, with the 4 new cases, and the
existing ones unchanged. Core passes in full (1295 + 4 = 1299).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core/Text/TextLocator.cs tests/DesktopGuides.Core.Tests/TextLocatorTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): capture a TXT place at any offset"
```

---
### Task 2: `TextReflowPlanner` and its fixture (Core)

**Files:**
- Create: `tests/fixtures/p2/txt-reflow.txt` (authored; PR b's installed modes use it too)
- Create: `src/DesktopGuides.Core/Text/TextReflowPlanner.cs`
- Test: `tests/DesktopGuides.Core.Tests/TextReflowPlannerTests.cs`

**Interfaces:**
- Consumes: `TextGuideDocument` and `TextSectionDetector.Detect`
  (`DetectedSection(int Offset, int Level, string Title)`).
- Produces (for PR b):
  - `enum ReflowKind { Prose, Preserved }`
  - `sealed record ReflowSegment(int DisplayStart, int SourceStart, int Length)`
  - `sealed record ReflowBlock(ReflowKind Kind, int Start, int End, string Text, IReadOnlyList<ReflowSegment> Segments)` with `int SourceOffsetAt(int displayIndex)` and `int DisplayIndexOf(int sourceOffset)`
  - `sealed record ReflowPlan(IReadOnlyList<ReflowBlock> Blocks)` with `int BlockAt(int sourceOffset)`
  - `static class TextReflowPlanner` with `TimeSpan Budget` (2 s) and `ReflowPlan? Plan(TextGuideDocument document, IReadOnlyList<DetectedSection> headings, CancellationToken token)`. It returns `null` for no prose, an empty guide, or cancellation.

- [ ] **Step 1: Author the fixture.** `tests/fixtures/p2/txt-reflow.txt`,
  ASCII with LF line ends and a final newline, exactly:

```text
=======================================================
 REFLOW GUIDE
=======================================================

This guide is an authored fixture for the reflow view. Its paragraphs
are hard-wrapped near seventy columns, the way many plain text guides
were written, so the reflow view can join them into running prose.

The forest path starts east of the village. Follow the stream until the
old bridge, then take the left fork past the ruined watchtower. Wolves
roam here at night, so travel by day and keep a torch in your pack.

MAP OF THE FOREST

      N
      |
  W --+-- E          [Village]----[Bridge]
      |                  |           |
      S              [Tower]     [Cave]

Item            Location        Price
Torch           Village shop    10
Rope            Bridge camp     25

- Torch
- Rope
- Map

    code: ABCD-1234
    code: EFGH-5678

The castle gate opens only at dawn. Wait by the moat until the bell
rings twice, then cross quickly before the drawbridge rises again. A
well-timed dash saves the long detour through the eastern marsh, where
Level 12 bandits wait. They say it's "hard", and they're right-
handed archers who rarely miss.
```

  It holds a boxed heading, three hard-wrapped prose paragraphs (the last
  with numbers, quotes and a line-end hyphen), a heading line, a map, a
  table, a list and an indented block.

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Text;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextReflowPlannerTests
{
    private static TextGuideDocument Doc(string text) =>
        TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));

    private static ReflowPlan? Plan(TextGuideDocument document) =>
        TextReflowPlanner.Plan(document, TextSectionDetector.Detect(document.Text, default), default);

    private static TextGuideDocument Fixture() =>
        TextGuideDocument.Decode(File.ReadAllBytes(Path.Combine(RepoRoot(), "tests", "fixtures", "p2", "txt-reflow.txt")));

    private static string Prose(params string[] paragraphs) => string.Join("\n\n", paragraphs) + "\n";

    // Three wrapped lines near seventy columns: prose by every rule.
    private const string Wrapped =
        "The forest path starts east of the village. Follow the stream until the\n" +
        "old bridge, then take the left fork past the ruined watchtower. Wolves\n" +
        "roam here at night, so travel by day and keep a torch in your pack.";

    [Fact]
    public void TheFixtureSplitsIntoProseAndPreservedBlocks()
    {
        ReflowPlan plan = Plan(Fixture())!;

        Assert.Equal(
            [
                ReflowKind.Preserved, ReflowKind.Prose, ReflowKind.Prose, ReflowKind.Preserved,
                ReflowKind.Preserved, ReflowKind.Preserved, ReflowKind.Preserved, ReflowKind.Preserved,
                ReflowKind.Prose
            ],
            plan.Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void ProseJoinsLinesWithOneSpaceAndKeepsLineEndHyphens()
    {
        ReflowBlock last = Plan(Fixture())!.Blocks[^1];

        Assert.DoesNotContain('\n', last.Text);
        Assert.Contains("rises again. A well-timed dash", last.Text);
        Assert.Contains("they're right- handed archers", last.Text);
    }

    [Fact]
    public void PreservedBlocksKeepTheirSourceText()
    {
        TextGuideDocument document = Fixture();
        ReflowBlock map = Plan(document)!.Blocks[4];

        Assert.StartsWith("      N\n      |\n", map.Text);
        Assert.Equal(document.Text.Substring(map.Segments[0].SourceStart, map.Text.Length), map.Text);
    }

    [Fact]
    public void BlocksCoverTheTextInOrder()
    {
        TextGuideDocument document = Fixture();
        IReadOnlyList<ReflowBlock> blocks = Plan(document)!.Blocks;

        Assert.Equal(0, blocks[0].Start);
        for (int i = 1; i < blocks.Count; i++) Assert.Equal(blocks[i - 1].End, blocks[i].Start);
        Assert.Equal(document.Text.Length, blocks[^1].End);
    }

    // The exit's offset round trip: every display index maps to a source
    // offset inside its block, and back to itself.
    [Fact]
    public void EveryDisplayIndexRoundTrips()
    {
        foreach (ReflowBlock block in Plan(Fixture())!.Blocks)
        {
            for (int index = 0; index <= block.Text.Length; index++)
            {
                int source = block.SourceOffsetAt(index);
                Assert.InRange(source, block.Start, block.End);
                Assert.Equal(index, block.DisplayIndexOf(source));
            }
        }
    }

    [Fact]
    public void EverySourceOffsetFindsItsBlockInOrder()
    {
        TextGuideDocument document = Fixture();
        ReflowPlan plan = Plan(document)!;
        int lastBlock = 0;
        int lastIndex = 0;
        for (int offset = 0; offset < document.Text.Length; offset++)
        {
            int at = plan.BlockAt(offset);
            ReflowBlock block = plan.Blocks[at];
            Assert.InRange(offset, block.Start, block.End - 1);
            int index = block.DisplayIndexOf(offset);
            if (at == lastBlock) Assert.True(index >= lastIndex, $"Offset {offset} went back in block {at}.");
            (lastBlock, lastIndex) = (at, index);
        }
    }

    [Fact]
    public void ProseMayHoldNumbersAndQuotes()
    {
        string text = Prose(Wrapped,
            "Level 12 is 'hard', isn't it? The bandits wait by the eastern marsh\n" +
            "gate, and the guards say (quietly) that \"nobody\" crosses at noon.");

        Assert.All(Plan(Doc(text))!.Blocks, block => Assert.Equal(ReflowKind.Prose, block.Kind));
    }

    [Fact]
    public void AnInteriorDoubleSpaceIsPreserved()
    {
        string text = Prose(Wrapped, Wrapped.Replace("fork past", "fork  past"));

        Assert.Equal([ReflowKind.Prose, ReflowKind.Preserved], Plan(Doc(text))!.Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void AHeadingParagraphIsPreserved()
    {
        TextGuideDocument document = Doc(Prose(Wrapped, Wrapped));
        int second = document.Text.IndexOf("\n\n", StringComparison.Ordinal) + 2;

        ReflowPlan plan = TextReflowPlanner.Plan(document, [new DetectedSection(second, 1, "Forest")], default)!;

        Assert.Equal([ReflowKind.Prose, ReflowKind.Preserved], plan.Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void LeadingBlankLinesBelongToTheFirstBlock()
    {
        ReflowPlan plan = Plan(Doc("\n\n" + Prose(Wrapped, Wrapped)))!;

        Assert.Equal(0, plan.Blocks[0].Start);
        Assert.StartsWith("The forest path", plan.Blocks[0].Text);
        Assert.Equal(2, plan.Blocks[0].SourceOffsetAt(0));
    }

    [Fact]
    public void AGuideWithNoProseHasNoPlan() =>
        Assert.Null(Plan(Doc(string.Concat(Enumerable.Range(1, 400).Select(n => $"Line {n:0000} | Numbered guide text.\n")))));

    [Theory]
    [InlineData("")]
    [InlineData("\n\n   \n")]
    public void AnEmptyGuideHasNoPlan(string text) => Assert.Null(Plan(Doc(text)));

    [Fact]
    public void ACancelledPlanIsNone()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Null(TextReflowPlanner.Plan(Fixture(), [], cancelled.Token));
    }

    // TR19.1: planning only reads; the text, hash and locators are unchanged.
    [Fact]
    public void PlanningLeavesTheDocumentUnchanged()
    {
        TextGuideDocument document = Fixture();
        string text = document.Text;
        string hash = document.ContentSha256;
        int[] starts = [.. document.LineStarts];
        Reading.ReaderLocation before = TextLocator.Capture(document, 12);

        Assert.NotNull(Plan(document));

        Assert.Same(text, document.Text);
        Assert.Equal(hash, document.ContentSha256);
        Assert.Equal(starts, document.LineStarts);
        Assert.Equal(before, TextLocator.Capture(document, 12));
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DesktopGuides.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("DesktopGuides.sln not found.");
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-2a && dotnet test tests\DesktopGuides.Core.Tests --filter "FullyQualifiedName~TextReflowPlannerTests" -nologo > E:\work\t19-2a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" E:\work\t19-2a-test.log'`
Expected: FAIL to compile, with `CS0246` for `ReflowPlan`.

- [ ] **Step 4: Write the planner.** `src/DesktopGuides.Core/Text/TextReflowPlanner.cs`:

```csharp
namespace DesktopGuides.Core.Text;

public enum ReflowKind { Prose, Preserved }

// A run of display characters that came from one run of source characters.
public sealed record ReflowSegment(int DisplayStart, int SourceStart, int Length);

// T19.2: one block of the reflow view. [Start, End) is its source range,
// trailing blank lines included; Text is what it shows.
public sealed record ReflowBlock(
    ReflowKind Kind, int Start, int End, string Text, IReadOnlyList<ReflowSegment> Segments)
{
    // A joining space maps to the end of the line before it.
    public int SourceOffsetAt(int displayIndex)
    {
        ReflowSegment segment = SegmentAtDisplay(Math.Clamp(displayIndex, 0, Text.Length));
        return segment.SourceStart + Math.Min(Math.Max(displayIndex - segment.DisplayStart, 0), segment.Length);
    }

    // A source offset past a segment (trailing spaces, the line end, blank
    // lines) maps to that segment's end.
    public int DisplayIndexOf(int sourceOffset)
    {
        ReflowSegment segment = Segments[0];
        foreach (ReflowSegment candidate in Segments)
        {
            if (candidate.SourceStart > sourceOffset) break;
            segment = candidate;
        }
        return segment.DisplayStart + Math.Clamp(sourceOffset - segment.SourceStart, 0, segment.Length);
    }

    private ReflowSegment SegmentAtDisplay(int displayIndex)
    {
        ReflowSegment segment = Segments[0];
        foreach (ReflowSegment candidate in Segments)
        {
            if (candidate.DisplayStart > displayIndex) break;
            segment = candidate;
        }
        return segment;
    }
}

public sealed record ReflowPlan(IReadOnlyList<ReflowBlock> Blocks)
{
    // The block whose source range holds the offset.
    public int BlockAt(int sourceOffset)
    {
        int low = 0;
        int high = Blocks.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (Blocks[middle].Start <= sourceOffset) low = middle;
            else high = middle - 1;
        }
        return low;
    }
}

// T19.2: splits a TXT guide into prose, which the reflow view wraps, and
// preserved blocks, which keep their layout. Doubtful paragraphs are
// preserved. It only reads the document (TR19.1).
public static class TextReflowPlanner
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);
    private const string ProsePunctuation = ".,;:'\"!?()-";
    private const int MinimumWidthSample = 30;

    // Null when the guide has no prose, is empty, or the token ends first.
    public static ReflowPlan? Plan(
        TextGuideDocument document, IReadOnlyList<DetectedSection> headings, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(headings);
        try
        {
            return Build(document, headings, token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static ReflowPlan? Build(
        TextGuideDocument document, IReadOnlyList<DetectedSection> headings, CancellationToken token)
    {
        string text = document.Text;
        List<(int Start, int Length)> lines = Lines(document, token);
        int? width = TypicalWidth(text, lines, token);
        if (width is not int typical) return null;

        List<List<int>> paragraphs = [];
        List<int>? current = null;
        for (int i = 0; i < lines.Count; i++)
        {
            if (i % 256 == 0) token.ThrowIfCancellationRequested();
            if (IsBlank(text, lines[i]))
            {
                current = null;
                continue;
            }
            if (current is null)
            {
                current = [];
                paragraphs.Add(current);
            }
            current.Add(i);
        }
        if (paragraphs.Count == 0) return null;

        HashSet<int> headingOffsets = [.. headings.Select(h => h.Offset)];
        List<ReflowBlock> blocks = [];
        for (int p = 0; p < paragraphs.Count; p++)
        {
            List<int> paragraph = paragraphs[p];
            int start = p == 0 ? 0 : lines[paragraph[0]].Start;
            int end = p + 1 < paragraphs.Count ? lines[paragraphs[p + 1][0]].Start : text.Length;
            blocks.Add(IsProse(text, lines, paragraph, typical, headingOffsets)
                ? ProseBlock(text, lines, paragraph, start, end)
                : PreservedBlock(text, lines, paragraph, start, end));
        }
        return blocks.Any(b => b.Kind == ReflowKind.Prose) ? new ReflowPlan(blocks) : null;
    }

    // Each line without its newline; the empty line after a final newline is left out.
    private static List<(int Start, int Length)> Lines(TextGuideDocument document, CancellationToken token)
    {
        string text = document.Text;
        IReadOnlyList<int> starts = document.LineStarts;
        List<(int, int)> lines = [];
        for (int i = 0; i < starts.Count; i++)
        {
            if (i % 256 == 0) token.ThrowIfCancellationRequested();
            int start = starts[i];
            int next = i + 1 < starts.Count ? starts[i + 1] - 1 : text.Length;
            if (start == text.Length && i > 0) break;
            lines.Add((start, Math.Max(next - start, 0)));
        }
        return lines;
    }

    // The 75th percentile of the trimmed lengths of texty, unindented lines
    // of 30 or more characters; null with fewer than three.
    private static int? TypicalWidth(string text, List<(int Start, int Length)> lines, CancellationToken token)
    {
        List<int> lengths = [];
        for (int i = 0; i < lines.Count; i++)
        {
            if (i % 256 == 0) token.ThrowIfCancellationRequested();
            ReadOnlySpan<char> line = Line(text, lines[i]);
            int trimmed = line.TrimEnd().Length;
            if (trimmed >= MinimumWidthSample && !IsIndented(line) && IsTexty(line)) lengths.Add(trimmed);
        }
        if (lengths.Count < 3) return null;
        lengths.Sort();
        return lengths[(int)(0.75 * (lengths.Count - 1))];
    }

    private static bool IsProse(
        string text, List<(int Start, int Length)> lines, List<int> paragraph, int typical, HashSet<int> headings)
    {
        (int firstStart, int firstLength) = lines[paragraph[0]];
        if (headings.Any(offset => offset >= firstStart && offset <= firstStart + firstLength)) return false;
        double minimum = 0.6 * typical;
        for (int k = 0; k < paragraph.Count; k++)
        {
            ReadOnlySpan<char> line = Line(text, lines[paragraph[k]]);
            if (IsIndented(line) || !IsTexty(line) || line.Trim().Contains("  ", StringComparison.Ordinal)) return false;
            bool last = k == paragraph.Count - 1;
            if ((!last || paragraph.Count == 1) && line.TrimEnd().Length < minimum) return false;
        }
        return true;
    }

    // Lines joined with one space; each line's trimmed text is one segment.
    private static ReflowBlock ProseBlock(
        string text, List<(int Start, int Length)> lines, List<int> paragraph, int start, int end)
    {
        System.Text.StringBuilder display = new();
        List<ReflowSegment> segments = [];
        foreach (int index in paragraph)
        {
            (int lineStart, int _) = lines[index];
            ReadOnlySpan<char> line = Line(text, lines[index]).TrimEnd();
            if (display.Length > 0) display.Append(' ');
            segments.Add(new ReflowSegment(display.Length, lineStart, line.Length));
            display.Append(line);
        }
        return new ReflowBlock(ReflowKind.Prose, start, end, display.ToString(), segments);
    }

    // The paragraph's own text, from its first line to the end of its last.
    private static ReflowBlock PreservedBlock(
        string text, List<(int Start, int Length)> lines, List<int> paragraph, int start, int end)
    {
        int from = lines[paragraph[0]].Start;
        (int lastStart, int lastLength) = lines[paragraph[^1]];
        int length = lastStart + lastLength - from;
        return new ReflowBlock(
            ReflowKind.Preserved, start, end, text.Substring(from, length), [new ReflowSegment(0, from, length)]);
    }

    private static ReadOnlySpan<char> Line(string text, (int Start, int Length) line) =>
        text.AsSpan(line.Start, line.Length);

    private static bool IsBlank(string text, (int Start, int Length) line) => Line(text, line).IsWhiteSpace();

    private static bool IsIndented(ReadOnlySpan<char> line) => line.Length > 0 && (line[0] == ' ' || line[0] == '\t');

    // Letters, digits, spaces and plain punctuation only, mostly letters and digits.
    private static bool IsTexty(ReadOnlySpan<char> line)
    {
        ReadOnlySpan<char> trimmed = line.Trim();
        if (trimmed.IsEmpty) return false;
        int words = 0;
        foreach (char c in trimmed)
        {
            if (char.IsLetterOrDigit(c)) words++;
            else if (c != ' ' && !ProsePunctuation.Contains(c)) return false;
        }
        return words >= 0.6 * trimmed.Length;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: the Step 3 command, then the whole Core suite.
Expected: `TextReflowPlannerTests` passes, 15/15 (13 facts and a 2-row
theory). Core passes in full (1299 + 15 = 1314).

- [ ] **Step 6: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tests/fixtures/p2/txt-reflow.txt src/DesktopGuides.Core/Text/TextReflowPlanner.cs tests/DesktopGuides.Core.Tests/TextReflowPlannerTests.cs
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): plan a TXT guide's prose and preserved blocks"
```

---
### Task 3: Schema 6 and the view preference

**Files:**
- Modify: `src/DesktopGuides.Core/Library/LibraryModels.cs` (`TextViewMode`, `ReaderPreferences.TextView`)
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs` (`SaveTextViewAsync`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibrarySchema.cs` (`Version6`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs`
- Modify: `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs` (stub)
- Modify: `tests/DesktopGuides.Infrastructure.Tests/Restore/RestoreFixture.cs` (a helper for backups from before schema 6)
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`, `Restore/LibraryRestorerSwapTests.cs`

**Interfaces:**
- Produces:
  - `RestoreFixture.ReaderPreferencesBeforeVersionSix()`, the SQL that rebuilds `ReaderPreferences` exactly as versions 1 to 5 had it (Task 4 uses it)
  - `enum TextViewMode { Original, Reflow }`
  - `sealed record ReaderPreferences(Guid GuideId, double? TextScale, TextViewMode TextView = TextViewMode.Original)`
  - `Task SaveTextViewAsync(Guid guideId, TextViewMode view, CancellationToken token = default)` on `ILibraryRepository`
  - schema 6

- [ ] **Step 1: Write the failing tests.** In `SqliteLibraryRepositoryTests`,
  beside the version 5 migration tests:

```csharp
    [Fact]
    public async Task UpgradesPopulatedVersionFiveWithOriginalViews()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionFive(directory, gameId, guideId);

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Assert.Equal(6L, ReadUserVersion(directory.Paths.DatabasePath));
        Assert.Equal(TextViewMode.Original, (await repository.GetReaderPreferencesAsync(guideId))?.TextView);
    }

    [Fact]
    public async Task FailedVersionSixMigrationStaysAtVersionFive()
    {
        using TestLibrary directory = new();
        CreatePopulatedVersionFive(directory, Guid.NewGuid(), Guid.NewGuid());

        await using (SqliteLibraryRepository failing = new(directory.Paths, null, version =>
        {
            if (version == 6) throw new IOException("Injected after the v6 column was added.");
        }))
        {
            LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(() => failing.InitializeAsync());
            Assert.Equal(LibraryOpenIssue.MigrationFailed, error.Issue);
        }

        Assert.Equal(5L, ReadUserVersion(directory.Paths.DatabasePath));
    }

    // Review Focus 5: each save changes only its own column.
    [Fact]
    public async Task TheViewAndTheTextSizeSaveApart()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Reflow", null, null);
        Guid guide = Guid.NewGuid();
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);

        await repository.SaveReaderPreferencesAsync(guide, 1.25);
        await repository.SaveTextViewAsync(guide, TextViewMode.Reflow);
        Assert.Equal(new ReaderPreferences(guide, 1.25, TextViewMode.Reflow), await repository.GetReaderPreferencesAsync(guide));

        await repository.SaveReaderPreferencesAsync(guide, 1.5);
        Assert.Equal(TextViewMode.Reflow, (await repository.GetReaderPreferencesAsync(guide))?.TextView);
        await repository.SaveTextViewAsync(guide, TextViewMode.Original);
        Assert.Equal(new ReaderPreferences(guide, 1.5, TextViewMode.Original), await repository.GetReaderPreferencesAsync(guide));
    }

    [Fact]
    public async Task SavingTheViewOfAMissingGuideFails()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveTextViewAsync(Guid.NewGuid(), TextViewMode.Reflow));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repository.SaveTextViewAsync(Guid.NewGuid(), (TextViewMode)7));
    }

    [Fact]
    public async Task TheColumnRefusesAnUnknownView()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Reflow", null, null);
        Guid guide = Guid.NewGuid();
        InsertGuide(directory.Paths.DatabasePath, guide, game.Id);

        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand update = connection.CreateCommand();
        update.CommandText = $"UPDATE ReaderPreferences SET TextView = 'Wide' WHERE GuideId = '{guide:N}'";
        Assert.Throws<SqliteException>(() => update.ExecuteNonQuery());
    }
```

  And the helper, beside `CreatePopulatedVersionFour`:

```csharp
    private static void CreatePopulatedVersionFive(TestLibrary directory, Guid gameId, Guid guideId)
    {
        CreatePopulatedVersionFour(directory, gameId, guideId);
        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = LibrarySchema.Version5;
        command.ExecuteNonQuery();
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-2a && dotnet test tests\DesktopGuides.Infrastructure.Tests --filter "FullyQualifiedName~VersionFive|FullyQualifiedName~VersionSix|FullyQualifiedName~SaveApart|FullyQualifiedName~ViewOfAMissingGuide|FullyQualifiedName~UnknownView" -nologo > E:\work\t19-2a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" E:\work\t19-2a-test.log'`
Expected: FAIL to compile, with `CS0246` for `TextViewMode` and `CS1061`
for `SaveTextViewAsync`.

- [ ] **Step 3: The model, the migration and the store.**
  - `LibraryModels.cs`: replace the `ReaderPreferences` record with:

```csharp
// T19.2: how a TXT guide is shown. Null in the library reads as Original.
public enum TextViewMode { Original, Reflow }

public sealed record ReaderPreferences(
    Guid GuideId, double? TextScale, TextViewMode TextView = TextViewMode.Original);
```

  - `ILibraryRepository.cs`: after `SaveReaderPreferencesAsync`:

```csharp
    // T19.2: changes only the view, so it never races a text-size save.
    Task SaveTextViewAsync(Guid guideId, TextViewMode view, CancellationToken token = default);
```

  - `ImporterFakes.cs`: after the `SaveReaderPreferencesAsync` stub:

```csharp
    public Task SaveTextViewAsync(Guid guideId, TextViewMode view, CancellationToken token = default) => throw new NotSupportedException();
```

  - `LibrarySchema.cs`: after `Version5`, add the following, then set
    `CurrentVersion = 6` and append `(6, Version6)` to `Migrations`:

```csharp
    // T19.2: a TXT guide's Original or Reflow view. Null reads as Original.
    public const string Version6 = """
        ALTER TABLE ReaderPreferences ADD COLUMN TextView TEXT
            CHECK (TextView IS NULL OR TextView IN ('Original', 'Reflow'));
        PRAGMA user_version = 6;
        """;
```

  - `SqliteLibraryRepository.GetReaderPreferencesAsync`: select
    `GuideId, TextScale, TextView`, and construct with a third argument:

```csharp
                    reader.IsDBNull(1) ? null : reader.GetDouble(1),
                    !reader.IsDBNull(2) && reader.GetString(2) == "Reflow" ? TextViewMode.Reflow : TextViewMode.Original)
```

  - And after `SaveReaderPreferencesAsync`:

```csharp
    public Task SaveTextViewAsync(Guid guideId, TextViewMode view, CancellationToken token = default)
    {
        if (!Enum.IsDefined(view)) throw new ArgumentOutOfRangeException(nameof(view));
        return WriteAsync(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ReaderPreferences SET TextView = $view WHERE GuideId = $id
                """;
            command.Parameters.AddWithValue("$id", guideId.ToString("N"));
            command.Parameters.AddWithValue("$view", view.ToString());
            RequireUpdated(command.ExecuteNonQuery(), "reader preferences");
        }, token);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command, then the whole Infrastructure suite, then Core.
Expected:
- the 5 new tests pass;
- the filter also matches the existing `UpgradesPopulatedVersionFourWithAnEmptyBookmarkTable` and `FailedVersionFiveMigrationStaysAtVersionFour`, which still pass;
- Infrastructure passes, except `LibraryRestorerSwapTests.AVersionFourBackupRestoresAndMigrates`. Its backup still has the new column, so the version 4 schema check refuses it. Step 5 fixes that test.
- Core passes in full (1314).

- [ ] **Step 5: Keep the version 4 backup a version 4 schema.** In
  `RestoreFixture.cs`, add the helper. `SQLite` stores a table's `CREATE`
  text as written, and restore compares it exactly, so the helper takes
  version 1's own statement from `LibrarySchema.Version1`:

```csharp
    // T19.2: ReaderPreferences as versions 1 to 5 created it, before schema
    // 6's TextView column, for backups from before schema 6.
    public static string ReaderPreferencesBeforeVersionSix()
    {
        string first = LibrarySchema.Version1;
        int start = first.IndexOf("CREATE TABLE ReaderPreferences", StringComparison.Ordinal);
        int end = first.IndexOf(");", start, StringComparison.Ordinal) + 2;
        return $"""
            PRAGMA foreign_keys = OFF;
            CREATE TABLE ReaderPreferencesBefore AS SELECT GuideId, TextScale FROM ReaderPreferences;
            DROP TABLE ReaderPreferences;
            {first[start..end]}
            INSERT INTO ReaderPreferences (GuideId, TextScale) SELECT GuideId, TextScale FROM ReaderPreferencesBefore;
            DROP TABLE ReaderPreferencesBefore;
            """;
    }
```

  Add `using DesktopGuides.Infrastructure.Storage;` if the file lacks it.
  In `LibraryRestorerSwapTests.AVersionFourBackupRestoresAndMigrates`,
  the backup's SQL becomes:

```csharp
        string backup = fixture.RewriteDatabase(
            RestoreFixture.ReaderPreferencesBeforeVersionSix() + " DROP TABLE Bookmarks; PRAGMA user_version = 4;",
            manifest => manifest with { SchemaVersion = 4 });
```

Run: the whole Infrastructure suite.
Expected: it passes in full (829 + 5 = 834).

- [ ] **Step 6: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add src/DesktopGuides.Core/Library src/DesktopGuides.Infrastructure/Storage tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs tests/DesktopGuides.Infrastructure.Tests/Restore
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "feat(p2): schema 6 stores each TXT guide's view"
```

---
### Task 4: Backup and restore coverage

**Files:**
- Modify: `tests/DesktopGuides.Infrastructure.Tests/ExportFixture.cs` (a Reflow view on the TXT guide)
- Test: `LibraryExporterTests.cs`, `Restore/LibraryRestorerValidationTests.cs`, `Restore/LibraryRestorerSwapTests.cs`

**Interfaces:**
- Consumes: Task 3's column and `RestoreFixture.ReaderPreferencesBeforeVersionSix()`.
- Produces: no production code. These tests pin that export, restore's
  validation and migration cover the column.

Only the snapshot test fails first; the fixture has no view yet. The
refusal and migration tests pin existing behaviour and pass when written.
After they pass, prove the refusal isn't vacuous: temporarily set the
value to `'Reflow'`, watch the test fail, then restore it.

- [ ] **Step 1: The snapshot test, failing first.** In `LibraryExporterTests.cs`:

```csharp
    [Fact]
    public async Task TheSnapshotHoldsTheTextView()
    {
        string zip = (await Export()).Path;

        Assert.Equal("Reflow", Scalar(zip, $"SELECT TextView FROM ReaderPreferences WHERE GuideId = '{fixture.TxtGuide:N}'"));
    }
```

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-2a && dotnet test tests\DesktopGuides.Infrastructure.Tests --filter "FullyQualifiedName~TheSnapshotHoldsTheTextView" -nologo > E:\work\t19-2a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"[FAIL]" E:\work\t19-2a-test.log'`
Expected: FAIL, because the value is `NULL`.

- [ ] **Step 2: Give the fixture a view.** In `ExportFixture.PopulateAsync`'s
  `Library.Execute` block, after the `TextScale` update:

```sql
            UPDATE ReaderPreferences SET TextView = 'Reflow' WHERE GuideId = '{{TxtGuide:N}}';
```

Run: the Step 1 command. Expected: PASS.

- [ ] **Step 3: The refusal and migration tests.**
  - In `LibraryRestorerValidationTests.cs`:

```csharp
    // A hand-edited backup can bypass the CHECK; integrity_check reports it.
    [Fact]
    public async Task AnUnknownTextViewIsInvalid() =>
        await Refused(fixture.RewriteDatabase(
                "PRAGMA ignore_check_constraints = ON; UPDATE ReaderPreferences SET TextView = 'Wide';"),
            LibraryRestoreIssue.DatabaseInvalid);
```

  - In `LibraryRestorerSwapTests.cs`:

```csharp
    // Review Focus 4: a backup from before schema 6 keeps its text sizes and opens as Original.
    [Fact]
    public async Task AVersionFiveBackupRestoresAndMigrates()
    {
        string backup = fixture.RewriteDatabase(
            RestoreFixture.ReaderPreferencesBeforeVersionSix() + " PRAGMA user_version = 5;",
            manifest => manifest with { SchemaVersion = 5 });
        LibraryRestoreStage stage = await fixture.StageAsync(backup);

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        await fixture.Target.RestartAsync();

        Assert.Equal(LibrarySchema.CurrentVersion.ToString(), fixture.Target.Scalar("PRAGMA user_version"));
        Assert.Equal("0", fixture.Target.Scalar("SELECT COUNT(*) FROM ReaderPreferences WHERE TextView IS NOT NULL"));
        Assert.Equal("1.25", fixture.Target.Scalar(
            $"SELECT TextScale FROM ReaderPreferences WHERE GuideId = '{fixture.Source.HtmlGuide:N}'"));
    }
```

- [ ] **Step 4: Run the new tests, prove the refusal bites, then run the suite**

Run: `stage && s 'cd /d E:\work\desktop-guides\t19-2a && dotnet test tests\DesktopGuides.Infrastructure.Tests --filter "FullyQualifiedName~TheSnapshotHoldsTheTextView|FullyQualifiedName~UnknownTextView|FullyQualifiedName~AVersionFiveBackup|FullyQualifiedName~AVersionFourBackup" -nologo > E:\work\t19-2a-test.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"[FAIL]" E:\work\t19-2a-test.log'`
Expected: 4 pass.

Then temporarily change `'Wide'` to `'Reflow'` in
`AnUnknownTextViewIsInvalid`. Rerun it, expect FAIL (a valid value
restores), and restore `'Wide'`. Then run the whole Infrastructure suite.
Expected: it passes in full (834 + 3 = 837).

- [ ] **Step 5: Commit**

```bash
git -C /Users/ilya.lissoboi/work/desktop-guides add tests/DesktopGuides.Infrastructure.Tests
git -C /Users/ilya.lissoboi/work/desktop-guides commit -m "test(p2): backups carry the TXT view, and a schema 5 backup migrates"
```

---
### Task 5: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t19-2-reflow-design.md` (Status; a "PR a verification record")
- Modify: `docs/p2/results.md` (the #129 row)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md`

- [ ] **Step 1: The full suites and the build.** Run Core (expected:
  1314), Infrastructure (expected: 837) and the Production build (expected:
  0 warnings) on the host.
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **The design's Status:** "Approved in conversation, 16 October 2026.
    PR a (the planner and the view preference) implemented on
    `feat/p2-t19-2-reflow`; PR b (the reflow view) follows."
  - **The "PR a verification record":** the tests per task, the suite
    counts, the review, and every ruling.
  - **`results.md`:** #129's row: 2026-10-16, `9313feb`, T19.1 (PR b), run
    38894293081, Core 1295, Infra 829, linking the T19.1 design's "PR b
    verification record". The note: "The Bookmarks tab; T19.1 and M9
    complete. Passed on the first attempt."
  - **The status pages:** "M9 complete (T19.1: #126 and #129). M10 in
    progress: T19.2 PR a (the reflow planner and the view preference) in
    review."
- [ ] **Step 4: Commit, push, open the PR, and wait for its run.**
  - **Commit:** "docs(p2): record T19.2 PR a, and T19.1 complete".
  - **The PR:** "T19.2 PR a: the reflow planner and the view preference",
    based on `main`. It names the task, the prerequisites (T18.2 and
    T19.1, merged) and the outcome (planning and storage, no UI yet), with
    the run, every ruling and the deferred minors. There's no UI change,
    so no screenshot.
  - **Expected:** every job passes. Record the run in the PR, then stop
    for the user's merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| Prose and preserved blocks covering the text, with source ranges | 2 |
| The prose rules; doubtful paragraphs preserved; headings preserved | 2 |
| Joining with one space; line-end hyphens kept | 2 |
| The segment map and the offset round trip | 2 |
| No prose, an empty guide or the budget gives no plan | 2 |
| TR19.1: text, hash and locators unchanged | 2 |
| The offset-based `TextLocator.CaptureAt` | 1 |
| Schema 6 `TextView`, `NULL` as Original, its own save | 3 |
| Backup carries the view; restore refuses a bad one; schema 5 migrates | 4 |
