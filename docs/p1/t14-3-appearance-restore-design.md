# T14.3 appearance restore design

Status: implemented; CI run 37583360621.
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
   then, unless `resizing` or `restoring` is set, reads the scroll and,
   only when the tracker's `Moved(lastScroll, scroll)` test says the reader
   moved, takes a fresh `CaptureAsync`. A capture that differs from
   `current` is a real move the tracker hadn't polled yet: it becomes
   `current` and raises `LocationChanged`. A failed capture keeps
   `current`.
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
- **A resize settle is pending.** Step 4 still runs, and the tracker stays
  stopped; `ReapplyAsync` later scrolls to the same `current` again and
  restarts it. (Designed as a skip; changed in implementation, because the
  size status opening above the reader is itself a resize. See the
  implementation notes.)
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

## Implementation notes

Rulings the plan made against this design:

1. **No unsubscribe.** The HTML attach path subscribes its session events
   without unsubscribing and filters with `ReferenceEquals(sender,
   readerSession)`; `AppearanceRestored` follows that pattern.
2. **One flag joins the save status and the restore.** T14.1 shows the
   size status after the save, so the save and the restore event land in
   either order. `ShellWindow.TextSize.cs` keeps `double?
   shiftedTextScale`: a size change clears it, an Approximate or
   Unavailable event sets it and shows `ShiftedStatus` unless a warning or
   error is showing, an Exact event clears it, and a successful save shows
   `ShiftedStatus` when the flag matches its scale. Whichever lands last,
   the status is right.
3. **The fallback check reads the locator, not the view.** "Estimated
   fraction within 0.05" became "the locator is unchanged": the app
   chooses the locator; the view's fraction after a zoom is Chromium's.
4. **Two more diagnostics fields.** `appearanceScale` (the clamped scale
   the restore ran at) and `appearanceRestores` (a count) join
   `appearanceKind` and `appearanceStep`, so the smoke waits for the
   restore of a given step.
5. **Appearance restores aren't counted as restores.**
   `HtmlSessionDiagnostics.RecordRestore` counts open-time restores only.
6. **Progress counts in the position passes.** `Invoke-HtmlPositionPass`
   opens the `ProgressDiagnostics` gate, and `Read-ProgressCounts` moves
   next to `Read-HtmlPosition`.
7. **`WriteAppearanceAsync` returns `Task<bool>`.** True when the page has
   the latest appearance; `OpenAsync` ignores it.
8. **The tracker and `GetLocationAsync` honor `restyling`.** A resize's
   `finally` can restart the tracker mid-restyle, so ticks return while the
   flag is set.
9. **The image-only guide joins the position seed.** `seed-html-position`
   also publishes `tests/fixtures/p1/html-pictures` as "Picture Web Guide"
   (a *Jump to the middle* link and six 900 px local images).
10. **A theme-only change still pauses.** Capture, pause and write run for
    every restyle; only the restore depends on the scale.
11. **A write bumps the generation again.** After each write that changed
    the scale, so an open-time restore that started during the restyle
    scrolls to its own target again.
12. **The fallback steps to 110%.** One *Larger text* from 100% is 110%,
    so the phase checks `Text size 110%. Your place may have shifted.`,
    not the 125% of the example above.

Rulings made during implementation:

- **The restyle restores even while a resize settle is pending.** The
  first run (37579601419) never restored: the size status InfoBar is an
  `Auto` row above the reader, so showing it resizes the WebView during
  every restyle. The resize's re-apply scrolls to the same point again and
  takes the baseline. If wrong, a real resize during a step costs one
  extra scroll.
- **The pre-change capture runs only after a real move.** Run 37580359630
  saved three times in four bursts: at a new zoom the same top character
  has a new scroll fraction, so a fresh capture always differed from
  `current`. The capture now runs only when the tracker's own
  `Moved(lastScroll, scroll)` says the reader moved. If wrong, a move made
  within 500 ms before a step and within 1 px of the baseline isn't taken
  first.

Other notes:

- The restore runs after each write that changed the scale, not once per
  burst; a step that arrives during the restore is written and restored
  again by the same loop. The four bursts of `position-text-size` show
  no drift, so the forced layout of the offset script was enough (see
  Risks).
- Run 37581077978's first attempt hung opening Picture Web Guide with no
  request reaching the handler; its second attempt, on the same commit,
  opened it. Not reproduced since.

## Verification

- Core tests went from 888 to 899: `HtmlLocationRulesTests`
  (`NeedsAppearanceRestore` for a first write, a theme-only change, a
  scale change and scales that clamp alike; `AppearanceOutcome` for Exact,
  Context, Fraction and no target), `TextSizeStepsTests` (`ShiftedStatus`
  text and culture) and `ReaderContractTests`
  (`AppearanceRestoredCarriesItsOutcome`). Infrastructure stays at 540.
- Full CI run 37583360621, artifact `production-shell-ui-html`, in both
  the light and the dark position pass:
  - `position-text-size`: with MARK-0420 on top, bursts to 150%
    (3 × *Larger text*), 200% (2 ×), 75% (7 × *Smaller text*) and 100%
    (2 ×). After each, MARK-0420 is still the top line, the locator's
    offset is unchanged, `appearanceKind` is `Exact`, the status is the
    plain `Text size <label>.`, and the progress save count hasn't grown.
  - `position-text-size-fallback`: Picture Web Guide, scrolled to its
    middle by its link; 110% and back to 100% each give `Approximate`
    with step `Fraction`, an unchanged locator, the status `Text size
    <label>. Your place may have shifted.` and no progress save
    ([light](evidence/t14-3-appearance-restore/html-position-light.html-place-shifted.png),
    [dark](evidence/t14-3-appearance-restore/html-position-dark.html-place-shifted.png),
    [light report](evidence/t14-3-appearance-restore/html-position-light.json),
    [dark report](evidence/t14-3-appearance-restore/html-position-dark.json)).
  - The other position phases (fragment, resize, the restores and the
    unimported link) still pass.
- TXT, PDF and theme changes are carried by existing checks in the same
  run: `text-size-whitespace` and `txt-remeasure` (TXT keeps its top line),
  `html-theme-switch` (the place survives a theme change), `pdf-zoom` and
  `text-size-pdf`.
- `html` group runs on the way: 37578966629 (the new phases, red before the
  session change), 37579601419 and 37580359630 (the two implementation
  rulings above), 37581077978 (the session green, the shell notice still
  red) and 37582559027 (green).

## Risks

- **Forced layout may not be enough.** The design assumes the offset
  script's measurement lays the page out at the new zoom before it
  scrolls. `position-text-size` shows no drift, so no frame wait was
  added; if a guide shows drift (for example from scroll anchoring after
  the write), the fallback is to wait one animation frame in that script
  before measuring.
- **Image-only pages always fall back.** A page with no text has only its
  fraction, so every size step there shows the notice. That is honest and
  rare: game-guide pages on the major sites are text-led, with images
  inside text.
- **A capture during step 1 costs one script call.** It runs once per
  write burst, not per step, so repeated key presses don't queue captures.
