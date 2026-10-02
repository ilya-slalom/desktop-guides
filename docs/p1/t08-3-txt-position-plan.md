# T08.3 TXT commands and position capture/restore implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Page a TXT guide and jump to its start or end from the Reader
toolbar, capture and restore its position as a line-start offset with
context, and keep the top line through a window resize or a Windows
text-size change (closing issue #29).

**Architecture:** A new Core `TextLocator` maps a `TextGuideDocument` to the
T12.1 `ReaderLocation`/`TextPosition` and back. The contract gains
`PageEdgeAction`. `TextReaderSession` gains `PageNavigation` and delegates
scrolling to `TextReaderView`, which tracks an anchor line and re-measures
its rows on a text-size change.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), xUnit, PowerShell 5.1 UI
Automation smoke on CI `production-shell-ui`.

**Spec:** [t08-3-txt-position-design.md](t08-3-txt-position-design.md)

## Global Constraints

- Context is at most 128 characters (`ReaderLocationCodec.MaxContextCharacters` is 160).
- A captured `ContentSha256` is lower-case hex (the codec accepts only lower-case).
- `SchemaVersion` is `ReaderLocationCodec.CurrentVersion` (1); format is `GuideFormat.Txt`.
- Approximate reason: `The guide changed, so this is an approximate position.`
- Unavailable reason: `This reading position can't be used with this guide.`
- `PageEdge` requires `ReaderCapabilities.PageNavigation`; no new capability flag.
- New toolbar buttons: **Go to start** (`ReaderStart`) and **Go to end** (`ReaderEnd`); Previous/Next page labels stay shared with PDF.
- Test hook event: `Local\DesktopGuides.Preview.TextRemeasure.{pid}`; test font multiplier 1.5.
- The P0 `TextLocation`, `TextGuideDocument.Capture` and `TextGuideDocument.Restore` stay unchanged (the P0 `TextProbe` uses them).
- Adapters never write SQLite; persistence is T12.2. No keyboard accelerators (T16.1).
- PowerShell scripts stay ASCII-only and run on Windows PowerShell 5.1.
- Never infer completion from the reading fraction.

## Review Focus

1. **Trailing newline line.** `LineStarts` has an entry equal to `Text.Length`
   after a final `\n`, but `TextLineList` shows no row for it. A restore or
   capture of that line must not scroll past the last row. Pinned by
   `CaptureOfTrailingEmptyLineRestoresExactly` (Task 2) and the view's clamp
   to `lines.Count - 1` (Task 3).
2. **Large repeated guides.** `txt-long` repeats one line 197,844 times, so a
   changed-content restore scans many matches. The scan must stop once it is
   past the saved offset. Pinned by `NearestMatchStopsAfterPassingOffset`
   (Task 2), which restores into a 200,000-line repeated document.
3. **End then Previous.** After Go to end, the requested anchor (last line) is
   below the real top line; Previous page must page from the real top, not
   the anchor. Pinned by the smoke `txt-commands` step "Previous after End"
   (Task 4).
4. **Switching guides keeps no state.** A new session starts at line 0 even
   when the previous one was paged down. Pinned by the smoke guide-switching
   phase (Task 4).
5. **Restore before load / after disposal.** A restore called before
   `Loaded` must apply once rows exist; one after `DisposeAsync` must do
   nothing. Pinned by the session's disposed check and the view's
   `OnLoaded` anchor apply (Task 3); exercised through the shell, which
   opens every guide before `Loaded` (Task 4 `txt-commands` starts at
   Line 0001).

## File map

| File | Change | Task |
| --- | --- | --- |
| `src/DesktopGuides.Core/Text/TextLocator.cs` | Create: `TextLocator`, `TextRestore` | 2 |
| `tests/DesktopGuides.Core.Tests/TextLocatorTests.cs` | Create | 2 |
| `src/DesktopGuides.Core/Reading/ReaderContract.cs` | `ReaderCommand.PageEdge`, `ReaderEdge`, `PageEdgeAction`, policy row | 2 |
| `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs` | PageEdge tests | 2 |
| `src/DesktopGuides.Production/TextReaderView.xaml(.cs)` | `ScrollToLine`, `PageBy`, `ScrollToEdge`, anchor, resize, re-measure, test hook | 3 |
| `src/DesktopGuides.Production/TextReaderSession.cs` | Capabilities, commands, capture/restore, `LocationChanged` | 3 |
| `src/DesktopGuides.Production/ReaderToolbar.xaml(.cs)` | Go to start / Go to end | 3 |
| `tools/p1/DesktopGuides.ReaderToolbarSmoke/ToolbarWindow.cs` | `Describe(PageEdgeAction)` | 3 |
| `tools/p1/windows_reader_toolbar_ui_smoke.ps1` | Invoke Go to start / end | 3 |
| `tests/fixtures/p1/txt-numbered.txt` | Create: 400 numbered lines | 4 |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | Seed Numbered Lines Guide (seven guides) | 4 |
| `tools/p1/windows_shell_ui_smoke.ps1` | txt-commands, txt-resize, txt-remeasure, guide switching | 4 |
| `docs/p1/evidence/t08-3-txt-position/` | Light and dark screenshots | 5 |
| `docs/p1/e2e-testing.md`, `docs/p1-technical-design.md`, `docs/p1/implementation-plan.md`, design doc | Docs | 6 |

Production has no unit-test project; its behavior is pinned by the
installed smoke on CI `production-shell-ui`. There is no dotnet on the Mac:
`/tmp/t083.sh "<cmd>"` copies the repo to `E:\work\desktop-guides\t08-3` on
`pcsx2-win` and runs the command there. Create it once with
`sed 's/t08-2/t08-3/g' /tmp/t082.sh > /tmp/t083.sh && chmod +x /tmp/t083.sh`.

Rulings already taken against the spec's wording (record them in the
design doc in Task 6):

- `ScrollToLine` uses `ListView.ScrollIntoView(item, ScrollIntoViewAlignment.Leading)`
  instead of `ChangeView(line * rowHeight)`: it is independent of container
  padding and works before the first layout.
- `txt-resize` uses the smoke's existing `Resize-ShellWindow` (Win32 move)
  instead of `TransformPattern`; both resize the window, and the helper is
  already proven on CI.
- `PageBy` pages from `FirstVisibleIndex` (the real top), not the anchor, so
  Previous after Go to end is correct (Review Focus 3).

---
### Task 1: Text-size event feasibility spike (throwaway)

Answers issue #29's open question: does a desktop process receive
`UISettings.TextScaleFactorChanged` while running? Issue #29 already shows
that the TXT rows' text re-scales live (the text grew and clipped), so only
the event delivery needs checking. Nothing from this task is committed
except the recorded answer (Task 6).

**Files:** none in the repo. Scratch: `/tmp/t083-textscale.ps1`.

