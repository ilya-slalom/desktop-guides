# T08.2 Virtualized TXT view Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Opening an imported TXT guide shows its text in the Reader as a
virtualized, fixed-width, unwrapped list of lines, with explicit failures
and P0-level response on the 10 MiB `txt-long` fixture.

**Architecture:** Core gets pure line presentation (`TextLineView`), a lazy
read-only `IList` over a `TextGuideDocument` (`TextLineList`), the widest
line scan (`TextLineMetrics`) and the failure sentences
(`TextGuideLoadMessages`). Production gets a `ListView`-based
`TextReaderView` and a `TextReaderSession : IReaderSession`. `ShellWindow`
loads TXT guides with T08.1's `ManagedTextGuideLoader` inside the existing
`ReaderRoute` render path and closes the session on every render. The
installed smoke gains a `txt-reader` mode, seeded from fixture files.

**Tech Stack:** .NET 10, C#, WinUI 3 (Windows App SDK), xUnit, SQLite
(ShellSeed), PowerShell 7 + UI Automation, GitHub Actions `windows-2025`.

**Spec:** [t08-2-txt-view-design.md](t08-2-txt-view-design.md)

## Global Constraints

- Display rules: a tab expands to the next multiple of **8** columns;
  U+0000–U+001F other than tab, and U+007F, display as **one space**; every
  other UTF-16 code unit passes through. A column is one UTF-16 code unit.
  `TextGuideDocument.Text` and its offsets are never changed.
- Row count excludes the empty line after a trailing `\n`; an empty
  document has 0 rows.
- No per-line cache: the only line strings alive are those of realized rows
  (TR08.2). No selection: the session never reports `SelectableText`.
- Session: `Format` `Txt`; `Capabilities` `Scroll` only.
- Font: Consolas, `TextWrapping=NoWrap`, `IsTextSelectionEnabled=False`.
- AutomationIds: list `ReaderTextLines`, load error `ReaderLoadError`,
  placeholder `ReaderPlaceholder` (unchanged).
- Blank (empty or whitespace-only) line automation name: `Blank line`.
- Statuses: `Loading guide…` (existing busy), `Guide ready.` (replaces
  `Guide details ready.` for every format).
- Failure sentences, exactly:
  - Missing: `This guide's file is missing from the library.`
  - TooLarge: `This guide is larger than the 64 MB limit for text files.`
  - Unreadable: `This guide's file can't be opened. Close any app that's using it, then open the guide again.`
  - InvalidMetadata: `This guide's saved details are damaged, so it can't be opened.`
  - NotUtf8: `This guide isn't valid UTF-8 text, so it can't be opened.`
  - Undecodable: `This guide can't be read with its saved encoding.`
- `txt-long` gates: first text ≤ **3000 ms** from Open; realized ListItems
  ≤ **300** after opening and after **8** `LargeIncrement` scrolls; fewer
  than **2** `WM_NULL` samples above **500 ms** and no timed-out sample.
- PowerShell scripts stay ASCII-only. CI is the performance gate.
- Pushing the CI workflow change needs the user's explicit OK.

## Review Focus

1. **Back during a long load.** Leaving the Reader while `txt-long` is
   still loading must leave the Game page clean: no text view, no toolbar
   session and no late `Guide ready.` (Task 5, step "Back during load").
2. **Legacy code page.** A guide saved with code page 437 shows its decoded
   characters (`Guide é`), not replacement characters (Task 4 seeds it,
   Task 5 asserts it).
3. **Whitespace-only line.** `txt-ascii`'s last line is three spaces; its
   row is named `Blank line` and still takes one row (Task 5 txt-ascii
   names).
4. **Trailing-newline and empty-line counts.** A file ending in `\n`
   gets no extra blank row, but an internal empty line does (Task 2
   `TextLineListTests`).
5. **Non-TXT after TXT.** Opening an HTML guide after a TXT guide shows
   the placeholder again, not the old text (Task 5 HTML step).

---

## File map

| File | Responsibility |
| --- | --- |
| `src/DesktopGuides.Core/Text/TextLineView.cs` (new) | Display text, display width and column→offset for one line |
| `src/DesktopGuides.Core/Text/TextLineList.cs` (new) | `TextLineItem` and the lazy read-only `IList` |
| `src/DesktopGuides.Core/Text/TextLineMetrics.cs` (new) | Widest line in display columns |
| `src/DesktopGuides.Core/Text/TextGuideLoadMessages.cs` (new) | One sentence per `TextGuideLoadError` |
| `tests/DesktopGuides.Core.Tests/TextLineViewTests.cs` (new) | |
| `tests/DesktopGuides.Core.Tests/TextLineListTests.cs` (new) | |
| `tests/DesktopGuides.Core.Tests/TextLineMetricsTests.cs` (new) | |
| `tests/DesktopGuides.Core.Tests/TextGuideLoadMessagesTests.cs` (new) | |
| `src/DesktopGuides.Production/TextReaderView.xaml(.cs)` (new) | The `ListView` and row sizing |
| `src/DesktopGuides.Production/TextReaderSession.cs` (new) | `IReaderSession` over one document |
| `src/DesktopGuides.Production/ShellWindow.xaml(.cs)` | Reader surface, load path, session lifetime |
| `tools/p1/windows_shell_ui_smoke.ps1` | Status rename, updated asserts, `txt-reader` mode |
| `tools/p1/windows_shell_response_monitor.cs` (new) | `WM_NULL` response monitor (from P0) |
| `tools/p1/windows_shell_install.ps1` | `Run-TxtReaderScenarios` |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | `seed-txt-reader` |
| `tests/fixtures/p1/txt-tabs.txt` (new), `.gitattributes` | Tab and form-feed fixture, kept byte-exact |
| `.github/workflows/windows-ci.yml` | Python and fixture generation in `production-shell-ui` |
| Docs (Task 6) | Traceability and test matrix |

Ruling: the design names `Production/Reading/` for the view and session;
Production keeps every control at its root (`ReaderToolbar.xaml` sits
there), so the new files go at the root in namespace
`DesktopGuides.Production`. Cost if wrong: a file move.

Test commands used below:

- Core: `dotnet test tests/DesktopGuides.Core.Tests -c Release`
  (baseline 323 passing).
- Full: `dotnet test tests/DesktopGuides.Core.Tests -c Release && dotnet test tests/DesktopGuides.Infrastructure.Tests -c Release`
  (baselines 323 and 438).
- Production builds only on Windows. From the Mac, stage and build on
  `pcsx2-win` with
  `/tmp/t082.sh "dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"`
  (a copy of `/tmp/t081.sh` that stages to `E:\work\desktop-guides\t08-2`).
  The installed UI gate is CI `production-shell-ui`.

### Task 1: TextLineView

**Files:**
- Create: `src/DesktopGuides.Core/Text/TextLineView.cs`
- Test: `tests/DesktopGuides.Core.Tests/TextLineViewTests.cs`

