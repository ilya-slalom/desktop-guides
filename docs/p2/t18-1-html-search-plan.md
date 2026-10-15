# T18.1 PR b (HTML Search) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HTML guides can be searched from PR a's find bar, with counts,
next and previous matches and "Back to my place", without changing the
saved reading place.

**Architecture:**
- **Matching stays in Core.** A fixed script returns the page's walk text
  (the same text node walk the position scripts use, so offsets are the
  locator's offsets), with the block edges and the hidden ranges it found.
  Core's `HtmlFindText` masks the hidden text, and `TextMatcher` reads each
  block edge as a space. TXT and HTML share one matcher.
- **Showing a match:** a second script highlights the match with the CSS
  Custom Highlight API (`CSS.highlights`, name `dg-find`), without changing
  the DOM, and scrolls it into view, including inside a scrolling box.
  `HtmlReaderStyle` gains a `::highlight(dg-find)` rule for each theme.
- **The place:** each find step runs under the session's restore turn, so
  the 500 ms tracker skips it. The captured point then follows the match,
  so the re-apply after the bar closes (a resize) keeps the match in view.
  PR a's progress pause keeps the step from saving.

**Tech Stack:** C# / .NET 10, WinUI 3, WebView2 (DevTools
`Runtime.evaluate`), xUnit, PowerShell 5.1 and 7, UI Automation, GitHub
Actions.

**Spec:** `docs/p2/t18-1-search-design.md`, and
`docs/p2-technical-design.md` section 3.

**Target:** T18.1, PR b of three, on `feat/p2-t18-1-html-search`.
**Prerequisites:** PR a (#113), merged; #112, merged.

## Global Constraints

- **Matching:** PR a's rules, unchanged: case-insensitive with ordinal
  `ToUpperInvariant`, whitespace runs match one space, the query trimmed,
  at most 10,000 matches. For HTML, also:
  - a block edge (a different block container, or a `<br>`) matches a
    space, so table cells and `<br>` lines don't run together;
  - text with no box (`display: none`) or not `visibility: visible` is
    never matched;
  - offsets are UTF-16 indexes into the walk text, the same as
    `HtmlPositionScripts`' offsets.
- **The DOM is never changed.** Highlighting uses `CSS.highlights` only.
  Locators stay valid.
- **Scripts:** host values enter only as `JsonSerializer` literals. DOM
  members are reached through their prototypes, because named elements can
  shadow them (the fixtures have `form name="body"` and
  `input name="scrollTop"`).
- **The saved place:** a find step never saves it, and closing the bar
  doesn't either. The user's next own scroll does.
- **Text:** a failed search reads "Couldn't search this guide" in the
  count, and announces "Couldn't search this guide.".
- **Shell rules:** PowerShell stays ASCII-only and runs under 5.1 and 7.
  There is no `cd` in local shell commands.

## Review Focus

1. **Ctrl+F while the keyboard is in the page.** WebView2 has browser
   accelerator keys off; Ctrl+F must still reach the shell and open the
   bar. Pinned by Task 4's `find-html`, which clicks page text before
   pressing Ctrl+F.
2. **Closing the bar resizes the view.** The session's re-apply scrolls to
   its captured point; if that point were still where the bar opened, the
   view would jump away from the match. Pinned by Task 4's top-mark check
   after Escape.
3. **Glued table cells, `<br>` lines and hidden panels,** common in saved
   site pages. Pinned by Task 1's matcher and parser tests, and Task 4's
   `find-html-text` counts.
4. **Shadowing names break a script,** so search fails on a hostile page.
   Pinned by the fixtures' `form name="body"` and `input name="scrollTop"`,
   in Task 4.
5. **A find step during a resize or a restyle.** The bar opening is itself
   a resize; a step in its 300 ms settle must still land on the match.
   Pinned by Task 3's generation loop, and Task 4, which types and presses
   Enter straight after the bar opens.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t18-1b) { Remove-Item -Recurse -Force E:\work\desktop-guides\t18-1b }; New-Item -ItemType Directory E:\work\desktop-guides\t18-1b | Out-Null"'
  COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='*/bin' --exclude='*/obj' --exclude=artifacts -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t18-1b'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t18-1b && dotnet test tests\DesktopGuides.Core.Tests --filter "<filter>" -nologo > E:\work\t18-1b-core.log 2>&1 & findstr /c:"Passed!" /c:"Failed!" /c:"error" E:\work\t18-1b-core.log'`
- **Production build:** `s 'cd /d E:\work\desktop-guides\t18-1b && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo > E:\work\t18-1b-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error" E:\work\t18-1b-build.log'`
- **Script tests:** `s 'cd /d E:\work\desktop-guides\t18-1b && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\<test>.ps1'`

---
### Task 1: Block edges in `TextMatcher`, and `HtmlFindText`

**Files:**
- Modify: `src/DesktopGuides.Core/Reading/TextMatcher.cs` (`FindAll` gains
  `breaks`)
- Modify: `src/DesktopGuides.Core/Reading/FindContract.cs`
  (`FindResult.Failure`)
- Create: `src/DesktopGuides.Core/Html/HtmlFindText.cs`
- Test: `tests/DesktopGuides.Core.Tests/TextMatcherTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/HtmlFindTextTests.cs`

**Interfaces:**
- Produces:
  - `TextMatcher.FindAll(string text, FindQuery query, int maxMatches = MaxMatches, IReadOnlyList<int>? breaks = null)`.
    `breaks` holds ascending offsets; the text before each one and the
    text at it never join into one word;
  - `FindResult.Failure`, which is `new(FindOutcome.Failed, null, null, false, 0)`;
  - `sealed record HtmlFindText(string Text, IReadOnlyList<int> Breaks)`,
    with `static HtmlFindText? Parse(string? json)`. The JSON is
    `{"text":string,"breaks":[int],"hidden":[[start,end]]}`. Hidden ranges
    are masked with `'\0'`. Anything malformed gives `null`.

- [ ] **Step 1: Write the failing matcher tests**

Append to `TextMatcherTests`:

```csharp
    [Fact]
    public void ABlockEdgeMatchesASpace()
    {
        // Two table cells, "Boss" and "Hydra", with no whitespace between.
        TextMatch match = Assert.Single(TextMatcher.FindAll("BossHydra", new FindQuery("boss hydra"), breaks: [4]));

        Assert.Equal(new TextMatch(0, 9), match);
    }

    [Fact]
    public void ABlockEdgeSplitsWords() =>
        Assert.Empty(TextMatcher.FindAll("BossHydra", new FindQuery("sshy"), breaks: [4]));

    [Fact]
    public void ABlockEdgeBesideWhitespaceIsOneSpace()
    {
        Assert.Single(TextMatcher.FindAll("Boss\nHydra", new FindQuery("boss hydra"), breaks: [4]));
        Assert.Single(TextMatcher.FindAll("Boss\nHydra", new FindQuery("boss hydra"), breaks: [5]));
        Assert.Single(TextMatcher.FindAll("Boss\nHydra", new FindQuery("boss hydra"), breaks: [4, 5]));
    }

    [Fact]
    public void BreaksAtTheEdgesChangeNothing() =>
        Assert.Equal(
            [new TextMatch(0, 4)],
            TextMatcher.FindAll("Boss", new FindQuery("boss"), breaks: [0, 4]));

    [Fact]
    public void MaskedTextNeverMatches() =>
        Assert.Empty(TextMatcher.FindAll("A\0\0\0\0\0\0B", new FindQuery("a b")));