- [ ] **Step 1: Write the listener**

```powershell
# /tmp/t083-textscale.ps1 - ASCII only, Windows PowerShell 5.1
$ErrorActionPreference = 'Stop'
[void][Windows.UI.ViewManagement.UISettings, Windows.UI.ViewManagement, ContentType = WindowsRuntime]
$settings = New-Object Windows.UI.ViewManagement.UISettings
$log = 'E:\work\desktop-guides\t08-3-textscale.log'
"start factor=$($settings.TextScaleFactor)" | Set-Content $log
$script:events = 0
Register-ObjectEvent -InputObject $settings -EventName TextScaleFactorChanged -Action {
    $script:events++
    "changed factor=$($Sender.TextScaleFactor)" | Add-Content 'E:\work\desktop-guides\t08-3-textscale.log'
} | Out-Null
$deadline = (Get-Date).AddMinutes(5)
while ((Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
"end factor=$($settings.TextScaleFactor)" | Add-Content $log
```

- [ ] **Step 2: Run it in the interactive session**

Copy with `scp /tmp/t083-textscale.ps1 pcsx2-win:E:/work/desktop-guides/`,
then start it through an interactive scheduled task (as in
`docs/p1/e2e-testing.md`):

```bash
ssh pcsx2-win 'schtasks /Create /F /TN DG-T083-TextScale /SC ONCE /ST 23:59 /IT /TR "powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\t083-textscale.ps1" && schtasks /Run /TN DG-T083-TextScale'
```

- [ ] **Step 3: Change the text size and restore it**

Record the original value from the log's `start factor=` line. Open
`ms-settings:easeofaccess-display`, move the **Text size** slider by
one step and press **Apply**; then set it back to the original value and
press **Apply** again. Drive this through UIA from a second interactive
task if it works; otherwise ask the user to change it by hand.

- [ ] **Step 4: Read the result and clean up**

Run: `ssh pcsx2-win 'type E:\work\desktop-guides\t08-3-textscale.log & schtasks /Delete /F /TN DG-T083-TextScale'`
Expected: at least two `changed factor=` lines, the last equal to the
`start factor=`. If no `changed` line appears, use `XamlRoot.Changed` as the
trigger in Task 3 instead of `UISettings.TextScaleFactorChanged` and note it
in the ledger as a ruling.

---

### Task 2: Core `TextLocator` and the `PageEdge` contract