**Interfaces:**
- Consumes: `TextGuideDocument.Decode(byte[], int?)`, `.Text`, `.LineStarts`.
- Produces: `public static class TextLineView` with
  `public const int TabWidth = 8;`,
  `public static string DisplayText(TextGuideDocument document, int line)`,
  `public static int DisplayColumns(TextGuideDocument document, int line)`,
  `public static int SourceOffset(TextGuideDocument document, int line, int column)`.
  `line` is a zero-based index into `LineStarts`; out of range throws
  `ArgumentOutOfRangeException`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextLineViewTests
{
    private static TextGuideDocument Document(string text) =>
        TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));

    [Theory]
    [InlineData("\tx", "", 8)]
    [InlineData("abc\tx", "abc", 5)]
    [InlineData("abcdefg\tx", "abcdefg", 1)]
    [InlineData("abcdefgh\tx", "abcdefgh", 8)]
    [InlineData("0123456789\tx", "0123456789", 6)]
    public void ATabExpandsToTheNextEightColumnStop(string line, string before, int spaces)
    {
        Assert.Equal(before + new string(' ', spaces) + "x", TextLineView.DisplayText(Document(line), 0));
    }

    [Fact]
    public void ConsecutiveTabsEachReachTheNextStop()
    {
        Assert.Equal("a" + new string(' ', 15) + "b", TextLineView.DisplayText(Document("a\t\tb"), 0));
    }

    [Theory]
    [InlineData("a\fb")]
    [InlineData("a\u001Ab")]
    [InlineData("a\u001Bb")]
    [InlineData("a\u007Fb")]
    [InlineData("a\0b")]
    public void AControlCharacterShowsAsOneSpace(string line)
    {
        Assert.Equal("a b", TextLineView.DisplayText(Document(line), 0));
    }

    [Fact]
    public void SpacesAndOtherCharactersAreKept()
    {
        const string line = "  lead  mid é ─┼─  trail  ";

        Assert.Equal(line, TextLineView.DisplayText(Document(line), 0));
    }

    [Fact]
    public void ALineExcludesItsNewline()
    {
        TextGuideDocument document = Document("one\r\ntwo\n");

        Assert.Equal("one", TextLineView.DisplayText(document, 0));
        Assert.Equal("two", TextLineView.DisplayText(document, 1));
        Assert.Equal("", TextLineView.DisplayText(document, 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ALineOutsideTheDocumentThrows(int line)
    {
        TextGuideDocument document = Document("one\ntwo\n");

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLineView.DisplayText(document, line));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLineView.DisplayColumns(document, line));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLineView.SourceOffset(document, line, 0));
    }

    [Fact]
    public void DisplayColumnsIsTheDisplayTextLength()
    {
        TextGuideDocument document = Document("\tx\nab\t\tc\n\fz\u007F\n\nplain\n0123456789\t!");

        for (int line = 0; line < document.LineStarts.Count; line++)
        {
            Assert.Equal(TextLineView.DisplayText(document, line).Length, TextLineView.DisplayColumns(document, line));
        }
    }

    [Fact]
    public void SourceOffsetRoundTripsEachCharactersColumn()
    {
        // Line 1 is "ab\tc", starting at offset 2; its characters sit at columns 0, 1, 2 and 8.
        TextGuideDocument document = Document("x\nab\tc");

        Assert.Equal(2, TextLineView.SourceOffset(document, 1, 0));
        Assert.Equal(3, TextLineView.SourceOffset(document, 1, 1));
        Assert.Equal(4, TextLineView.SourceOffset(document, 1, 2));
        Assert.Equal(5, TextLineView.SourceOffset(document, 1, 8));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void AColumnInsideATabMapsToTheTab(int column)
    {
        Assert.Equal(4, TextLineView.SourceOffset(Document("x\nab\tc"), 1, column));
    }

    [Theory]
    [InlineData(-5, 2)]
    [InlineData(9, 6)]
    [InlineData(1000, 6)]
    public void AColumnOutsideTheLineClampsToIt(int column, int offset)
    {
        Assert.Equal(offset, TextLineView.SourceOffset(Document("x\nab\tc"), 1, column));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DesktopGuides.Core.Tests -c Release --filter FullyQualifiedName~TextLineViewTests`
Expected: build FAIL with `CS0103: The name 'TextLineView' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text;

namespace DesktopGuides.Core.Text;

/// <summary>How one TXT guide line is shown: a tab expands to the next
/// 8-column stop and a C0 control character other than tab, or DEL, shows
/// as one space. A column is one UTF-16 code unit. The document's text and
/// offsets are never changed.</summary>
public static class TextLineView
{
    public const int TabWidth = 8;

    public static string DisplayText(TextGuideDocument document, int line)
    {
        (int start, int end) = Bounds(document, line);
        ReadOnlySpan<char> source = document.Text.AsSpan(start, end - start);
        if (source.IndexOfAnyInRange('\0', '\x1F') < 0 && !source.Contains('\x7F'))
        {
            return source.ToString();
        }
        StringBuilder shown = new(DisplayColumns(document, line));
        foreach (char character in source)
        {
            if (character == '\t')
            {
                shown.Append(' ', TabWidth - shown.Length % TabWidth);
            }
            else
            {
                shown.Append(character < ' ' || character == '\x7F' ? ' ' : character);
            }
        }
        return shown.ToString();
    }

    public static int DisplayColumns(TextGuideDocument document, int line)
    {
        (int start, int end) = Bounds(document, line);
        int columns = 0;
        for (int index = start; index < end; index++)
        {
            columns += Width(document.Text[index], columns);
        }
        return columns;
    }

    /// <summary>The offset in <see cref="TextGuideDocument.Text"/> shown at
    /// <paramref name="column"/>, clamped to the line. A column inside an
    /// expanded tab maps to the tab; a column past the end maps to the
    /// line's end.</summary>
    public static int SourceOffset(TextGuideDocument document, int line, int column)
    {
        (int start, int end) = Bounds(document, line);
        int columns = 0;
        for (int index = start; index < end; index++)
        {
            columns += Width(document.Text[index], columns);
            if (column < columns)
            {
                return index;
            }
        }
        return end;
    }

    private static int Width(char character, int column) =>
        character == '\t' ? TabWidth - column % TabWidth : 1;

    private static (int Start, int End) Bounds(TextGuideDocument document, int line)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<int> starts = document.LineStarts;
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, starts.Count);
        int end = line + 1 < starts.Count ? starts[line + 1] - 1 : document.Text.Length;
        return (starts[line], end);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DesktopGuides.Core.Tests -c Release --filter FullyQualifiedName~TextLineViewTests`
Expected: PASS, 22 tests.

- [ ] **Step 5: Run the Core suite and commit**

Run: `dotnet test tests/DesktopGuides.Core.Tests -c Release`
Expected: PASS, 345 tests (323 + 22).

```bash
git add src/DesktopGuides.Core/Text/TextLineView.cs tests/DesktopGuides.Core.Tests/TextLineViewTests.cs
git commit -m "feat(core): add TXT line display rules for T08.2"
```

### Task 2: TextLineList and TextLineMetrics

**Files:**
- Create: `src/DesktopGuides.Core/Text/TextLineList.cs`
- Create: `src/DesktopGuides.Core/Text/TextLineMetrics.cs`
- Test: `tests/DesktopGuides.Core.Tests/TextLineListTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/TextLineMetricsTests.cs`

**Interfaces:**
- Consumes: Task 1 `TextLineView.DisplayText(document, line)` and
  `TextLineView.DisplayColumns(document, line)`.
- Produces:
  - `public sealed record TextLineItem(int Index, string Text);`
  - `public sealed class TextLineList : IReadOnlyList<TextLineItem>, IList`
    with `public TextLineList(TextGuideDocument document)`,
    `public TextGuideDocument Document { get; }`, `Count`, and the
    indexer `TextLineItem this[int index]`.
  - `public static class TextLineMetrics` with
    `public static int MaxColumns(TextGuideDocument document, CancellationToken token)`.

- [ ] **Step 1: Write the failing list tests**

```csharp
using System.Collections;
using System.Text;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextLineListTests
{
    private static TextLineList Lines(string text) =>
        new(TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text)));

    [Theory]
    [InlineData("a", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\nb\n", 2)]
    [InlineData("", 0)]
    [InlineData("\n", 1)]
    [InlineData("a\n\n", 2)]
    public void CountExcludesOnlyTheEmptyLineAfterAFinalNewline(string text, int count)
    {
        TextLineList lines = Lines(text);

        Assert.Equal(count, lines.Count);
        Assert.Equal(count, ((ICollection)lines).Count);
    }

    [Fact]
    public void ItemsCarryTheirIndexAndDisplayText()
    {
        TextLineList lines = Lines("a\tb\r\n\fc\n\n");

        Assert.Equal(
            [new TextLineItem(0, "a       b"), new TextLineItem(1, " c"), new TextLineItem(2, "")],
            lines.ToArray());
    }

    [Fact]
    public void TheIndexerBuildsAFreshItemEachTime()
    {
        TextLineList lines = Lines("one\ntwo");

        TextLineItem first = lines[1];
        TextLineItem second = lines[1];

        Assert.Equal(first, second);
        Assert.NotSame(first, second);
        Assert.Equal(first, ((IList)lines)[1]);
    }

    [Fact]
    public void IndexOfUsesTheItemIndex()
    {
        IList lines = Lines("a\nb\nc");

        Assert.Equal(1, lines.IndexOf(new TextLineItem(1, "anything")));
        Assert.True(lines.Contains(new TextLineItem(2, "c")));
    }

    [Fact]
    public void IndexOfRejectsOtherObjectsAndIndexesOutsideTheList()
    {
        IList lines = Lines("a\nb\n");

        Assert.Equal(-1, lines.IndexOf("b"));
        Assert.Equal(-1, lines.IndexOf(null));
        Assert.Equal(-1, lines.IndexOf(new TextLineItem(2, "")));
        Assert.Equal(-1, lines.IndexOf(new TextLineItem(-1, "a")));
        Assert.False(lines.Contains(new TextLineItem(5, "a")));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void AnIndexOutsideTheListThrows(int index)
    {
        TextLineList lines = Lines("a\nb\n");

        Assert.Throws<ArgumentOutOfRangeException>(() => lines[index]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ((IList)lines)[index]);
    }

    [Fact]
    public void TheListCantBeChanged()
    {
        IList lines = Lines("a\nb");
        TextLineItem item = new(0, "a");

        Assert.True(lines.IsReadOnly);
        Assert.True(lines.IsFixedSize);
        Assert.Throws<NotSupportedException>(() => lines.Add(item));
        Assert.Throws<NotSupportedException>(() => lines.Clear());
        Assert.Throws<NotSupportedException>(() => lines.Insert(0, item));
        Assert.Throws<NotSupportedException>(() => lines.Remove(item));
        Assert.Throws<NotSupportedException>(() => lines.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => lines[0] = item);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void EnumerationYieldsEveryRowInOrder()
    {
        IEnumerable lines = Lines("x\ny\nz\n");

        Assert.Equal(["x", "y", "z"], lines.Cast<TextLineItem>().Select(item => item.Text));
    }

    [Fact]
    public void CopyToFillsTheArrayFromTheIndex()
    {
        ICollection lines = Lines("x\ny");
        object[] target = new object[3];

        lines.CopyTo(target, 1);

        Assert.Null(target[0]);
        Assert.Equal(new TextLineItem(0, "x"), target[1]);
        Assert.Equal(new TextLineItem(1, "y"), target[2]);
    }
}
```

- [ ] **Step 2: Write the failing metrics tests**

```csharp
using System.Text;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextLineMetricsTests
{
    private static TextGuideDocument Document(string text) =>
        TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void MaxColumnsCountsTabExpansion()
    {
        Assert.Equal(17, TextLineMetrics.MaxColumns(Document("ab\n\t\tx\nabc"), CancellationToken.None));
    }

    [Fact]
    public void AnEmptyDocumentHasNoColumns()
    {
        Assert.Equal(0, TextLineMetrics.MaxColumns(Document(""), CancellationToken.None));
    }

    [Fact]
    public void ACancelledTokenThrows()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            TextLineMetrics.MaxColumns(Document("a\nb"), cancel.Token));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/DesktopGuides.Core.Tests -c Release --filter "FullyQualifiedName~TextLineListTests|FullyQualifiedName~TextLineMetricsTests"`
Expected: build FAIL with `CS0246: The type or namespace name 'TextLineList' could not be found` and
`CS0103: The name 'TextLineMetrics' does not exist in the current context`.

- [ ] **Step 4: Write `TextLineList.cs`**

```csharp
using System.Collections;

namespace DesktopGuides.Core.Text;

public sealed record TextLineItem(int Index, string Text);

/// <summary>The rows of a TXT guide for a virtualizing list. Each access
/// builds a new item with <see cref="TextLineView.DisplayText"/>; nothing
/// is cached, so only realized rows hold line strings. The empty line after
/// a final newline is not a row.</summary>
public sealed class TextLineList : IReadOnlyList<TextLineItem>, IList
{
    public TextLineList(TextGuideDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        IReadOnlyList<int> starts = document.LineStarts;
        Count = starts[^1] == document.Text.Length ? starts.Count - 1 : starts.Count;
    }

    public TextGuideDocument Document { get; }
    public int Count { get; }
    public bool IsFixedSize => true;
    public bool IsReadOnly => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public TextLineItem this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return new TextLineItem(index, TextLineView.DisplayText(Document, index));
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw ReadOnly();
    }

    // Items are rebuilt on each access, so a list control finds one by its index.
    public int IndexOf(object? value) =>
        value is TextLineItem item && item.Index >= 0 && item.Index < Count ? item.Index : -1;

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (int line = 0; line < Count; line++)
        {
            array.SetValue(this[line], index + line);
        }
    }

    public IEnumerator<TextLineItem> GetEnumerator()
    {
        for (int line = 0; line < Count; line++)
        {
            yield return this[line];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Add(object? value) => throw ReadOnly();
    public void Clear() => throw ReadOnly();
    public void Insert(int index, object? value) => throw ReadOnly();
    public void Remove(object? value) => throw ReadOnly();
    public void RemoveAt(int index) => throw ReadOnly();

    private static NotSupportedException ReadOnly() => new("A guide's lines can't be changed.");
}
```

- [ ] **Step 5: Write `TextLineMetrics.cs`**

```csharp
namespace DesktopGuides.Core.Text;

public static class TextLineMetrics
{
    private const int LinesPerCheck = 4096;

    /// <summary>The widest line in display columns. Scans every line, so
    /// callers run it off the UI thread.</summary>
    public static int MaxColumns(TextGuideDocument document, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        int widest = 0;
        int lines = document.LineStarts.Count;
        for (int line = 0; line < lines; line++)
        {
            if (line % LinesPerCheck == 0)
            {
                token.ThrowIfCancellationRequested();
            }
            widest = Math.Max(widest, TextLineView.DisplayColumns(document, line));
        }
        return widest;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/DesktopGuides.Core.Tests -c Release --filter "FullyQualifiedName~TextLineListTests|FullyQualifiedName~TextLineMetricsTests"`
Expected: PASS, 18 tests (15 list, 3 metrics).

- [ ] **Step 7: Run the Core suite and commit**

Run: `dotnet test tests/DesktopGuides.Core.Tests -c Release`
Expected: PASS, 363 tests (345 + 18).

```bash
git add src/DesktopGuides.Core/Text/TextLineList.cs src/DesktopGuides.Core/Text/TextLineMetrics.cs \
  tests/DesktopGuides.Core.Tests/TextLineListTests.cs tests/DesktopGuides.Core.Tests/TextLineMetricsTests.cs
git commit -m "feat(core): add lazy TXT line list and width scan for T08.2"
```

### Task 3: Reader TXT view, session and shell wiring

**Files:**
- Create: `src/DesktopGuides.Core/Text/TextGuideLoadMessages.cs`
- Test: `tests/DesktopGuides.Core.Tests/TextGuideLoadMessagesTests.cs`
- Create: `src/DesktopGuides.Production/TextReaderView.xaml`, `TextReaderView.xaml.cs`
- Create: `src/DesktopGuides.Production/TextReaderSession.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml:448-455` (reader surface)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (fields, `InitializeCoreAsync`, `RenderCurrentAsync`, `ReaderRoute` case)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (status rename, `normal` and design-language reader asserts)

**Interfaces:**
- Consumes: Task 2 `TextLineList`, `TextLineItem`, `TextLineMetrics.MaxColumns`;
  T08.1 `ManagedTextGuideLoader(ManagedPathResolver)` and
  `Task<TextGuideLoad> LoadAsync(Guide, CancellationToken)`.
- Produces:
  - `public static class TextGuideLoadMessages` with
    `public static string For(TextGuideLoadError error)`.
  - `public sealed partial class TextReaderView : UserControl` with
    `TextReaderView(TextLineList lines, int maxColumns)`,
    `int FirstVisibleIndex`, `void ScrollByViewport(double fraction)`, `void Clear()`.
  - `internal sealed class TextReaderSession : IReaderSession` with
    `TextReaderSession(TextGuideDocument document, int maxColumns)` and
    `TextReaderView View`.
  - UI Automation: list `ReaderTextLines` (name `Guide text`), each row
    named by its display text or `Blank line`; error text `ReaderLoadError`;
    status `Guide ready.` for every format.

Production has no unit test project and builds only on Windows. TDD here
covers the Core messages; the view, session and shell wiring are pinned by
the installed smoke in this task (existing modes) and Task 5 (`txt-reader`),
which CI runs. Steps 1-4 are TDD; steps 5-10 are verified by the Windows
build and CI.

- [ ] **Step 1: Write the failing messages test**

```csharp
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextGuideLoadMessagesTests
{
    [Theory]
    [InlineData(TextGuideLoadError.Missing, "This guide's file is missing from the library.")]
    [InlineData(TextGuideLoadError.TooLarge, "This guide is larger than the 64 MB limit for text files.")]
    [InlineData(TextGuideLoadError.Unreadable, "This guide's file can't be opened. Close any app that's using it, then open the guide again.")]
    [InlineData(TextGuideLoadError.InvalidMetadata, "This guide's saved details are damaged, so it can't be opened.")]
    [InlineData(TextGuideLoadError.NotUtf8, "This guide isn't valid UTF-8 text, so it can't be opened.")]
    [InlineData(TextGuideLoadError.Undecodable, "This guide can't be read with its saved encoding.")]
    public void EachErrorHasOneSentence(TextGuideLoadError error, string message)
    {
        Assert.Equal(message, TextGuideLoadMessages.For(error));
    }

    [Fact]
    public void EveryErrorIsCovered()
    {
        Assert.All(Enum.GetValues<TextGuideLoadError>(), error =>
            Assert.False(string.IsNullOrWhiteSpace(TextGuideLoadMessages.For(error))));
    }

    [Fact]
    public void AnUnknownErrorThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextGuideLoadMessages.For((TextGuideLoadError)99));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/DesktopGuides.Core.Tests -c Release --filter FullyQualifiedName~TextGuideLoadMessagesTests`
Expected: build FAIL with `CS0103: The name 'TextGuideLoadMessages' does not exist in the current context`.

- [ ] **Step 3: Write `TextGuideLoadMessages.cs`**

```csharp
namespace DesktopGuides.Core.Text;

public static class TextGuideLoadMessages
{
    public static string For(TextGuideLoadError error) => error switch
    {
        TextGuideLoadError.Missing => "This guide's file is missing from the library.",
        TextGuideLoadError.TooLarge => "This guide is larger than the 64 MB limit for text files.",
        TextGuideLoadError.Unreadable =>
            "This guide's file can't be opened. Close any app that's using it, then open the guide again.",
        TextGuideLoadError.InvalidMetadata => "This guide's saved details are damaged, so it can't be opened.",
        TextGuideLoadError.NotUtf8 => "This guide isn't valid UTF-8 text, so it can't be opened.",
        TextGuideLoadError.Undecodable => "This guide can't be read with its saved encoding.",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass, then commit**

Run: `dotnet test tests/DesktopGuides.Core.Tests -c Release`
Expected: PASS, 371 tests (363 + 8).

```bash
git add src/DesktopGuides.Core/Text/TextGuideLoadMessages.cs tests/DesktopGuides.Core.Tests/TextGuideLoadMessagesTests.cs
git commit -m "feat(core): add TXT guide load failure messages for T08.2"
```

- [ ] **Step 5: Write `TextReaderView.xaml`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl x:Class="DesktopGuides.Production.TextReaderView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <!-- Measures one Consolas cell; never shown or read. -->
        <TextBlock x:Name="CellProbe"
                   FontFamily="Consolas"
                   Text="MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM"
                   Opacity="0"
                   IsHitTestVisible="False"
                   HorizontalAlignment="Left"
                   VerticalAlignment="Top"
                   AutomationProperties.AccessibilityView="Raw" />
        <ListView x:Name="Lines"
                  AutomationProperties.AutomationId="ReaderTextLines"
                  AutomationProperties.Name="Guide text"
                  SelectionMode="None"
                  IsItemClickEnabled="False"
                  ScrollViewer.HorizontalScrollMode="Enabled"
                  ScrollViewer.HorizontalScrollBarVisibility="Auto"
                  ScrollViewer.VerticalScrollMode="Enabled"
                  ScrollViewer.VerticalScrollBarVisibility="Auto"
                  ContainerContentChanging="LineContainerChanging">
            <ListView.Resources>
                <SolidColorBrush x:Key="ListViewItemBackgroundPointerOver" Color="Transparent" />
                <SolidColorBrush x:Key="ListViewItemBackgroundPressed" Color="Transparent" />
            </ListView.Resources>
            <ListView.ItemsPanel>
                <ItemsPanelTemplate>
                    <ItemsStackPanel />
                </ItemsPanelTemplate>
            </ListView.ItemsPanel>
            <ListView.ItemContainerStyle>
                <Style TargetType="ListViewItem">
                    <Setter Property="Padding" Value="12,0" />
                    <Setter Property="MinHeight" Value="0" />
                    <Setter Property="HorizontalContentAlignment" Value="Left" />
                </Style>
            </ListView.ItemContainerStyle>
            <ListView.ItemTemplate>
                <DataTemplate>
                    <TextBlock FontFamily="Consolas"
                               TextWrapping="NoWrap"
                               IsTextSelectionEnabled="False"
                               Foreground="{ThemeResource DesktopGuidesPrimaryTextBrush}" />
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</UserControl>
```

- [ ] **Step 6: Write `TextReaderView.xaml.cs`**

```csharp
using DesktopGuides.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DesktopGuides.Production;

// One row per guide line, realized only while visible. Rows take the widest
// line's width and one measured Consolas line's height, so the scroll ranges
// are right from the first frame and don't change as wide lines appear.
public sealed partial class TextReaderView : UserControl
{
    private const int ProbeColumns = 64;
    private readonly TextLineList lines;
    private readonly int maxColumns;
    private double rowWidth;
    private double rowHeight;

    public TextReaderView(TextLineList lines, int maxColumns)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(maxColumns);
        this.lines = lines;
        this.maxColumns = maxColumns;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public int FirstVisibleIndex =>
        Lines.ItemsPanelRoot is ItemsStackPanel panel ? Math.Max(panel.FirstVisibleIndex, 0) : 0;

    public void ScrollByViewport(double fraction)
    {
        if (!double.IsFinite(fraction))
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "A scroll must be a finite fraction.");
        }
        if (FindScrollViewer(Lines) is ScrollViewer viewer)
        {
            viewer.ChangeView(null, viewer.VerticalOffset + fraction * viewer.ViewportHeight, null, true);
        }
    }

    public void Clear()
    {
        Loaded -= OnLoaded;
        Lines.ItemsSource = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        CellProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        rowWidth = Math.Ceiling(CellProbe.DesiredSize.Width / ProbeColumns * maxColumns);
        rowHeight = Math.Ceiling(CellProbe.DesiredSize.Height);
        Lines.ItemsSource = lines;
    }

    private void LineContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not TextLineItem line)
        {
            return;
        }
        if (args.ItemContainer.ContentTemplateRoot is TextBlock text)
        {
            text.Text = line.Text;
            // A minimum, so a fallback glyph wider than a Consolas cell isn't clipped.
            text.MinWidth = rowWidth;
            text.Height = rowHeight;
        }
        AutomationProperties.SetName(
            args.ItemContainer, string.IsNullOrWhiteSpace(line.Text) ? "Blank line" : line.Text);
        args.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, index)) is ScrollViewer found)
            {
                return found;
            }
        }
        return null;
    }
}
```

- [ ] **Step 7: Write `TextReaderSession.cs`**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;

namespace DesktopGuides.Production;

// The Reader's session for one loaded TXT guide. The shell builds it from a
// document T08.1's loader returned; paging and restore arrive with T08.3.
internal sealed class TextReaderSession : IReaderSession
{
    private readonly TextGuideDocument document;
    private bool disposed;

    public TextReaderSession(TextGuideDocument document, int maxColumns)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.document = document;
        View = new TextReaderView(new TextLineList(document), maxColumns);
    }

    public TextReaderView View { get; }
    public GuideFormat Format => GuideFormat.Txt;
    public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll;

    // Neither changes during a T08.2 session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    public event EventHandler<LocationChangedEventArgs>? LocationChanged { add { } remove { } }

    public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
        throw new NotSupportedException("A TXT session is built from a loaded document.");

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IReadOnlyList<int> starts = document.LineStarts;
        int line = Math.Clamp(View.FirstVisibleIndex, 0, starts.Count - 1);
        TextLocation captured = document.Capture(starts[line]);
        return Task.FromResult(new ReaderLocation(
            GuideFormat.Txt, captured.SchemaVersion, captured.ContentSha256,
            new TextPosition(captured.CharacterOffset, captured.ContextQuote), captured.Fraction));
    }

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token) =>
        Task.FromResult(new RestoreOutcome(
            RestoreKind.Unavailable, "Restoring a reading position isn't available yet."));

    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        token.ThrowIfCancellationRequested();
        if (action is not ScrollAction scroll)
        {
            throw new NotSupportedException($"TXT guides don't support {action.Command}.");
        }
        View.ScrollByViewport(scroll.VerticalViewportFraction);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            View.Clear();
        }
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 8: Replace the reader surface in `ShellWindow.xaml`**

Replace the `ContentControl x:Name="ReaderSurface"` element (with the
`ReaderPlaceholder` inside it) with:

```xml
                    <Grid>
                        <TextBlock x:Name="ReaderPlaceholder"
                                   Text="Reading this guide is unavailable in this preview."
                                   Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                                   AutomationProperties.AutomationId="ReaderPlaceholder" />
                        <TextBlock x:Name="ReaderLoadError"
                                   Visibility="Collapsed"
                                   TextWrapping="Wrap"
                                   Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                                   AutomationProperties.AutomationId="ReaderLoadError" />
                        <ContentControl x:Name="ReaderSurface"
                                        HorizontalAlignment="Stretch"
                                        VerticalAlignment="Stretch"
                                        HorizontalContentAlignment="Stretch"
                                        VerticalContentAlignment="Stretch" />
                    </Grid>