```

- [ ] **Step 2: Write the failing parser tests**

Create `tests/DesktopGuides.Core.Tests/HtmlFindTextTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlFindTextTests
{
    [Fact]
    public void ParsesTextAndBreaks()
    {
        HtmlFindText? text = HtmlFindText.Parse("""{"text":"BossHydra","breaks":[4],"hidden":[]}""");

        Assert.NotNull(text);
        Assert.Equal("BossHydra", text.Text);
        Assert.Equal([4], text.Breaks);
    }

    [Fact]
    public void HiddenTextIsMaskedAndOffsetsKeep()
    {
        HtmlFindText text = HtmlFindText.Parse(
            """{"text":"Open Secret door","breaks":[],"hidden":[[5,11]]}""")!;

        Assert.Equal("Open \0\0\0\0\0\0 door", text.Text);
        Assert.Empty(TextMatcher.FindAll(text.Text, new FindQuery("secret")));
        Assert.Equal(new TextMatch(12, 4), Assert.Single(TextMatcher.FindAll(text.Text, new FindQuery("door"))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"breaks":[],"hidden":[]}""")]
    [InlineData("""{"text":"abc","breaks":[4],"hidden":[]}""")]
    [InlineData("""{"text":"abc","breaks":[2,1],"hidden":[]}""")]
    [InlineData("""{"text":"abc","breaks":[],"hidden":[[2,1]]}""")]
    [InlineData("""{"text":"abc","breaks":[],"hidden":[[0,4]]}""")]
    [InlineData("""{"text":"abc","breaks":[],"hidden":[[0]]}""")]
    [InlineData("""{"text":"abc","breaks":["1"],"hidden":[]}""")]
    [InlineData("not json")]
    public void MalformedRepliesAreRefused(string? json) =>
        Assert.Null(HtmlFindText.Parse(json));
}
```

- [ ] **Step 3: Run them to see them fail**

Run: the Core tests command with filter
`FullyQualifiedName~TextMatcherTests|FullyQualifiedName~HtmlFindTextTests`.
Expected: the build fails: `FindAll` has no `breaks` parameter, and
`HtmlFindText` doesn't exist.

- [ ] **Step 4: Implement**

In `TextMatcher.FindAll`, add the parameter and, at the top of the
`while (index < text.Length)` loop, before the whitespace check:

```csharp
            // A block edge (a table cell, a <br>) reads as a space, so two
            // cells never join into one word.
            while (breaks is not null && nextBreak < breaks.Count && breaks[nextBreak] <= index)
            {
                if (breaks[nextBreak] == index && folded.Length > 0 && folded[^1] != ' ' &&
                    !char.IsWhiteSpace(text[index]))
                {
                    folded.Append(' ');
                    starts.Add(index);
                    ends.Add(index);
                }
                nextBreak++;
            }
```

with `int nextBreak = 0;` declared beside `int index = 0;`. A match never
starts or ends on the added space, because the query is trimmed, so the
source ranges stay exact.

In `FindContract.cs`, inside `FindResult`:

```csharp
    public static FindResult Failure { get; } = new(FindOutcome.Failed, null, null, false, 0);
```

Create `src/DesktopGuides.Core/Html/HtmlFindText.cs`:

```csharp
using System.Text.Json;

namespace DesktopGuides.Core.Html;

/// <summary>
/// T18.1: an HTML page's searchable text, as the find script reads it: the
/// position walk's text, so offsets are locator offsets. Hidden text is
/// masked with NUL, which no query contains; breaks are block edges.
/// </summary>
public sealed record HtmlFindText(string Text, IReadOnlyList<int> Breaks)
{
    public static HtmlFindText? Parse(string? json)
    {
        if (json is null) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("text", out JsonElement textValue) || textValue.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("breaks", out JsonElement breaksValue) || breaksValue.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("hidden", out JsonElement hiddenValue) || hiddenValue.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            char[] text = textValue.GetString()!.ToCharArray();
            List<int> breaks = [];
            foreach (JsonElement item in breaksValue.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out int at) ||
                    at < 0 || at > text.Length || (breaks.Count > 0 && at < breaks[^1]))
                {
                    return null;
                }
                breaks.Add(at);
            }
            foreach (JsonElement item in hiddenValue.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2 ||
                    item[0].ValueKind != JsonValueKind.Number || item[1].ValueKind != JsonValueKind.Number ||
                    !item[0].TryGetInt32(out int start) || !item[1].TryGetInt32(out int end) ||
                    start < 0 || end < start || end > text.Length)
                {
                    return null;
                }
                Array.Fill(text, '\0', start, end - start);
            }
            return new HtmlFindText(new string(text), breaks);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: Run them to see them pass, then the whole suite**

Run: the same filter, then the Core tests with no filter.
Expected: the filtered run passes, 15 matcher tests and 13 parser tests;
then the full suite passes (1190 before, plus the new tests).

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Core tests/DesktopGuides.Core.Tests
git commit -m "feat(p2): match HTML text across block edges, with hidden text masked"
```

---
### Task 2: The `::highlight(dg-find)` rule

**Files:**
- Modify: `src/DesktopGuides.Core/Html/HtmlReaderStyle.cs`
- Test: `tests/DesktopGuides.Core.Tests/HtmlReaderStyleTests.cs`

**Interfaces:**
- Produces: `HtmlReaderStyle.FindHighlight = "dg-find"`, the highlight
  name Task 3's scripts register. Each theme's CSS ends with its
  `::highlight(dg-find)` rule.

A custom highlight with no rule draws nothing, so every theme needs one,
High Contrast included. High Contrast uses the system colours, which
forced-colours mode already maps.

- [ ] **Step 1: Update the three exact tests (failing)**

In `HtmlReaderStyleTests`:
- `LightOnlySetsZeroSpecificityDefaults`: append
  `"\n::highlight(dg-find) { background-color: Mark; color: MarkText; }"`
  to the expected string;
- `DarkForcesThePaletteAndKeepsImages`: append
  `"\n::highlight(dg-find) { background-color: #5C4B00; color: #FFFFFF; }"`;
- rename `HighContrastOnlyScales` to `HighContrastScalesAndUsesSystemHighlightColours`,
  and expect
  `"html { zoom: 0.75 !important; }\n::highlight(dg-find) { background-color: Highlight; color: HighlightText; }"`.

- [ ] **Step 2: Run them to see them fail**

Run: the Core tests with filter `FullyQualifiedName~HtmlReaderStyleTests`.
Expected: the three tests fail on the missing rule; the rest pass.

- [ ] **Step 3: Implement**

In `HtmlReaderStyle`:

```csharp
    // T18.1: the find bar's current match, drawn by CSS.highlights.
    public const string FindHighlight = "dg-find";

    private const string LightFind = "::highlight(dg-find) { background-color: Mark; color: MarkText; }";
    // The Dark palette's own mark colour, with 8.8:1 contrast for white.
    private const string DarkFind = "::highlight(dg-find) { background-color: #5C4B00; color: #FFFFFF; }";
    private const string ContrastFind = "::highlight(dg-find) { background-color: Highlight; color: HighlightText; }";
```

and in `Css`:

```csharp
        return theme switch
        {
            ReaderTheme.Light => zoom + "\n" + Light + "\n" + LightFind,
            ReaderTheme.Dark => zoom + "\n" + Dark + "\n" + DarkFind,
            _ => zoom + "\n" + ContrastFind
        };
```

Change the comment above `Css` to: "High contrast sets no page colours:
WebView2 applies Windows' forced colours. Its find highlight uses the
system colours."

- [ ] **Step 4: Run them to see them pass, then the whole suite**

Expected: `HtmlReaderStyleTests` all pass, then the full Core suite.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Html/HtmlReaderStyle.cs tests/DesktopGuides.Core.Tests/HtmlReaderStyleTests.cs
git commit -m "feat(p2): add the find highlight to each HTML reader theme"
```

---
### Task 3: HTML search in the reader

**Files:**
- Modify: `src/DesktopGuides.Production/HtmlPositionScripts.cs` (`Prelude`
  becomes `internal`)
- Create: `src/DesktopGuides.Production/HtmlFindScripts.cs`
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs`
  (`IFindableReader`, the `Find` capability)
- Modify: `src/DesktopGuides.Production/FindBar.xaml.cs` and
  `src/DesktopGuides.Production/ShellWindow.Find.cs` (the failure text)

**Interfaces:**
- Consumes: Task 1's `TextMatcher.FindAll(..., breaks)`, `HtmlFindText.Parse`
  and `FindResult.Failure`; Task 2's `HtmlReaderStyle.FindHighlight`; PR a's
  `IFindableReader` and `TextMatcher.Step`.
- Produces: `HtmlFindScripts.ReadText`, `HtmlFindScripts.Show(int start, int length)`
  and `HtmlFindScripts.Clear`, all returning script text;
  `HtmlReaderSession : IReaderSession, IFindableReader`, declaring
  `ReaderCapabilities.Find`.

The Production project has no unit tests. This task's behaviour is
verified by Task 4's installed modes. Its gate here is a clean build.

- [ ] **Step 1: Share the walk**

In `HtmlPositionScripts`, change `private const string Prelude` to
`internal const string Prelude`, and add to its comment: "HtmlFindScripts
uses the same walk, so find offsets are locator offsets."

- [ ] **Step 2: Write the scripts**

Create `src/DesktopGuides.Production/HtmlFindScripts.cs`:

```csharp
using System.Text.Json;
using DesktopGuides.Core.Html;

namespace DesktopGuides.Production;

// T18.1: the find scripts. They read the walk's text and draw the match;
// Core matches. Highlighting goes through CSS.highlights, so the DOM, and
// every locator, stays as it was.
internal static class HtmlFindScripts
{
    private static readonly string Name = JsonSerializer.Serialize(
        HtmlReaderStyle.FindHighlight, ProductionJsonContext.Default.String);

    // The walk's text; hidden text's ranges; and the offsets where a new
    // block (a cell, a paragraph) or a <br> starts, read as spaces.
    public const string ReadText = "(() => {" + HtmlPositionScripts.Prelude + """
        const w = walk();
        const body = bodyOf.call(document);
        const previousOf = getter(Node.prototype, 'previousSibling');
        const styles = new Map();
        const styleOf = (element) => {
          let cached = styles.get(element);
          if (!cached) {
            const style = getComputedStyle(element);
            cached = {
              inline: style.display.startsWith('inline') || style.display === 'contents',
              visible: style.visibility === 'visible',
              boxed: elementRects.call(element).length > 0
            };
            styles.set(element, cached);
          }
          return cached;
        };
        const blockOf = (element) => {
          let p = element;
          while (p && p !== body && p instanceof Element && styleOf(p).inline) p = parentOf.call(p);
          return p;
        };
        // The nearest earlier sibling, at the first level that has one.
        const afterBreak = (node) => {
          for (let n = node; n && n !== body; n = parentOf.call(n)) {
            const previous = previousOf.call(n);
            if (previous) return String(nameOf.call(previous)).toUpperCase() === 'BR';
          }
          return false;
        };
        let text = '', last = null;
        const breaks = [], hidden = [];
        for (let i = 0; i < w.nodes.length; i++) {
          const node = w.nodes[i], parent = parentOf.call(node), data = dataOf.call(node);
          text += data;
          const style = styleOf(parent);
          if (!style.boxed || !style.visible) {
            hidden.push([w.starts[i], w.starts[i] + data.length]);
            continue;
          }
          const block = blockOf(parent);
          if (last !== null && (block !== last || afterBreak(node))) breaks.push(w.starts[i]);
          last = block;
        }
        return { text, breaks, hidden };
        })()
        """;

    // Highlights the walk range, then scrolls it a quarter of the way down:
    // first inside any scrolling box around it (a wide map in a <pre>),
    // then the page. True once the highlight is set.
    public static string Show(int start, int length) =>
        "(() => {" + HtmlPositionScripts.Prelude +
        "const start = " + JsonSerializer.Serialize(start, ProductionJsonContext.Default.Int32) + ";" +
        "const end = start + " + JsonSerializer.Serialize(length, ProductionJsonContext.Default.Int32) + ";" +
        "const name = " + Name + ";" + """
        const w = walk();
        if (w.nodes.length === 0 || end > w.length || end <= start) return false;
        const a = locate(w, start), b = locate(w, end - 1);
        const match = Document.prototype.createRange.call(document);
        match.setStart(w.nodes[a], start - w.starts[a]);
        match.setEnd(w.nodes[b], end - w.starts[b]);
        CSS.highlights.set(name, new Highlight(match));
        const top = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollTop');
        const left = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollLeft');
        const body = bodyOf.call(document);
        for (let p = parentOf.call(w.nodes[a]); p && p !== body && p instanceof Element; p = parentOf.call(p)) {
          const style = getComputedStyle(p);
          if (!/auto|scroll/.test(style.overflowX + ' ' + style.overflowY)) continue;
          const outer = elementRect.call(p), box = match.getBoundingClientRect();
          if (box.top < outer.top || box.bottom > outer.bottom) {
            top.set.call(p, top.get.call(p) + box.top - outer.top - outer.height / 4);
          }
          if (box.left < outer.left || box.right > outer.right) {
            left.set.call(p, left.get.call(p) + box.left - outer.left - outer.width / 4);
          }
        }
        const box = match.getBoundingClientRect();
        if (box.width === 0 && box.height === 0) return true;
        const x = box.left < 0 || box.right > innerWidth ? scrollX + box.left - innerWidth / 4 : scrollX;
        scrollTo(x, scrollY + box.top - innerHeight / 4);
        return true;
        })()
        """;

    public static readonly string Clear =
        "(() => { CSS.highlights.delete(" + Name + "); return true; })()";
}
```

`ProductionJsonContext` already serializes `Int32`. If it has no `String`
entry, add `[JsonSerializable(typeof(string))]` beside the others, and
ledger it.

- [ ] **Step 3: Make the session findable**

In `HtmlReaderSession`:
- declare `internal sealed class HtmlReaderSession : IReaderSession, IFindableReader`;
- `Capabilities` becomes
  `ReaderCapabilities.Scroll | ReaderCapabilities.TextSize | ReaderCapabilities.Find`;
- add, after `ExecuteAsync`:

```csharp
    // T18.1: the last query's matches over the walk's text. A find step is
    // a restore to the match: it takes the restore turn, so the tracker and
    // the re-apply leave the page to it, and the point follows the match.
    // Progress is paused while the bar is open, so nothing is raised.
    private string? findText;
    private IReadOnlyList<TextMatch> matches = [];
    private int? matchIndex;

    public async Task<FindResult> FindAsync(FindQuery query, FindDirection direction, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await entryLoad.Task.WaitAsync(token) || disposed) return FindResult.Failure;
        await restoreTurn.WaitAsync(token);
        restoring = true;
        try
        {
            if (query.Text != findText)
            {
                HtmlFindText? text = HtmlFindText.Parse(await RunScriptAsync(HtmlFindScripts.ReadText));
                if (text is null)
                {
                    ResetFind();
                    return FindResult.Failure;
                }
                matches = await Task.Run(
                    () => TextMatcher.FindAll(text.Text, query, TextMatcher.MaxMatches, text.Breaks), token);
                findText = query.Text;
                matchIndex = null;
            }
            token.ThrowIfCancellationRequested();
            matchIndex = TextMatcher.Step(matches, matchIndex, current?.Offset ?? 0, direction);
            if (matchIndex is not int index)
            {
                await RunScriptAsync(HtmlFindScripts.Clear);
                return FindResult.None;
            }
            return await ShowMatchAsync(matches[index])
                ? new FindResult(FindOutcome.Found, index + 1, matches.Count, false, 0)
                : FindResult.Failure;
        }
        finally
        {
            restoring = false;
            restoreTurn.Release();
        }
    }

    // A resize or a restyle during the step reflowed the page: show the
    // match again once it settles, as a restore does.
    private async Task<bool> ShowMatchAsync(TextMatch match)
    {
        string script = HtmlFindScripts.Show(match.Start, match.Length);
        int started = generation;
        if (await RunScriptAsync(script) != "true") return false;
        while (started != generation && !disposed)
        {
            started = generation;
            await Task.Delay(ResizeSettle);
            if (await RunScriptAsync(script) != "true") return false;
        }
        HtmlScroll? scroll = HtmlLocationRules.ParseScroll(await RunScriptAsync(HtmlPositionScripts.ReadScroll));
        HtmlCapture? capture = await CaptureAsync();
        if (disposed) return false;
        if (scroll is not null && started == generation) lastScroll = scroll;
        if (capture is not null && capture != current)
        {
            current = capture;
            WritePositionForTest();
        }
        return true;
    }

    public void ClearFind()
    {
        ResetFind();
        _ = RunScriptAsync(HtmlFindScripts.Clear);
    }

    private void ResetFind()
    {
        findText = null;
        matches = [];
        matchIndex = null;
    }
```

Add `using DesktopGuides.Core.Html;` if it isn't there (it is, line 3).
`RunScriptAsync` never throws, and returns `null` after dispose, so the
fire-and-forget clear is safe when the reader closes.

- [ ] **Step 4: Report a failed search**

In `FindBar.ShowResult`:

```csharp
        Count.Text = result.Outcome switch
        {
            FindOutcome.Found => $"{result.Index} of {result.Total}{(result.TotalIsPartial ? "+" : "")}",
            FindOutcome.Failed => "Couldn't search this guide",
            _ => "No matches"
        };
```

In `ShellWindow.FindStepAsync`, replace the announcement with:

```csharp
        AnnounceStatus(result.Outcome switch
        {
            FindOutcome.Found => $"Match {result.Index} of {result.Total}{(result.TotalIsPartial ? " so far" : "")}.",
            FindOutcome.Failed => "Couldn't search this guide.",
            _ => $"No matches for {query.Text}."
        });
```

- [ ] **Step 5: Build**

Run: the Production build command (after `stage`).
Expected: `0 Warning(s)` and `0 Error(s)`. The analyzers may flag the
fire-and-forget `RunScriptAsync`; if so, use the form the project already
uses for discarded tasks, and ledger it.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(p2): search HTML guides from the find bar"
```

---
### Task 4: The `find-html` and `find-html-text` modes

**Files:**
- Create: `tests/fixtures/p2/html-find/guide.html` (authored)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`seed-find`, and
  the usage text)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (two modes in the shared
  `find-*` branch, the mode lists at lines 29 and 1560; `find-unsupported`
  moves to the PDF guide)
- Modify: `tools/p1/windows_shell_install.ps1` (`Invoke-ProgressPass`
  gains `-HtmlPosition`; `Run-NavigationScenarios` runs the new modes)
- Modify: `docs/p1/e2e-testing.md` (the "In-guide search" row)

**Interfaces:**
- Consumes: Task 3's session; PR a's AutomationIds (`ReaderFindBar`,
  `FindQueryInput`, `FindCount`, `FindBackButton`); the smoke's
  `Open-TextGuide`, `Wait-PageName`, `Wait-PageVisible`, `Click-Element`,
  `Get-PageTopLine`, `Wait-HtmlPosition` and `Read-ProgressCounts`.
- Produces: the ShellSeed command `seed-find <app-data-root> <fixtures-root>`,
  which adds "Search Web Guide" to `seed-progress`'s "Progress Game".

- [ ] **Step 1: Author the fixture**

Create `tests/fixtures/p2/html-find/guide.html`. Each line tests one rule,
and the counts in Step 3 follow from it:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Search Web Guide</title>
</head>
<body>
<h1>Search Web Guide</h1>
<p>An authored fixture for in-guide search. Each block tests one rule.</p>
<table><tr><td>Boss</td><td>Hydra</td></tr><tr><td>Reward</td><td>Silver Bow</td></tr></table>
<p>Line one<br>Line two</p>
<p>The Hy<b>dra</b> lair is past the bridge.</p>
<div style="display:none">Secret phrase in a closed panel.</div>
<p style="visibility:hidden">Ghost words nobody sees.</p>
<p>The Iron&nbsp;Key opens the vault.</p>
<form name="body" style="overflow:auto; height:4em; border:1px solid">
<input type="hidden" name="scrollTop"><input type="hidden" name="parentNode">
<p>The scroll box starts here.</p>
<p>Filler line one.</p>
<p>Filler line two.</p>
<p>Filler line three.</p>
<p>Filler line four.</p>
<p>Deep in the scroll box lies the moon key.</p>
</form>
</body>
</html>
```

- [ ] **Step 2: Add `seed-find`**

In `Program.cs`, after the `seed-progress` block:

```csharp
if (args.Length == 3 && args[0] == "seed-find")
{
    // T18.1 PR b: the HTML search fixture, in seed-progress's game.
    ManagedPathResolver findPaths = new(args[1]);
    await using SqliteLibraryRepository findRepository = new(findPaths);
    await findRepository.InitializeAsync();
    Game findGame = (await findRepository.ListGamesAsync()).Single(game => game.Title == "Progress Game");
    string fixture = Path.Combine(Path.GetFullPath(args[2]), "p2", "html-find", "guide.html");
    ImportInspection inspection = await new GuideImportValidator().InspectAsync(fixture, CancellationToken.None);
    if (inspection is not ImportReady ready)
    {
        throw new InvalidOperationException($"The html-find fixture failed the import preview: {inspection}.");
    }
    Guid search = await new GuideImportPublisher(findRepository, findPaths).PublishAsync(
        ready.Manifest, findGame.Id, "Search Web Guide", false, null, CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new { search = search.ToString("N") }));
    return 0;
}
```

Add `seed-find` to the usage line beside `seed-progress`.

- [ ] **Step 3: Write the smoke modes**

Read first: `Get-PageTopLine`, `Wait-PageName`, `Wait-PageVisible` and
`Wait-TopMark` (around line 1900) may be defined inside the `html-*`
branch. If the shared `find-*` branch can't see them, move the ones it
needs up to the shared reader helpers, unchanged, and ledger it.

Add `'find-html'` and `'find-html-text'` to both mode lists, beside
`'find-txt'`. In the `find-*` branch, add the helpers after `Read-Saves`:

```powershell
            # The MARK number of the page's top line, within a range.
            function Wait-TopMarkBetween([int] $low, [int] $high, [string] $step) {
                $deadline = (Get-Date).AddSeconds(10)
                $seen = 'nothing'
                do {
                    $line = Get-PageTopLine
                    if ($line -match '^MARK-(\d{4})\b') {
                        $seen = [int] $Matches[1]
                        if ($seen -ge $low -and $seen -le $high) { return $seen }
                    }
                    Start-Sleep -Milliseconds 250
                } while ((Get-Date) -lt $deadline)
                [void](Save-WindowScreenshot 'find-top-mark')
                throw "After $step the top line was MARK-$seen; expected MARK-$low to MARK-$high."
            }
            function Find-Count([string] $query, [string] $expected) {
                (Get-SearchPattern 'FindQueryInput').SetValue($query)
                (Wait-VisibleById 'FindQueryInput').SetFocus()
                Wait-FocusedId 'FindQueryInput'
                [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                [void](Wait-Name 'FindCount' $expected)
            }
```

Then the branches, after `find-back`:

```powershell
            elseif ($Mode -eq 'find-html') {
                Open-TextGuide 'Long Web Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-PageName 'Long Web Guide')
                $start = Wait-HtmlPosition { param($p) $p.locator } 'a first capture'
                Start-Sleep -Seconds 2
                $baseline = Read-Saves
                # The keyboard in the page: Ctrl+F must still reach the shell.
                Click-Element (Wait-PageVisible 'Form-wrapped introduction text.')
                [System.Windows.Forms.SendKeys]::SendWait('^f')
                [void](Wait-VisibleById 'ReaderFindBar')
                Wait-FocusedId 'FindQueryInput'
                # Typed and sent at once: the step lands inside the bar's own resize.
                [System.Windows.Forms.SendKeys]::SendWait('mark-1500 step{ENTER}')
                [void](Wait-Name 'FindCount' '1 of 1')
                $report.findHtmlTopMark = Wait-TopMarkBetween 1480 1500 'finding MARK-1500'
                [void](Wait-HtmlPosition { param($p) $p.offset -gt $start.offset } 'the point following the match')
                Invoke-Element (Wait-VisibleById 'FindBackButton')
                [void](Wait-HtmlPosition { param($p) $p.offset -eq $start.offset } 'the place the bar opened at')
                Find-Count 'keep left at the fork' '1 of 2997'
                [System.Windows.Forms.SendKeys]::SendWait('{F3}')
                [void](Wait-Name 'FindCount' '2 of 2997')
                [System.Windows.Forms.SendKeys]::SendWait('+{F3}')
                [void](Wait-Name 'FindCount' '1 of 2997')
                [System.Windows.Forms.SendKeys]::SendWait('+{F3}')
                [void](Wait-Name 'FindCount' '2997 of 2997')
                [void](Wait-TopMarkBetween 2950 3000 'wrapping to the last match')
                # Across nested spans and a line break: MARK-0420 is "the target line."
                Find-Count 'TARGET LINE.   mark-0421 step' '1 of 1'
                $beforeClose = Wait-TopMarkBetween 400 421 'finding MARK-0421 across lines'
                $report.findHtmlScreenshot = Save-WindowScreenshot 'find-html'
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Wait-HiddenById 'ReaderFindBar'
                # Past the 300 ms settle and the re-apply: the match stays in view.
                Start-Sleep -Seconds 1
                [void](Wait-TopMarkBetween ($beforeClose - 2) ($beforeClose + 2) 'closing the find bar')
                # Longer than the 1 s quiet timer and the 4 s deadline.
                Start-Sleep -Seconds 5
                $afterSearch = Read-Saves
                if ($afterSearch -ne $baseline) {
                    throw "Searching saved the place: saves went from $baseline to $afterSearch."
                }
                $report.findHtmlSavedNothing = $true
                # Into the page, on the line on top, then a real scroll.
                Click-Element (Wait-PageVisible (Get-PageTopLine))
                [System.Windows.Forms.SendKeys]::SendWait('{PGDN}')
                $deadline = (Get-Date).AddSeconds(8)
                do {
                    Start-Sleep -Milliseconds 250
                    $afterMove = Read-Saves
                } while ($afterMove -le $baseline -and (Get-Date) -lt $deadline)
                if ($afterMove -le $baseline) { throw 'After closing the find bar, scrolling saved nothing.' }
                $report.phases += 'find-html'
            }
            elseif ($Mode -eq 'find-html-text') {
                Open-TextGuide 'Search Web Guide'
                [void](Wait-Status 'Guide ready.')
                Wait-StatusClosed
                [void](Wait-PageName 'Search Web Guide')
                [System.Windows.Forms.SendKeys]::SendWait('^f')
                [void](Wait-VisibleById 'ReaderFindBar')
                $report.findHtmlText = [ordered]@{}
                foreach ($case in @(
                    @{ query = 'boss hydra'; count = '1 of 1'; rule = 'table cells read as separate words' },
                    @{ query = 'sshy'; count = 'No matches'; rule = 'cells never join into one word' },
                    @{ query = 'line one line two'; count = '1 of 1'; rule = 'a <br> reads as a space' },
                    @{ query = 'HYDRA'; count = '1 of 2'; rule = 'case, and a word split by <b>' },
                    @{ query = 'secret phrase'; count = 'No matches'; rule = 'display:none text' },
                    @{ query = 'ghost words'; count = 'No matches'; rule = 'visibility:hidden text' },
                    @{ query = 'iron key'; count = '1 of 1'; rule = 'a no-break space' },
                    @{ query = 'moon key'; count = '1 of 1'; rule = 'a match inside a scrolling form' })) {
                    Find-Count $case.query $case.count
                    $report.findHtmlText[$case.query] = $case.count
                }
                # The scroll box scrolled to the match, through the shadowed scrollTop.
                [void](Wait-PageVisible 'Deep in the scroll box lies the moon key.')
                $report.findHtmlTextScreenshot = Save-WindowScreenshot 'find-html-text'
                $report.phases += 'find-html-text'
            }
```

Change `find-unsupported` to open `'Long PDF Guide'`, with the comment
"PDF search arrives in T18.1 PR c; until then Ctrl+F says so."

If `Wait-PageVisible` can't tell a line clipped by the scroll box from a
visible one, assert the form's scroll instead: read the line's bounding
rectangle against the form's, and ledger the change.

- [ ] **Step 4: Wire the installer**

In `windows_shell_install.ps1`:
- `Invoke-ProgressPass` gains `[switch] $HtmlPosition`. With it, the gate
  list also holds `'HtmlPosition'`:

```powershell
    $names = @('ProgressDiagnostics', 'ProgressOverride')
    if ($HtmlPosition) { $names += 'HtmlPosition' }
```

  and the `foreach` walks `$names`.
- In `Run-NavigationScenarios`, after `seed-progress`:

```powershell
    Invoke-ShellSeed @('seed-find', $dataRoot, $fixtureRoot) | Out-Null
```

  and after `findBack`, before `findUnsupported`:

```powershell
    Invoke-ShellSeed @('clear-reading-locations', $dataRoot) | Out-Null
    $report.findHtml = Invoke-ProgressPass 'find-html' 0 -HtmlPosition
    if ($null -eq (& $estimateOf 'Long Web Guide')) {
        throw 'After closing the find bar and scrolling, no HTML place was stored.'
    }

    $report.findHtmlText = Invoke-ProgressPass 'find-html-text' 0
```

- [ ] **Step 5: Document**

In `docs/p1/e2e-testing.md`, the "In-guide search" row:
- after `seed-progress`, add "and `seed-find`";
- after the `find-back` sentence, add: "`find-html`: Ctrl+F from inside
  the page opens the bar; `mark-1500 step` reads `1 of 1` with MARK-1500
  in view; *Back to my place* returns to the opening point; `keep left at
  the fork` steps `1`, `2`, `1`, then wraps to `2997 of 2997`; a query
  across nested spans and a line break matches; closing keeps the match in
  view and saves nothing, and the next scroll saves. `find-html-text`
  (`Search Web Guide`): table cells and `<br>` lines read as separate
  words, hidden text is never matched, a no-break space matches a space,
  and a match in a scrolling form scrolls into view.";
- `find-unsupported` now names "a PDF guide (until T18.1 PR c)";
- the screenshots become `find-txt`, `find-html` and `find-html-text`.

- [ ] **Step 6: Check, commit, push and dispatch**

```bash
stage
s 'cd /d E:\work\desktop-guides\t18-1b && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_windows_scripts_parse.ps1 & powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_start_installed_task.ps1'
s 'cd /d E:\work\desktop-guides\t18-1b && dotnet build tools\p1\DesktopGuides.ShellSeed -c Release -nologo > E:\work\t18-1b-seed.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" E:\work\t18-1b-seed.log'
grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git add tests/fixtures/p2 tools/p1 docs/p1/e2e-testing.md
git commit -m "test(p2): add the HTML search scenarios to the navigation group"
git push -u origin feat/p2-t18-1-html-search
gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p2-t18-1-html-search -f shell-scope=navigation -f shell-lane=both
```

Expected:
- both script tests pass, the seed builds with 0 warnings, and the
  PowerShell files are ASCII-only;
- the dispatch's `navigation` shard passes on both lanes, with every
  find mode passing. The `find-html` screenshot shows the highlighted
  match.

If a mode fails, use superpowers:systematic-debugging on its evidence.
If Ctrl+F from inside the page doesn't reach the shell (Review Focus 1),
that is a product defect, not a test to relax: find where WebView2 keeps
the key, and fix it in the shell.

---

### Task 5: Records, the full PR run and the PR

**Files:**
- Modify: `docs/p2/t18-1-search-design.md` (Status; the HTML deviations;
  a "PR b verification record")
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md` (T18.1
  PR b in review)

- [ ] **Step 1: Write the records**

- **The design's Status:** "PR a merged (#113). PR b (HTML) implemented on
  `feat/p2-t18-1-html-search`; PR c (PDF) follows."
- **Deviations,** added to the list:
  3. HTML matching runs in Core over the walk text a script returns, not
     in the script, so TXT and HTML share one matcher. The script adds the
     block edges and hidden ranges the matcher needs.
  4. The 500 ms tracker isn't paused for the whole time the bar is open.
     Each find step runs under the restore turn, which the tracker skips,
     so the captured point follows the match and closing the bar (a
     resize) doesn't jump back.
- **"PR b verification record":** the red and green test runs, the
  dispatch ID, the screenshot, and every ledgered ruling.
- **The status pages:** "M7 in progress: T18.1 PR a merged; PR b (HTML
  search) in review."

- [ ] **Step 2: Commit, push, open the PR, and wait for its run**

```bash
git add docs
git commit -m "docs(p2): record T18.1 PR b"
git push
```

Commit the `find-html` screenshot under `docs/p2/evidence/ci/t18-1/`, as
PR a did. Title: "T18.1 PR b: HTML search". Base: `main`. The body names
the task, the prerequisites (PR a, merged) and the outcome; shows the
screenshot; and lists the dispatch, every ruling, and any deferred minors.

Expected: every job passes, including `html` and `navigation` on both
lanes. Record the run in the PR description, then stop for the user's
merge decision.

## Traceability

| Design item | Task |
| --- | --- |
| HTML search through the walk, with block edges and hidden text | 1, 3 |
| `::highlight(dg-find)`, with a colour for each theme | 2 |
| `HtmlReaderSession` declares `Find`; highlight with `CSS.highlights` | 3 |
| Search never saves the place; the view stays at the match after closing | 3, 4 |
| A failed search is reported in the bar | 3 |
| `find-html` and `find-html-text` on both lanes | 4 |
| PDF search (PR c) | a later plan |