**Files:**
- Create: `src/DesktopGuides.Core/Text/TextLocator.cs`
- Create: `tests/DesktopGuides.Core.Tests/TextLocatorTests.cs`
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs` (enum at line 19, actions near line 33, policy rows at line 48)
- Modify: `tests/DesktopGuides.Core.Tests/ReaderContractTests.cs`

**Interfaces:**
- Consumes: `TextGuideDocument.Text`, `.ContentSha256` (upper-case), `.LineStarts`, `.LineAtOffset(int)`; `ReaderLocation`, `TextPosition`, `RestoreOutcome`, `RestoreKind`, `ReaderLocationCodec.CurrentVersion`/`Serialize`/`Deserialize`.
- Produces:
  - `public sealed record TextRestore(int Line, RestoreOutcome Outcome)` (namespace `DesktopGuides.Core.Text`)
  - `public static class TextLocator` with `const int ContextLength = 128`, `const string ApproximateReason`, `const string UnavailableReason`, `static ReaderLocation Capture(TextGuideDocument document, int line)`, `static TextRestore Restore(TextGuideDocument document, ReaderLocation location)`
  - `ReaderCommand.PageEdge`, `public enum ReaderEdge { Start, End }`, `public sealed record PageEdgeAction(ReaderEdge Edge) : ReaderAction(ReaderCommand.PageEdge)`

- [ ] **Step 1: Write the failing locator tests**

```csharp
using System.Text;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextLocatorTests
{
    private static readonly string OtherHash = new('0', 64);

    private static TextGuideDocument Doc(string text) =>
        TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));

    private static ReaderLocation Location(int offset, string context, double? fraction) =>
        new(GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, OtherHash,
            new TextPosition(offset, context), fraction);

    [Fact]
    public void CaptureUsesLineStartAndFollowingText()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");

        ReaderLocation location = TextLocator.Capture(document, 1);

        Assert.Equal(GuideFormat.Txt, location.Format);
        Assert.Equal(ReaderLocationCodec.CurrentVersion, location.SchemaVersion);
        Assert.Equal(new TextPosition(6, "beta\ngamma\n"), location.Payload);
        Assert.Equal(6.0 / 17, location.EstimatedFraction);
    }

    [Fact]
    public void CaptureLowerCasesTheDocumentHash()
    {
        TextGuideDocument document = Doc("alpha\n");

        ReaderLocation location = TextLocator.Capture(document, 0);

        Assert.Matches("^[0-9A-F]{64}$", document.ContentSha256);
        Assert.Equal(document.ContentSha256.ToLowerInvariant(), location.ContentSha256);
    }

    [Fact]
    public void CapturedLocationRoundTripsThroughTheCodec()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");
        ReaderLocation location = TextLocator.Capture(document, 2);

        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(location), GuideFormat.Txt,
            document.ContentSha256.ToLowerInvariant());

        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Equal(location, decoded.Location);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99, 3)]
    public void CaptureClampsTheLine(int requested, int expected)
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");

        ReaderLocation location = TextLocator.Capture(document, requested);

        Assert.Equal(document.LineStarts[expected],
            Assert.IsType<TextPosition>(location.Payload).CharacterOffset);
    }

    [Fact]
    public void CaptureLimitsContextLength()
    {
        ReaderLocation location = TextLocator.Capture(Doc(new string('a', 300)), 0);

        Assert.Equal(TextLocator.ContextLength,
            Assert.IsType<TextPosition>(location.Payload).Context.Length);
    }

    [Fact]
    public void CaptureStopsContextBeforeNul()
    {
        ReaderLocation location = TextLocator.Capture(Doc("ab\0cd"), 0);

        Assert.Equal("ab", Assert.IsType<TextPosition>(location.Payload).Context);
        _ = ReaderLocationCodec.Serialize(location);
    }

    [Fact]
    public void CaptureDropsATrailingHighSurrogate()
    {
        string text = new string('a', TextLocator.ContextLength - 1) + "\U0001F600z";

        ReaderLocation location = TextLocator.Capture(Doc(text), 0);

        Assert.Equal(new string('a', TextLocator.ContextLength - 1),
            Assert.IsType<TextPosition>(location.Payload).Context);
        _ = ReaderLocationCodec.Serialize(location);
    }

    [Fact]
    public void EmptyGuideCapturesAndRestoresLineZero()
    {
        TextGuideDocument document = Doc("");

        ReaderLocation location = TextLocator.Capture(document, 3);
        TextRestore restore = TextLocator.Restore(document, location);

        Assert.Equal(new TextPosition(0, ""), location.Payload);
        Assert.Equal(0.0, location.EstimatedFraction);
        Assert.Equal(new TextRestore(0, new RestoreOutcome(RestoreKind.Exact)), restore);
    }

    [Fact]
    public void UnchangedContentRestoresExactly()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");

        TextRestore restore = TextLocator.Restore(document, TextLocator.Capture(document, 2));

        Assert.Equal(new TextRestore(2, new RestoreOutcome(RestoreKind.Exact)), restore);
    }

    [Fact]
    public void MidLineOffsetSnapsToItsLine()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");
        ReaderLocation location = TextLocator.Capture(document, 0) with
        {
            Payload = new TextPosition(8, "")
        };

        Assert.Equal(new TextRestore(1, new RestoreOutcome(RestoreKind.Exact)),
            TextLocator.Restore(document, location));
    }

    [Fact]
    public void OffsetBeyondTextFallsBackToContext()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");
        ReaderLocation location = TextLocator.Capture(document, 0) with
        {
            Payload = new TextPosition(999, "beta\n")
        };

        Assert.Equal(new TextRestore(1, new RestoreOutcome(RestoreKind.Context)),
            TextLocator.Restore(document, location));
    }

    [Fact]
    public void ChangedGuideFindsContextOnTheMovedLine()
    {
        ReaderLocation location = TextLocator.Capture(Doc("alpha\nbeta\ngamma\n"), 2);

        TextRestore restore = TextLocator.Restore(Doc("intro\nalpha\nbeta\ngamma\n"), location);

        Assert.Equal(new TextRestore(3, new RestoreOutcome(RestoreKind.Context)), restore);
    }

    [Fact]
    public void NearestRepeatedContextWins()
    {
        // "Boss" starts at offsets 0 and 15; 15 is nearer to 13.
        TextRestore restore = TextLocator.Restore(
            Doc("Boss\naaaa\naaaa\nBoss\naaaa\n"), Location(13, "Boss", null));

        Assert.Equal(new TextRestore(3, new RestoreOutcome(RestoreKind.Context)), restore);
    }

    [Fact]
    public void EquallyNearContextsFallBackToApproximate()
    {
        // "Boss" starts at 0 and 10, both 5 from the saved offset.
        TextRestore restore = TextLocator.Restore(
            Doc("Boss\naaaa\nBoss\n"), Location(5, "Boss", 0.5));

        Assert.Equal(new TextRestore(1,
            new RestoreOutcome(RestoreKind.Approximate, TextLocator.ApproximateReason)), restore);
    }

    [Fact]
    public void MissingContextUsesTheFraction()
    {
        TextRestore restore = TextLocator.Restore(Doc("a\nb\nc"), Location(0, "zzz", 1.0));

        Assert.Equal(new TextRestore(2,
            new RestoreOutcome(RestoreKind.Approximate, TextLocator.ApproximateReason)), restore);
    }

    [Fact]
    public void MissingContextAndFractionIsUnavailable()
    {
        TextRestore restore = TextLocator.Restore(Doc("a\nb\nc"), Location(0, "zzz", null));

        Assert.Equal(new TextRestore(0,
            new RestoreOutcome(RestoreKind.Unavailable, TextLocator.UnavailableReason)), restore);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("payload")]
    [InlineData("version")]
    public void WrongFormatPayloadOrVersionIsUnavailable(string defect)
    {
        ReaderLocation usable = Location(0, "a", 0);
        ReaderLocation location = defect switch
        {
            "format" => usable with { Format = GuideFormat.Pdf },
            "payload" => usable with { Payload = new PdfPosition(0, 0) },
            _ => usable with { SchemaVersion = ReaderLocationCodec.CurrentVersion + 1 }
        };

        Assert.Equal(new TextRestore(0,
                new RestoreOutcome(RestoreKind.Unavailable, TextLocator.UnavailableReason)),
            TextLocator.Restore(Doc("a\nb\n"), location));
    }

    [Fact]
    public void AnotherGuidesLocationIsNeverExact()
    {
        TextGuideDocument first = Doc("Chapter 1\nBoss fight\n");
        TextGuideDocument second = Doc("Other guide\nBoss fight\nEnd\n");

        TextRestore shared = TextLocator.Restore(second, TextLocator.Capture(first, 1));
        TextRestore unique = TextLocator.Restore(second, TextLocator.Capture(first, 0));

        Assert.Equal(new TextRestore(1, new RestoreOutcome(RestoreKind.Context)), shared);
        Assert.Equal(RestoreKind.Approximate, unique.Outcome.Kind);
    }

    [Fact]
    public void EveryLineRoundTripsExactly()
    {
        string text = string.Join("\n",
            Enumerable.Range(0, 50).Select(index => index % 7 == 0 ? "" : $"Row {index}")) + "\n";
        TextGuideDocument document = Doc(text);

        for (int line = 0; line < document.LineStarts.Count; line++)
        {
            Assert.Equal(new TextRestore(line, new RestoreOutcome(RestoreKind.Exact)),
                TextLocator.Restore(document, TextLocator.Capture(document, line)));
        }
    }

    [Fact]
    public void CaptureOfTrailingEmptyLineRestoresExactly()
    {
        TextGuideDocument document = Doc("alpha\nbeta\n");
        int last = document.LineStarts.Count - 1;

        TextRestore restore = TextLocator.Restore(document, TextLocator.Capture(document, last));

        Assert.Equal(2, last);
        Assert.Equal(new TextRestore(last, new RestoreOutcome(RestoreKind.Exact)), restore);
    }

    [Fact]
    public void NearestMatchStopsAfterPassingOffset()
    {
        // 200,000 identical lines, like txt-long; the saved offset is line 10.
        string line = "Area 001 | Proceed north, then open the chest. |\n";
        TextGuideDocument document = Doc(string.Concat(Enumerable.Repeat(line, 200_000)));
        ReaderLocation location = Location(10 * line.Length + 1, "Area 001", null);

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        TextRestore restore = TextLocator.Restore(document, location);
        clock.Stop();

        Assert.Equal(new TextRestore(10, new RestoreOutcome(RestoreKind.Context)), restore);
        Assert.True(clock.ElapsedMilliseconds < 50, $"Restore took {clock.ElapsedMilliseconds} ms.");
    }
}
```

- [ ] **Step 2: Write the failing contract tests** (append to `ReaderContractTests`, before `FakeReader`)

```csharp
    [Fact]
    public void PageEdgeIsVisibleOnlyWithPageNavigation()
    {
        using FakeReader scroll = new(GuideFormat.Txt, ReaderCapabilities.Scroll);
        using FakeReader paged = new(GuideFormat.Txt,
            ReaderCapabilities.Scroll | ReaderCapabilities.PageNavigation);

        Assert.DoesNotContain(ReaderCommand.PageEdge, ReaderCommandPolicy.VisibleCommands(scroll));
        Assert.Contains(ReaderCommand.PageEdge, ReaderCommandPolicy.VisibleCommands(paged));
    }

    [Fact]
    public async Task PageEdgeDispatchesOnlyWithPageNavigation()
    {
        using FakeReader scroll = new(GuideFormat.Txt, ReaderCapabilities.Scroll);
        using FakeReader paged = new(GuideFormat.Txt, ReaderCapabilities.PageNavigation);
        ReaderAction end = new PageEdgeAction(ReaderEdge.End);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ReaderCommandPolicy.ExecuteAsync(scroll, end, CancellationToken.None));
        await ReaderCommandPolicy.ExecuteAsync(paged, end, CancellationToken.None);

        Assert.Null(scroll.LastAction);
        Assert.Same(end, paged.LastAction);
    }
