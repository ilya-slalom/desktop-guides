# T14.3 appearance restore design

Status: designed; not yet implemented.
Prerequisites: T08.3 (TXT locator), T09.3 (HTML locator), T10.3 (PDF
locator), T14.1 (text size, PR #51) and T14.2 (theme setting) are merged.

## Intent

A text-size step reflows an HTML guide: T09.2's CSS `zoom` changes every
line's height, so the reader lands somewhere else. The session's 500 ms
tracker then reads that scroll as the reader moving, captures the new top
and raises `LocationChanged`, and progress saves the drifted place. T14.1
left this open on purpose (its Risks: "The status doesn't claim the place
was kept").

T14.3 keeps the reading place across appearance changes. Success means:

- After any text-size step, a TXT guide shows the same top line and an
  HTML guide shows the same context at the top. A PDF guide keeps its page
  and fraction.
- A theme change (the T14.2 setting, the Windows theme, high contrast)
  leaves the place where it was in every reader.
- Progress never saves a place that moved only because the appearance
  changed.
- When HTML can only come back by fraction, the user is told, briefly,
  that the place may have shifted.

Traces: the T14.3 row of [implementation-plan.md](implementation-plan.md)
(TR14.1, TR14.2), [work-breakdown.md](../work-breakdown.md) S14 T14.3, and
[p1-technical-design.md](../p1-technical-design.md) §8 S14 T14.3 ("Capture
a guide locator before a size/theme transition, apply appearance, wait for
layout, and restore through the same adapter").

## Decisions

Decisions made during brainstorming:

- **Each adapter keeps its own place.** "Restore through the same adapter"
  is met inside each session, not by a shell-driven capture and restore.
  The HTML session pauses its tracker, applies the style and scrolls back
  as one step, so no tick can save a drifted place in between. A
  shell-driven `GetLocationAsync` → `ApplyAppearanceAsync` →
  `RestoreLocationAsync` was rejected: the shell can't stop the tracker
  between the calls, can't tell when layout has settled, and would replay
  open-time restore work (Find, the changed-content check). A
  `ProgressCoordinator` suspend window on top was rejected as redundant.
- **TXT and PDF need no code.** TXT's `Remeasure` already ends with
  `ScrollToLine(anchorLine)`, and a theme doesn't change its layout. PDF
  has no `TextSize` capability and its preview has no theme; zoom already
  keeps page and fraction (T10.2).
- **Only a scale change restores.** The HTML style's theme part sets
  colors only; layout and fonts stay as authored, and `zoom` is the only
  layout property. A theme-only change can't reflow, so it skips the
  restore and avoids a pixel snap to the top character's box.
- **A transient notice.** A fraction fallback shows an informational
  status that dismisses itself and is replaced by the next one. Repeated
  steps would pile up a sticky notice. Diagnostics-only was rejected: the
  fallback must be announced.
- **Tests assert the app's choices.** The installed checks read the top
  line, the locator, the outcome diagnostics, the status text and the
  progress counts. They don't re-test Chromium's layout.

## Contract

`ReaderContract.cs` gains:

```csharp
public sealed class AppearanceRestoredEventArgs(RestoreOutcome outcome) : EventArgs
{
    public RestoreOutcome Outcome { get; } = outcome;
}

// IReaderSession
event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored;
```

TXT and PDF never raise it and declare it `{ add { } remove { } }`, as they
do `CapabilitiesChanged`. The test fakes in `ReaderContractTests`,
`ProgressCoordinatorTests` and `SqliteLibraryRepositoryTests` get the same
stub.

## Core rules

In `HtmlLocationRules`, pure and unit-tested:

- `NeedsAppearanceRestore(ReaderAppearance? applied, ReaderAppearance next)`
  is true only when `HtmlReaderStyle.ClampScale` of the two scales
  differs. A first write (`applied` null) is false: the open's own restore
  owns the place then. A theme-only change, or two scales that clamp to
  the same value, is false.
- `AppearanceOutcome(HtmlRestoreTarget? landed)` maps the target
  `ScrollToTargetAsync` returned: `Exact` or `Context` step → `Exact`;
  `Fraction` → `Approximate` with a null reason; null → `Unavailable` with
  `UnavailableReason`. The content can't change within a session, so no
  changed-content reason applies.

## HTML session

`ApplyAppearanceAsync` stores the appearance as today. When it starts the
write loop (none is running), it:

1. **Takes the pre-change point.** It records the appearance applied so far,
   then, unless `resizing` or `restoring` is set, takes a fresh
   `CaptureAsync`. A capture that differs from `current` is a real move the
   tracker hadn't polled yet: it becomes `current` and raises
   `LocationChanged`. A failed capture keeps `current`.
2. **Pauses tracking.** It bumps `generation`, stops the tracker and sets
   a `restyling` flag. `GetLocationAsync` skips its fresh capture while the
   flag is set, as it does for `resizing` and `restoring`; a tick already in
   flight sees the new generation and drops its result.
3. **Writes.** The existing loop runs unchanged. Steps that arrive during it
   (100 → 110 → 125 within one write) are picked up by the loop, so the
   restore below runs once, after the last write, to the original point.
4. **Restores, when `NeedsAppearanceRestore(before, applied)`.** Unless
   `resizing` or `restoring` is set, it scrolls to `current` with
   `ScrollToTargetAsync` (Exact step when the point has a quote, else
   Fraction), as `ReapplyAsync` does. The offset script measures the target
   character's box, which makes Chromium lay out at the new zoom first: that
   is the wait for layout, with no timer.
5. **Re-baselines.** It reads the scroll as the new `lastScroll`, clears
   `restyling` and restarts the tracker unless a resize settle is pending.
   Only when step 4 scrolled does it record the outcome for diagnostics and
   raise `AppearanceRestored` with `AppearanceOutcome(landed)`; a skipped
   step 4 raises nothing. A throwing handler is swallowed, as in
   `RaiseLocationChanged`.

It doesn't capture after the restore, so `current` stays the pre-change
point. The locator's offset doesn't depend on zoom, so progress keeps the
true place even when the view came back only by fraction, and the next tick
sees no move from the new baseline.

Overlaps:

- **An open-time restore is running.** Step 4 is skipped. `RestoreAsync`
  sees the generation change and scrolls to its own target again, as it
  does for a resize.
- **A resize settle is pending.** Step 4 is skipped and the tracker stays
  stopped; `ReapplyAsync` later scrolls to the same `current` and restarts
  it.
- **A failed write.** The loop returns as today, with the page as
  authored; step 4 still compares against what was applied, so nothing is
  restored and no event is raised, and tracking resumes.
- **Disposal.** Every step checks `disposed` as the resize path does; a
  disposed session raises nothing.

Test diagnostics: `html-position-<pid>.json` gains `appearanceKind` and
`appearanceStep` next to `kind` and `step`, written behind the existing
position gate.

## Shell

`ShellWindow` subscribes to `AppearanceRestored` when it attaches a reader
session and unsubscribes when it detaches one, next to the toolbar's
`CapabilitiesChanged`. An event from a session other than the current
`readerSession` is ignored.

- `Approximate` or `Unavailable`: a transient status,
  `TextSizeSteps.ShiftedStatus(scale)` = "Text size 150%. Your place may
  have shifted." It extends T14.1's `Status(scale)`, so it replaces the
  plain size status rather than competing with it.
- `Exact`: nothing. The plain "Text size 150%." stays.
- The notice never replaces a warning or error that is showing, such as
  T14.1's save-failure error after a revert.
- No progress call. The session's point didn't change.

## Installed checks

In `tools/p1/windows_shell_ui_smoke.ps1`, html shard:

- **`position-text-size`**, in `html-position` after `position-resize`.
  With MARK-0420 on top, step the size 100 → 150 → 200 → 75 from the
  toolbar. After each step: MARK-0420 is still the top line, the locator's
  offset in `html-position-<pid>.json` is unchanged, `appearanceKind` is
  `Exact`, the status is the plain size status, and the progress
  diagnostics' save count hasn't grown. It resets to 100% before the next
  phase.
- **`position-text-size-fallback`**. A new fixture HTML guide of tall local
  images and no text, scrolled to its middle: a size step gives
  `appearanceKind` `Approximate`, the status "Text size 125%. Your place may
  have shifted." and an estimated fraction within 0.05 of the one before.
  A screenshot of the notice goes into the evidence and the PR.

Existing checks carry the rest of the evidence: `text-size-whitespace` and
`txt-remeasure` (TXT keeps its top line), `html-theme-switch` (the place
survives a theme change), and `pdf-zoom` and `text-size-pdf` (PDF keeps its
page and never shows text size).

## Docs

- This design: implementation notes, verification and CI runs once done.
- [implementation-plan.md](implementation-plan.md): row 890 and a T14.3
  paragraph.
- [work-breakdown.md](../work-breakdown.md): T14.3 status.
- [p1-technical-design.md](../p1-technical-design.md): the T14.3 steps.
- [t14-1-text-size-design.md](t14-1-text-size-design.md): its Risks entry
  for the HTML place, resolved by T14.3.
- [e2e-testing.md](e2e-testing.md): the two new phases.
- Evidence in `docs/p1/evidence/t14-3-appearance-restore/`.

## Risks

- **Forced layout may not be enough.** The design assumes the offset
  script's measurement lays the page out at the new zoom before it
  scrolls. If `position-text-size` shows drift (for example from scroll
  anchoring after the write), the fallback is to wait one animation frame
  in that script before measuring, and to record the change here.
- **Image-only pages always fall back.** A page with no text has only its
  fraction, so every size step there shows the notice. That is honest and
  rare: game-guide pages on the major sites are text-led, with images
  inside text.
- **A capture during step 1 costs one script call.** It runs once per
  write burst, not per step, so repeated key presses don't queue captures.