```

- [ ] **Step 9: Wire the load path in `ShellWindow.xaml.cs`**

Add `using DesktopGuides.Core.Reading;`, `using DesktopGuides.Core.Text;` and
`using DesktopGuides.Infrastructure.Reading;` to the usings. Add fields
after `private GameRemover? gameRemover;`:

```csharp
    private ManagedTextGuideLoader? textLoader;
    private CancellationTokenSource? readerLoad;
    private IReaderSession? readerSession;
```

In `InitializeCoreAsync`, after `gameRemover = new GameRemover(repository, paths, artwork);`:

```csharp
            textLoader = new ManagedTextGuideLoader(paths);
```

Add these methods next to `PauseReaderMetadataReadForTestAsync`:

```csharp
    private void ShowReaderSurface(bool placeholder, string? error = null, UIElement? view = null)
    {
        ReaderPlaceholder.Visibility = placeholder ? Visibility.Visible : Visibility.Collapsed;
        ReaderLoadError.Text = error ?? string.Empty;
        ReaderLoadError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        ReaderSurface.Content = view;
    }

    // Every render closes the Reader: a load in flight is cancelled, and the
    // toolbar and surface drop the old session before it is disposed.
    private async Task CloseReaderSessionAsync()
    {
        readerLoad?.Cancel();
        readerLoad?.Dispose();
        readerLoad = null;
        ReaderActions.SetSession(null);
        ShowReaderSurface(placeholder: true);
        IReaderSession? closing = readerSession;
        readerSession = null;
        if (closing is not null)
        {
            await closing.DisposeAsync();
        }
    }