```

- [ ] **Step 3: Run them to verify they fail**

Run: `/tmp/t083.sh "dotnet test tests\DesktopGuides.Core.Tests -c Release"`
Expected: build FAILS with CS0103/CS0246 for `TextLocator`, `TextRestore`, `PageEdgeAction`, `ReaderEdge`.

- [ ] **Step 4: Add the contract types**

In `ReaderContract.cs`, add `PageEdge` to `ReaderCommand` after `PageTurn`;
after `PageTurnAction` add:

```csharp
public enum ReaderEdge
{
    Start,
    End
}

public sealed record PageEdgeAction(ReaderEdge Edge)
    : ReaderAction(ReaderCommand.PageEdge);
```

and in `Commands`, after the `PageTurn` row:

```csharp
        (ReaderCommand.PageEdge, ReaderCapabilities.PageNavigation),
```

- [ ] **Step 5: Write `TextLocator`**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Text;

public sealed record TextRestore(int Line, RestoreOutcome Outcome);

// Maps a TXT document to the T12.1 locator: a line's start offset plus the
// text that follows it, so a changed guide can still find the same place.
public static class TextLocator
{
    public const int ContextLength = 128;
    public const string ApproximateReason = "The guide changed, so this is an approximate position.";
    public const string UnavailableReason = "This reading position can't be used with this guide.";

    public static ReaderLocation Capture(TextGuideDocument document, int line)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<int> starts = document.LineStarts;
        string text = document.Text;
        int offset = starts[Math.Clamp(line, 0, starts.Count - 1)];
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

    public static TextRestore Restore(TextGuideDocument document, ReaderLocation location)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(location);
        if (location.Format != GuideFormat.Txt ||
            location.Payload is not TextPosition position ||
            location.SchemaVersion != ReaderLocationCodec.CurrentVersion)
        {
            return Unavailable();
        }

        string text = document.Text;
        if (string.Equals(location.ContentSha256, document.ContentSha256, StringComparison.OrdinalIgnoreCase) &&
            position.CharacterOffset >= 0 && position.CharacterOffset <= text.Length)
        {
            return new(document.LineAtOffset(position.CharacterOffset), new(RestoreKind.Exact));
        }
        if (NearestMatch(text, position) is int match)
        {
            return new(document.LineAtOffset(match), new(RestoreKind.Context));
        }
        if (location.EstimatedFraction is double fraction && double.IsFinite(fraction))
        {
            int offset = (int)Math.Round(Math.Clamp(fraction, 0, 1) * text.Length);
            return new(document.LineAtOffset(offset), new(RestoreKind.Approximate, ApproximateReason));
        }
        return Unavailable();
    }

    // The match nearest the saved offset, or null when there is none or two
    // are equally near. Matches arrive in order, so the scan stops once they
    // are past the offset and moving away from it.
    private static int? NearestMatch(string text, TextPosition position)
    {
        string context = position.Context;
        if (context.Length == 0)
        {
            return null;
        }
        int? best = null;
        long bestDistance = long.MaxValue;
        bool tie = false;
        for (int index = text.IndexOf(context, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(context, index + 1, StringComparison.Ordinal))
        {
            long distance = Math.Abs((long)index - position.CharacterOffset);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
                tie = false;
            }
            else if (distance == bestDistance)
            {
                tie = true;
            }
            else if (index > position.CharacterOffset)
            {
                break;
            }
        }
        return tie ? null : best;
    }

    private static TextRestore Unavailable() =>
        new(0, new RestoreOutcome(RestoreKind.Unavailable, UnavailableReason));
}
```

- [ ] **Step 6: Run the Core tests**

Run: `/tmp/t083.sh "dotnet test tests\DesktopGuides.Core.Tests -c Release"`
Expected: PASS; total = 371 + the new tests (24 locator cases including theory rows, 2 contract), 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/DesktopGuides.Core/Text/TextLocator.cs src/DesktopGuides.Core/Reading/ReaderContract.cs tests/DesktopGuides.Core.Tests/TextLocatorTests.cs tests/DesktopGuides.Core.Tests/ReaderContractTests.cs
git commit -m "feat(p1): add TXT locator and PageEdge command for T08.3"
```

---
### Task 3: Production view, session and toolbar

No unit-test project covers Production; this task's behavior is pinned by
the smoke in Task 4 (written first in that task's steps for the shell, and
here for the toolbar host). The toolbar smoke change is the failing check
for this task.

**Files:**
- Modify: `src/DesktopGuides.Production/TextReaderView.xaml.cs`
- Modify: `src/DesktopGuides.Production/TextReaderSession.cs`
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml`, `ReaderToolbar.xaml.cs`
- Modify: `tools/p1/DesktopGuides.ReaderToolbarSmoke/ToolbarWindow.cs:102-111`
- Modify: `tools/p1/windows_reader_toolbar_ui_smoke.ps1:273-276`

**Interfaces:**
- Consumes (Task 2): `TextLocator.Capture`, `TextLocator.Restore`, `TextRestore`, `PageEdgeAction`, `ReaderEdge`.
- Produces (Task 4 relies on these UI facts):
  - TXT sessions expose `Scroll | PageNavigation`; the toolbar shows **Go to start**, **Previous page**, **Next page**, **Go to end** for TXT, in that order.
  - `TextReaderView` opens `Local\DesktopGuides.Preview.TextRemeasure.{pid}` (AutoReset, created by the smoke) on `Loaded`; each signal re-measures at 1.5× font size.
  - `TextReaderView.ScrollToLine(int)`, `PageBy(int)`, `ScrollToEdge(ReaderEdge)`, `event EventHandler? TopLineChanged`, `int FirstVisibleIndex`.

- [ ] **Step 1: Add the failing toolbar smoke checks**

