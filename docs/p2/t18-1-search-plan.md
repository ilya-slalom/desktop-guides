# T18.1 PR a (Search Contract, TXT Search and the Find Bar) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** TXT guides can be searched from a find bar, with counts, next and
previous matches and "Back to my place", without changing the saved
reading place. The contract and the find bar are ready for HTML (PR b)
and PDF (PR c).

**Architecture:**
- **The contract (Core):** an `IFindableReader` interface replaces the
  unused `FindAction`. A Core `TextMatcher` does case-insensitive,
  whitespace-collapsing matching, returning source ranges.
- **Not saving while searching:** `ProgressCoordinator` gains a pause, so
  moving to matches never saves the reading place.
- **TXT:** the session searches its normalized text and highlights the
  match in its row.
- **The shell:** a `FindBar` control below the reader toolbar. Ctrl+F, F3
  and Shift+F3 join T16.1's keyboard map.

**Tech Stack:** C# / .NET 10, WinUI 3, xUnit, PowerShell 5.1 and 7, UI
Automation, GitHub Actions.

**Spec:** `docs/p2/t18-1-search-design.md`, and
`docs/p2-technical-design.md` section 3.

**Target:** T18.1, PR a of three (a: contract, TXT and find bar; b: HTML;
c: PDF), on `feat/p2-t18-1-search`.
**Prerequisites:** P1 complete; #112 (the P2 design and plan), open and
stacked under this branch.

## Global Constraints

- **Matching:** case-insensitive, using ordinal `ToUpperInvariant` per
  character. Whitespace runs (space, tab, `\n`) in the text and in the
  query each match one space. The query is trimmed, and an empty query is
  refused. At most 10,000 matches per search.
- **The saved place:** moving to a match never saves the reading place.
  Closing the find bar doesn't save it either. Only the user's next own
  movement does. "Back to my place" returns to the place captured when
  the bar opened.
- **TR18.1:** the find bar and its keys exist only for sessions that
  implement `IFindableReader`. Elsewhere, Ctrl+F in the reader says "Search
  isn't available for this guide."
- **Keys:** Ctrl+F opens or focuses the bar. Enter or F3 goes to the next
  match; Shift+Enter or Shift+F3 to the previous one; Escape closes the
  bar. In the library, Ctrl+F stays the library search.
- **Text:**
  - the count reads "`<i>` of `<n>`" or "No matches";
  - the bar's announcement is "Match `<i>` of `<n>`." or "No matches for
    `<query>`.";
  - the buttons are "Previous match", "Next match", "Back to my place" and
    "Close search".
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.
  There is no `cd` in local shell commands.

## Review Focus

1. **A late scroll event after the bar closes saves the match's place.**
   TXT's `ScrollToLine` raises `LocationChanged` at Low dispatcher priority,
   so one can arrive after the pause ends. Pinned by Task 2's test (a move
   reported after resuming saves; the matches during the pause don't), and
   by Task 6's `find-txt` locator check after closing.
2. **The guide is closed, or the user goes Back, with the bar open.** The
   saved place must be unchanged, and the pause released. Pinned by Task
   2's dispose-while-paused test, and by Task 6's `find-back` close step.
3. **A match inside a tab-expanded line is highlighted in the wrong
   column.** Pinned by Task 3's `ColumnOf` tests.
4. **Typing in the find box triggers reader keys** (Page Down, Ctrl+End).
   Pinned by Task 5: the shell's preview handler already skips TextBox
   sources. Task 6's `find-txt` types a query containing a space and checks
   the top line doesn't move until Enter.
5. **Huge or match-dense queries,** for example a single letter in a 2 MB
   guide, freeze the UI. Pinned by Task 1's 10,000 cap test, and Task 3,
   which runs the search off the UI thread.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t18-1) { Remove-Item -Recurse -Force E:\work\desktop-guides\t18-1 }; New-Item -ItemType Directory E:\work\desktop-guides\t18-1 | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t18-1'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t18-1 && dotnet test tests\DesktopGuides.Core.Tests --filter "<filter>" -nologo'`
- **Production build:** `s 'cd /d E:\work\desktop-guides\t18-1 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo'`
- **Script tests:** `s 'cd /d E:\work\desktop-guides\t18-1 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\<test>.ps1'`

---

### Task 1: The search contract and `TextMatcher`

**Files:**
- Create: `src/DesktopGuides.Core/Reading/FindContract.cs`
- Create: `src/DesktopGuides.Core/Reading/TextMatcher.cs`
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs` (remove
  `FindAction`, lines 62-63)
- Test: `tests/DesktopGuides.Core.Tests/TextMatcherTests.cs`