```

At the top of `RenderCurrentAsync`, replace `int generation = ++renderGeneration;` with:

```csharp
        int generation = ++renderGeneration;
        await CloseReaderSessionAsync();
        if (generation != renderGeneration)
        {
            return false;
        }
```

In the `ReaderRoute` case, replace `ShowTransientStatus("Guide details ready.");`
(the line before `break;`) with:

```csharp
                    if (guide.Format != GuideFormat.Txt)
                    {
                        ShowTransientStatus("Guide ready.");
                        break;
                    }
                    ShowReaderSurface(placeholder: false);
                    readerLoad = new CancellationTokenSource();
                    CancellationToken readerToken = readerLoad.Token;
                    TextGuideLoad textLoad;
                    try
                    {
                        textLoad = await textLoader!.LoadAsync(guide, readerToken);
                    }
                    catch (OperationCanceledException) when (readerToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    if (textLoad is TextGuideLoadFailed failed)
                    {
                        string message = TextGuideLoadMessages.For(failed.Error);
                        ShowReaderSurface(placeholder: false, error: message);
                        ShowWarningStatus(message);
                        break;
                    }
                    // ContentChanged is T12.3's; T08.2 shows the file as it is.
                    TextGuideDocument document = ((TextGuideLoaded)textLoad).Document;
                    int maxColumns;
                    try
                    {
                        maxColumns = await Task.Run(
                            () => TextLineMetrics.MaxColumns(document, readerToken), readerToken);
                    }
                    catch (OperationCanceledException) when (readerToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    if (generation != renderGeneration)
                    {
                        return false;
                    }
                    TextReaderSession session = new(document, maxColumns);
                    readerSession = session;
                    ShowReaderSurface(placeholder: false, view: session.View);
                    ReaderActions.SetSession(session);
                    ShowTransientStatus("Guide ready.");
                    break;
```

`readerToken` is captured before any await because `CloseReaderSessionAsync`
disposes the source; a token from a disposed source still reports
cancellation.

- [ ] **Step 10: Update the existing smoke asserts**

In `tools/p1/windows_shell_ui_smoke.ps1`:

1. Replace every `'Guide details ready.'` with `'Guide ready.'`, including
   the `Wait-Status` transient list:
   `sed -i '' "s/'Guide details ready\.'/'Guide ready.'/g" tools/p1/windows_shell_ui_smoke.ps1`
2. Design-language reader-narrow: replace `Assert-InsideWindow 'ReaderPlaceholder'`
   with `Assert-InsideWindow 'ReaderTextLines'`.
3. `normal` mode: replace

```powershell
        [void](Wait-Name 'ReaderPlaceholder' `
            'Reading this guide is unavailable in this preview.')
        $commands = Find-ById 'ReaderCommands'
        if ($commands -and -not $commands.Current.IsOffscreen) {
            throw 'The preview reader exposed commands without an adapter.'
        }
```

with

```powershell
        [void](Wait-VisibleById 'ReaderTextLines')
        [void](Wait-Name 'ReaderTextLines' 'Guide text')
        $firstLine = (Find-ById 'ReaderTextLines').FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)))
        if (-not $firstLine -or $firstLine.Current.Name -ne 'Test guide.') {
            throw 'The TXT reader did not show the seeded guide text.'
        }
        Assert-Absent 'ReaderPlaceholder'
        $commands = Find-ById 'ReaderCommands'
        if ($commands -and -not $commands.Current.IsOffscreen) {
            throw 'The TXT reader exposed commands that T08.2 does not provide.'
        }
```

Check the remaining references; each must be intentional:

Run: `grep -n "Guide details ready\|ReaderPlaceholder" tools/p1/*.ps1 src/DesktopGuides.Production/*.cs src/DesktopGuides.Production/*.xaml`
Expected: no `Guide details ready`; `ReaderPlaceholder` only in `ShellWindow.xaml`, `ShellWindow.xaml.cs`
(`ShowReaderSurface`) and the `normal`-mode `Assert-Absent`.

- [ ] **Step 11: Build on Windows and commit**

Run: `sed 's/t08-1/t08-2/g' /tmp/t081.sh > /tmp/t082.sh && chmod +x /tmp/t082.sh` (once), then
`/tmp/t082.sh "dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64"`
Expected: `Build succeeded.` with `0 Error(s)` and no new warnings in the four changed Production files.

Run: `dotnet test tests/DesktopGuides.Core.Tests -c Release && dotnet test tests/DesktopGuides.Infrastructure.Tests -c Release`
Expected: PASS, 371 and 438.

```bash
git add src/DesktopGuides.Production/TextReaderView.xaml src/DesktopGuides.Production/TextReaderView.xaml.cs \
  src/DesktopGuides.Production/TextReaderSession.cs src/DesktopGuides.Production/ShellWindow.xaml \
  src/DesktopGuides.Production/ShellWindow.xaml.cs tools/p1/windows_shell_ui_smoke.ps1
git commit -m "feat(reader): show TXT guides in a virtualized fixed-width view for T08.2"
```

### Task 4: Tab fixture and `seed-txt-reader`

**Files:**
- Create: `tests/fixtures/p1/txt-tabs.txt`
- Modify: `.gitattributes`
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (new 3-argument mode before the 2-argument usage check; `InsertGuideAsync` split)

**Interfaces:**
- Consumes: the existing ShellSeed `InsertGuideAsync(paths, gameId, guideId, title, now, format, primaryPath, params string[] assetPaths)`
  and `repository.AddGameAsync(title, null, null)`.
- Produces: `DesktopGuides.ShellSeed seed-txt-reader <app-data-root> <fixtures-root>`,
  where `<fixtures-root>` is `tests/fixtures`. It needs an empty library and
  creates game `Text Reader Game` with these guides:

  | Title | Source | Code page | Note |
  | --- | --- | --- | --- |
  | `ASCII Map Guide` | `p0/txt-ascii.txt` | null | |
  | `Tab Table Guide` | `p1/txt-tabs.txt` | null | |
  | `Long Text Guide` | `p0/generated/txt-long.txt` | null | 10 MiB |
  | `Legacy Code Page Guide` | `p0/txt-legacy.txt` | 437 | |
  | `Missing File Guide` | `p0/txt-ascii.txt` | null | managed file deleted after insert |
  | `Web Page Guide` | `Test guide.` | n/a | `Html`, `guide.html` |

This is test tooling with no unit tests of its own (TDD skipped as
mechanical seed code); Step 4 checks the seeded rows and files directly, and
Task 5's smoke consumes them.

- [ ] **Step 1: Add the fixture and keep it byte-exact**

```bash
mkdir -p tests/fixtures/p1
printf 'Item\tCost\tWhere\nPotion\t50\tItem shop\nElixir\t1500\tSecret room\n\fChapter 2\n' > tests/fixtures/p1/txt-tabs.txt
printf 'tests/fixtures/p1/** -text\ntests/fixtures/p1/*.txt -diff\n' >> .gitattributes
od -c tests/fixtures/p1/txt-tabs.txt | head -6
```

Expected: `od` shows `\t` separators, LF line ends, `\f` before `Chapter 2`,
and 71 bytes in all (`wc -c < tests/fixtures/p1/txt-tabs.txt` prints `71`).

Its rows display as `Item    Cost    Where`, `Potion  50      Item shop`,
`Elixir  1500    Secret room` and ` Chapter 2` (4 rows).

- [ ] **Step 2: Split `InsertGuideAsync` so content can come from a file**

Replace the body of `InsertGuideAsync` from `byte[] bytes = await File.ReadAllBytesAsync(content);`
to the end of the method with a call to a new row writer, and add
`InsertTextGuideAsync`:

```csharp
    byte[] bytes = await File.ReadAllBytesAsync(content);
    InsertGuideRow(paths, gameId, guideId, title, now, format, primaryPath, bytes, codePage: null);
}

static async Task InsertTextGuideAsync(
    ManagedPathResolver paths, Guid gameId, Guid guideId, string title, long now,
    byte[] content, int? codePage = null)
{
    string guideRoot = paths.GetGuideRoot(guideId);
    Directory.CreateDirectory(guideRoot);
    await File.WriteAllBytesAsync(Path.Combine(guideRoot, "guide.txt"), content);
    InsertGuideRow(paths, gameId, guideId, title, now, "Txt", "guide.txt", content, codePage);
}

static void InsertGuideRow(
    ManagedPathResolver paths, Guid gameId, Guid guideId, string title, long now,
    string format, string primaryPath, byte[] bytes, int? codePage)
{
    string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
    using SqliteConnection connection = new(new SqliteConnectionStringBuilder
    {
        DataSource = paths.DatabasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false,
        ForeignKeys = true
    }.ToString());
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO Guides (
            Id, GameId, Title, Format, ManagedRelativeRoot, PrimaryRelativePath,
            ContentSha256, ContentBytes, TextCodePage, ImportedUtcMs, UpdatedUtcMs
        ) VALUES (
            $guide, $game, $title, $format, $root, $primary,
            $hash, $bytes, $codePage, $now, $now
        );
        INSERT INTO ReadingStates (GuideId) VALUES ($guide);
        INSERT INTO ReaderPreferences (GuideId) VALUES ($guide);
        """;
    command.Parameters.AddWithValue("$guide", guideId.ToString("N"));
    command.Parameters.AddWithValue("$game", gameId.ToString("N"));
    command.Parameters.AddWithValue("$title", title);
    command.Parameters.AddWithValue("$root", $"content/{guideId:N}");
    command.Parameters.AddWithValue("$format", format);
    command.Parameters.AddWithValue("$primary", primaryPath);
    command.Parameters.AddWithValue("$hash", hash);
    command.Parameters.AddWithValue("$bytes", bytes.LongLength);
    command.Parameters.AddWithValue("$codePage", codePage is int page ? page : DBNull.Value);
    command.Parameters.AddWithValue("$now", now);
    command.ExecuteNonQuery();
}
```

The existing statements before that line (`guideRoot`, the `Test guide.`
write and the asset loop) stay as they are.

- [ ] **Step 3: Add the `seed-txt-reader` mode**

Insert before `if (args.Length != 2 ||`:

```csharp
if (args.Length == 3 && args[0] == "seed-txt-reader")
{
    ManagedPathResolver textPaths = new(args[1]);
    await using SqliteLibraryRepository textRepository = new(textPaths);
    await textRepository.InitializeAsync();
    if ((await textRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The TXT reader seed needs an empty library.");
    }
    string fixtures = Path.GetFullPath(args[2]);
    byte[] Fixture(string relative) =>
        File.ReadAllBytes(Path.Combine(fixtures, relative.Replace('/', Path.DirectorySeparatorChar)));
    long textNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    Game textGame = await textRepository.AddGameAsync("Text Reader Game", null, null);
    await InsertTextGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "ASCII Map Guide", textNow,
        Fixture("p0/txt-ascii.txt"));
    await InsertTextGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "Tab Table Guide", textNow,
        Fixture("p1/txt-tabs.txt"));
    await InsertTextGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "Long Text Guide", textNow,
        Fixture("p0/generated/txt-long.txt"));
    await InsertTextGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "Legacy Code Page Guide", textNow,
        Fixture("p0/txt-legacy.txt"), codePage: 437);
    Guid missingGuideId = Guid.NewGuid();
    await InsertTextGuideAsync(textPaths, textGame.Id, missingGuideId, "Missing File Guide", textNow,
        Fixture("p0/txt-ascii.txt"));
    File.Delete(Path.Combine(textPaths.GetGuideRoot(missingGuideId), "guide.txt"));
    await InsertGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "Web Page Guide", textNow,
        "Html", "guide.html");
    Console.WriteLine("Seeded the TXT reader game with six guides.");
    return 0;
}
```

Extend the usage message with `"or seed-txt-reader <app-data-root> <fixtures-root> " +`
after the `seed-linked-game|…` line.

- [ ] **Step 4: Run the seed on the Mac and check its rows and files**

```bash
python3 tools/p0/make_fixtures.py && python3 tools/p0/verify_fixtures.py
rm -rf /tmp/t082-seed && mkdir /tmp/t082-seed
dotnet run --project tools/p1/DesktopGuides.ShellSeed -c Release -- seed-txt-reader /tmp/t082-seed tests/fixtures
db=$(find /tmp/t082-seed -name library.sqlite)
sqlite3 "$db" "SELECT Title, Format, PrimaryRelativePath, ContentBytes, IFNULL(TextCodePage, '-') FROM Guides ORDER BY Title;"
find /tmp/t082-seed -name 'guide.*' -type f | wc -l
dotnet run --project tools/p1/DesktopGuides.ShellSeed -c Release -- seed-txt-reader /tmp/t082-seed tests/fixtures; echo "exit $?"
```

Expected:
- `Seeded the TXT reader game with six guides.`
- Six rows: `ASCII Map Guide|Txt|guide.txt|2123|-`,
  `Legacy Code Page Guide|Txt|guide.txt|20|437`,
  `Long Text Guide|Txt|guide.txt|<txt-long bytes>|-`,
  `Missing File Guide|Txt|guide.txt|2123|-`,
  `Tab Table Guide|Txt|guide.txt|71|-`, `Web Page Guide|Html|guide.html|11|-`.
  `<txt-long bytes>` equals `wc -c < tests/fixtures/p0/generated/txt-long.txt`.
- `5` managed files (the missing guide has none).
- The second run fails with `The TXT reader seed needs an empty library.` and a non-zero exit.

Then run the earlier seeds that use `InsertGuideAsync` to show the split
kept them working:

```bash
rm -rf /tmp/t082-seed && mkdir /tmp/t082-seed
dotnet run --project tools/p1/DesktopGuides.ShellSeed -c Release -- seed-navigation /tmp/t082-seed
sqlite3 "$(find /tmp/t082-seed -name library.sqlite)" "SELECT COUNT(*), SUM(TextCodePage IS NULL) FROM Guides;"
rm -rf /tmp/t082-seed
```

Expected: `Seeded three navigation games.` and `4|4`.

- [ ] **Step 5: Commit**

```bash
git add tests/fixtures/p1/txt-tabs.txt .gitattributes tools/p1/DesktopGuides.ShellSeed/Program.cs
git commit -m "test(p1): seed TXT reader guides from fixtures for T08.2"
```

### Task 5: `txt-reader` smoke, installer scenarios and CI fixtures

**Files:**
- Create: `tools/p1/windows_shell_response_monitor.cs`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (ValidateSet, `Add-Type`, new mode block)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-TxtReaderScenarios` and its call)
- Modify: `.github/workflows/windows-ci.yml` (`production-shell-ui` job)

**Interfaces:**
- Consumes: Task 3 AutomationIds `ReaderTextLines` (name `Guide text`),
  `ReaderLoadError`, `ReaderPlaceholder`, status `Guide ready.`, Missing
  sentence; Task 4 `seed-txt-reader <app-data-root> <fixtures-root>` and its
  six guide titles under `Text Reader Game`.
- Produces: smoke mode `txt-reader`; report keys `txtReaderLight` and
  `txtReaderDark` in the install report, each with `txtLong` measures.

The smoke is the test for Tasks 3-5. It can't run on the Mac; CI
`production-shell-ui` is the gate (Step 6). Use `pcsx2-win` only to debug
a CI failure, following `docs/p1/e2e-testing.md`.

- [ ] **Step 1: Write `windows_shell_response_monitor.cs`**

Copied from P0's `WindowResponseMonitor` (`tools/p0/windows_ui_smoke.ps1`),
with a count of samples above 500 ms:

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// Samples window-message dispatch while UI Automation scrolls the TXT view.
public sealed class WindowResponseMonitor {
    private const uint WmNull = 0;
    private const uint SmtoAbortIfHung = 2;
    private const long SlowMilliseconds = 500;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr handle, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeoutMilliseconds, out IntPtr result);
    private readonly IntPtr handle;
    private Thread worker;
    private volatile bool running;
    private long maximumMilliseconds;
    private long samples;
    private long slowSamples;
    private long timeouts;
    public WindowResponseMonitor(IntPtr handle) { this.handle = handle; }
    public long MaximumMilliseconds { get { return Interlocked.Read(ref maximumMilliseconds); } }
    public long Samples { get { return Interlocked.Read(ref samples); } }
    public long SlowSamples { get { return Interlocked.Read(ref slowSamples); } }
    public long Timeouts { get { return Interlocked.Read(ref timeouts); } }
    public void Start() {
        running = true;
        worker = new Thread(Run);
        worker.IsBackground = true;
        worker.Start();
    }
    public void Stop() {
        running = false;
        if (worker != null && !worker.Join(3000)) {
            throw new TimeoutException("UI response monitor did not stop.");
        }
    }
    private void Run() {
        while (running) {
            Stopwatch timer = Stopwatch.StartNew();
            IntPtr result;
            IntPtr status = SendMessageTimeout(
                handle, WmNull, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, 1000, out result);
            timer.Stop();
            Interlocked.Increment(ref samples);
            if (status == IntPtr.Zero) Interlocked.Increment(ref timeouts);
            long duration = timer.ElapsedMilliseconds;
            if (duration > SlowMilliseconds) Interlocked.Increment(ref slowSamples);
            long previous = Interlocked.Read(ref maximumMilliseconds);
            while (duration > previous) {
                long actual = Interlocked.CompareExchange(
                    ref maximumMilliseconds, duration, previous);
                if (actual == previous) break;
                previous = actual;
            }
            Thread.Sleep(25);
        }
    }
}
```

- [ ] **Step 2: Register the mode and the monitor in the smoke**

In the `[ValidateSet(…)]` for `$Mode`, add `'txt-reader'` after
`'game-actions-persisted'`. After
`Add-Type -Path (Join-Path $PSScriptRoot 'windows_shell_foreground_probe.cs')` add:

```powershell
    Add-Type -Path (Join-Path $PSScriptRoot 'windows_shell_response_monitor.cs')
```

- [ ] **Step 3: Add the `txt-reader` mode block**

Add before the final `else` of the mode chain (after the
`game-actions-persisted` block):

```powershell
    elseif ($Mode -eq 'txt-reader') {
        $textGame = 'Text Reader Game'
        $missingMessage = "This guide's file is missing from the library."
        $listItem = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        $fixtureRoot = Join-Path $PSScriptRoot '..\..\tests\fixtures'
        # The reader names a blank or whitespace-only row "Blank line".
        $asciiNames = @(
            [System.IO.File]::ReadAllText((Join-Path $fixtureRoot 'p0\txt-ascii.txt')) -split "`n" |
                ForEach-Object { if ([string]::IsNullOrWhiteSpace($_)) { 'Blank line' } else { $_ } })
        if ($asciiNames.Count -ne 8) {
            throw "Expected 8 txt-ascii lines; read $($asciiNames.Count)."
        }

        function Get-TextRows {
            $lines = Find-ById 'ReaderTextLines'
            if (-not $lines -or $lines.Current.IsOffscreen) { return @() }
            return @($lines.FindAll($scope, $listItem))
        }

        function Wait-FirstTextRow {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $lines = Find-ById 'ReaderTextLines'
                if ($lines -and -not $lines.Current.IsOffscreen -and
                    $lines.FindFirst($scope, $listItem)) {
                    return
                }
                Start-Sleep -Milliseconds 50
            } while ((Get-Date) -lt $deadline)
            throw 'The TXT reader showed no lines.'
        }

        function Assert-RowNames([string] $guide, [string[]] $expected) {
            Wait-FirstTextRow
            $names = @(Get-TextRows | ForEach-Object { $_.Current.Name })
            if ($names.Count -ne $expected.Count) {
                throw "$guide showed $($names.Count) rows; expected $($expected.Count)."
            }
            for ($index = 0; $index -lt $expected.Count; $index++) {
                if ($names[$index] -ne $expected[$index]) {
                    throw "$guide row $index was '$($names[$index])'; expected '$($expected[$index])'."
                }
            }
        }

        function Assert-NoReaderCommands([string] $guide) {
            $commands = Find-ById 'ReaderCommands'
            if ($commands -and -not $commands.Current.IsOffscreen) {
                throw "$guide exposed reader commands."
            }
        }

        function Open-TextGuide([string] $guide) {
            Open-GuideFromGame $guide
            [void](Wait-Name 'ReaderHeading' $guide)
        }

        function Back-ToTextGame {
            Go-Back
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')
        }

        [void](Wait-Name 'LibraryHeading' 'Library')
        Select-Element $textGame
        [void](Wait-Name 'GameHeading' $textGame)
        [void](Wait-Status 'Game ready.')

        # txt-ascii: exact lines, whitespace kept, a 2048-column line scrolls sideways.
        Open-TextGuide 'ASCII Map Guide'
        [void](Wait-Status 'Guide ready.')
        Assert-RowNames 'ASCII Map Guide' $asciiNames
        [void](Wait-Name 'ReaderTextLines' 'Guide text')
        $scroll = (Find-ById 'ReaderTextLines').GetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern)
        if (-not $scroll.Current.HorizontallyScrollable) {
            throw 'The long txt-ascii line did not make the reader scroll sideways.'
        }
        Assert-Absent 'ReaderPlaceholder'
        Assert-Absent 'ReaderLoadError'
        Assert-NoReaderCommands 'ASCII Map Guide'
        $report.txtReaderScreenshot = Save-WindowScreenshot 'txt-reader'
        $report.phases += 'txt-ascii'

        # txt-tabs: 8-column tab stops; a form feed shows as a space.
        Back-ToTextGame
        Open-TextGuide 'Tab Table Guide'
        Assert-RowNames 'Tab Table Guide' @(
            'Item    Cost    Where',
            'Potion  50      Item shop',
            'Elixir  1500    Secret room',
            ' Chapter 2')
        $report.phases += 'txt-tabs'

        # A guide saved with code page 437 shows its decoded characters.
        Back-ToTextGame
        Open-TextGuide 'Legacy Code Page Guide'
        Assert-RowNames 'Legacy Code Page Guide' @("Guide $([char]0x00E9)", 'Item list')
        $report.phases += 'txt-legacy'

        # txt-long: the P0 measures, gated at the P0 thresholds.
        Back-ToTextGame
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        Open-GuideFromGame 'Long Text Guide'
        Wait-FirstTextRow
        $clock.Stop()
        [void](Wait-Status 'Guide ready.')
        $realizedAfterOpen = @(Get-TextRows).Count
        $scroll = (Find-ById 'ReaderTextLines').GetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern)
        $monitor = [WindowResponseMonitor]::new($process.MainWindowHandle)
        $monitor.Start()
        try {
            for ($step = 0; $step -lt 8; $step++) {
                $scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount,
                    [System.Windows.Automation.ScrollAmount]::LargeIncrement)
            }
            Start-Sleep -Milliseconds 500
        }
        finally {
            $monitor.Stop()
        }
        $realizedAfterScroll = @(Get-TextRows).Count
        $report.txtLong = [ordered]@{
            firstTextMilliseconds = $clock.ElapsedMilliseconds
            realizedAfterOpen = $realizedAfterOpen
            realizedAfterScroll = $realizedAfterScroll
            verticalScrollPercent = $scroll.Current.VerticalScrollPercent
            responseSamples = $monitor.Samples
            responseMaximumMilliseconds = $monitor.MaximumMilliseconds
            responseSlowSamples = $monitor.SlowSamples
            responseTimeouts = $monitor.Timeouts
        }
        if ($clock.ElapsedMilliseconds -gt 3000) {
            throw "txt-long first text took $($clock.ElapsedMilliseconds) ms; the limit is 3000 ms."
        }
        if ($realizedAfterOpen -gt 300 -or $realizedAfterScroll -gt 300) {
            throw "txt-long realized $realizedAfterOpen then $realizedAfterScroll rows; the limit is 300."
        }
        if ($scroll.Current.VerticalScrollPercent -le 0) {
            throw 'txt-long did not scroll.'
        }
        if ($monitor.Samples -lt 5) {
            throw "The response monitor took only $($monitor.Samples) samples."
        }
        if ($monitor.SlowSamples -ge 2 -or $monitor.Timeouts -gt 0) {
            throw "txt-long had $($monitor.SlowSamples) responses over 500 ms and $($monitor.Timeouts) timeouts."
        }
        $report.phases += 'txt-long'

        # Back while txt-long loads leaves a clean Game page and no late status.
        Back-ToTextGame
        Open-TextGuide 'Long Text Guide'
        $report.txtBackDuringLoadTextShown = [bool](Find-ById 'ReaderTextLines')
        Back-ToTextGame
        Start-Sleep -Seconds 3
        Assert-Absent 'ReaderTextLines'
        Assert-NoReaderCommands 'Game page after Back'
        $status = (Find-RawById 'ShellContent').Current.ItemStatus
        if ($status -eq 'Guide ready.') {
            throw 'A cancelled TXT load reported Guide ready. after Back.'
        }
        $report.phases += 'txt-back-during-load'

        # A missing managed file shows one sentence and no text or commands.
        Open-TextGuide 'Missing File Guide'
        [void](Wait-Status $missingMessage)
        [void](Wait-Name 'ReaderLoadError' $missingMessage)
        Assert-Absent 'ReaderTextLines'
        Assert-Absent 'ReaderPlaceholder'
        Assert-NoReaderCommands 'Missing File Guide'
        $report.phases += 'txt-missing'

        # A non-TXT guide after TXT guides shows the placeholder again.
        Back-ToTextGame
        Open-TextGuide 'Web Page Guide'
        [void](Wait-Status 'Guide ready.')
        [void](Wait-Name 'ReaderPlaceholder' 'Reading this guide is unavailable in this preview.')
        Assert-Absent 'ReaderTextLines'
        Assert-Absent 'ReaderLoadError'
        $report.phases += 'html-placeholder'

        # Reopening reads the file again.
        Back-ToTextGame
        Open-TextGuide 'ASCII Map Guide'
        Assert-RowNames 'ASCII Map Guide (reopened)' $asciiNames
        $report.phases += 'txt-reopen'
    }