In `windows_reader_toolbar_ui_smoke.ps1`, after `Invoke-Command 'Next page' 'Page turn 1'`:

```powershell
    Invoke-Command 'Go to start' 'Page edge Start'
    Invoke-Command 'Go to end' 'Page edge End'
```

In `ToolbarWindow.cs` `Describe`, after the `PageTurnAction` case:

```csharp
        PageEdgeAction edge => $"Page edge {edge.Edge}",
```

- [ ] **Step 2: Verify the toolbar host builds and the check fails**

Run: `/tmp/t083.sh "dotnet build tools\p1\DesktopGuides.ReaderToolbarSmoke -c Release -p:Platform=x64"`
Expected: build succeeds. The CI `reader-toolbar-ui` smoke would fail at
`Go to start` (no such button); this is confirmed on the CI run in Task 5.

- [ ] **Step 3: Add the toolbar buttons**

In `ReaderToolbar.xaml`, wrap the page buttons so the order is Go to start,
Previous page, Next page, Go to end:

```xml
            <AppBarButton x:Name="PageStart"
                          Label="Go to start"
                          AutomationProperties.AutomationId="ReaderStart"
                          Visibility="Collapsed"
                          Click="PageStartClicked">
                <AppBarButton.Icon>
                    <FontIcon Glyph="&#xE74A;" />
                </AppBarButton.Icon>
            </AppBarButton>
            <!-- existing PreviousPage and NextPage buttons -->
            <AppBarButton x:Name="PageEnd"
                          Label="Go to end"
                          AutomationProperties.AutomationId="ReaderEnd"
                          Visibility="Collapsed"
                          Click="PageEndClicked">
                <AppBarButton.Icon>
                    <FontIcon Glyph="&#xE74B;" />
                </AppBarButton.Icon>
            </AppBarButton>
```

In `ReaderToolbar.xaml.cs` `RefreshCommands`:

```csharp
        bool edges = supported.Contains(ReaderCommand.PageEdge);
        // ...
        PageStart.Visibility = Show(edges);
        PageEnd.Visibility = Show(edges);
        // ...
        Commands.Visibility = Show(
            pages || edges || textSize || zoom || pageJump || fitWidth || find);
```

Add `PageStart` before `PreviousPage` and `PageEnd` after `NextPage` in the
`RestorePromptFocus` control list, and the handlers:

```csharp
    private async void PageStartClicked(object sender, RoutedEventArgs args) =>
        await ExecuteAsync(new PageEdgeAction(ReaderEdge.Start), "go to the start");

    private async void PageEndClicked(object sender, RoutedEventArgs args) =>
        await ExecuteAsync(new PageEdgeAction(ReaderEdge.End), "go to the end");
```

- [ ] **Step 4: Add scrolling, anchor, resize and re-measure to `TextReaderView`**

Replace the fields, constructor, `Clear` and `OnLoaded`, and change
`LineContainerChanging`; keep `FirstVisibleIndex`, `ScrollByViewport` and
`FindScrollViewer`. New usings: `DesktopGuides.Core.Reading`,
`Microsoft.UI.Dispatching`, `Windows.UI.ViewManagement`.

```csharp
    private const int ProbeColumns = 64;
    private const double TestFontScale = 1.5;
    private readonly TextLineList lines;
    private readonly int maxColumns;
    private double rowWidth;
    private double rowHeight;
    private double baseFontSize;
    private double fontScale = 1;
    // The line to keep at the top through layout changes; it follows the
    // reader's scrolling except while the view applies its own scroll.
    private int anchorLine;
    private int reportedTopLine;
    private bool restoring;
    private ScrollViewer? viewer;
    private DispatcherQueue? dispatcher;
    private UISettings? uiSettings;
    private RegisteredWaitHandle? remeasureWait;
    private EventWaitHandle? remeasureSignal;

    public TextReaderView(TextLineList lines, int maxColumns)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(maxColumns);
        this.lines = lines;
        this.maxColumns = maxColumns;
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public event EventHandler? TopLineChanged;

    public int LineCount => lines.Count;

    // Shows the line at the top; before the first layout it is kept and
    // applied once rows exist.
    public void ScrollToLine(int line)
    {
        anchorLine = Math.Clamp(line, 0, Math.Max(lines.Count - 1, 0));
        if (Lines.ItemsSource is null || lines.Count == 0)
        {
            return;
        }
        restoring = true;
        Lines.ScrollIntoView(lines[anchorLine], ScrollIntoViewAlignment.Leading);
        dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () => restoring = false);
    }

    public void PageBy(int delta)
    {
        int rows = viewer is null || rowHeight <= 0
            ? 1
            : Math.Max(1, (int)Math.Floor(viewer.ViewportHeight / rowHeight));
        // Pages from the real top, not the anchor, which can sit below it after Go to end.
        long target = (long)FirstVisibleIndex + (long)delta * rows;
        ScrollToLine((int)Math.Clamp(target, 0, Math.Max(lines.Count - 1, 0)));
    }

    public void ScrollToEdge(ReaderEdge edge) =>
        ScrollToLine(edge == ReaderEdge.Start ? 0 : lines.Count - 1);

    public void Clear()
    {
        Loaded -= OnLoaded;
        SizeChanged -= OnSizeChanged;
        if (viewer is not null)
        {
            viewer.ViewChanged -= OnViewChanged;
            viewer = null;
        }
        if (uiSettings is not null)
        {
            uiSettings.TextScaleFactorChanged -= OnTextScaleFactorChanged;
            uiSettings = null;
        }
        remeasureWait?.Unregister(null);
        remeasureWait = null;
        remeasureSignal?.Dispose();
        remeasureSignal = null;
        Lines.ItemsSource = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        dispatcher = DispatcherQueue;
        baseFontSize = CellProbe.FontSize;
        Measure();
        Lines.ItemsSource = lines;
        viewer = FindScrollViewer(Lines);
        if (viewer is not null)
        {
            viewer.ViewChanged += OnViewChanged;
        }
        uiSettings = new UISettings();
        uiSettings.TextScaleFactorChanged += OnTextScaleFactorChanged;
        OpenRemeasureHook();
        if (anchorLine > 0)
        {
            ScrollToLine(anchorLine);
        }
    }

    private void Measure()
    {
        CellProbe.FontSize = baseFontSize * fontScale;
        CellProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        rowWidth = Math.Ceiling(CellProbe.DesiredSize.Width / ProbeColumns * maxColumns);
        rowHeight = Math.Ceiling(CellProbe.DesiredSize.Height);
    }

    // Issue #29: the system text size changed, so rows measured at the old
    // size would clip. Re-measure, resize realized rows, and keep the top line.
    private void Remeasure()
    {
        if (Lines.ItemsSource is null)
        {
            return;
        }
        double oldWidth = rowWidth;
        Measure();
        if (Lines.ItemsPanelRoot is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is ListViewItem { ContentTemplateRoot: TextBlock text })
                {
                    ApplyRowSize(text);
                }
            }
        }
        if (viewer is not null && oldWidth > 0)
        {
            viewer.ChangeView(viewer.HorizontalOffset * rowWidth / oldWidth, null, null, true);
        }
        ScrollToLine(anchorLine);
    }

    private void ApplyRowSize(TextBlock text)
    {
        text.FontSize = baseFontSize * fontScale;
        // A minimum, so a fallback glyph wider than a Consolas cell isn't clipped.
        text.MinWidth = rowWidth;
        text.Height = rowHeight;
    }

    private void OnTextScaleFactorChanged(UISettings sender, object args) =>
        dispatcher?.TryEnqueue(Remeasure);

    // Installed tests can't change the system text size, so they signal this
    // event to re-measure at a larger test font size instead.
    private void OpenRemeasureHook()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(
                $@"Local\DesktopGuides.Preview.TextRemeasure.{Environment.ProcessId}",
                out EventWaitHandle? signal))
            {
                return;
            }
            remeasureSignal = signal;
            remeasureWait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
                dispatcher?.TryEnqueue(() =>
                {
                    fontScale = TestFontScale;
                    Remeasure();
                }), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (UnauthorizedAccessException)
        {
            // Optional installed-test synchronization must not affect normal reading.
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (Lines.ItemsSource is not null)
        {
            ScrollToLine(anchorLine);
        }
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (args.IsIntermediate)
        {
            return;
        }
        int top = FirstVisibleIndex;
        if (!restoring)
        {
            anchorLine = top;
        }
        if (top != reportedTopLine)
        {
            reportedTopLine = top;
            TopLineChanged?.Invoke(this, EventArgs.Empty);
        }
    }
```

