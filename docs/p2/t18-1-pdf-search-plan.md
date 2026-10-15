# T18.1 PR c (PDF Search) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PDF guides can be searched from the find bar. The count shows
"N+" while pages are still being scanned. Image-only and unreadable pages
are reported. A match opens its page and is selected in the page-text
pane. The saved reading place never changes.

**Architecture:**
- **A second PdfPig document.** The loader opens a search text source
  beside the display one. Both use the same password once, because the
  password is never stored. The display source, with its 8-page cache,
  stays untouched.
- **The scan (Infrastructure).** `PdfTextScan` reads pages in order on a
  background task, through an `IPdfPageTextReader` interface, and matches
  each page with Core's `TextMatcher`. A page with no letters counts as
  image-only. A page that can't be read is recorded, and the scan goes
  on.
- **The session (Production).** `PdfReaderSession` implements
  `IFindableReader`. A step waits only as far as the scan needs to answer
  it. It goes to the match's page and selects the match in the
  `DocumentText` pane. A new `ResultUpdated` event refreshes the count when
  the scan finishes.

**Tech Stack:** C# / .NET 10, WinUI 3, PdfPig, xUnit, PowerShell 5.1 and
7, UI Automation, GitHub Actions.

**Spec:** `docs/p2/t18-1-search-design.md` (the PDF bullets, and deviation
1: the match is shown in the page-text pane, not over the page image), and
`docs/p2-technical-design.md` section 3.

**Target:** T18.1, PR c of three, on `feat/p2-t18-1-pdf-search`.
**Prerequisites:** PR a (#113) and PR b (#116), both merged.

## Global Constraints

- **Matching:** PR a's rules, unchanged. A match never crosses a page
  boundary, because each page is matched on its own. At most 10,000
  matches in the whole guide.
- **The display source is untouched.** Its 8-page cache and the installed
  `pdf-*` assertions on its peak pages stay as they are. The scan uses its
  own source, with a cache of one page.
- **The password** is used once at load, for both sources, and never
  stored.
- **Order:** the scan runs from page 1 to the last page, so a match's
  index never changes once it is found.
- **Counts:** "`<i>` of `<n>`+" while scanning, and "`<i>` of `<n>`" when
  done. Notes say "`<k>` image-only page" or "`<k>` image-only pages",
  and "Couldn't search page `<p>`" or "Couldn't search pages `<p>`,
  `<q>`" (at most three numbers, then "and `<m>` more").
- **The saved place:** a step never saves it, and closing the bar doesn't
  either. "Back to my place" restores the page and fraction the bar
  opened at.
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.
  There is no `cd` in local shell commands.

## Review Focus

1. **A large PDF, where the match is far ahead.** On a 1,000-page guide the
   reader is on page 1, and the first match is on page 800. The step must
   wait for the scan, not fail or report "No matches" early. Pinned by
   Task 2's waiting test (`NextWaitsForTheScanToReachAMatch`), and by Task
   4's `find-pdf`, which searches for a match on page 150.
2. **Closing the bar or the guide mid-scan.** The scan must stop, and the
   search source must close without blocking Back. Pinned by Task 2's
   cancellation test, and by Task 3's dispose path. That path uses the
   display source's `CloseAsync` wait.
3. **Line endings in the page text.** If PdfPig's text has `\r\n`, the
   `TextBox` holds `\r`, so a selection made from raw offsets lands too far
   along. Pinned by Task 1's `TextBoxOffsets` tests, and by Task 4's
   `find-pdf` selection check.
4. **An image-only or unreadable page.** It must be reported, never shown
   as a silent "No matches". Pinned by Task 2's tests, and by Task 4's
   `find-pdf-scan` on `pdf-scan.pdf`.
5. **Page turns while the bar is open.** A search step and a page turn race
   through the scheduler; the selection must only ever apply to its own
   page. Pinned by Task 3's apply check: a pending match applies only when
   the shown index is its page.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t18-1c) { Remove-Item -Recurse -Force E:\work\desktop-guides\t18-1c }; New-Item -ItemType Directory E:\work\desktop-guides\t18-1c | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t18-1c'
  s 'cd /d E:\work\desktop-guides\t18-1c && python tools\p0\make_fixtures.py > E:\work\t18-1c-fixtures.log 2>&1'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t18-1c && dotnet test tests\DesktopGuides.Core.Tests --filter "<filter>" -nologo > E:\work\t18-1c-core.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error CS" /c:"[FAIL]" E:\work\t18-1c-core.log'`
- **Infrastructure tests:** the same, with `tests\DesktopGuides.Infrastructure.Tests` and `t18-1c-infra.log`.
- **Production build:** `s 'cd /d E:\work\desktop-guides\t18-1c && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t18-1c-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t18-1c-build.log'`
- **Files with non-ASCII text** (JSON replies, fixtures) go to the host with `tar`, never through a PowerShell `$input` pipe, which re-encodes them.

---
### Task 1: The contract's additions, `TextBoxOffsets` and `FindResultText`

**Files:**
- Modify: `src/DesktopGuides.Core/Reading/FindContract.cs`
- Create: `src/DesktopGuides.Core/Reading/TextBoxOffsets.cs`
- Create: `src/DesktopGuides.Core/Reading/FindResultText.cs`
- Modify: `src/DesktopGuides.Production/TextReaderSession.cs` and
  `src/DesktopGuides.Production/HtmlReaderSession.cs` (an empty
  `ResultUpdated`)
- Test: `tests/DesktopGuides.Core.Tests/TextBoxOffsetsTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/FindResultTextTests.cs`