**Interfaces:**
- Produces:
  - `FindQuery(string)`, with a `Text` property holding the trimmed and
    collapsed query; it throws `ArgumentException` when that is empty;
  - `enum FindDirection { Next, Previous }`;
  - `enum FindOutcome { Found, NotFound, Cancelled, Failed }`;
  - `record FindResult(FindOutcome Outcome, int? Index, int? Total, bool TotalIsPartial, int UnsearchablePages)`;
  - `interface IFindableReader { Task<FindResult> FindAsync(FindQuery, FindDirection, CancellationToken); void ClearFind(); }`;
  - `readonly record struct TextMatch(int Start, int Length)`;
  - `TextMatcher.FindAll(string text, FindQuery query, int maxMatches = 10_000)`
    returns `IReadOnlyList<TextMatch>`;
  - `TextMatcher.Step(IReadOnlyList<TextMatch>, int? current, int fromOffset, FindDirection)`
    returns `int?`, the index of the next match.

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Core.Tests/TextMatcherTests.cs`:

```csharp
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextMatcherTests
{
    [Fact]
    public void MatchesIgnoreCaseAndReturnSourceRanges()
    {
        IReadOnlyList<TextMatch> matches = TextMatcher.FindAll("The Boss key. the BOSS door.", new FindQuery("boss"));

        Assert.Equal([new TextMatch(4, 4), new TextMatch(18, 4)], matches);
    }

    [Fact]
    public void WhitespaceRunsMatchOneSpaceAcrossLines()
    {
        const string text = "Take the\n   silver   key now";
        IReadOnlyList<TextMatch> matches = TextMatcher.FindAll(text, new FindQuery("the  silver key"));

        TextMatch match = Assert.Single(matches);
        Assert.Equal("the\n   silver   key", text.Substring(match.Start, match.Length));
    }

    [Fact]
    public void OverlappingMatchesAreAllFound()
    {
        Assert.Equal(
            [new TextMatch(0, 3), new TextMatch(1, 3), new TextMatch(2, 3)],
            TextMatcher.FindAll("aaaaa", new FindQuery("aaa")));
    }

    [Fact]
    public void NoMatchIsEmpty()
    {
        Assert.Empty(TextMatcher.FindAll("nothing here", new FindQuery("dragon")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \t\n ")]
    public void AnEmptyQueryIsRefused(string query)
    {
        Assert.Throws<ArgumentException>(() => new FindQuery(query));
    }

    [Fact]
    public void TheQueryIsTrimmedAndCollapsed()
    {
        Assert.Equal("boss key", new FindQuery("  boss \t key \n").Text);
    }

    [Fact]
    public void MatchesStopAtTheCap()
    {
        Assert.Equal(10_000, TextMatcher.FindAll(new string('a', 50_000), new FindQuery("a")).Count);
        Assert.Equal(3, TextMatcher.FindAll("a a a a a", new FindQuery("a"), maxMatches: 3).Count);
    }

    [Fact]
    public void FirstStepFindsTheMatchAtOrAfterTheOffset()
    {
        TextMatch[] matches = [new(10, 2), new(50, 2), new(90, 2)];

        Assert.Equal(1, TextMatcher.Step(matches, current: null, fromOffset: 40, FindDirection.Next));
        Assert.Equal(0, TextMatcher.Step(matches, current: null, fromOffset: 40, FindDirection.Previous));
        Assert.Equal(0, TextMatcher.Step(matches, current: null, fromOffset: 95, FindDirection.Next));
    }

    [Fact]
    public void LaterStepsWrapAround()
    {
        TextMatch[] matches = [new(10, 2), new(50, 2), new(90, 2)];

        Assert.Equal(0, TextMatcher.Step(matches, current: 2, fromOffset: 0, FindDirection.Next));
        Assert.Equal(2, TextMatcher.Step(matches, current: 0, fromOffset: 0, FindDirection.Previous));
        Assert.Null(TextMatcher.Step([], current: null, fromOffset: 0, FindDirection.Next));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t18-1 && dotnet test tests\DesktopGuides.Core.Tests --filter TextMatcherTests -nologo'`
Expected: a build FAILURE, because `TextMatcher`, `FindQuery` and
`TextMatch` don't exist.

- [ ] **Step 3: Write the contract and the matcher**

`src/DesktopGuides.Core/Reading/FindContract.cs`:

```csharp
namespace DesktopGuides.Core.Reading;

// T18.1: in-guide search. A session that implements IFindableReader
// declares ReaderCapabilities.Find; the shell shows the find bar only then.
public sealed record FindQuery
{
    public FindQuery(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string collapsed = TextMatcher.Collapse(text).Trim();
        if (collapsed.Length == 0)
        {
            throw new ArgumentException("Enter text to find.", nameof(text));
        }
        Text = collapsed;
    }

    public string Text { get; }
}

public enum FindDirection { Next, Previous }

public enum FindOutcome { Found, NotFound, Cancelled, Failed }

// Index is 1-based. TotalIsPartial means more pages are still being
// searched (PDF). UnsearchablePages counts image-only pages (PDF).
public sealed record FindResult(
    FindOutcome Outcome, int? Index, int? Total, bool TotalIsPartial, int UnsearchablePages)
{
    public static FindResult None { get; } = new(FindOutcome.NotFound, null, 0, false, 0);
}

public interface IFindableReader
{
    Task<FindResult> FindAsync(FindQuery query, FindDirection direction, CancellationToken token);
    void ClearFind();
}
```

`src/DesktopGuides.Core/Reading/TextMatcher.cs`:

```csharp
using System.Text;

namespace DesktopGuides.Core.Reading;

public readonly record struct TextMatch(int Start, int Length);

// T18.1: case-insensitive (ordinal, invariant), with whitespace runs in the
// text and the query matching one space. Ranges are in the source text.
public static class TextMatcher
{
    public const int MaxMatches = 10_000;

    public static IReadOnlyList<TextMatch> FindAll(string text, FindQuery query, int maxMatches = MaxMatches)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        // folded[i] came from source[starts[i]..ends[i]).
        StringBuilder folded = new(text.Length);
        List<int> starts = new(text.Length);
        List<int> ends = new(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                int runStart = index;
                while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
                folded.Append(' ');
                starts.Add(runStart);
                ends.Add(index);
            }
            else
            {
                folded.Append(char.ToUpperInvariant(text[index]));
                starts.Add(index);
                ends.Add(index + 1);
                index++;
            }
        }
        string haystack = folded.ToString();
        string needle = query.Text.ToUpperInvariant();
        List<TextMatch> matches = [];
        int at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0 && matches.Count < maxMatches)
        {
            int start = starts[at];
            matches.Add(new TextMatch(start, ends[at + needle.Length - 1] - start));
            at = haystack.IndexOf(needle, at + 1, StringComparison.Ordinal);
        }
        return matches;
    }

    // The match to show next. With no current match, the first one at or
    // after fromOffset (Next), or the last one before it (Previous),
    // wrapping at the ends.
    public static int? Step(IReadOnlyList<TextMatch> matches, int? current, int fromOffset, FindDirection direction)
    {
        if (matches.Count == 0) return null;
        if (current is int at)
        {
            return direction == FindDirection.Next
                ? (at + 1) % matches.Count
                : (at - 1 + matches.Count) % matches.Count;
        }
        if (direction == FindDirection.Next)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Start >= fromOffset) return i;
            }
            return 0;
        }
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i].Start < fromOffset) return i;
        }
        return matches.Count - 1;
    }

    internal static string Collapse(string text)
    {
        StringBuilder collapsed = new(text.Length);
        bool space = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!space) collapsed.Append(' ');
                space = true;
            }
            else
            {
                collapsed.Append(character);
                space = false;
            }
        }
        return collapsed.ToString();
    }
}
```

In `ReaderContract.cs`, delete the `FindAction` record (lines 62-63). Keep
`ReaderCommand.Find` and its `ReaderCapabilities.Find` policy entry: they
control the toolbar's Find button.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command.
Expected: PASS for all `TextMatcherTests`. The Production build fails
until Task 5 removes `FindInGuideClicked`'s use of `FindAction`. That is
expected; don't build Production until Task 5.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Reading/FindContract.cs src/DesktopGuides.Core/Reading/TextMatcher.cs src/DesktopGuides.Core/Reading/ReaderContract.cs tests/DesktopGuides.Core.Tests/TextMatcherTests.cs
git commit -m "feat(p2): add the in-guide search contract and text matcher"
```

---

### Task 2: Pause progress saving while searching

**Files:**
- Modify: `src/DesktopGuides.Core/Reading/ProgressCoordinator.cs`
  (`IProgressTracking`; `Tracking`'s fields, `OnLocationChanged` and
  `SaveAsync`)
- Test: `tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs`

**Interfaces:**
- Produces: `IProgressTracking.PauseAsync(CancellationToken)` returns
  `Task<IDisposable>`. It saves a pending move first. Until the handle is
  disposed, movement isn't saved, and neither flush nor dispose saves.
  Disposing the handle twice is harmless.

- [ ] **Step 1: Write the failing tests**

Add to `ProgressCoordinatorTests.cs`, using its `ManualClock`, `FakeStore`,
`FakeSession` and `Track` helpers:

```csharp
    [Fact]
    public async Task PauseSavesAPendingMoveThenIgnoresMovement()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, null);

        session.Move();
        string before = session.CurrentJson;
        IDisposable pause = await tracking.PauseAsync(CancellationToken.None);
        Assert.Equal(before, store.Writes[^1].Json);

        session.Move();
        clock.Advance(TimeSpan.FromSeconds(10));
        await tracking.FlushAsync(CancellationToken.None);
        Assert.Single(store.Writes);
        pause.Dispose();
    }

    [Fact]
    public async Task AfterResumingTheNextMoveSaves()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, session.CurrentJson);

        IDisposable pause = await tracking.PauseAsync(CancellationToken.None);
        session.Move();
        pause.Dispose();
        pause.Dispose();
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(store.Writes);

        session.Move();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(session.CurrentJson, Assert.Single(store.Writes).Json);
    }

    [Fact]
    public async Task DisposeWhilePausedSavesNothing()
    {
        ProgressCoordinator coordinator = new(store, clock);
        FakeSession session = new();
        IProgressTracking tracking = Track(coordinator, GuideA, session, session.CurrentJson);

        _ = await tracking.PauseAsync(CancellationToken.None);
        session.Move();
        await tracking.DisposeAsync();

        Assert.Empty(store.Writes);
    }
```

Read the existing helpers first. If `FakeSession.Move()` or `Track` have
other names, use those, and ledger it.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t18-1 && dotnet test tests\DesktopGuides.Core.Tests --filter ProgressCoordinatorTests -nologo'`
Expected: a build FAILURE, because `PauseAsync` doesn't exist.

- [ ] **Step 3: Add the pause**

In `IProgressTracking`, after `void Abandon();`:

```csharp
    // T18.1: saves a pending move, then saves nothing (not even on flush or
    // dispose) until the returned handle is disposed. Search holds it, so
    // moving to matches never moves the saved place.
    Task<IDisposable> PauseAsync(CancellationToken token);
```

In `Tracking`, add the field `private int pauses;`, and these members:

```csharp
        public async Task<IDisposable> PauseAsync(CancellationToken token)
        {
            await FlushAsync(token);
            lock (gate)
            {
                pauses++;
                StopTimers();
            }
            return new Pause(this);
        }

        private void Resume()
        {
            lock (gate)
            {
                if (pauses > 0) pauses--;
            }
        }

        private sealed class Pause(Tracking owner) : IDisposable
        {
            private int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Resume();
            }
        }
```

In `OnLocationChanged`, the guard
`if (ended || disposing) return;` becomes
`if (ended || disposing || pauses > 0) return;`.

In `SaveAsync`, inside the first locked block after `turn.WaitAsync`, the
guard `if (ended || !dirty) return;` becomes
`if (ended || !dirty || pauses > 0) return;`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command.
Expected: every `ProgressCoordinatorTests` test passes, the existing ones
and the three new ones.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Reading/ProgressCoordinator.cs tests/DesktopGuides.Core.Tests/ProgressCoordinatorTests.cs
git commit -m "feat(p2): pause progress saving while searching"
```

---
### Task 3: TXT search in the reader

**Files:**
- Modify: `src/DesktopGuides.Core/Text/TextLineView.cs` (a `ColumnOf` helper)
- Test: `tests/DesktopGuides.Core.Tests/TextLineViewTests.cs`
- Modify: `src/DesktopGuides.Production/TextReaderSession.cs` (implement `IFindableReader`; declare `Find`)
- Modify: `src/DesktopGuides.Production/TextReaderView.xaml.cs` (`ShowMatch`, `ClearMatch`, and highlights in `LineContainerChanging`)
- Modify: `src/DesktopGuides.Production/Styles/DesignTokens.xaml` (a `DesktopGuidesFindHighlightBrush` in each theme dictionary)

**Interfaces:**
- Consumes: Task 1's `IFindableReader`, `FindQuery`, `FindResult`,
  `TextMatcher` and `TextMatch`.
- Produces:
  - `TextLineView.ColumnOf(TextGuideDocument document, int line, int offset)`
    returns the display column of `offset` in `line`;
  - `TextReaderView.ShowMatch(IReadOnlyList<(int Line, int Column, int Length)> segments)`;
  - `TextReaderView.ClearMatch()`;
  - `TextReaderSession` implements `IFindableReader`, and its
    `Capabilities` include `Find`.

- [ ] **Step 1: Write the failing `ColumnOf` tests**

In `TextLineViewTests.cs`, using its `Document(string)` helper:

```csharp
    [Fact]
    public void ColumnOfCountsTabExpansion()
    {
        TextGuideDocument document = Document("ab\tcd\nx\ty");

        Assert.Equal(0, TextLineView.ColumnOf(document, 0, 0));
        Assert.Equal(2, TextLineView.ColumnOf(document, 0, 2));
        Assert.Equal(8, TextLineView.ColumnOf(document, 0, 3));
        Assert.Equal(9, TextLineView.ColumnOf(document, 0, 4));
        Assert.Equal(8, TextLineView.ColumnOf(document, 1, 8));
    }

    [Fact]
    public void ColumnOfClampsToTheLine()
    {
        TextGuideDocument document = Document("abc\ndef");

        Assert.Equal(3, TextLineView.ColumnOf(document, 0, 99));
        Assert.Equal(0, TextLineView.ColumnOf(document, 1, 0));
    }

    [Fact]
    public void ColumnOfRoundTripsWithSourceOffset()
    {
        TextGuideDocument document = Document("a\tb\tcdef");
        for (int offset = 0; offset < document.Text.Length; offset++)
        {
            Assert.Equal(offset, TextLineView.SourceOffset(document, 0, TextLineView.ColumnOf(document, 0, offset)));
        }
    }
```

`offset` is absolute in `document.Text`. In the first test, line 1
(`x\ty`) starts at offset 6, so offset 8 is `y`, at column 8 after the tab.

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t18-1 && dotnet test tests\DesktopGuides.Core.Tests --filter TextLineViewTests -nologo'`
Expected: a build FAILURE, because `ColumnOf` doesn't exist.

- [ ] **Step 3: Write `ColumnOf`**

In `TextLineView.cs`, after `SourceOffset`:

```csharp
    /// <summary>The display column of <paramref name="offset"/> (an index
    /// into <see cref="TextGuideDocument.Text"/>) in <paramref name="line"/>,
    /// clamped to the line.</summary>
    public static int ColumnOf(TextGuideDocument document, int line, int offset)
    {
        (int start, int end) = Bounds(document, line);
        int stop = Math.Clamp(offset, start, end);
        int columns = 0;
        for (int index = start; index < stop; index++)
        {
            columns += Width(document.Text[index], columns);
        }
        return columns;
    }
```

Run: the Step 2 command.
Expected: PASS.

- [ ] **Step 4: Add the highlight to the view**

In `DesignTokens.xaml`, add a `DesktopGuidesFindHighlightBrush` to each
theme dictionary, beside `DesktopGuidesAccentBrush`:
- **Light:** a `SolidColorBrush` with color `#66FFD54F`.
- **Dark:** `#80B8860B`.
- **HighContrast:** a `StaticResource` to the system `SystemColorHighlightColor`
  brush, following that dictionary's pattern for its other brushes.

In `TextReaderView.xaml.cs`, add a field and two methods:

```csharp
    // T18.1: the current match, as segments per line (a match can span
    // lines when whitespace runs collapse).
    private IReadOnlyDictionary<int, (int Column, int Length)> matchSegments =
        new Dictionary<int, (int Column, int Length)>();

    public void ShowMatch(IReadOnlyList<(int Line, int Column, int Length)> segments)
    {
        matchSegments = segments.ToDictionary(s => s.Line, s => (s.Column, s.Length));
        RefreshRealizedHighlights();
    }

    public void ClearMatch()
    {
        matchSegments = new Dictionary<int, (int Column, int Length)>();
        RefreshRealizedHighlights();
    }

    private void RefreshRealizedHighlights()
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (Lines.ContainerFromIndex(index) is ListViewItem { ContentTemplateRoot: TextBlock text })
            {
                ApplyHighlight(text, index);
            }
        }
    }

    private void ApplyHighlight(TextBlock text, int line)
    {
        text.TextHighlighters.Clear();
        if (matchSegments.TryGetValue(line, out (int Column, int Length) segment) && segment.Length > 0)
        {
            TextHighlighter highlighter = new()
            {
                Background = (Brush)Application.Current.Resources["DesktopGuidesFindHighlightBrush"]
            };
            highlighter.Ranges.Add(new TextRange { StartIndex = segment.Column, Length = segment.Length });
            text.TextHighlighters.Add(highlighter);
        }
    }
```

In `LineContainerChanging`, after `ApplyRowSize(text);`, add
`ApplyHighlight(text, line.Index);`.

`RefreshRealizedHighlights` walks only the realized containers:
`ContainerFromIndex` returns null for the rest, and those get their
highlight in `LineContainerChanging` when they're realized. If iterating
`lines.Count` is too slow for a 400,000-line guide, limit the loop to
`FirstVisibleIndex - 50` through `FirstVisibleIndex + 200`, and ledger it.
Add the `using`s it needs: `Microsoft.UI.Xaml.Documents` and
`Microsoft.UI.Xaml.Media`.

- [ ] **Step 5: Make the session searchable**

In `TextReaderSession.cs`:
- the class declaration becomes `internal sealed class TextReaderSession : IReaderSession, IFindableReader`;
- `Capabilities` adds `| ReaderCapabilities.Find`;
- add these members:

```csharp
    // T18.1: the last query's matches, so Next and Previous step through them.
    private string? findText;
    private IReadOnlyList<TextMatch> matches = [];
    private int? current;

    public async Task<FindResult> FindAsync(FindQuery query, FindDirection direction, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Text != findText)
        {
            string text = document.Text;
            matches = await Task.Run(() => TextMatcher.FindAll(text, query), token);
            findText = query.Text;
            current = null;
        }
        token.ThrowIfCancellationRequested();
        int top = document.LineStarts[Math.Clamp(View.FirstVisibleIndex, 0, document.LineStarts.Count - 1)];
        current = TextMatcher.Step(matches, current, top, direction);
        if (current is not int index)
        {
            View.ClearMatch();
            return FindResult.None;
        }
        TextMatch match = matches[index];
        View.ShowMatch(Segments(match));
        // A little context above the match, as a reader expects.
        View.ScrollToLine(Math.Max(0, document.LineAtOffset(match.Start) - 3));
        return new FindResult(FindOutcome.Found, index + 1, matches.Count, false, 0);
    }

    public void ClearFind()
    {
        findText = null;
        matches = [];
        current = null;
        View.ClearMatch();
    }

    private IReadOnlyList<(int Line, int Column, int Length)> Segments(TextMatch match)
    {
        int first = document.LineAtOffset(match.Start);
        int last = document.LineAtOffset(match.Start + match.Length - 1);
        List<(int Line, int Column, int Length)> segments = [];
        for (int line = first; line <= last; line++)
        {
            int lineStart = document.LineStarts[line];
            int from = Math.Max(match.Start, lineStart);
            int to = match.Start + match.Length;
            int column = TextLineView.ColumnOf(document, line, from);
            int endColumn = TextLineView.ColumnOf(document, line, to);
            segments.Add((line, column, Math.Max(0, endColumn - column)));
        }
        return segments;
    }
```

Check `LineAtOffset`'s name and signature in `TextGuideDocument.cs`, which
the explorer reported as `LineAtOffset(int)`. If it differs, use it, and
ledger it.

- [ ] **Step 6: Commit**

The Production build still fails until Task 5 removes `FindAction`. The
Core tests pass:

Run: `stage && s 'cd /d E:\work\desktop-guides\t18-1 && dotnet test tests\DesktopGuides.Core.Tests -nologo'`
Expected: `Passed!`.

```bash
git add src/DesktopGuides.Core/Text/TextLineView.cs tests/DesktopGuides.Core.Tests/TextLineViewTests.cs src/DesktopGuides.Production/TextReaderSession.cs src/DesktopGuides.Production/TextReaderView.xaml.cs src/DesktopGuides.Production/Styles/DesignTokens.xaml
git commit -m "feat(p2): search TXT guides and highlight the match"
```

---

### Task 4: The keyboard map's search keys

**Files:**
- Modify: `src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs`
- Test: `tests/DesktopGuides.Core.Tests/KeyboardShortcutsTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/ShortcutRoutingTests.cs`

**Interfaces:**
- Produces: `ShortcutCommand.FindInGuide`, `ShortcutCommand.FindNext` and
  `ShortcutCommand.FindPrevious`, all in `ShortcutContext.Reader`.

- [ ] **Step 1: Write the failing tests**

In `KeyboardShortcutsTests.TheTableMatchesTheSpec`, insert after the
`Ctrl+0` row:

```csharp
                (ShortcutContext.Reader, "Ctrl+F", ShortcutCommand.FindInGuide, "Search in the guide"),
                (ShortcutContext.Reader, "F3", ShortcutCommand.FindNext, "Next match"),
                (ShortcutContext.Reader, "Shift+F3", ShortcutCommand.FindPrevious, "Previous match"),
```

In `ShortcutRoutingTests.cs`, add:

```csharp
    [Fact]
    public void SearchKeysRunInTheReaderEvenFromTheFindBox()
    {
        foreach (ShortcutCommand command in new[] { ShortcutCommand.FindInGuide, ShortcutCommand.FindNext, ShortcutCommand.FindPrevious })
        {
            Assert.Equal(ShortcutDecision.Run, ShortcutRouting.Resolve(command, new ReaderRoute(Guid.NewGuid()), textInputFocused: true, dialogOpen: false));
            Assert.Equal(ShortcutDecision.Ignore, ShortcutRouting.Resolve(command, new LibraryRoute(), textInputFocused: false, dialogOpen: false));
        }
        Assert.Equal(ShortcutDecision.Ignore,
            ShortcutRouting.Resolve(ShortcutCommand.FocusLibrarySearch, new ReaderRoute(Guid.NewGuid()), false, false));
    }
```

Build each route the way the file's other tests do. If `ReaderRoute` or
`LibraryRoute` take different arguments, copy them from an existing
test.

- [ ] **Step 2: Run them to verify they fail**

Run: `stage && s 'cd /d E:\work\desktop-guides\t18-1 && dotnet test tests\DesktopGuides.Core.Tests --filter "KeyboardShortcutsTests|ShortcutRoutingTests" -nologo'`
Expected: a build FAILURE, because the three commands don't exist.

- [ ] **Step 3: Add the keys**

In `KeyboardShortcuts.cs`:
- the enum adds `FindInGuide, FindNext, FindPrevious` before `CloseDialog`;
- the table adds, after the `Ctrl+0` entry:

```csharp
        new(ShortcutContext.Reader, "Ctrl+F", ShortcutCommand.FindInGuide, "Search in the guide"),
        new(ShortcutContext.Reader, "F3", ShortcutCommand.FindNext, "Next match"),
        new(ShortcutContext.Reader, "Shift+F3", ShortcutCommand.FindPrevious, "Previous match"),
```

`ShortcutRouting` needs no change: Reader-context commands outside
`TypingKeys` already run while a text box has focus.

- [ ] **Step 4: Run them to verify they pass, and commit**

Run: the Step 2 command.
Expected: PASS, including `EveryCommandAppearsOnce`.

```bash
git add src/DesktopGuides.Core/Navigation/KeyboardShortcuts.cs tests/DesktopGuides.Core.Tests/KeyboardShortcutsTests.cs tests/DesktopGuides.Core.Tests/ShortcutRoutingTests.cs
git commit -m "feat(p2): add the reader's search keys to the keyboard map"
```

---

### Task 5: The find bar and the shell's wiring

**Files:**
- Create: `src/DesktopGuides.Production/FindBar.xaml`, `FindBar.xaml.cs`
- Create: `src/DesktopGuides.Production/ShellWindow.Find.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (the toolbar `StackPanel` at 510-511)
- Modify: `src/DesktopGuides.Production/ShellWindow.Keyboard.cs` (Ctrl+F by route; F3 and Shift+F3)
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml.cs` (`FindInGuideClicked` raises `FindRequested`)
- Modify: `src/DesktopGuides.Production/ShellWindow.Progress.cs` (close the find bar when tracking ends)

**Interfaces:**
- Consumes: Tasks 1-4.
- Produces:
  - `FindBar` events `SearchRequested(FindDirection)`, `BackRequested` and
    `CloseRequested`; a `Query` property; and `ShowResult(FindResult, string query)`;
  - `ShellWindow.OpenFindAsync()`, `CloseFind()`;
  - `ReaderToolbar.FindRequested`;
  - AutomationIds `ReaderFindBar`, `FindQueryInput`, `FindCount`,
    `FindPreviousButton`, `FindNextButton`, `FindBackButton` and
    `FindCloseButton`, used by Task 6.

- [ ] **Step 1: The control**

`FindBar.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl x:Class="DesktopGuides.Production.FindBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             AutomationProperties.AutomationId="ReaderFindBar"
             AutomationProperties.Name="Search in the guide">
    <Grid ColumnSpacing="8" Margin="{StaticResource DesktopGuidesReaderGapAbove}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" MaxWidth="420" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBox x:Name="QueryBox"
                 PlaceholderText="Search this guide"
                 AutomationProperties.AutomationId="FindQueryInput"
                 AutomationProperties.Name="Search this guide"
                 KeyDown="QueryKeyDown" />
        <TextBlock x:Name="Count" Grid.Column="1" VerticalAlignment="Center"
                   AutomationProperties.AutomationId="FindCount"
                   AutomationProperties.LiveSetting="Polite" />
        <Button Grid.Column="2" Click="PreviousClicked" AutomationProperties.AutomationId="FindPreviousButton"
                AutomationProperties.Name="Previous match" ToolTipService.ToolTip="Previous match (Shift+F3)">
            <SymbolIcon Symbol="Up" />
        </Button>
        <Button Grid.Column="3" Click="NextClicked" AutomationProperties.AutomationId="FindNextButton"
                AutomationProperties.Name="Next match" ToolTipService.ToolTip="Next match (F3)">
            <SymbolIcon Symbol="Download" />
        </Button>
        <Button Grid.Column="4" Content="Back to my place" Click="BackClicked"
                AutomationProperties.AutomationId="FindBackButton" />
        <Button Grid.Column="5" Click="CloseClicked" AutomationProperties.AutomationId="FindCloseButton"
                AutomationProperties.Name="Close search" ToolTipService.ToolTip="Close search (Esc)">
            <SymbolIcon Symbol="Cancel" />
        </Button>
    </Grid>
</UserControl>
```

`Download` is a placeholder glyph for "down". If the project's Fluent
icon set (see `ReaderToolbar.xaml`) has an up and down chevron pair, use
those instead, and ledger it.

`FindBar.xaml.cs`:

```csharp
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DesktopGuides.Production;

// T18.1: the reader's find bar. It reports intent; the shell searches.
public sealed partial class FindBar : UserControl
{
    public FindBar() => InitializeComponent();

    public event EventHandler<FindDirection>? SearchRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? CloseRequested;

    public string Query => QueryBox.Text;

    public void FocusQuery()
    {
        QueryBox.Focus(FocusState.Keyboard);
        QueryBox.SelectAll();
    }

    public void Reset()
    {
        QueryBox.Text = string.Empty;
        Count.Text = string.Empty;
    }

    public void ShowResult(FindResult result, string query)
    {
        Count.Text = result.Outcome == FindOutcome.Found
            ? $"{result.Index} of {result.Total}{(result.TotalIsPartial ? "+" : "")}"
            : "No matches";
    }

    private void QueryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            SearchRequested?.Invoke(this, shift ? FindDirection.Previous : FindDirection.Next);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
        }
    }

    private void PreviousClicked(object sender, RoutedEventArgs args) =>
        SearchRequested?.Invoke(this, FindDirection.Previous);

    private void NextClicked(object sender, RoutedEventArgs args) =>
        SearchRequested?.Invoke(this, FindDirection.Next);

    private void BackClicked(object sender, RoutedEventArgs args) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void CloseClicked(object sender, RoutedEventArgs args) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
```

`ShellWindow.Keyboard.cs` already has a `CurrentModifiers()` helper. If
the shift check above doesn't compile in this project, use the same
approach `CurrentModifiers` uses, and ledger it.

- [ ] **Step 2: Place it**

In `ShellWindow.xaml`, after `<local:ReaderToolbar x:Name="ReaderActions" />`:

```xml
                    <local:FindBar x:Name="ReaderFind" Visibility="Collapsed" />
```

- [ ] **Step 3: The shell's search**

`ShellWindow.Find.cs`:

```csharp
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T18.1: while the bar is open, progress is paused, so moving to
    // matches never saves the reading place; Back returns to findStart.
    private IDisposable? findPause;
    private ReaderLocation? findStart;
    private CancellationTokenSource? findCancel;

    private void InitializeFind()
    {
        ReaderFind.SearchRequested += (_, direction) => _ = FindStepAsync(direction);
        ReaderFind.BackRequested += (_, _) => _ = FindBackAsync();
        ReaderFind.CloseRequested += (_, _) => CloseFind();
        ReaderActions.FindRequested += (_, _) => _ = OpenFindAsync();
    }

    private async Task OpenFindAsync()
    {
        if (readerSession is not IFindableReader)
        {
            ShowStatus("Search isn't available for this guide.", InfoBarSeverity.Informational, true, true);
            AnnounceStatus("Search isn't available for this guide.");
            return;
        }
        if (ReaderFind.Visibility != Visibility.Visible)
        {
            IReaderSession session = readerSession;
            findStart = await session.GetLocationAsync(CancellationToken.None);
            findPause = progressTracking is null
                ? null
                : await progressTracking.PauseAsync(CancellationToken.None);
            if (!ReferenceEquals(session, readerSession))
            {
                findPause?.Dispose();
                findPause = null;
                return;
            }
            ReaderFind.Reset();
            ReaderFind.Visibility = Visibility.Visible;
        }
        ReaderFind.FocusQuery();
    }

    private async Task FindStepAsync(FindDirection direction)
    {
        if (ReaderFind.Visibility != Visibility.Visible || readerSession is not IFindableReader findable)
        {
            return;
        }
        FindQuery query;
        try
        {
            query = new FindQuery(ReaderFind.Query);
        }
        catch (ArgumentException)
        {
            AnnounceStatus("Enter text to find.");
            return;
        }
        findCancel?.Cancel();
        findCancel = new CancellationTokenSource();
        FindResult result;
        try
        {
            result = await findable.FindAsync(query, direction, findCancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        ReaderFind.ShowResult(result, query.Text);
        AnnounceStatus(result.Outcome == FindOutcome.Found
            ? $"Match {result.Index} of {result.Total}{(result.TotalIsPartial ? " so far" : "")}."
            : $"No matches for {query.Text}.");
    }

    private async Task FindBackAsync()
    {
        if (readerSession is IReaderSession session && findStart is ReaderLocation start)
        {
            await session.RestoreLocationAsync(start, CancellationToken.None);
        }
    }

    // Also called when the reader closes; never saves the place.
    private void CloseFind()
    {
        findCancel?.Cancel();
        findCancel = null;
        if (readerSession is IFindableReader findable) findable.ClearFind();
        findPause?.Dispose();
        findPause = null;
        findStart = null;
        if (ReaderFind.Visibility == Visibility.Visible)
        {
            ReaderFind.Visibility = Visibility.Collapsed;
        }
    }
}
```

Read first and adapt to the real code:
- **The fields:** `readerSession` (in `ShellWindow.HtmlReader.cs`) and
  `progressTracking` (in `ShellWindow.Progress.cs`). If TXT and PDF use
  another field for their session, use the one that holds the open
  reader's `IReaderSession`.
- **The helpers:** `ShowStatus` and `AnnounceStatus` take the arguments
  their declarations show. Match them.
- **Focus after closing:** the reader content keeps focus when the bar
  hides only if focus moves there. After hiding, focus the reader view the
  way Back-to-reader code does (`grep -n "Focus(FocusState" ShellWindow*.cs`).

Call `InitializeFind();` where `InitializeKeyboard();` is called.

- [ ] **Step 4: The keys and the toolbar button**

In `ShellWindow.Keyboard.cs`:
- **Ctrl+F by route:** the single Ctrl+F accelerator decides by route. Replace
  `AddShortcut(VirtualKey.F, ShortcutCommand.FocusLibrarySearch, FocusLibrarySearch);` with:

```csharp
        AddShortcut(VirtualKey.F, ShortcutCommand.FocusLibrarySearch, () =>
            navigator.Current is ReaderRoute ? OpenFindFromKeyboard() : FocusLibrarySearch());
```

  `AddShortcut` resolves only `FocusLibrarySearch`'s routing, which ignores
  the reader. So in `AddShortcut`'s `Invoked` handler, a reader Ctrl+F
  must also be let through:

```csharp
            ShortcutCommand resolved = navigator.Current is ReaderRoute && command == ShortcutCommand.FocusLibrarySearch
                ? ShortcutCommand.FindInGuide
                : command;
            if (ShortcutRouting.Resolve(resolved, navigator.Current, TextInputFocused(), DialogOpen())
                == ShortcutDecision.Run)
```

  replacing the existing `ShortcutRouting.Resolve(command, ...)` line.

- **F3 and Shift+F3:** these work even from the find box, so they're
  handled at the top of `ShellPreviewKeyDown`, before the TextBox check:

```csharp
        if (navigator.Current is ReaderRoute && args.Key == VirtualKey.F3 && ReaderFind.Visibility == Visibility.Visible)
        {
            args.Handled = true;
            _ = FindStepAsync(modifiers == VirtualKeyModifiers.Shift ? FindDirection.Previous : FindDirection.Next);
            return;
        }
```

- **The helper:**

```csharp
    private bool OpenFindFromKeyboard()
    {
        _ = OpenFindAsync();
        return true;
    }
```

In `ReaderToolbar.xaml.cs`, replace the body of `FindInGuideClicked`:

```csharp
    // T18.1: the shell opens the find bar; the old prompt dialog is gone.
    public event EventHandler? FindRequested;

    private void FindInGuideClicked(object sender, RoutedEventArgs args) =>
        FindRequested?.Invoke(this, EventArgs.Empty);
```

Remove the old `async void FindInGuideClicked` with its `PromptAsync` and
`FindAction` use.

In `ShellWindow.Progress.cs`, where the tracking ends (the block that sets
`progressTracking = null;` near line 91), call `CloseFind();` first. That
way leaving the reader drops the pause, and the paused tracking saved
nothing. Then also call `CloseFind()` wherever the HTML and PDF sessions
are torn down without going through that block: `ShellWindow.HtmlReader.cs`
line 108, and the matching PDF path.

- [ ] **Step 5: Build, run Core, and commit**

```bash
stage
s 'cd /d E:\work\desktop-guides\t18-1 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo' | grep -E "error|Warning\(s\)|Error\(s\)"
s 'cd /d E:\work\desktop-guides\t18-1 && dotnet test tests\DesktopGuides.Core.Tests -nologo' | tail -1
```

Expected: `0 Error(s)`, no new warnings, and `Passed!`.

```bash
git add src/DesktopGuides.Production
git commit -m "feat(p2): add the reader's find bar for searchable guides"
```

---

### Task 6: The `navigation` scenario group: `find-txt`, `find-back` and `find-unsupported`

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (three modes, and the
  mode list at line 3)
- Modify: `tools/p1/windows_shell_install.ps1` (a `-NavigationOnly`
  switch; a `navigation` entry in the group table; `Run-NavigationScenarios`;
  the dispatch)
- Modify: `tools/p1/start_installed_task.ps1` (mirror `-NavigationOnly`)
- Modify: `.github/workflows/windows-ci.yml` (`shell-scope` gains
  `navigation`; the shard matrix gains a `navigation` shard)
- Modify: `docs/p1/e2e-testing.md` (the scenario table gains a row)

**Interfaces:**
- Consumes: Task 5's AutomationIds, and the `seed-progress` seed:
  `Progress Game` with `Numbered Lines Guide` (400 lines,
  `Line 0001 | Numbered guide text.`), `Long Web Guide` and
  `Long PDF Guide`.

- [ ] **Step 1: Read the existing TXT smoke helpers**

Read `windows_shell_ui_smoke.ps1` around `Open-TextGuide` (line 1664) and
`Wait-TopLine` (line 2231). Note their scope: they may be defined inside a
mode branch. If so, put the find modes in that same branch, or move the
two helpers up to the shared helpers. Ledger which you did.

- [ ] **Step 2: Write the smoke modes**

Add `'find-txt'`, `'find-back'` and `'find-unsupported'` to the mode
`ValidateSet`. Add the branches, with the helper names adapted to what
Step 1 found:

```powershell
            elseif ($Mode -eq 'find-txt') {
                # T18.1: TXT search; the saved place must not move until the user moves.
                Enter-ProgressGame
                Open-TextGuide 'Numbered Lines Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-TopLine 1 'Opening'
                [System.Windows.Forms.SendKeys]::SendWait('^f')
                [void](Wait-VisibleById 'ReaderFindBar')
                Wait-FocusedId 'FindQueryInput'
                (Get-SearchPattern 'FindQueryInput').SetValue('line 0250')
                Start-Sleep -Milliseconds 300
                Wait-TopLine 1 'Typing a query'
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-Name 'FindCount' '1 of 1')
                $top = Get-TopLine
                if ($top -gt 250 -or $top -lt 240) { throw "After finding Line 0250 the top line is $top." }
                Invoke-Element (Wait-VisibleById 'FindBackButton')
                Wait-TopLine 1 'Back to my place'
                (Get-SearchPattern 'FindQueryInput').SetValue('line 02')
                (Wait-VisibleById 'FindQueryInput').SetFocus()
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-Name 'FindCount' '1 of 100')
                [System.Windows.Forms.SendKeys]::SendWait('{F3}')
                [void](Wait-Name 'FindCount' '2 of 100')
                [System.Windows.Forms.SendKeys]::SendWait('+{F3}')
                [void](Wait-Name 'FindCount' '1 of 100')
                [System.Windows.Forms.SendKeys]::SendWait('+{F3}')
                [void](Wait-Name 'FindCount' '100 of 100')
                $report.findScreenshot = Save-WindowScreenshot 'find-txt'
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Wait-GoneById 'ReaderFindBar'
                $report.phases += 'find-txt-searched'
                # The installer checks no place was saved; then one real move saves.
                Wait-InstallerStep 'find-txt-closed'
                [System.Windows.Forms.SendKeys]::SendWait('{PGDN}')
                Start-Sleep -Seconds 2
                $report.phases += 'find-txt-moved'
            }
            elseif ($Mode -eq 'find-back') {
                # T18.1: leaving the guide with the bar open saves nothing.
                Enter-ProgressGame
                Open-TextGuide 'Numbered Lines Guide'
                [void](Wait-Status 'Guide ready.')
                [System.Windows.Forms.SendKeys]::SendWait('^f')
                (Get-SearchPattern 'FindQueryInput').SetValue('line 0300')
                (Wait-VisibleById 'FindQueryInput').SetFocus()
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-Name 'FindCount' '1 of 1')
                [System.Windows.Forms.SendKeys]::SendWait('%{LEFT}')
                [void](Wait-Name 'GameHeading' 'Progress Game')
                $report.phases += 'find-back-left'
            }
            elseif ($Mode -eq 'find-unsupported') {
                # T18.1: HTML search arrives in PR b; until then Ctrl+F says so.
                Enter-ProgressGame
                Open-TextGuide 'Long Web Guide'
                [void](Wait-Status 'Guide ready.')
                [System.Windows.Forms.SendKeys]::SendWait('^f')
                [void](Wait-Status "Search isn't available for this guide.")
                Assert-Absent 'ReaderFindBar'
                $report.phases += 'find-unsupported'
            }
```

Read first and adapt:
- **Helpers this sketch assumes:** `Enter-ProgressGame`, `Get-TopLine`,
  `Wait-FocusedId`, `Wait-GoneById` and `Wait-InstallerStep`. For each one
  that doesn't exist:
  - **Entering the game:** use the progress group's own way of entering
    `Progress Game`, from the `progress-*` modes.
  - **The top line:** read it the way `Wait-TopLine` does.
  - **The other waits:** write the smallest helper, in the style of
    `Wait-VisibleById`. For the installer hand-off, use the handshake
    the progress modes already use between installer and smoke (look for
    `Wait-` plus `ready`/`release` files, as with `hold-write-lock`).
- **The game page's heading** may have another AutomationId than
  `GameHeading`; use the one the game-actions modes wait for.
- Ledger every adaptation.

- [ ] **Step 3: Write the installer group**

In `windows_shell_install.ps1`:
- add `[switch] $NavigationOnly,` after `$RcOnly`, and mirror it in
  `start_installed_task.ps1` after its `$RcOnly`;
- add `'navigation' = $NavigationOnly.IsPresent` to `$scenarioGroups`, after
  `'workflow'` and before `'rc'`;
- add `Run-NavigationScenarios` beside `Run-TxtReaderScenarios`:

```powershell
# T18.1: in-guide search. Search must never save the reading place.
function Run-NavigationScenarios {
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    Invoke-ShellSeed @('seed-progress', $dataRoot, $fixtureRoot) | Out-Null
    $estimateOf = {
        param([string] $title)
        @(Invoke-ShellSeed @('describe-progress', $dataRoot) | ConvertFrom-Json |
            Where-Object { $_.title -eq $title })[0].estimate
    }

    Start-InstalledShell
    $report.findTxt = Run-ShellSmoke 'find-txt'
    Close-InstalledShell
    # find-txt pauses after closing the bar; the check runs inside it (Step 2).
    if ($null -eq (& $estimateOf 'Numbered Lines Guide')) {
        throw 'After closing the find bar and paging down, the place was not saved.'
    }

    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    Start-InstalledShell
    $report.findBack = Run-ShellSmoke 'find-back'
    Close-InstalledShell
    if ($null -ne (& $estimateOf 'Numbered Lines Guide')) {
        throw 'Leaving the guide with the find bar open saved the place.'
    }

    Start-InstalledShell
    $report.findUnsupported = Run-ShellSmoke 'find-unsupported'
    Close-InstalledShell
}
```

The `find-txt-closed` hand-off: before the smoke pages down, the installer
asserts `& $estimateOf 'Numbered Lines Guide'` is still `$null`, which
means search saved nothing. Wire it with the same ready and release
handshake Step 2 chose, and record the result as
`$report.findTxtSavedNothing = $true`.

- **The dispatch:** after the workflow group's line, add
  `if (Enter-ScenarioGroup 'navigation') { Run-NavigationScenarios }`.

Read `clear-reading-locations` in `ShellSeed/Program.cs` first. It clears
the stored place and estimate. If it needs another argument, add it, and
ledger it.

- [ ] **Step 4: CI**

In `windows-ci.yml`:
- add `- navigation` to the `shell-scope` choice options;
- in the shard matrix's default list, add
  `{"shard":"navigation","groups":"navigation"}` after the workflow shard.

- [ ] **Step 5: Document the group**

In `docs/p1/e2e-testing.md`'s scenario table, after the "Text size" row,
add:

```markdown
| In-guide search | `-NavigationOnly` (the `navigation` group), after `seed-progress`. `find-txt`: Ctrl+F opens the find bar with focus in `FindQueryInput`; typing doesn't move the guide; `line 0250` then Enter reads `1 of 1` with Line 0250 in view; *Back to my place* returns to line 1; `line 02` reads `1 of 100`, F3 `2 of 100`, Shift+F3 `1 of 100` then wraps to `100 of 100`; Escape closes the bar. The installer then checks no place was saved; after Page Down one is. `find-back`: leaving the guide with the bar open saves nothing. `find-unsupported`: Ctrl+F in an HTML guide says "Search isn't available for this guide." and shows no bar (until T18.1 PR b). Screenshot: `find-txt`. | T18.1, TR18.1 |
```

- [ ] **Step 6: Check, commit, push and dispatch**

```bash
stage
s 'cd /d E:\work\desktop-guides\t18-1 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_start_installed_task.ps1'
grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git add tools/p1 .github/workflows/windows-ci.yml docs/p1/e2e-testing.md
git commit -m "test(p2): add the navigation group with TXT search scenarios"
git push -u origin feat/p2-t18-1-search
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p2-t18-1-search -f shell-scope=navigation -f shell-lane=both
```

Expected:
- both script tests pass, and the files are ASCII-only;
- the dispatch's `navigation` shard passes on both lanes, with
  `find-txt`, `find-back` and `find-unsupported` passing and the
  `find-txt` screenshot showing the highlighted match.

If a mode fails, use superpowers:systematic-debugging on its evidence.

---

### Task 7: Records, the full PR run and the PR

**Files:**
- Modify: `docs/p2/t18-1-search-design.md` (Status; a "PR a verification
  record")
- Create: `docs/p2/results.md` (the P2 results log, with a #-row once
  merged)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md` (T18.1
  in progress: PR a in review)

- [ ] **Step 1: Write the records**

- **The design:**
  - Status: "PR a (contract, TXT search and find bar) implemented on
    `feat/p2-t18-1-search`; PR b (HTML) and PR c (PDF) follow."
  - Add a "PR a verification record": the red and green test runs, the
    dispatch ID, and every ledgered ruling.
- **`docs/p2/results.md`:** headed "P2 results", with a PR table in the
  `docs/p1/results.md` format (PR, merged, commit, task, final CI run,
  Core and Infra counts, record, notes), left empty until merge.
- **The status pages:** in `implementation-plan.md` and `progress.md`, the
  P2 status becomes "M7 in progress: T18.1 PR a (TXT search and the find
  bar) in review."

- [ ] **Step 2: Commit, push, open the PR, and wait for its run**

```bash
git add docs
git commit -m "docs(p2): record T18.1 PR a"
git push
```

Title: "T18.1 PR a: in-guide search contract, TXT search and the find
bar". The body names:
- the task, the prerequisites (#112) and the outcome;
- a screenshot of the find bar with a highlighted match, from the
  `find-txt` evidence, since this is a UI change;
- the dispatch;
- every ruling.

**Base:** while #112 is open, target `docs/p2-technical-design`. Retarget
to `main` once #112 merges.

Expected: every job passes, including the new `navigation` shard on both
lanes. Record the run in the PR description, then stop for the user's
merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| `IFindableReader`, `FindQuery`, `FindResult`; `FindAction` replaced | 1, 5 |
| `TextMatcher`: case, whitespace runs, caps, Step | 1 |
| The progress pause: search never saves; leaving saves nothing | 2, 5, 6 |
| TXT search and highlight, `ColumnOf` | 3 |
| Keyboard map: Ctrl+F, F3, Shift+F3 | 4, 5 |
| The find bar, "Back to my place", announcements, unsupported guides | 5, 6 |
| The `navigation` group and CI shard | 6 |
| HTML search (PR b) and PDF search (PR c) | later plans |