In `LineContainerChanging`, replace the three `text.*` lines after
`text.Text = line.Text;` with `ApplyRowSize(text);`.

`TextLineList[int]` builds a fresh `TextLineItem` and `IndexOf` finds an
item by its `Index`, so `ScrollIntoView(lines[anchorLine], ...)` locates the
bound row; no change to `TextLineList` is needed.

- [ ] **Step 5: Wire the session**

Replace the body of `TextReaderSession` from `Capabilities` down to
`DisposeAsync`:

```csharp
    public ReaderCapabilities Capabilities =>
        ReaderCapabilities.Scroll | ReaderCapabilities.PageNavigation;

    // Capabilities don't change during a TXT session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;

    public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
        throw new NotSupportedException("A TXT session is built from a loaded document.");

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(TextLocator.Capture(document, View.FirstVisibleIndex));
    }

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        TextRestore restore = TextLocator.Restore(document, location);
        token.ThrowIfCancellationRequested();
        if (!disposed && restore.Outcome.Kind != RestoreKind.Unavailable)
        {
            View.ScrollToLine(restore.Line);
        }
        return Task.FromResult(restore.Outcome);
    }

    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        token.ThrowIfCancellationRequested();
        switch (action)
        {
            case ScrollAction scroll:
                View.ScrollByViewport(scroll.VerticalViewportFraction);
                break;
            case PageTurnAction page:
                View.PageBy(page.Delta);
                break;
            case PageEdgeAction edge:
                View.ScrollToEdge(edge.Edge);
                break;
            default:
                throw new NotSupportedException($"TXT guides don't support {action.Command}.");
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            View.TopLineChanged -= OnTopLineChanged;
            View.Clear();
        }
        return ValueTask.CompletedTask;
    }

    private void OnTopLineChanged(object? sender, EventArgs args) =>
        LocationChanged?.Invoke(this, new LocationChangedEventArgs());
```

In the constructor, after `View = ...`, add `View.TopLineChanged += OnTopLineChanged;`.
Update the class comment to: "The Reader's session for one loaded TXT
guide. The shell builds it from a document T08.1's loader returned."

- [ ] **Step 6: Build Production and the toolbar host**

Run: `/tmp/t083.sh "dotnet build src\DesktopGuides.Production -c Release -p:Platform=x64 && dotnet build tools\p1\DesktopGuides.ReaderToolbarSmoke -c Release -p:Platform=x64"`
Expected: both builds succeed with 0 errors and no new warnings.

- [ ] **Step 7: Commit**

```bash
git add src/DesktopGuides.Production tools/p1/DesktopGuides.ReaderToolbarSmoke tools/p1/windows_reader_toolbar_ui_smoke.ps1
git commit -m "feat(p1): page TXT guides and keep their top line for T08.3"
```

---
### Task 4: Numbered fixture, seed and shell smoke