**Interfaces:**
- Produces:
  - `FindResult.UnreadablePages`, an `IReadOnlyList<int>` init property
    holding 1-based page numbers. It defaults to empty. The positional
    shape is unchanged, so PR a's and PR b's callers still compile;
  - `IFindableReader.ResultUpdated`, an `event EventHandler<FindResult>?`.
    A reader raises it when a result's totals change after the step
    returned. PDF raises it once, when the scan finishes;
  - `TextBoxOffsets.Map(string text, TextMatch match)`, which returns
    `(int Start, int Length)` in TextBox characters;
  - `FindResultText.Note(FindResult result)`, which returns the note line,
    or `null` when there is nothing to note.

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/TextBoxOffsetsTests.cs`:

```csharp
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextBoxOffsetsTests
{
    [Fact]
    public void LineFeedsMapOneToOne() =>
        Assert.Equal((2, 1), TextBoxOffsets.Map("a\nb", new TextMatch(2, 1)));

    [Fact]
    public void EachEarlierCarriageReturnLineFeedIsOneCharacter() =>
        Assert.Equal((4, 1), TextBoxOffsets.Map("x\r\ny\r\nz", new TextMatch(6, 1)));

    [Fact]
    public void AMatchAcrossALineBreakLosesOneCharacter() =>
        Assert.Equal((0, 9), TextBoxOffsets.Map("end.\r\nNext", new TextMatch(0, 10)));

    [Fact]
    public void ALoneCarriageReturnIsOneCharacter() =>
        Assert.Equal((2, 1), TextBoxOffsets.Map("a\rb", new TextMatch(2, 1)));
}
```

Create `tests/DesktopGuides.Core.Tests/FindResultTextTests.cs`:

```csharp
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class FindResultTextTests
{
    private static FindResult With(int imageOnly, params int[] unreadable) =>
        new FindResult(FindOutcome.Found, 1, 3, false, imageOnly) { UnreadablePages = unreadable };

    [Fact]
    public void NothingToNoteIsNull() => Assert.Null(FindResultText.Note(With(0)));

    [Theory]
    [InlineData(1, "1 image-only page")]
    [InlineData(4, "4 image-only pages")]
    public void ImageOnlyPagesAreCounted(int pages, string expected) =>
        Assert.Equal(expected, FindResultText.Note(With(pages)));

    [Fact]
    public void OneUnreadablePageIsNamed() =>
        Assert.Equal("Couldn't search page 14", FindResultText.Note(With(0, 14)));

    [Fact]
    public void UpToThreeUnreadablePagesAreListed() =>
        Assert.Equal("Couldn't search pages 14, 20, 31", FindResultText.Note(With(0, 14, 20, 31)));

    [Fact]
    public void MoreThanThreeAreSummed() =>
        Assert.Equal("Couldn't search pages 14, 20, 31 and 2 more", FindResultText.Note(With(0, 14, 20, 31, 40, 41)));

    [Fact]
    public void BothNotesAreJoined() =>
        Assert.Equal("2 image-only pages · Couldn't search page 9", FindResultText.Note(With(2, 9)));
}
```

- [ ] **Step 2: Run them to see them fail**

Run: the Core tests, with filter
`FullyQualifiedName~TextBoxOffsetsTests|FullyQualifiedName~FindResultTextTests`.
Expected: the build fails: `TextBoxOffsets`, `FindResultText` and
`UnreadablePages` don't exist.

- [ ] **Step 3: Implement**

In `FindContract.cs`, inside `FindResult`:

```csharp
    // 1-based numbers of pages that couldn't be read (PDF); the scan goes on.
    public IReadOnlyList<int> UnreadablePages { get; init; } = [];
```

and in `IFindableReader`:

```csharp
    // Raised when a result's totals change after the step returned, as
    // when a PDF scan finishes.
    event EventHandler<FindResult>? ResultUpdated;
```

Create `src/DesktopGuides.Core/Reading/TextBoxOffsets.cs`:

```csharp
namespace DesktopGuides.Core.Reading;

// T18.1: WinUI's TextBox keeps each line break as one '\r', so a "\r\n"
// before an offset is one character fewer in the box.
public static class TextBoxOffsets
{
    public static (int Start, int Length) Map(string text, TextMatch match)
    {
        ArgumentNullException.ThrowIfNull(text);
        int start = ToTextBox(text, match.Start);
        return (start, ToTextBox(text, match.Start + match.Length) - start);
    }

    private static int ToTextBox(string text, int offset)
    {
        int pairs = 0;
        for (int i = 0; i + 1 < offset && i + 1 < text.Length; i++)
        {
            if (text[i] == '\r' && text[i + 1] == '\n') pairs++;
        }
        return offset - pairs;
    }
}
```

Create `src/DesktopGuides.Core/Reading/FindResultText.cs`:

```csharp
using System.Globalization;

namespace DesktopGuides.Core.Reading;

// T18.1: the find bar's note under the count, for pages search couldn't read.
public static class FindResultText
{
    private const int ListedPages = 3;

