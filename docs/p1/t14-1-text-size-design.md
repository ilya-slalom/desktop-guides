# T14.1 text size design

Status: designed.
Prerequisites: T03.2 (versioned migrations, PR #4), T08.2 (TXT view),
T09.1 (HTML adapter) and T11.3 (the capability toolbar) are merged. T09.2
(PR #49) already applies a guide's stored HTML text scale at open.

## Intent

A TXT or HTML guide can't change its text size today. The storage is in
place: the `ReaderPreferences` table holds a per-guide `TextScale`,
bounded by the schema to 0.75–2.0, and the repository reads and saves it.
The toolbar has hidden *Smaller text* and *Larger text* buttons. But no
session declares the `TextSize` capability, so the buttons never show,
nothing handles the action, and nothing saves a change.

T14.1 adds bounded per-guide text size for TXT and HTML guides, saved and
restored on reopen. Success means:

- In a TXT or HTML guide, the user makes the text smaller or larger in
  fixed steps from 75% to 200%, from the toolbar or the keyboard, and sees
  the current size next to the buttons. Reset returns to 100%.
- The size applies at once, is saved for that guide only, and survives a
  relaunch. Another guide keeps its own size.
- A TXT guide keeps its fixed-width layout at every size: runs of spaces
  and columns stay intact, and lines don't wrap.
- A PDF guide keeps its own zoom and never writes a text size.
- A failed save puts the stored size back and explains the error.

Traces: the T14.1 row of [implementation-plan.md](implementation-plan.md)
(TR14.1), [work-breakdown.md](../work-breakdown.md) S14 T14.1, and
[p1-technical-design.md](../p1-technical-design.md) §8 S14 T14.1 ("Keep
reader text-size commands native").

## Decisions

Decisions made during brainstorming:

- **Fixed steps.** The size moves through 75, 90, 100, 110, 125, 150, 175
  and 200%, as `PdfZoom` moves through its steps. The value shown and
  stored is always exact, and repeated steps don't drift. Multiplying by
  0.9 and 1.1 was rejected: it gives 1.21, 1.331 and so on, shown rounded.
- **A label and a reset.** A percent label sits between *Smaller text* and
  *Larger text*. *Reset text size* is in the toolbar's overflow. The keys
  are Ctrl+Minus, Ctrl+Plus (and Ctrl+=) and Ctrl+0. Rejected: a label
  with no reset, and a status-bar message only.
- **The shell owns the steps.** `TextSizeAction` carries the target scale,
  not a factor. The sessions apply a scale; they don't step.
- **HTML keeps T09.2's CSS `zoom`.** The style's zoom already scales the
  guide, images included; T14.1 only changes the scale it's given.
- **Keeping the reading place is T14.3.** TXT already keeps its top line
  when its rows re-measure. HTML's place across a size change is T14.3's
  capture and restore.
- **Tests assert the app's choices.** The installed checks read the label,
  the command states, the stored row and the readers' diagnostics. They
  don't measure rendered pixels.

## Core: `TextSizeSteps`

`DesktopGuides.Core/Reading/TextSizeSteps.cs` is pure and unit-tested:

```csharp
public static class TextSizeSteps
{
    public const double Default = 1.0;
    public static IReadOnlyList<double> Steps { get; } // 0.75 … 2.0

    public static double Normalize(double? stored);     // missing/invalid → 1.0
    public static double Larger(double scale);          // next step up, or unchanged
    public static double Smaller(double scale);         // next step down, or unchanged
    public static bool CanLarger(double scale);
    public static bool CanSmaller(double scale);
    public static string Label(double scale);           // "110%"
    public static string Status(double scale);          // "Text size 110%."
    public static string SaveFailed(string message);    // "Could not save the text size: <message>"
}
```

- `Normalize` turns a missing, non-finite or out-of-range stored value
  into 1.0. A value inside the range but between steps (an older or
  hand-edited database) is kept.
- `Larger` and `Smaller` move to the nearest step strictly above or below,
  with a small tolerance so that a value a hair from a step isn't offered
  as a step that changes nothing. At an end they return the value
  unchanged. A between-steps value is labelled with its rounded percent.
- `Label` uses the invariant culture, as `PdfZoom.Label` does.

`HtmlReaderStyle.ClampScale` stays as the CSS guard.

## Sessions

- `ReaderCommand.TextSize` stays; `TextSizeAction(double Factor)` becomes
  `TextSizeAction(double Scale)`. A scale outside the steps' range is
  rejected with `ArgumentOutOfRangeException`.
- `TextReaderSession` and `HtmlReaderSession` declare
  `ReaderCapabilities.TextSize`. `PdfReaderSession` doesn't, so a PDF guide
  shows only its zoom.
- **TXT:** `TextReaderView` gains a `TextScale` property. Setting it sets
  `fontScale` and runs the existing `Remeasure()`: the cell probe takes the
  new size, rows keep Consolas, their measured `MinWidth` and no wrapping,
  and the top line stays in place. The initial scale is set before the
  first measure, so a stored size applies at open without a second layout.
  The test remeasure hook keeps its own scale.
- **HTML:** `ExecuteAsync(TextSizeAction)` calls the existing appearance
  write with the current theme and the new scale.
- Neither session writes SQLite.

## Shell

### Toolbar

`ReaderToolbar` shows *Smaller text* | `TextSizeValue` | *Larger text*.
`TextSizeValue` is a `TextBlock` whose text is `TextSizeSteps.Label`. Its
automation name is `Text size`, and it is a live region, so a change is
announced. *Reset text size* (`ResetTextSize`) goes in the overflow, shown
with the other two and disabled at 100%.

The keys share the zoom bindings: Ctrl+Plus, Ctrl+= and Ctrl+Shift+= are
*Larger text*, Ctrl+Minus is *Smaller text*, and Ctrl+0 is *Reset text
size*. A session never shows both text size and zoom, so `CommandFor`
returns whichever is visible. The tooltips and `AcceleratorKey` match PDF
zoom.

At 75% *Smaller text* is disabled and at 200% *Larger text* is disabled.
A focused button that becomes disabled moves focus to its partner, as
`SetZoomAvailability` does.

### Changing the size

`ShellWindow.TextSize.cs` owns the current guide's scale and its saves:

1. At open, the shell reads `GetReaderPreferencesAsync` once, normalizes
   it, and passes it to the session: TXT through the view's `TextScale`,
   HTML through the `ReaderAppearance` it already sends. A failed read
   gives 100%: the size never blocks reading.
2. A command computes the target with `TextSizeSteps`, applies it at once
   through `TextSizeAction`, updates the label and the command states, and
   calls `SaveReaderPreferencesAsync(guideId, scale)`.
3. Saves are serialized. Only the latest pending one counts, as with the
   theme. On success the status bar shows `Text size 110%.` as a transient
   status.
4. On failure, when no newer change is pending, the shell applies the
   stored size again and shows the save-failure text as an error status.
5. A save started for one guide carries that guide's id. A save that
   finishes after the Reader has moved to another guide doesn't touch the
   new guide's label, size or status.

A size change isn't reader movement: it doesn't save a reading place.
Restoring the place across a size change is T14.3.

## Installed checks

A new `text-size` scenario group runs after `theme` and before `import`.
The CI matrix puts it in the `core` shard (`core,txt,text-size,game-actions`).
Its guides are `txt-ascii` (A), `txt-utf8` (B) and `html-static`.
`txt-ascii` line 6 reads `Columns:   one     two`.

| Mode | Checks |
| --- | --- |
| `text-size-steps` | Keyboard only, on A: the label reads `100%`. Ctrl+Plus steps through `110%`, `125%`, `150%`, `175%` and `200%`; *Larger text* is then disabled and focus is on *Smaller text*. Ctrl+0 returns to `100%`. Ctrl+Minus steps to `90%` and `75%`; *Smaller text* is disabled. Each status reads `Text size <label>.` |
| `text-size-whitespace` | On A at 150%: line 6's automation name is still `Columns:   one     two`, the diagnostics report scale 1.5 and a row width 1.5 times the 100% width (within one cell), and the first visible line hasn't changed. |
| `text-size-restart` | A is set to 125% and `html-static` to 90%; B is opened and left alone. After a relaunch, A's label reads `125%`, `html-static` reads `90%` and its appearance diagnostics report scale 0.9, and B reads `100%`. The stored `TextScale` values are 1.25, 0.9 and null. |
| `text-size-pdf` | A PDF guide shows *Zoom in* and *Zoom out* and no text-size commands. After Ctrl+Plus zooms, its stored `TextScale` is still null. |
| `text-size-error` | With the write lock held, *Larger text* on A shows the save-failure text, and the label and diagnostics return to the stored size. After the lock is released, a retry saves. |

The TXT diagnostics are a test gate like the HTML appearance diagnostics:
the view writes its applied scale and measured row width to the cache's
diagnostics folder only when the gate is open.

Runs: the `text-size` group on CI with `shell-scope=text-size`, then the
`txt` and `html` groups, then a full run. Host runs on `pcsx2-win` follow
[e2e-testing.md](e2e-testing.md) only to debug a CI failure. Evidence goes
in `docs/p1/evidence/t14-1-text-size/`.

## Docs

In the implementing branch:

- the implementation notes and verification in this file;
- the `text-size` group, its `-TextSizeOnly` switch and the `core` shard in
  [e2e-testing.md](e2e-testing.md), and `text-size` in the workflow's
  `shell-scope` choices;
- the T14.1 lines in [implementation-plan.md](implementation-plan.md) and
  [work-breakdown.md](../work-breakdown.md);
- the S14 T14.1 entry in [p1-technical-design.md](../p1-technical-design.md),
  with the fixed steps.

## Risks

- **Ctrl+Plus and Ctrl+Minus in WebView2.** The HTML view already turns
  off browser zoom (`IsZoomControlEnabled`) and browser accelerator keys,
  so the page can't zoom itself. But a key pressed while the page has focus
  may not reach the XAML toolbar. The plan checks the keys with focus in
  the page. If they don't arrive, the buttons and the keys from the shell
  still work, and the page-focus case is recorded here, not worked around
  with page script.
- **HTML place across a size change.** CSS `zoom` reflows the page, so the
  view can land elsewhere until T14.3. The status doesn't claim the place
  was kept.
- **TXT re-measure at large sizes.** At 200% a long line's row is twice as
  wide; the horizontal offset scales with it, as it does for a system text
  size change.