**Files:**
- Create: `tests/fixtures/p1/txt-numbered.txt`
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs:387-416` (`seed-txt-reader`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (txt block, about lines 1170-1415)

**Interfaces:**
- Consumes (Task 3): TXT toolbar buttons named `Go to start`, `Previous page`,
  `Next page`, `Go to end`; the `TextRemeasure` event; rows named by their text.
- Produces: result JSON phases `txt-commands`, `txt-resize`, `txt-remeasure`,
  `txt-switch`, and `$report.txtPosition` with the measured page step and
  row-size ratios.

- [ ] **Step 1: Generate the fixture**

`txt-long`'s lines are identical, so the top line can't be read from it.
This fixture has 400 unique lines, LF endings and a final newline:

```bash
python3 -c "import sys; sys.stdout.write(''.join(f'Line {n:04d} | Numbered guide text.\n' for n in range(1, 401)))" > tests/fixtures/p1/txt-numbered.txt
wc -l tests/fixtures/p1/txt-numbered.txt; head -1 tests/fixtures/p1/txt-numbered.txt; tail -1 tests/fixtures/p1/txt-numbered.txt
```

Expected: `400`, `Line 0001 | Numbered guide text.`, `Line 0400 | Numbered guide text.`
(`tests/fixtures/p1/**` is already `-text` in `.gitattributes`.)

- [ ] **Step 2: Seed it**

In `seed-txt-reader`, after the Legacy Code Page Guide insert:

```csharp
    await InsertTextGuideAsync(textPaths, textGame.Id, Guid.NewGuid(), "Numbered Lines Guide", textNow,
        Fixture("p1/txt-numbered.txt"));
```

and change the message to `Seeded the TXT reader game with seven guides.`

- [ ] **Step 3: Write the failing smoke checks**

In the txt block, after `Back-ToTextGame`, add helpers:

```powershell
        # The first row whose middle is inside the list is the top line.
        function Get-TopRow {
            $top = (Find-ById 'ReaderTextLines').Current.BoundingRectangle.Top
            $rows = @(Get-TextRows | Where-Object { -not $_.Current.IsOffscreen } |
                Sort-Object { $_.Current.BoundingRectangle.Top })
            foreach ($row in $rows) {
                $bounds = $row.Current.BoundingRectangle
                if ($bounds.Top + $bounds.Height / 2 -ge $top) { return $row }
            }
            throw 'The TXT reader showed no rows.'
        }

        function Get-TopLine {
            $name = (Get-TopRow).Current.Name
            if ($name -notmatch '^Line (\d{4}) ') { throw "Unexpected top row '$name'." }
            return [int]$Matches[1]
        }

        function Wait-TopLine([int] $expected, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $actual = Get-TopLine
                if ($actual -eq $expected) { return }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "$step left line $actual at the top; expected line $expected."
        }

        function Wait-TopLineChange([int] $from, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $actual = Get-TopLine
                if ($actual -ne $from) { Start-Sleep -Milliseconds 300; return Get-TopLine }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "$step did not move the top line from $from."
        }

        function Get-FullyVisibleRows {
            $list = (Find-ById 'ReaderTextLines').Current.BoundingRectangle
            return @(Get-TextRows | Where-Object {
                $bounds = $_.Current.BoundingRectangle
                -not $_.Current.IsOffscreen -and
                    $bounds.Top -ge $list.Top - 1 -and $bounds.Bottom -le $list.Bottom + 1
            }).Count
        }

        function Invoke-ReaderCommand([string] $name) {
            $button = Find-ByName $name
            if (-not $button -or $button.Current.IsOffscreen) {
                throw "The TXT reader has no visible '$name' command."
            }
            Invoke-Element $button
        }
```

At the start of the `else` branch, before `Wait-Name 'LibraryHeading'`:

```powershell
            # The view re-measures at a larger test font size on this signal.
            $remeasure = [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::AutoReset,
                "Local\DesktopGuides.Preview.TextRemeasure.$($process.Id)")
            $report.txtPosition = [ordered]@{}
```

In txt-ascii, replace `Assert-NoReaderCommands 'ASCII Map Guide'` with:

```powershell
            foreach ($name in 'Go to start', 'Previous page', 'Next page', 'Go to end') {
                $button = Find-ByName $name
                if (-not $button -or $button.Current.IsOffscreen) {
                    throw "ASCII Map Guide has no visible '$name' command."
                }
            }
```

(`Assert-NoReaderCommands` stays for "Game page after Back" and "Missing File Guide".)

After `$report.phases += 'txt-reopen'`, before the closing `}` of `else`:

```powershell
            # txt-commands: whole-row pages, Start and End.
            Back-ToTextGame
            Open-TextGuide 'Numbered Lines Guide'
            [void](Wait-Status 'Guide ready.')
            Wait-FirstTextRow
            Wait-TopLine 1 'Opening the guide'
            $visibleRows = Get-FullyVisibleRows
            Invoke-ReaderCommand 'Next page'
            $pageStep = (Wait-TopLineChange 1 'Next page') - 1
            if ($pageStep -lt 1 -or $pageStep -gt $visibleRows) {
                throw "Next page moved $pageStep rows; $visibleRows rows were fully visible."
            }
            Invoke-ReaderCommand 'Next page'
            Wait-TopLine (1 + 2 * $pageStep) 'A second Next page'
            Invoke-ReaderCommand 'Previous page'
            Wait-TopLine (1 + $pageStep) 'Previous page'
            Invoke-ReaderCommand 'Go to end'
            $endTop = Wait-TopLineChange (1 + $pageStep) 'Go to end'
            $last = @(Get-TextRows | Where-Object {
                -not $_.Current.IsOffscreen -and $_.Current.Name -like 'Line 0400 *' })
            if ($last.Count -ne 1) { throw 'Go to end did not show Line 0400.' }
            # Previous after End pages from the real top line.
            Invoke-ReaderCommand 'Previous page'
            Wait-TopLine ([Math]::Max(1, $endTop - $pageStep)) 'Previous page after Go to end'
            Invoke-ReaderCommand 'Go to start'
            Wait-TopLine 1 'Go to start'
            $report.txtPosition.pageStep = $pageStep
            $report.txtPosition.visibleRows = $visibleRows
            $report.txtPosition.endTopLine = $endTop
            $report.phases += 'txt-commands'

            # txt-resize: a shorter then restored window keeps the top line.
            Invoke-ReaderCommand 'Next page'
            $anchor = Wait-TopLineChange 1 'Next page before resizing'
            $window = $root.Current.BoundingRectangle
            Resize-ShellWindow ([int]$window.Width) ([int]($window.Height - 160))
            Wait-TopLine $anchor 'A shorter window'
            Resize-ShellWindow ([int]$window.Width) ([int]$window.Height)
            Wait-TopLine $anchor 'The restored window'
            $report.phases += 'txt-resize'

            # txt-remeasure (issue #29): larger text grows the rows, keeps the line.
            $heightBefore = (Get-TopRow).Current.BoundingRectangle.Height
            [void]$remeasure.Set()
            $deadline = (Get-Date).AddSeconds(10)
            do {
                Start-Sleep -Milliseconds 100
                $heightRatio = (Get-TopRow).Current.BoundingRectangle.Height / $heightBefore
            } while (($heightRatio -lt 1.3 -or $heightRatio -gt 1.7) -and (Get-Date) -lt $deadline)
            if ($heightRatio -lt 1.3 -or $heightRatio -gt 1.7) {
                throw "Re-measured rows were $([Math]::Round($heightRatio, 2))x as tall; expected about 1.5x."
            }
            Wait-TopLine $anchor 'Re-measuring the rows'
            $report.txtPosition.heightRatio = $heightRatio

            # The 2,048-column line widens with the text, so it still scrolls fully.
            Back-ToTextGame
            Open-TextGuide 'ASCII Map Guide'
            Assert-RowNames 'ASCII Map Guide (before re-measuring)' $asciiNames
            # Row rectangles are clipped to the viewport, so compare the
            # horizontal view size (viewport / extent) instead.
            $scroll = (Find-ById 'ReaderTextLines').GetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern)
            $viewBefore = $scroll.Current.HorizontalViewSize
            [void]$remeasure.Set()
            $deadline = (Get-Date).AddSeconds(10)
            do {
                Start-Sleep -Milliseconds 100
                $widthRatio = $viewBefore / $scroll.Current.HorizontalViewSize
            } while (($widthRatio -lt 1.3 -or $widthRatio -gt 1.7) -and (Get-Date) -lt $deadline)
            if ($widthRatio -lt 1.3 -or $widthRatio -gt 1.7) {
                throw "The re-measured extent was $([Math]::Round($widthRatio, 2))x as wide; expected about 1.5x."
            }
            $report.txtPosition.widthRatio = $widthRatio
            $report.phases += 'txt-remeasure'

            # txt-switch: a new guide starts at its first line with normal rows.
            Back-ToTextGame
            Open-TextGuide 'Numbered Lines Guide'
            Wait-FirstTextRow
            Wait-TopLine 1 'Reopening the Numbered guide'
            $switchRatio = (Get-TopRow).Current.BoundingRectangle.Height / $heightBefore
            if ([Math]::Abs($switchRatio - 1) -gt 0.1) {
                throw "A new TXT session kept the test text size (rows $([Math]::Round($switchRatio, 2))x)."
            }
            Invoke-ReaderCommand 'Next page'
            Wait-TopLine (1 + $pageStep) 'Next page after switching guides'
            $report.phases += 'txt-switch'
            $remeasure.Dispose()
```

- [ ] **Step 4: Parse-check the script**

Run: `/tmp/t083.sh "powershell -NoProfile -Command \"[void][System.Management.Automation.Language.Parser]::ParseFile('tools\p1\windows_shell_ui_smoke.ps1',[ref]\$null,[ref]\$e); if (\$e) { \$e; exit 1 }\""`
Expected: no output, exit 0. Then `LC_ALL=C grep -nP '[^\x00-\x7F]' tools/p1/*.ps1` prints nothing.

- [ ] **Step 5: Build the seed tool**

Run: `/tmp/t083.sh "dotnet build tools\p1\DesktopGuides.ShellSeed -c Release"`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add tests/fixtures/p1/txt-numbered.txt tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(p1): smoke TXT paging, resize and re-measure for T08.3"
```

---
### Task 5: CI run, host text-size check and evidence

**Files:**
- Create: `docs/p1/evidence/t08-3-txt-position/txt-reader-light.png`, `txt-reader-dark.png`

**Interfaces:**
- Consumes: the branch with Tasks 2-4 pushed; the CI `Windows` workflow
  (`pull_request` trigger) jobs `core-tests`, `reader-toolbar-ui` and
  `production-shell-ui`.
- Produces: a green CI run ID and the two screenshots, cited in Task 6 and
  the PR.

- [ ] **Step 1: Push and open the draft PR**

```bash
git push -u origin feat/p1-t08-3-txt-position
```

Open a draft PR against `main` (GitHub MCP `create_pull_request`, `draft: true`)
naming T08.3, prerequisites T08.2 (merged, PR #30) and T12.1 (merged, M0 PR #3),
the intended outcome, and `Closes #29`. Screenshots are added in Step 4.

- [ ] **Step 2: Wait for CI and read the result**

Run: `gh run list --branch feat/p1-t08-3-txt-position --limit 1` then
`gh run watch <id> --exit-status`.
Expected: all jobs pass; the `production-shell-ui` result JSON for
txt-reader light and dark lists phases `txt-commands`, `txt-resize`,
`txt-remeasure`, `txt-switch`, with `heightRatio` and `widthRatio` between
1.3 and 1.7. On a failure, use superpowers:systematic-debugging; reproduce on
`pcsx2-win` only by following the backup and cleanup rules in
`docs/p1/e2e-testing.md`.

- [ ] **Step 3: Check a real text-size change on the host**

Install the CI-built package on `pcsx2-win` through the interactive
scheduled task (backup and cleanup per `docs/p1/e2e-testing.md`), open
Numbered Lines Guide, page down, then change **Text size** as in Task 1 and
restore it. Expected: after each change the rows are not clipped and the
same line stays at the top. Record the observation (no screenshots of
personal settings) for Task 6. If the user has to change the setting by
hand, ask them.

- [ ] **Step 4: Commit the screenshots**

Download the run's `production-shell-ui` artifact (`gh run download <id>`).
The smoke saves the `txt-reader` screenshot next to the `txt-reader-light`
and `txt-reader-dark` result JSON. Check both show the four new commands,
then copy them to `docs/p1/evidence/t08-3-txt-position/` as
`txt-reader-light.png` and `txt-reader-dark.png` (the T08.2 names), and commit:

```bash
git add docs/p1/evidence/t08-3-txt-position
git commit -m "docs(p1): add T08.3 TXT reader screenshots"
```

Reference them in the PR body as absolute blob URLs with `?raw=true`.

---

### Task 6: Docs

**Files:**
- Modify: `docs/p1/e2e-testing.md:252` (TXT reader row) and add a row for the T08.3 checks
- Modify: `docs/p1-technical-design.md:645` (T08.3 in §7)
- Modify: `docs/p1/implementation-plan.md:766` (T08.3 row)
- Modify: `docs/p1/t08-3-txt-position-design.md` (status and rulings)

**Interfaces:**
- Consumes: Task 1's answer, Task 5's run ID and host observation.
- Produces: docs that match what shipped.

- [ ] **Step 1: Update the e2e TXT reader row**

Change "six guides" to "seven guides", add `Numbered Lines Guide`
(`txt-numbered`) to the list, and replace "with no placeholder or reader
commands" with "with no placeholder, and shows Go to start, Previous page,
Next page and Go to end". Keep the traceability cell and add `T08.3`.

- [ ] **Step 2: Add the T08.3 e2e row after it**

```markdown
| TXT position | In light and dark, on Numbered Lines Guide: Next page moves the top line forward by at least one and at most the fully visible rows, a second Next page moves it the same amount again, and Previous page brings it back one page; Go to end shows `Line 0400`, Previous page then pages up from the real top line, and Go to start shows `Line 0001`. After a page down, a window 160 px shorter and then its original size keep the same top line. Signalling `Local\DesktopGuides.Preview.TextRemeasure.{pid}` re-measures at a 1.5x test font size: rows grow 1.3-1.7x with the same top line, and on ASCII Map Guide the horizontal extent grows 1.3-1.7x. Reopening Numbered Lines Guide starts at `Line 0001` with normal row heights and pages again. Page step, visible rows, end top line and the two ratios go to the result JSON. | T08.3, TR08.3 |
```

- [ ] **Step 3: Update the design and plan docs**

- `p1-technical-design.md` T08.3 line: add the `PageEdge` contract
  addition (`ReaderEdge`, `PageEdgeAction`, gated by `PageNavigation`), the
  text-size trigger confirmed in Task 1 (`UISettings.TextScaleFactorChanged`
  or the recorded fallback), and "closes issue #29".
- `implementation-plan.md` T08.3 row: status "In review (PR #<n>)", the CI
  run ID, and the host text-size observation.
- Design doc: set Status to "implemented in PR #<n>" and add a
  "Implementation notes" section with the three rulings from this plan's
  header (ScrollIntoView, Resize-ShellWindow, PageBy from the real top) plus
  any ledger rulings.

- [ ] **Step 4: Commit and push**

```bash
git add docs
git commit -m "docs(p1): record T08.3 TXT position verification"
git push
```

Update the PR body with the final run ID and screenshots; mark it ready only
after CI is green on the final commit.