    public static string? Note(FindResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        List<string> parts = [];
        if (result.UnsearchablePages > 0)
        {
            parts.Add(result.UnsearchablePages == 1
                ? "1 image-only page"
                : $"{result.UnsearchablePages} image-only pages");
        }
        IReadOnlyList<int> unreadable = result.UnreadablePages;
        if (unreadable.Count == 1)
        {
            parts.Add($"Couldn't search page {unreadable[0]}");
        }
        else if (unreadable.Count > 1)
        {
            string listed = string.Join(", ",
                unreadable.Take(ListedPages).Select(page => page.ToString(CultureInfo.InvariantCulture)));
            parts.Add(unreadable.Count > ListedPages
                ? $"Couldn't search pages {listed} and {unreadable.Count - ListedPages} more"
                : $"Couldn't search pages {listed}");
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
```

In `TextReaderSession` and `HtmlReaderSession`, beside their other events:

```csharp
    // T18.1: a step's result is final when it returns.
    public event EventHandler<FindResult>? ResultUpdated { add { } remove { } }
```

- [ ] **Step 4: Run them to see them pass, then the whole suite and the
  build**

Expected:
- the filtered run passes, 4 offset tests and 7 note tests;
- the full Core suite passes (1210 before, plus 11);
- the Production build has 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src tests/DesktopGuides.Core.Tests
git commit -m "feat(p2): report unreadable pages and late totals in the search contract"
```

---
### Task 2: `PdfTextScan`, and the loader's search source

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Reading/PdfTextScan.cs`
  (`IPdfPageTextReader`, `PdfMatch` and `PdfTextScan`)
- Modify: `src/DesktopGuides.Infrastructure/Reading/PdfPageTextSource.cs`
  (implements `IPdfPageTextReader`)
- Modify: `src/DesktopGuides.Infrastructure/Reading/ManagedPdfGuideLoader.cs`
  (`PdfGuideLoaded` gains `Search`)
- Modify: `src/DesktopGuides.Production/ShellWindow.PdfReader.cs` (the
  abandoned-load path disposes `Search` too)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/PdfTextScanTests.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedPdfGuideLoaderTests.cs`

**Interfaces:**
- Consumes: Task 1's `FindResult.UnreadablePages`; PR a's `TextMatcher`,
  `FindQuery` and `FindDirection`.
- Produces:
  - `interface IPdfPageTextReader { int PageCount { get; } Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token); }`;
  - `readonly record struct PdfMatch(int PageIndex, int Start, int Length)`;
  - `sealed class PdfTextScan : IDisposable` with:
    - `PdfTextScan(IPdfPageTextReader pages, FindQuery query, int maxMatches = TextMatcher.MaxMatches)`, which starts the scan;
    - `Task Completion`;
    - `event EventHandler? Finished`, raised once, on a worker thread,
      when the last page is done;
    - `Task<int?> StepAsync(int? current, int fromPage, FindDirection direction, CancellationToken token)`,
      where `fromPage` is 0-based;
    - `PdfMatch MatchAt(int index)`;
    - `FindResult ResultFor(int? index)`;
  - `PdfGuideLoaded(string FilePath, PdfPageTextSource Text, PdfPageTextSource Search, string ContentSha256)`.

- [ ] **Step 1: Write the failing scan tests**

Create `tests/DesktopGuides.Infrastructure.Tests/Reading/PdfTextScanTests.cs`:

```csharp
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class PdfTextScanTests
{
    // A null page can't be read. HoldAt makes one page wait for Release.
    private sealed class FakePages(params PdfPageText?[] pages) : IPdfPageTextReader
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int? HoldAt { get; init; }
        public int PageCount => pages.Length;
        public void Release() => gate.TrySetResult();

        public async Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token)
        {
            if (HoldAt == pageIndex) await gate.Task.WaitAsync(token);
            return pages[pageIndex] ?? throw new PdfPageTextException(pageIndex, new InvalidDataException("broken"));
        }
    }

    private static PdfPageText Page(string text) => new(text, text.Any(char.IsLetter), false);

    private static async Task<bool> StillWaiting(Task task)
    {
        await Task.Delay(200);
        return !task.IsCompleted;
    }

    [Fact]
    public async Task MatchesComeInPageOrderWithStableIndexes()
    {
        using PdfTextScan scan = new(new FakePages(Page("a key"), Page("none"), Page("key key")), new FindQuery("key"));
        await scan.Completion;

        Assert.Equal(0, await scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None));
        Assert.Equal(3, scan.ResultFor(0).Total);
        Assert.False(scan.ResultFor(0).TotalIsPartial);
        Assert.Equal(new PdfMatch(2, 4, 3), scan.MatchAt(2));
    }

    [Fact]
    public async Task PagesWithoutLettersAreCountedAsImageOnly()
    {
        using PdfTextScan scan = new(new FakePages(Page("key"), Page("   "), Page("12 34")), new FindQuery("key"));
        await scan.Completion;

        Assert.Equal(2, scan.ResultFor(0).UnsearchablePages);
    }

    [Fact]
    public async Task AnUnreadablePageIsRecordedAndTheScanGoesOn()
    {
        using PdfTextScan scan = new(new FakePages(Page("key"), null, Page("key")), new FindQuery("key"));
        await scan.Completion;

        FindResult result = scan.ResultFor(1);
        Assert.Equal(2, result.Total);
        Assert.Equal([2], result.UnreadablePages);
    }

    [Fact]
    public async Task NextWaitsForTheScanToReachAMatch()
    {
        FakePages pages = new(Page("x"), Page("x"), Page("key")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));

        Task<int?> step = scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None);

        Assert.True(await StillWaiting(step));
        Assert.True(scan.ResultFor(null).TotalIsPartial);
        pages.Release();
        Assert.Equal(0, await step);
        Assert.Equal(2, scan.MatchAt(0).PageIndex);
    }

    [Fact]
    public async Task PreviousNeedsOnlyTheEarlierPages()
    {
        FakePages pages = new(Page("key"), Page("x"), Page("x")) { HoldAt = 2 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));

        Assert.Equal(0, await scan.StepAsync(null, 2, FindDirection.Previous, CancellationToken.None));
        pages.Release();
    }

    [Fact]
    public async Task WrappingWaitsForTheEnd()
    {
        FakePages pages = new(Page("key"), Page("x")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));
        Assert.Equal(0, await scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None));

        Task<int?> wrap = scan.StepAsync(0, 0, FindDirection.Next, CancellationToken.None);

        Assert.True(await StillWaiting(wrap));
        pages.Release();
        Assert.Equal(0, await wrap);
    }

    [Fact]
    public async Task NoMatchesIsAnsweredOnlyWhenTheScanIsDone()
    {
        FakePages pages = new(Page("x"), Page("x")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));

        Task<int?> step = scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None);

        Assert.True(await StillWaiting(step));
        pages.Release();
        Assert.Null(await step);
    }

    [Fact]
    public async Task DisposingStopsTheScanAndAWaitingStep()
    {
        FakePages pages = new(Page("x"), Page("x")) { HoldAt = 1 };
        PdfTextScan scan = new(pages, new FindQuery("key"));
        Task<int?> step = scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None);

        scan.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step);
        await scan.Completion;
    }

    [Fact]
    public async Task ACancelledStepLeavesTheScanRunning()
    {
        FakePages pages = new(Page("x"), Page("key")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));
        using CancellationTokenSource cancel = new();
        Task<int?> step = scan.StepAsync(null, 0, FindDirection.Next, cancel.Token);

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step);
        pages.Release();
        await scan.Completion;
        Assert.Equal(1, scan.ResultFor(0).Total);
    }

    [Fact]
    public async Task TheMatchCapEndsTheScan()
    {
        using PdfTextScan scan = new(new FakePages(Page("a a a"), Page("a a")), new FindQuery("a"), maxMatches: 3);
        await scan.Completion;

        Assert.Equal(3, scan.ResultFor(0).Total);
        Assert.False(scan.ResultFor(0).TotalIsPartial);
    }

    [Fact]
    public async Task FinishedIsRaisedOnceWhenTheLastPageIsDone()
    {
        int finished = 0;
        FakePages pages = new(Page("x"), Page("key")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));
        scan.Finished += (_, _) => Interlocked.Increment(ref finished);

        pages.Release();
        await scan.Completion;

        Assert.Equal(1, finished);
    }

    [Fact]
    public async Task ARealGuideIsScannedThroughItsOwnSource()
    {
        using PdfPageTextSource source = PdfPageTextSource.Open(
            File.OpenRead(P0Fixtures.Resolve("generated/pdf-long.pdf")), CancellationToken.None, maxPages: 1);
        using PdfTextScan scan = new(source, new FindQuery("page 150 of 200"));
        await scan.Completion;

        Assert.Equal(1, scan.ResultFor(0).Total);
        Assert.Equal(149, scan.MatchAt(0).PageIndex);
        Assert.Equal(1, source.PeakPages);
    }
}
```

Two things to check before running:
- **The Finished subscription is racy.** The scan can finish before
  `Finished +=` runs. `HoldAt = 1` holds page 2, and the subscription
  happens before `Release`.
- **The page count.** `pdf-long`'s pages read "Desktop Guides P0 - page N
  of 200", and the existing `TheLastPageOfALongDocumentHasItsOwnText`
  relies on that.

- [ ] **Step 2: Run them to see them fail**

Run: the Infrastructure tests, with filter `FullyQualifiedName~PdfTextScanTests`.
Expected: the build fails: `PdfTextScan`, `PdfMatch` and
`IPdfPageTextReader` don't exist.

- [ ] **Step 3: Implement the scan**

In `PdfPageTextSource.cs`, declare `public sealed class PdfPageTextSource : IPdfPageTextReader, IDisposable`.
`PageCount` and `GetPageTextAsync` already match the interface.

Create `src/DesktopGuides.Infrastructure/Reading/PdfTextScan.cs`:

```csharp
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Infrastructure.Reading;

public interface IPdfPageTextReader
{
    int PageCount { get; }
    Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token);
}

public readonly record struct PdfMatch(int PageIndex, int Start, int Length);

/// <summary>
/// T18.1: one query's scan of a PDF, from the first page to the last, on a
/// worker. Matches are found in page order, so an index never changes once
/// found. A step waits only as far as the scan must go to answer it.
/// </summary>
public sealed class PdfTextScan : IDisposable
{
    private readonly object sync = new();
    private readonly List<PdfMatch> matches = [];
    private readonly List<int> unreadable = [];
    private readonly CancellationTokenSource stop = new();
    private TaskCompletionSource progressed = NewSignal();
    private int scanned;
    private int imageOnly;
    private bool complete;
    private bool stopped;

    public PdfTextScan(IPdfPageTextReader pages, FindQuery query, int maxMatches = TextMatcher.MaxMatches)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(query);
        CancellationToken token = stop.Token;
        Completion = Task.Run(() => RunAsync(pages, query, maxMatches, token));
    }

    public Task Completion { get; }
    public event EventHandler? Finished;

    public async Task<int?> StepAsync(int? current, int fromPage, FindDirection direction, CancellationToken token)
    {
        while (true)
        {
            Task wait;
            lock (sync)
            {
                if (stopped) throw new OperationCanceledException();
                if (TryStep(current, fromPage, direction, out int? answer)) return answer;
                wait = progressed.Task;
            }
            await wait.WaitAsync(token).ConfigureAwait(false);
        }
    }

    public PdfMatch MatchAt(int index)
    {
        lock (sync) return matches[index];
    }

    public FindResult ResultFor(int? index)
    {
        lock (sync)
        {
            FindOutcome outcome = index is null ? FindOutcome.NotFound : FindOutcome.Found;
            return new FindResult(outcome, index + 1, matches.Count, !complete, imageOnly)
            {
                UnreadablePages = unreadable.Select(page => page + 1).ToArray()
            };
        }
    }

    public void Dispose() => stop.Cancel();

    // Under the lock. True when the scan so far settles the answer.
    private bool TryStep(int? current, int fromPage, FindDirection direction, out int? answer)
    {
        answer = null;
        int count = matches.Count;
        if (count == 0) return complete;
        if (current is int at)
        {
            int next = direction == FindDirection.Next ? at + 1 : at - 1;
            if (next >= 0 && next < count)
            {
                answer = next;
                return true;
            }
            if (!complete) return false;
            answer = direction == FindDirection.Next ? 0 : count - 1;
            return true;
        }
        if (direction == FindDirection.Next)
        {
            int first = matches.FindIndex(match => match.PageIndex >= fromPage);
            if (first >= 0)
            {
                answer = first;
                return true;
            }
            if (!complete) return false;
            answer = 0;
            return true;
        }
        // Matches before fromPage are all known once those pages are read.
        if (!complete && scanned < fromPage) return false;
        int last = matches.FindLastIndex(match => match.PageIndex < fromPage);
        if (last >= 0)
        {
            answer = last;
            return true;
        }
        if (!complete) return false;
        answer = count - 1;
        return true;
    }

    private async Task RunAsync(IPdfPageTextReader pages, FindQuery query, int maxMatches, CancellationToken token)
    {
        try
        {
            for (int page = 0; page < pages.PageCount; page++)
            {
                PdfPageText? text = null;
                try
                {
                    text = await pages.GetPageTextAsync(page, token).ConfigureAwait(false);
                }
                catch (PdfPageTextException)
                {
                    // A broken page is reported; the next may read.
                }
                int room;
                lock (sync) room = maxMatches - matches.Count;
                IReadOnlyList<TextMatch> found = text is { HasLetters: true }
                    ? TextMatcher.FindAll(text.Text, query, room)
                    : [];
                lock (sync)
                {
                    if (text is null) unreadable.Add(page);
                    else if (!text.HasLetters) imageOnly++;
                    foreach (TextMatch match in found) matches.Add(new PdfMatch(page, match.Start, match.Length));
                    scanned = page + 1;
                    if (matches.Count >= maxMatches) break;
                    Signal();
                }
            }
            lock (sync)
            {
                complete = true;
                Signal();
            }
            Finished?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Cancelled, or the source closed under the scan: waiting steps stop.
            lock (sync)
            {
                stopped = true;
                Signal();
            }
        }
    }

    private void Signal()
    {
        progressed.TrySetResult();
        progressed = NewSignal();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
```

The `break` inside the `lock` leaves the loop with `complete` still
false, and the block after the loop then completes it. Check that the cap
test reads `TotalIsPartial == false`.

- [ ] **Step 4: Run them to see them pass**

Expected: 12 `PdfTextScanTests` pass.

- [ ] **Step 5: Write the failing loader tests**

In `ManagedPdfGuideLoaderTests.cs`, add:

```csharp
    [Fact]
    public async Task ALoadedGuideHasItsOwnSearchSource()
    {
        // Use the file's existing harness and guide setup for pdf-long.
        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
        using PdfPageTextSource text = loaded.Text;
        using PdfPageTextSource search = loaded.Search;

        Assert.NotSame(text, search);
        Assert.Equal(text.PageCount, search.PageCount);
        for (int page = 0; page < search.PageCount; page++) await search.GetPageTextAsync(page, CancellationToken.None);
        Assert.Equal(0, text.PeakPages);
        Assert.Equal(1, search.PeakPages);
    }
```

and, in the password test that already opens `pdf-locked` with its
password (the one asserting "Locked guide secret page"):

```csharp
        using PdfPageTextSource search = loaded.Search;
        Assert.Contains("Locked guide secret page", (await search.GetPageTextAsync(0, CancellationToken.None)).Text);
```

Read the file first. Build the harness and guide the way its neighbours
do, and ledger the setup you used.

Every existing test that takes `loaded.Text` must also dispose
`loaded.Search` (`using PdfPageTextSource search = loaded.Search;`, or
`loaded.Search.Dispose();` beside `loaded.Text.Dispose();`). Otherwise an
open handle stops the harness deleting its folder on Windows.

- [ ] **Step 6: Run them to see them fail, then implement**

Expected: the build fails: `PdfGuideLoaded` has no `Search`.

In `ManagedPdfGuideLoader.cs`:
- The record becomes
  `public sealed record PdfGuideLoaded(string FilePath, PdfPageTextSource Text, PdfPageTextSource Search, string ContentSha256) : PdfGuideLoad;`.
- In `Open`, after the `PageCount == 0` check, open the search source
  with the same password. A failure here disposes `text` and falls
  through to the existing catches:

```csharp
            // T18.1: search reads its own document, so it never competes with
            // the page shown or evicts its cache. The password opens both
            // documents now and isn't kept.
            PdfPageTextSource search;
            try
            {
                FileStream searchFile = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    search = PdfPageTextSource.Open(searchFile, token, password, maxPages: 1);
                }
                catch
                {
                    searchFile.Dispose();
                    throw;
                }
            }
            catch
            {
                text.Dispose();
                throw;
            }
            return new PdfGuideLoaded(path, text, search, contentSha256);
```

`text.Dispose()` already disposes `file`. Check that the outer catches
then don't dispose `file` a second time. `FileStream.Dispose` is
idempotent, but ledger what you find.

In `ShellWindow.PdfReader.cs`, the abandoned-load line becomes:

```csharp
            if (load is PdfGuideLoaded abandoned)
            {
                abandoned.Text.Dispose();
                abandoned.Search.Dispose();
            }
```

- [ ] **Step 7: Run the Infrastructure suite, then build**

Expected: the full Infrastructure suite passes (790 before, plus 13), and
the Production build has 0 warnings. `PdfReaderSession` still compiles,
because it reads only `loaded.Text`. Task 3 gives it `Search`.

- [ ] **Step 8: Commit**

```bash
git add src tests/DesktopGuides.Infrastructure.Tests
git commit -m "feat(p2): scan a PDF's pages for search on their own document"
```

---
### Task 3: PDF search in the reader, the bar's note, and late totals

**Files:**
- Modify: `src/DesktopGuides.Production/PdfReaderSession.cs`
  (`IFindableReader`, `Find`, the search source's lifetime)
- Modify: `src/DesktopGuides.Production/PdfReaderView.xaml` and
  `PdfReaderView.xaml.cs` (`ShowMatch`, `ClearMatch`)
- Modify: `src/DesktopGuides.Production/FindBar.xaml` and `FindBar.xaml.cs`
  (the note)
- Modify: `src/DesktopGuides.Production/ShellWindow.Find.cs`
  (`ResultUpdated`, and the note in announcements)

**Interfaces:**
- Consumes: Task 1's `FindResultText.Note`, `TextBoxOffsets.Map` and
  `ResultUpdated`; Task 2's `PdfTextScan`, `PdfMatch` and
  `PdfGuideLoaded.Search`.
- Produces:
  - `PdfReaderSession : IReaderSession, IFindableReader`, declaring
    `ReaderCapabilities.Find`;
  - `PdfReaderView.ShowMatch(int start, int length)` and
    `PdfReaderView.ClearMatch()`;
  - the find bar's note, with AutomationId `FindNote`.

The Production project has no unit tests. This task's gate is a clean
build. Task 4's installed modes verify the behaviour.

- [ ] **Step 1: The view selects a match**

In `PdfReaderView.xaml`, give `DocumentText` a selection colour that stays
visible while the find box has focus:

```xml
                     SelectionHighlightColorWhenNotFocused="{ThemeResource TextControlSelectionHighlightColor}"
```

In `PdfReaderView.xaml.cs` (add `using CommunityToolkit.WinUI;` and
`using Windows.Foundation;`), after `ShowPage`:

```csharp
    // T18.1: the current match, selected in the page text without taking
    // focus from the find box, and scrolled a quarter of the way down.
    public void ShowMatch(int start, int length)
    {
        int end = Math.Min(DocumentText.Text.Length, start + length);
        if (start < 0 || start >= end) return;
        DocumentText.Select(start, end - start);
        if (DocumentText.FindDescendant<ScrollViewer>() is ScrollViewer scroller)
        {
            Rect box = DocumentText.GetRectFromCharacterIndex(start, false);
            scroller.ChangeView(null, Math.Max(0, scroller.VerticalOffset + box.Top - scroller.ViewportHeight / 4),
                null, disableAnimation: true);
        }
    }

    public void ClearMatch() => DocumentText.Select(0, 0);
```

`GetRectFromCharacterIndex` returns the box relative to the TextBox. The
`ScrollViewer` is the template's `ContentElement`. If the rectangle turns
out to be content-relative instead (the match scrolls too far), drop the
`VerticalOffset +`, and ledger which it was.

- [ ] **Step 2: The session searches**

In `PdfReaderSession`:
- declare `internal sealed class PdfReaderSession : IReaderSession, IFindableReader`;
- add `ReaderCapabilities.Find` to `Capabilities`;
- add the field `private readonly PdfPageTextSource search;`, and set it
  in the constructor with `search = loaded.Search;`;
- add `private PdfPageText? shownText;`. In `Apply`, after
  `View.ShowPage(...)`, add:

```csharp
            shownText = result.Text;
            // A pending match shows only on its own page.
            if (pendingMatch is PdfMatch match && match.PageIndex == index) ApplyMatch(match, result.Text);
```

- add, after `ExecuteAsync`:

```csharp
    // T18.1: the last query's scan, on the search source's own document.
    // A step waits only as far as the scan must go, then shows the match's
    // page and selects the match in the page text. Progress is paused
    // while the bar is open, so the page turns save nothing.
    private PdfTextScan? scan;
    private string? findText;
    private int? matchIndex;
    private PdfMatch? pendingMatch;

    public event EventHandler<FindResult>? ResultUpdated;

    public async Task<FindResult> FindAsync(FindQuery query, FindDirection direction, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (document is null || disposed) return FindResult.Failure;
        if (query.Text != findText)
        {
            ClearFind();
            PdfTextScan started = new(search, query);
            started.Finished += OnScanFinished;
            scan = started;
            findText = query.Text;
        }
        PdfTextScan current = scan!;
        int? index = await current.StepAsync(matchIndex, target, direction, token);
        // A newer query, a Close or a dispose came in while the step waited.
        if (!ReferenceEquals(current, scan) || disposed) throw new OperationCanceledException(token);
        matchIndex = index;
        if (index is not int at)
        {
            pendingMatch = null;
            View.ClearMatch();
            return current.ResultFor(null);
        }
        ShowMatch(current.MatchAt(at));
        return current.ResultFor(at);
    }

    public void ClearFind()
    {
        if (scan is not null)
        {
            scan.Finished -= OnScanFinished;
            scan.Dispose();
            scan = null;
        }
        findText = null;
        matchIndex = null;
        pendingMatch = null;
        if (!disposed) View.ClearMatch();
    }

    // The shown page selects now; another page selects when its load shows.
    private void ShowMatch(PdfMatch match)
    {
        pendingMatch = match;
        if (match.PageIndex == target && match.PageIndex == position.Page) ApplyMatch(match, shownText);
        else GoTo(match.PageIndex, 0);
    }

    private void ApplyMatch(PdfMatch match, PdfPageText? text)
    {
        if (text is not { HasLetters: true }) return;
        (int start, int length) = TextBoxOffsets.Map(text.Text, new TextMatch(match.Start, match.Length));
        View.ShowMatch(start, length);
    }

    // On a worker: the total is final now, so the bar loses its "+".
    private void OnScanFinished(object? sender, EventArgs args) =>
        View.DispatcherQueue.TryEnqueue(() =>
        {
            if (disposed || !ReferenceEquals(sender, scan) || scan is null) return;
            try
            {
                ResultUpdated?.Invoke(this, scan.ResultFor(matchIndex));
            }
            catch (Exception)
            {
                // A handler must not escape into the dispatcher.
            }
        });
```

- In `DisposeAsync`, right after `disposed = true;`, add `ResultUpdated = null;`.
  Before the text source's close, stop the scan:

```csharp
        // The scan's page read honours its token; its document closes when free.
        scan?.Dispose();
        scan = null;
```

  After the `text.CloseAsync` block, add:

```csharp
        try
        {
            // Never waits: a page still being read closes the document after.
            await search.CloseAsync(TimeSpan.Zero);
        }
        catch (Exception)
        {
            clean = false;
        }
```

  `ClearFind` checks `disposed` before touching the view, so the shell's
  `CloseFind` during teardown stays safe.

- [ ] **Step 3: The bar's note**

In `FindBar.xaml`, add a second row to the grid
(`<Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="Auto" /></Grid.RowDefinitions>`),
and after the Close button:

```xml
        <TextBlock x:Name="Note" Grid.Row="1" Grid.ColumnSpan="6" Visibility="Collapsed"
                   TextWrapping="Wrap"
                   AutomationProperties.AutomationId="FindNote"
                   Style="{StaticResource DesktopGuidesSecondaryBodyStyle}" />
```

In `FindBar.xaml.cs`:
- `Reset` also clears the note (`Note.Text = string.Empty; Note.Visibility = Visibility.Collapsed;`);
- `ShowResult` ends with:

```csharp
        string? note = FindResultText.Note(result);
        Note.Text = note ?? string.Empty;
        Note.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
```

- [ ] **Step 4: The shell shows late totals, and announces the note**

In `ShellWindow.Find.cs`:
- add the field `private IFindableReader? findUpdates;`;
- in `OpenFindAsync`, just before `ReaderFind.Reset();`:

```csharp
            if (session is IFindableReader updates)
            {
                updates.ResultUpdated += OnFindResultUpdated;
                findUpdates = updates;
            }
```

- in `CloseFind`, before `findPause?.Dispose();`:

```csharp
        if (findUpdates is not null)
        {
            findUpdates.ResultUpdated -= OnFindResultUpdated;
            findUpdates = null;
        }
```

- add the handler:

```csharp
    // A PDF scan finished after the step returned: the count loses its "+".
    private void OnFindResultUpdated(object? sender, FindResult result)
    {
        if (!ReferenceEquals(sender, readerSession) || ReaderFind.Visibility != Visibility.Visible) return;
        ReaderFind.ShowResult(result, ReaderFind.Query);
    }
```

- in `FindStepAsync`, after the announcement's `switch`, add the note:
  build the text first, then
  `AnnounceStatus(FindResultText.Note(result) is string note ? $"{text} {note}." : text);`.

- [ ] **Step 5: Build**

Run: the Production build (after `stage`).
Expected: 0 warnings and 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(p2): search PDF guides from the find bar"
```

---
### Task 4: The `find-pdf` and `find-pdf-scan` modes; `find-unsupported` retires

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`seed-find` also
  adds "Scanned PDF Guide")
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (two modes; the mode
  lists; `find-unsupported` removed)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-NavigationScenarios`)
- Modify: `tools/p1/windows_reader_toolbar_ui_smoke.ps1` (TR18.1: no Find
  without the capability)
- Modify: `docs/p1/e2e-testing.md` (the "In-guide search" row)

**Interfaces:**
- Consumes:
  - Task 3's `FindNote`;
  - PR a's AutomationIds;
  - the smoke's `Open-TextGuide`, `Wait-PdfPage`, `Get-PdfText`,
    `Find-Count` and `Read-Saves`;
  - `seed-progress`'s "Long PDF Guide" (`pdf-long`: 200 pages, each
    reading "Desktop Guides P0 - page N of 200").

- [ ] **Step 1: Seed the scanned guide**

In `seed-find`, after "Search Web Guide", publish `p0/pdf-scan.pdf` the
same way, titled "Scanned PDF Guide". Return its id beside `search`, as
`scanned`. `pdf-scan.pdf` is a checked-in fixture: one page, an image
only.

- [ ] **Step 2: Write the modes**

In the `find-*` branch's helpers, after `Find-Count`:

```powershell
            function Get-PdfSelection {
                $box = Find-ById 'PdfDocumentText'
                if (-not $box) { return '' }
                $ranges = $box.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern).GetSelection()
                return (@($ranges) | ForEach-Object { $_.GetText(-1) }) -join ''
            }
```

After `find-html-close`:

```powershell
            elseif ($Mode -eq 'find-pdf') {
                Open-TextGuide 'Long PDF Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-PdfPage 1 200 'page 1 of 200')
                Start-Sleep -Seconds 2
                $baseline = Read-Saves
                [System.Windows.Forms.SendKeys]::SendWait('^f')
                [void](Wait-VisibleById 'ReaderFindBar')
                Wait-FocusedId 'FindQueryInput'
                # Far ahead: the step waits for the scan to reach page 150.
                [System.Windows.Forms.SendKeys]::SendWait('page 150 of 200{ENTER}')
                [void](Wait-Name 'FindCount' '1 of 1')
                [void](Wait-PdfPage 150 200 'page 150 of 200')
                $selected = Get-PdfSelection
                if ($selected -ne 'page 150 of 200') { throw "The page text selected '$selected'." }
                $report.findPdfSelection = $selected
                Invoke-Element (Wait-VisibleById 'FindBackButton')
                [void](Wait-PdfPage 1 200 'page 1 of 200')
                Find-Count 'of 200' '1 of 200'
                [System.Windows.Forms.SendKeys]::SendWait('{F3}')
                [void](Wait-Name 'FindCount' '2 of 200')
                [void](Wait-PdfPage 2 200 'page 2 of 200')
                [System.Windows.Forms.SendKeys]::SendWait('+{F3}')
                [void](Wait-Name 'FindCount' '1 of 200')
                [System.Windows.Forms.SendKeys]::SendWait('+{F3}')
                [void](Wait-Name 'FindCount' '200 of 200')
                [void](Wait-PdfPage 200 200 'page 200 of 200')
                $report.findPdfScreenshot = Save-WindowScreenshot 'find-pdf'
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Wait-HiddenById 'ReaderFindBar'
                # Longer than the 1 s quiet timer and the 4 s deadline.
                Start-Sleep -Seconds 5
                $afterSearch = Read-Saves
                if ($afterSearch -ne $baseline) {
                    throw "Searching saved the place: saves went from $baseline to $afterSearch."
                }
                $report.findPdfSavedNothing = $true
                # A real page turn: back one page from the last.
                [System.Windows.Forms.SendKeys]::SendWait('{PGUP}')
                [void](Wait-PdfPage 199 200 'page 199 of 200')
                $deadline = (Get-Date).AddSeconds(8)
                do {
                    Start-Sleep -Milliseconds 250
                    $afterMove = Read-Saves
                } while ($afterMove -le $baseline -and (Get-Date) -lt $deadline)
                if ($afterMove -le $baseline) { throw 'After closing the find bar, a page turn saved nothing.' }
                $report.phases += 'find-pdf'
            }
            elseif ($Mode -eq 'find-pdf-scan') {
                Open-TextGuide 'Scanned PDF Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [System.Windows.Forms.SendKeys]::SendWait('^f')
                [void](Wait-VisibleById 'ReaderFindBar')
                Find-Count 'map' 'No matches'
                [void](Wait-Name 'FindNote' '1 image-only page')
                $report.findPdfScanScreenshot = Save-WindowScreenshot 'find-pdf-scan'
                $report.phases += 'find-pdf-scan'
            }
```

Then:
- **The mode lists:** at lines 29 and 1560, replace `'find-unsupported'`
  with `'find-pdf', 'find-pdf-scan'`.
- **The old branch:** delete the `find-unsupported` branch.

Read first, and adapt:
- **`PGUP` after closing.** `CloseFind` focuses the reader toolbar, and the
  shell's preview handler runs page keys from there. If the key doesn't
  turn the page, focus `PdfPreviewScroller` first, as the `pdf-*` modes do
  before `Send-ReaderKeys`. Ledger which you used.
- **Ctrl+F with the guide open.** If it doesn't open the bar in a PDF
  guide, use `Invoke-OverflowCommand 'Find in guide'`, as `find-html`
  does, and ledger it.

- [ ] **Step 3: The installer**

In `Run-NavigationScenarios`, replace the `find-unsupported` line with:

```powershell
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.findPdf = Invoke-ProgressPass 'find-pdf' 0
    if ($null -eq (& $estimateOf 'Long PDF Guide')) {
        throw 'After closing the find bar and turning a page, no PDF place was stored.'
    }

    $report.findPdfScan = Invoke-ProgressPass 'find-pdf-scan' 0
```

- [ ] **Step 4: TR18.1 stays covered in the toolbar smoke**

Every guide format now supports search, so no installed guide can show
the "Search isn't available for this guide." path. The rule that search
appears only with the capability is still pinned. In
`windows_reader_toolbar_ui_smoke.ps1`'s `TextControls` phase, after
`[void](Wait-HiddenByName 'Zoom in')`:

```powershell
    # T18.1: without the Find capability the overflow has no search.
    if (Find-More) {
        Open-Overflow
        [void](Wait-HiddenByName 'Find in guide')
        Close-Overflow
    }
    $report.phases += 'no-find-without-the-capability'
```

Read `ToolbarWindow.cs` first. `TextControls` must not include
`ReaderCapabilities.Find`. If it does, use the capability set that
doesn't, and ledger it.

- [ ] **Step 5: Document**

In `docs/p1/e2e-testing.md`, in the "In-guide search" row:
- `seed-find` "also adds Scanned PDF Guide";
- replace the `find-unsupported` sentence with:

  > `find-pdf`: `page 150 of 200` reads `1 of 1`, on page 150, with the
  > match selected in the page text; *Back to my place* returns to page 1;
  > `of 200` steps `1`, `2`, `1`, and wraps to `200 of 200` on page 200;
  > closing saves nothing, and the next page turn saves. `find-pdf-scan`:
  > the image-only guide reads `No matches`, with the note `1 image-only
  > page`. The "not available" path is covered by the toolbar smoke's
  > `no-find-without-the-capability`, since every format now supports
  > search.
- the screenshots add `find-pdf` and `find-pdf-scan`.

- [ ] **Step 6: Check, commit, push and dispatch**

```bash
stage
s 'cd /d E:\work\desktop-guides\t18-1c && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_start_installed_task.ps1'
s 'cd /d E:\work\desktop-guides\t18-1c && dotnet build tools\p1\DesktopGuides.ShellSeed -c Release -nologo > E:\work\t18-1c-seed.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" E:\work\t18-1c-seed.log'
LC_ALL=C grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1 tools/p1/windows_reader_toolbar_ui_smoke.ps1
git add tools/p1 docs/p1/e2e-testing.md
git commit -m "test(p2): add the PDF search scenarios to the navigation group"
git push -u origin feat/p2-t18-1-pdf-search
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p2-t18-1-pdf-search -f shell-scope=navigation -f shell-lane=both
```

Expected:
- the script tests pass, the seed builds with 0 warnings, and the
  PowerShell files are ASCII-only;
- the dispatch's `navigation` shard passes on both lanes, with every find
  mode passing. The `reader-toolbar-ui` job passes too.

If a mode fails, use superpowers:systematic-debugging on its evidence. As
in PR b, first make a failing wait report what it saw.

---

### Task 5: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t18-1-search-design.md` (Status; a "PR c verification
  record")
- Modify: `docs/p2/results.md` (the #116 row)
- Modify: `docs/p2/implementation-plan.md`, `docs/progress.md` and
  `docs/work-breakdown.md` (T18.1's status)

- [ ] **Step 1: The final review**

Run the whole-branch review before writing the records, as in PR b. Its
fixes land first.

- [ ] **Step 2: Write the records**

- **The design's Status:** "PR a (#113) and PR b (#116) merged. PR c
  (PDF) implemented on `feat/p2-t18-1-pdf-search`."
- **Its PDF bullets** describe what was built:
  - the search source opened by the loader;
  - `PdfTextScan`;
  - "N+" while scanning, and `ResultUpdated`;
  - the note;
  - the selection in `DocumentText`.

  Add deviation 5: the scanner's document is opened by the loader, not by
  the scanner, because the password is never stored.
- **The "PR c verification record":** the red and green runs, the
  dispatch, the screenshots, the review, and every ruling.
- **`results.md`:** the #116 row, in #113's format.
- **The status pages:** "M7 in progress: T18.1 PR c (PDF search) in
  review." When PR c merges, T18.1 is complete, and so is M7's search
  exit. The follow-ups are #45 and #50, and the page-image overlay.

- [ ] **Step 3: Commit, push, open the PR, and wait for its run**

```bash
git add docs
git commit -m "docs(p2): record T18.1 PR c"
git push
```

Commit the `find-pdf` and `find-pdf-scan` screenshots under
`docs/p2/evidence/ci/t18-1/`. Title: "T18.1 PR c: PDF search". Base:
`main`. The body names the task, the prerequisites (#113 and #116, both
merged) and the outcome. It shows the screenshots, and lists the dispatch,
every ruling and the deferred minors.

Expected: every job passes, including `pdf`, `navigation` and
`reader-toolbar-ui`. Record the run in the PR description, then stop for
the user's merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| PDF search on its own PdfPig document; the display source untouched | 2 |
| Scanning in page order with cancellation; "N+" while scanning | 2, 3 |
| Image-only pages counted and reported; unreadable pages reported, the scan going on | 1, 2, 3, 4 |
| A match goes to its page and is selected in the page-text pane (overlay deferred) | 1, 3, 4 |
| Search never saves the place; Back to my place | 3, 4 |
| TR18.1: search only with the capability | 4 (toolbar smoke) |
| `find-pdf` and `find-pdf-scan` on both lanes | 4 |
