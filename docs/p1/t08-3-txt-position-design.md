# T08.3 TXT commands and position capture/restore design

Status: implemented in PR #31; in review. Design approved in brainstorming
on 2 October 2026; implementation planned in
[t08-3-txt-position-plan.md](t08-3-txt-position-plan.md) and verified in CI
run [36957516416](https://github.com/ilya-slalom/desktop-guides/actions/runs/36957516416)
(see the [verification record](#t083-verification-record)).
Prerequisites: T08.2 is merged (PR #30, merge commit `1294e4b`); T12.1 is
merged (M0, PR #3).

## Intent

A TXT guide in the Reader can be paged and jumped to its start or end, and
its reading position can be captured and restored as a normalized character
offset with context rather than a pixel coordinate (TR08.3):

- **Commands.** Previous page, Next page, Go to start and Go to end appear in
  the Reader toolbar for TXT guides and run through `IReaderSession`.
- **Capture.** `GetLocationAsync` returns a T12.1 `ReaderLocation` whose
  payload is the first visible line's start offset plus the text that
  follows it. The locator serializes through `ReaderLocationCodec`.
- **Restore.** `RestoreLocationAsync` returns the exact line for unchanged
  content, the nearest matching context for changed content, then a labeled
  approximate fraction, and otherwise reports the location unavailable.
- **Layout changes keep the line.** A window resize or a Windows text-size
  change keeps the same top line. The text-size change also re-measures the
  rows, which closes issue #29 (rows clipped until the guide is reopened).

Traces: the T08.3 row of [implementation-plan.md](implementation-plan.md)
(TR08.3), [work-breakdown.md](../work-breakdown.md) S08 and T08.3,
[p1-technical-design.md](../p1-technical-design.md) §3 and §7 S08, and the
locator contract in `src/DesktopGuides.Core/Reading/ReaderLocation.cs`.

Decisions made during brainstorming:

- **Restore is exercised by layout changes, not persistence.** Saving and
  resuming across a reopen is T12.2's `ProgressCoordinator`; adapters never
  write SQLite. T08.3 provides the capture/restore API, Core tests for it,
  and the in-view restore after a resize or text-size change.
- **Commands get toolbar buttons; keys wait for T16.1.** No keyboard
  accelerators are mapped in T08.3.
- **Resize plus issue #29.** The Windows text-size setting is the only font
  change a running TXT reader sees before T14.1 adds a text scale, so it is
  the font-change trigger for TR08.3.
- **Approach A: a new Core `TextLocator`.** It maps a `TextGuideDocument`
  straight to the T12.1 `TextPosition(CharacterOffset, Context)` with a
  forward-anchored context, so the merged contract and codec stay
  unchanged. The P0 `TextLocation`, `TextGuideDocument.Capture` and
  `TextGuideDocument.Restore` stay as they are because the P0 `TextProbe`
  regression lane still uses them.

Rejected approaches:

- **B: reuse the P0 capture/restore and translate in the session.** Its
  32-character centred quote often repeats in guides (separator lines,
  repeated headers), its context offset can't be carried by `TextPosition`
  without guessing, and it keeps P0 and production coupled.
- **C: add `ContextOffset` to `TextPosition`.** The most precise model, but
  it changes a merged v1 contract and codec, and TR08.3 needs only line
  precision ("within one visible logical line").

## Core: `TextLocator`

New static class in `src/DesktopGuides.Core/Text/TextLocator.cs`.

### Capture

`ReaderLocation Capture(TextGuideDocument document, int line)`

- `line` is clamped to `[0, LineStarts.Count - 1]`. The character offset is
  `LineStarts[line]`, so a captured offset is always a line start.
- `Context` is `Text[offset..]`, at most 128 characters (the codec allows
  160). It stops before the first `\0`, which the codec rejects, and drops
  a trailing high surrogate, which `Utf8JsonWriter` rejects.
- `ContentSha256` is `document.ContentSha256.ToLowerInvariant()`.
  `TextGuideDocument` produces upper-case hex, but the codec accepts only
  lower-case hex; without this, serializing a captured TXT location throws.
- `EstimatedFraction` is `offset / Text.Length`, or 0 for an empty guide.
- `SchemaVersion` is `ReaderLocationCodec.CurrentVersion`; format is `Txt`.

### Restore

`TextRestore Restore(TextGuideDocument document, ReaderLocation location)`
returns `TextRestore(int Line, RestoreOutcome Outcome)`. Steps run in order
and the first that applies wins:

1. **Unavailable** when the format isn't `Txt`, the payload isn't a
   `TextPosition`, or the schema version isn't current.
2. **Exact** when the hash matches the document's (ordinal, ignoring case)
   and `0 <= CharacterOffset <= Text.Length`. The line is
   `document.LineAtOffset(CharacterOffset)`, so a mid-line offset lands on
   its own line.
3. **Context** when `Context` is non-empty and has a single nearest ordinal
   match to the saved offset. Every match is scanned (`IndexOf` from one
   past the previous match); the line is the one containing the nearest
   match. Two matches at the same distance are a tie and fall through.
4. **Approximate** when `EstimatedFraction` is present. The line contains
   offset `round(fraction * Text.Length)`. Reason: "The guide changed, so
   this is an approximate position."
5. **Unavailable** otherwise. Reason: "This reading position can't be used
   with this guide."

Consequences:

- An offset beyond the text on unchanged content (only possible from
  corrupt data) skips Exact and tries Context, then Approximate.
- A location from another guide is never Exact, because its hash differs.
- Exact and Context outcomes carry no reason.
- Every returned line is within `[0, LineStarts.Count - 1]`, including for
  an empty guide (line 0).

## Contract: Start/End command

In `src/DesktopGuides.Core/Reading/ReaderContract.cs`:

- `ReaderCommand.PageEdge`, `enum ReaderEdge { Start, End }` and
  `sealed record PageEdgeAction(ReaderEdge Edge) : ReaderAction(ReaderCommand.PageEdge)`.
- `ReaderCommandPolicy` requires `ReaderCapabilities.PageNavigation` for
  `PageEdge`. No new capability flag; `PageTurn` and `PageTurnAction` are
  unchanged. A later PDF reader inherits Start/End with page navigation.

## Production: session, view and toolbar

### `TextReaderSession`

- **Capabilities:** `Scroll | PageNavigation`.
- **`PageTurnAction(delta)`:** moves the top line by `delta` pages, where a
  page is the number of whole rows that fit in the viewport (at least 1).
  The result is clamped to `[0, last top line]`.
- **`PageEdgeAction`:** Start shows line 0 at the top. End scrolls to the
  bottom of the list, so the last line is visible.
- **`GetLocationAsync`:** `TextLocator.Capture(document, View.FirstVisibleIndex)`.
- **`RestoreLocationAsync`:** `TextLocator.Restore`, then
  `View.ScrollToLine(line)`, then returns the outcome. Before the view has
  loaded, the line is kept as pending and applied in `OnLoaded` after
  `ItemsSource` is set. Cancellation before the scroll, or disposal, drops
  the pending line.
- **`LocationChanged`:** raised after a final (non-intermediate)
  `ViewChanged` that changes the top line; not raised while the top line
  stays the same. This is T12.2's movement signal.
- Unsupported actions still throw `NotSupportedException`.

### `TextReaderView`

- **`ScrollToLine(int line)`:** clamps the line and calls
  `ChangeView(null, line * rowHeight, null, disableAnimation: true)`. With
  fixed-height rows this is exact.
- **`PageBy(int delta)`** and **`ScrollToEdge(ReaderEdge edge)`** implement
  the commands above on top of `ScrollToLine` and the ScrollViewer extent.
- **Anchor tracking.** `anchorLine` follows `FirstVisibleIndex` after each
  final `ViewChanged`. While the view applies its own restore scroll, a flag
  holds the anchor until that scroll's final `ViewChanged`, so an
  intermediate layout can't overwrite it.
- **Resize.** On `SizeChanged`, when `FirstVisibleIndex != anchorLine`, the
  view calls `ScrollToLine(anchorLine)`. This covers the ListView clamping
  its offset when a taller window reaches the end of the guide.
- **Text-size change (issue #29, option 1).** `OnLoaded` subscribes to
  `UISettings.TextScaleFactorChanged`; `Clear` unsubscribes. The handler runs
  off the UI thread and posts `Remeasure()` through `DispatcherQueue`. A
  post that arrives after `Clear` does nothing. `Remeasure()`:
  1. measures `CellProbe` again and updates `rowWidth` and `rowHeight`;
  2. updates `MinWidth` and `Height` on realized rows (`ItemsPanelRoot`
     children); rows realized later pick up the new sizes in
     `LineContainerChanging`;
  3. scales the horizontal offset by the change in row width;
  4. calls `ScrollToLine(anchorLine)`.
- **Feasibility check first.** Issue #29 asks whether WinUI 3 desktop apps
  receive `TextScaleFactorChanged` and re-scale text while running. The
  plan's first task checks this on `pcsx2-win` with a throwaway build and
  restores the original text-size setting afterwards. If the event doesn't
  arrive, the fallback trigger is `XamlRoot.Changed` or window activation,
  and the choice is recorded here and in
  [p1-technical-design.md](../p1-technical-design.md).
- **Test hook.** CI can't change a per-user accessibility setting, so the
  smoke signals a named event `Local\DesktopGuides.Preview.TextRemeasure.{pid}`,
  following the T08.2 `TextLoad` gate pattern. On that signal the view sets a
  test font-size multiplier of 1.5 and applies it to `CellProbe` only; the
  probe's `SizeChanged` then runs `Remeasure()`, which applies it to the row
  text. This exercises app-owned code (the trigger, re-measure, row sizes,
  anchor restore), not Windows text scaling.

### `ReaderToolbar`

- The existing **Previous page** and **Next page** buttons appear for TXT;
  their labels stay shared with PDF.
- New primary buttons **Go to start** (`AutomationId` `ReaderStart`) and
  **Go to end** (`ReaderEnd`), shown when `PageEdge` is visible.
- Failures use the existing `CommandFailed` path, for example "Could not go
  to the start: …".

## Testing

### Core xUnit

`TextLocatorTests` (new) and additions to `ReaderContractTests`:

- Unchanged content: Exact on the same line; a mid-line offset snaps to its
  line; an offset beyond the text falls back to Context.
- Changed bytes with a unique quote: Context on the moved line.
- Repeated quotes: the nearest match to the saved offset wins; a tie at
  equal distance gives Approximate.
- No match: Approximate with its reason; no match and no fraction:
  Unavailable with its reason.
- Wrong format, payload or schema version: Unavailable.
- Guide switching: a location captured in guide A and restored in guide B
  is never Exact; it is Context only when the quote occurs in B, otherwise
  Approximate.
- Capture emits a lower-case hash from a document with an upper-case hash,
  and the result round-trips through `ReaderLocationCodec.Serialize` and
  `Deserialize`.
- Context truncation at `\0` and before a trailing high surrogate; the 128
  character limit; empty and one-line guides.
- Line independence: the restored line depends only on the offset, so it is
  the same for any row height (the Core half of the font-change check).
- `PageEdge` is visible only with `PageNavigation`, and the policy rejects it
  otherwise.

### Installed smoke

CI `production-shell-ui`, txt-reader block of
`tools/p1/windows_shell_ui_smoke.ps1`. Every check reads the top line as the
UIA name of the first on-screen `ListItem` in `ReaderTextLines`.

- **`txt-commands` (txt-long):** the four buttons are visible for TXT; Next
  page moves the top line forward by a whole number of rows and Previous page
  brings it back; Go to end shows the last line; Go to start shows line 1.
- **`txt-resize`:** in a window about three rows tall, Go to end and
  Previous page; restoring the window clamps the top line, and shrinking it
  again brings the kept line back.
- **`txt-remeasure`:** after paging down, signal the TextRemeasure event;
  row height (bounding rectangle) grows by about 1.5×, the top line is
  unchanged, and the 2,048-column line still scrolls fully into view.
- **Guide switching:** page down in guide A, go Back, open guide B; B starts
  at its line 1 and its paging works.
- Light and dark screenshots with the new buttons go to
  `docs/p1/evidence/t08-3-txt-position/`.
- `docs/p1/e2e-testing.md` gains rows for the new checks.

`pcsx2-win` is used only for the text-size feasibility check and when CI
fails, following the backup and cleanup rules in
[e2e-testing.md](e2e-testing.md).

## Docs

- Update the T08.3 lines in [p1-technical-design.md](../p1-technical-design.md)
  and [implementation-plan.md](implementation-plan.md) with the `PageEdge`
  contract addition, the text-size trigger once checked, and the issue #29
  closure.
- TR08.3 traceability in [work-breakdown.md](../work-breakdown.md) is
  unchanged.
- The PR says "Closes #29".

## Out of scope

- Saving and resuming across a reopen, and the generation token for stale
  writes: T12.2.
- Keyboard accelerators for paging: T16.1.
- Text scale and theme settings, and restore across them: T14.1–T14.3.
- Announcing an Approximate or Unavailable restore in the UI: T12.2/T14.3.
  Nothing in T08.3 produces one at runtime.
- Tall fallback glyphs and a cap for very long lines: deferred T08.2 minors.

## Implementation notes

Where the build differs from the design above:

- **Scrolling uses `ListView.ScrollIntoView(item, Leading)`**, not
  `ChangeView` to `line * rowHeight`. It lands on the bound row without
  depending on the list's header and padding offsets.
- **The resize check uses `Resize-ShellWindow`**, the shell smoke's
  existing helper, not UIA `TransformPattern`. The window shrinks by half
  the text area's height: on the CI desktop the list is about 180 px tall,
  so a fixed 160 px shrink left no rows on screen.
- **`PageBy` counts from `FirstVisibleIndex`**, not the anchor. After Go to
  end the anchor is the last line while the top line is about a page above
  it, so Previous page pages up from the line the reader sees.
- **Text-size trigger.** `UISettings.TextScaleFactorChanged` reaches a
  WinUI 3 desktop process: a throwaway listener on `pcsx2-win` saw 1.25
  after the Settings slider moved and 1 after it was restored. The final
  review found that a handler posting `Remeasure` isn't ordered after XAML's
  own re-layout, so it could measure the probe at the old size. The view
  now re-measures when `CellProbe` raises `SizeChanged` with a new height,
  which happens only after the new size is in effect. The test hook now
  changes only the probe's font size, so CI exercises that same path.
- **The normal-mode shell smoke** still asserted that the TXT reader had no
  commands (a T08.2 check); it now requires the four TXT commands.

## T08.3 verification record

- **Unit tests.** On `pcsx2-win`, Core passed 397/397 (371 before, plus
  24 `TextLocatorTests` cases and 2 contract tests). The Production,
  ReaderToolbarSmoke and ShellSeed builds had 0 warnings.
- **Installed.** CI run
  [36957516416](https://github.com/ilya-slalom/desktop-guides/actions/runs/36957516416)
  on `8a231a0` passed every job:
  - `reader-toolbar-ui`: Go to start and Go to end dispatch
    `PageEdgeAction(Start)` and `PageEdgeAction(End)`.
  - `production-shell-ui`, `txt-reader` in light and dark: the T08.2
    phases plus `txt-commands`, `txt-resize`, `txt-remeasure` and
    `txt-switch`.

    | Measure | Light | Dark | Expected |
    |---|---|---|---|
    | Page step (rows) | 8 | 8 | 1 to the fully visible rows |
    | Fully visible rows | 9 | 9 | |
    | Top line after Go to end | 392 | 392 | `Line 0400` on screen |
    | Row height after re-measure | 1.47x | 1.47x | 1.3-1.7x |
    | ASCII horizontal extent after re-measure | 1.50x | 1.50x | 1.3-1.7x |

- **Final-review fixes.** On `a27b5df`, run
  [36967638178](https://github.com/ilya-slalom/desktop-guides/actions/runs/36967638178)
  passed `production-shell-ui` in light and dark with the same numbers as
  above. In the new `txt-resize`, the kept line 394 clamped to 389 in the
  taller window and came back in the shorter one. Before the fixes, two
  throwaway runs failed as intended:
  [36966460925](https://github.com/ilya-slalom/desktop-guides/actions/runs/36966460925)
  (the probe-only hook with the `UISettings` trigger: rows stayed 1x) and
  [36966463838](https://github.com/ilya-slalom/desktop-guides/actions/runs/36966463838)
  (no resize anchor: `txt-resize` failed).
- **CI fixes.** Run
  [36955267358](https://github.com/ilya-slalom/desktop-guides/actions/runs/36955267358)
  failed on the T08.2 no-commands check in normal mode (fixed in
  `ae7cc40`). Run
  [36956267049](https://github.com/ilya-slalom/desktop-guides/actions/runs/36956267049)
  failed `txt-resize` because the 160 px shrink emptied the list (fixed in
  `8a231a0`).
- **Not seen.** A real Windows text-size change with the installed app was
  not run: `pcsx2-win` is used only for the feasibility check and CI
  failures. CI covers the re-measure through the `TextRemeasure` test hook,
  and the spike showed the system event arrives. The step between them,
  XAML re-laying out the probe at a new system text size, is reviewed, not
  observed.
- **Evidence.** ASCII Map Guide with the TXT commands:
  [light](evidence/t08-3-txt-position/txt-reader-light.png) and
  [dark](evidence/t08-3-txt-position/txt-reader-dark.png).