```

Check that the script stays ASCII: the code page row uses `[char]0x00E9`.

Run: `LC_ALL=C grep -n '[^ -~]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1; echo "non-ASCII lines: $?"`
Expected: no lines printed and `non-ASCII lines: 1`.

- [ ] **Step 4: Add `Run-TxtReaderScenarios` to the installer**

After `Run-StableNavigationScenarios` in `tools/p1/windows_shell_install.ps1`:

```powershell
function Run-TxtReaderScenarios {
    # The smoke only reads, so one seed serves both themes.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\txt-long.txt'))) {
        throw 'txt-long.txt is missing. Run tools/p0/make_fixtures.py first.'
    }
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Invoke-ShellSeed @('seed-txt-reader', $dataRoot, $fixtureRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.txtReaderLight = Run-ShellSmoke 'txt-reader' -ResultName 'txt-reader-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.txtReaderDark = Run-ShellSmoke 'txt-reader' -ResultName 'txt-reader-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}
```

In the full scenario list (the second `Run-StableNavigationScenarios` call,
not the `-CatalogOnly` branch), call it right after
`Run-StableNavigationScenarios`:

```powershell
    Run-StableNavigationScenarios
    Run-TxtReaderScenarios
```

(`Run-ImportScenarios` follows after its existing data-root wipe.)

- [ ] **Step 5: Generate fixtures in `production-shell-ui`**

In `.github/workflows/windows-ci.yml`, job `production-shell-ui`, insert
before the "Sign, install, and test production shell" step:

```yaml
      - uses: actions/setup-python@a26af69be951a213d495a4c3e4e4022e16d87065 # v5.6.0
        with:
          python-version: '3.11'

      - name: Generate fixtures
        shell: pwsh
        run: |
          python tools/p0/make_fixtures.py
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          python tools/p0/verify_fixtures.py
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Match the step indentation and `shell:` usage of the job's existing steps.

- [ ] **Step 6: Check, commit, and gate on CI**

Run: `LC_ALL=C grep -n '[^ -~]' tools/p1/*.ps1 tools/p1/*.cs; echo "non-ASCII lines: $?"`
Expected: no lines printed and `non-ASCII lines: 1`.

Run: `/tmp/t082.sh "pwsh -NoProfile -Command \"\$e=\$null; [void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path tools\p1\windows_shell_ui_smoke.ps1), [ref]\$null, [ref]\$e); \$e.Count; [void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path tools\p1\windows_shell_install.ps1), [ref]\$null, [ref]\$e); \$e.Count\""`
Expected: `0` and `0` (both scripts parse).

```bash
git add tools/p1/windows_shell_response_monitor.cs tools/p1/windows_shell_ui_smoke.ps1 \
  tools/p1/windows_shell_install.ps1 .github/workflows/windows-ci.yml
git commit -m "test(p1): add installed TXT reader smoke with P0 measures for T08.2"
```

The installed gate is CI `production-shell-ui` on the PR. Pushing this
commit changes CI and needs the user's explicit OK. Expected on CI: the job
passes, and the install report has `txtReaderLight.txtLong` and
`txtReaderDark.txtLong` within the thresholds, plus the `txt-reader`
screenshots for light and dark.

### Task 6: Traceability and test-matrix docs

**Files:**
- Modify: `docs/work-breakdown.md` (S08, the T08.2 bullet)
- Modify: `docs/p1-technical-design.md` (§7 S08, the T08.2 bullet)
- Modify: `docs/p1/e2e-testing.md` (scenario table)

**Interfaces:**
- Consumes: the class names from Tasks 1-3, the `txt-reader` mode and
  thresholds from Task 5.
- Produces: nothing code depends on.

Mechanical doc change; no TDD cycle.

- [ ] **Step 1: Note T08.2 in the work breakdown**

In `docs/work-breakdown.md`, replace the T08.2 bullet with:

```markdown
- **T08.2** Render using a bounded or virtualized native view with monospace
  preformatted layout by default. A `ListView` over a lazy line list shows
  Consolas, unwrapped lines; tabs expand to 8-column stops and other C0
  controls show as a space; a load failure shows one sentence (P1 T08.2).
```

- [ ] **Step 2: Note T08.2 in the P1 technical design**

In `docs/p1-technical-design.md` §7 S08, append to the T08.2 bullet (after
"never silently drops trailing text."):

```markdown
  `TextLineView` (Core) applies the display rules; `TextLineList` is the
  lazy `IList` the `ListView` reads, built fresh per row, and
  `TextLineMetrics` sizes every row to the widest line so the scroll range
  is fixed from the first frame. `TextReaderSession` reports only `Scroll`
  and shows no selection. The P0 comparison runs in CI
  `production-shell-ui` (`txt-reader` smoke) against the P0 thresholds;
  `pcsx2-win` is used only when CI fails or a measure is within 20% of its
  threshold.
```

- [ ] **Step 3: Add the TXT reader row and fix the Shell smoke row**

In `docs/p1/e2e-testing.md`, in the Shell smoke row replace
"Reader is still a placeholder." with "A seeded TXT guide opens in the text
view; other formats show the Reader placeholder." Then add after the Stable
navigation row:

```markdown
| TXT reader | Seed Text Reader Game with six guides from fixtures: ASCII Map Guide (`txt-ascii`), Legacy Code Page Guide (`txt-legacy`, CP437), Long Text Guide (generated `txt-long`), Missing File Guide (its managed file deleted), Tab Table Guide (`txt-tabs`) and Web Page Guide (HTML). In light and dark: ASCII rows are named exactly as the fixture's lines, with blank and whitespace-only lines named `Blank line`, and the 2,048-column line makes `ReaderTextLines` horizontally scrollable with no placeholder or reader commands; Tab Table rows show 8-column tab stops and a form feed as a space; Legacy shows `Guide é`. Long Text shows its first row within 3 s, realizes at most 300 rows after opening and after eight large scroll steps, and has fewer than two `WM_NULL` responses over 500 ms and no timeouts; the numbers go to the result JSON. Back during the long load leaves the Game page with no text view and no late `Guide ready.`; Missing File shows `This guide's file is missing from the library.` and no text or commands; Web Page shows the placeholder; reopening ASCII shows its rows again. | T08.2, TR08.1, TR08.2, TR11.3 |
```

- [ ] **Step 4: Check and commit**

Run: `git diff --stat -- docs/`
Expected: three files changed under `docs/`.

```bash
git add docs/work-breakdown.md docs/p1-technical-design.md docs/p1/e2e-testing.md
git commit -m "docs(p1): record T08.2 TXT view and test matrix"
```

After the PR merges, a separate commit marks T08.2 merged in
`docs/p1/progress.md`, `docs/p1/implementation-plan.md` and the design's
status line.
