# T09.3 HTML locator and restore design

Status: implemented; CI run 37203750673 passed the installed `html-position`
mode in light and dark.
Prerequisites: T09.1 is merged (PR #35, merge commit `9f2ad26`); T12.1's
locator codec is merged (PR #3); T07.3's WebView2 policy is merged (PR #34).

## Intent

T09.1 opens a static HTML guide in a restricted WebView2, but its session
reports a stub location (the entry at fraction 0) and restores nothing. A
link to an HTML page that wasn't imported is cancelled without a word.
T09.3 completes the HTML locator: it captures where the reader is in the
entry document, keeps that point across window resizes, restores a saved
point by exact offset, then by text context, then by scroll fraction, and
tells the reader when a link goes to a page that isn't part of the guide.

Success means:

- `html-long`, scrolled to a line inside its long `<pre>`: after resizing to
  600, 1100 and 1500 px the same line is at the top of the page, within one
  line.
- Restoring that point in the same bytes is `Exact`, also when an image
  above it loads late. Restoring it in changed bytes is `Approximate`
  through the text context and shows the same line. A malformed locator is
  `Unavailable` and the page stays at its start.
- Any value the page's DOM returns is validated in Core before it is
  stored or used; a result for another page, guide or directory is
  rejected (TR09.1, TR09.2).
- Clicking a link to `part2.html`, which wasn't imported, shows "This link
  goes to a page that isn't part of the imported guide." The position does
  not move and nothing new is served.

Traces: the T09.3 row of [implementation-plan.md](implementation-plan.md)
(TR09.1, TR09.2), [work-breakdown.md](../work-breakdown.md) T09.3 and the
S09 acceptance lines on fragment links and resume after reflow, and
[p1-technical-design.md](../p1-technical-design.md) §6 S09 T09.3 and the
host DOM script rule ("Fixed host DOM scripts check the current origin and
validate returned JSON before storage"). The capture and restore algorithm
follows [p0-technical-design.md](../p0-technical-design.md) §8 and the P0
`HtmlProbe`, with the changes below.

Decisions made during brainstorming:

- **In-session only.** As in T08.3 and T10.3, T09.3 captures and restores
  within an open guide. Saving the locator and restoring it on reopen is
  T12.2's `ProgressCoordinator`. An HTML guide still reopens at its start
  until T12.2.
- **Unimported links are reported** in an informational Reader bar, the
  design's "reported as unavailable"; they stay denied.
- **Approach A:** fixed host scripts run through the DevTools protocol's
  `Runtime.evaluate` with `userGesture: false`;
  every decision and every check of their replies lives in Core; a light
  poll tracks movement. T07.3's policy is unchanged: page scripts, web
  messages and host objects stay off. Pushing scroll events over web
  messages (approach B) was rejected because it opens a page-to-host
  channel; a fraction-only locator (approach C) can't survive reflow.

## The position

The T12.1 shape is used unchanged, so there is no schema bump:
`HtmlPosition(DocumentPath, ElementId, TextQuote, TextOffset,
ScrollFraction)` inside a version-1 `ReaderLocation`.

| Field | Meaning |
| --- | --- |
| `DocumentPath` | The entry's managed relative path, from the session's request policy. Never read from the page (TR09.2). |
| `TextOffset` | Character offset, in the document's text walk, of the first character at the viewport's top-left. |
| `TextQuote` | Up to 160 characters of raw text starting at that offset; null when there is no text. |
| `ElementId` | `id` of the nearest ancestor with one, up to 128 characters; null when none. |
| `ScrollFraction` | `scrollY / (scrollHeight − innerHeight)`; 0 for a page that doesn't scroll. |

`EstimatedFraction` repeats `ScrollFraction`.

**The text walk.** One fixed walk over the body's text nodes, in document
order, skipping `script`, `style`, `template` and `noscript`, is shared by
capture and restore. Offsets count UTF-16 code units of the nodes' raw data,
so equal bytes give equal offsets.

**Why a document offset and not a block plus a pixel delta.** P0 anchored
to the nearest block and a pixel delta. Many public game guides that ship
as HTML are one large `<pre>` (GameFAQs-style text exports) or a few long
blocks; a block anchor there resolves to the start of the guide, and a
pixel delta is wrong after any reflow. A character offset names the same
line at every width. The `ElementId` and quote remain for changed bytes.

## Core: `DesktopGuides.Core/Html/`

### `HtmlLocationRules` (new, pure)

- `HtmlCapture? ParseCapture(string json, Uri entry)` treats the capture
  script's reply as untrusted. It returns null unless the reply is a JSON
  object with exactly the fields `offset`, `quote`, `id`, `fraction` and
  `href`, and:
  - `offset` is an integer from 0 to `int.MaxValue`;
  - `quote` is null or a string of at most 160 characters, and `id` null or
    at most 128 characters, neither containing NUL;
  - `fraction` is a finite number in [0, 1];
  - `href`, without its fragment, is the entry URI. Another page, another
    guide's host or another directory is rejected; a fragment difference is
    accepted.

  The reply is read with a size cap of the codec's 4096 bytes and a depth
  of 4.
- `ReaderLocation Capture(string sha256, string documentPath, HtmlCapture
  capture)` builds a location the T12.1 codec serializes.
- `HtmlRestorePlan PlanRestore(LocationDecodeResult result)` decides what
  the restore script may try, from the codec's decode of the saved value
  (which already checked the format, version, size and entry path):
  - `Valid`: exact at the offset, verified by the quote; then context; then
    the fraction.
  - `ContentChanged`: context; then the fraction. No exact step.
  - `UnsupportedVersion` or `Invalid`: nothing.

  Context means the quote's occurrence nearest the saved offset, searched
  within the `ElementId` element first and then the whole walk. A tie
  between two equally near occurrences is no match, as in TXT.
- `RestoreOutcome Outcome(HtmlRestorePlan plan, string? reply)` validates
  the restore script's reply (`{"step": "exact"|"context"|"fraction"|
  "none", "offset": int}`, the step one the plan allowed) and maps it:
  - `exact` → `Exact`;
  - `context` with unchanged bytes → `Context`;
  - `context` with changed bytes, or `fraction` → `Approximate`, with "The
    guide changed, so this is an approximate position.";
  - `none`, a step the plan didn't allow, or a bad reply → `Unavailable`,
    with "This reading position can't be used with this guide."

  The reason strings are TXT's and PDF's.

### `HtmlNavigationPolicy` (changed)

New kind `Unavailable`: an `http`/`https` target on the guide's own host
whose path or query differs from the entry's, started by a person. A
non-user navigation (a redirect, a refresh or a meta refresh), a frame
navigation, and any other guide's host stay `Deny`.

## Production

### `HtmlPositionScripts` (new)

Three fixed constants that share one prelude (the text walk and the
helpers). Host values enter only as `JsonSerializer` literals.

- `ReadScroll` returns `[scrollY, scrollHeight, innerHeight]`.
- `Capture` finds text at a point just inside the viewport's top-left with
  `caretRangeFromPoint`. If that point is an image, a gap or padding, it
  steps down the left edge 8 px at a time, up to half the viewport. It maps
  the caret to the walk's offset and returns the offset, the quote, the
  nearest `id`, the fraction and `location.href`. Without text it returns
  offset 0, no quote and the fraction.
- `Restore(plan)` applies the plan's steps in order and scrolls so the
  target character's line box is at the viewport top, clamped to the
  scroll range. It returns the step and the offset it used. No image is
  still loading at that point: with page scripts off, Chromium ignores
  `loading="lazy"` and loads every image eagerly, and the session restores
  only after the entry's load event, which waits for them. (A CI probe of
  the delayed-image run showed the lazy route map already complete at the
  first scroll.) A resize during the restore scrolls to the target again.

**Clobbering.** Page scripts can't run, but named elements can still
shadow `document` and `window` properties (`<form name="querySelectorAll">`).
The scripts call DOM methods through prototypes
(`Document.prototype.createTreeWalker.call(document, …)`,
`Element.prototype.getBoundingClientRect.call(…)`) and read no named
properties of `document` or `window`.

### `HtmlReaderSession` (changed)

- **Origin check.** Before any script, `CoreWebView2.Source` without its
  fragment must be the entry URI; otherwise the script is not run.
- `GetLocationAsync` runs `Capture`, parses the reply in Core and returns
  the location. A rejected reply, a script error or a timeout returns the
  last valid point, or the document start when there is none, and counts a
  rejection in diagnostics.
- `RestoreLocationAsync` decodes with `ReaderLocationCodec.Deserialize`
  (expected `Html`, the guide's SHA-256 and the entry path), plans in Core,
  runs `Restore` and returns `Outcome`. A restore requested before the
  entry has loaded waits for the load and then applies.
- **Tracking.** A 500 ms `DispatcherQueueTimer` starts after open and stops
  at dispose. It runs `ReadScroll`; when `scrollY`, `scrollHeight` or
  `innerHeight` moved by more than 1 CSS px since the last read, it runs
  `Capture`, stores the result as the current point and raises
  `LocationChanged`. Ticks don't overlap; a tick still running skips the
  next one.
- **Resize.** The WebView's `SizeChanged` pauses tracking. After 300 ms
  without another size change, the session re-applies the current point
  with an exact plan, then reads the scroll again and resumes tracking
  from there. Chromium's own scroll change during reflow is therefore
  never captured as a reader's move (the HTML form of T10.3's P1 rule).
- **Fragment links** need nothing new: the jump moves the page and the
  next tick captures it.
- **Unimported links.** `NavigationStarting` and `NewWindowRequested` raise
  `UnavailableLinkRequested` for `HtmlNavigationKind.Unavailable`. The
  request is cancelled and recorded as denied, as before.
- **Failures.** Script errors, timeouts (5 s per call) and a renderer crash
  are caught; capture keeps the last point and restore returns
  `Unavailable`. Crash reporting stays as T09.1 built it.

### Shell

`ReaderUnavailableLinkBar` is a second Reader InfoBar (Informational,
closable, no action) with the fixed message "This link goes to a page that
isn't part of the imported guide." It never shows the target's path or the
page's text. Showing either link bar hides the other. Both close when the
guide closes or another guide opens.

## Logging and untrusted input

- Script replies, saved locators and the test restore file are untrusted.
  Each passes Core validation before it is stored, used or written.
- `HtmlSessionDiagnostics` gains counts only: rejected captures and restore
  outcomes by kind. It holds no guide text.
- The `HtmlPosition` test gate below is the one place guide text is
  written: fixture text, only while the gate is open, deleted by the
  smoke's cleanup.

## Testing

### Core (`core-tests`)

- `HtmlLocationRulesTests`:
  - parse: each wrong type, an extra or missing field, a negative,
    fractional or oversized offset, a 161-character quote, a 129-character
    id, NUL, NaN, infinite and out-of-range fractions, another page, guide
    host or directory, an oversized or too-deep reply, and an accepted
    fragment-only difference;
  - capture: round trip through the T12.1 codec;
  - plan: one row per decode status;
  - outcome: each step with unchanged and changed bytes, a step the plan
    didn't allow, and bad replies.
- `HtmlNavigationPolicyTests`: a user click to another path or query on
  the guide's host is `Unavailable`; the same without a user, and another
  guide's host, are `Deny`.

### Fixtures (`tests/fixtures/p1/`)

- `html-long/guide.html`: headings with and without ids; an image without
  width or height above the target; one long `<pre>` of numbered lines
  `MARK-0001` onwards with an `id` inside it and a fragment link to it; a
  link to `part2.html`, which isn't imported; and clobbering elements
  (`<form name="querySelectorAll">`, `<img name="createTreeWalker">`,
  `<img name="getElementById">`).
- `html-long-changed/guide.html`: the same document with paragraphs
  inserted above the target.

### Test gates

Named-event gates in the existing `TestGate` pattern:

- `HtmlPosition`: writes `html-position-<pid>.json` to the cache's
  diagnostics folder whenever the current point or a restore outcome
  changes (the encoded locator, the outcome kind, the step and the
  reason). While it is open, the session also restores the locator in
  `LocalState\test\html-restore.json` after open, if that file exists.
  This stands in for T12.2's reopen in the smoke only.
- `HtmlAssetDelay`: image responses are delayed by 1 s.

### Installed smoke: `html-position` (light and dark)

1. Open `html-long`, follow the fragment link. The target `MARK-` line is
   at the page's top within one line, read from the Chromium UI Automation
   tree; the diagnostics hold its offset.
2. Resize to 600, 1100 and 1500 px. After each, the same line is at the
   top and the offset is unchanged.
3. Reopen with that locator in the restore file: `Exact`, line at the top.
   Again with `HtmlAssetDelay` open: `Exact`, line at the top; the open
   waited for the late images.
4. Open `html-long-changed` with the same locator: `Approximate` from the
   context step with the changed reason; the same line is at the top.
5. A malformed restore file: `Unavailable`; the page is at its start.
6. Click the `part2.html` link: the unavailable bar is shown, the position
   is unchanged, the denied count rises by one and the served set doesn't
   change.

Screenshots: the unavailable bar and the restored page, light and dark.

**Early check.** The plan's first installed run includes a minimal
capture, to confirm that `Runtime.evaluate` runs under T07.3's
`script-src 'none'` page CSP with page scripts off. If it doesn't, work
stops for a revised approach.

The first early check used `ExecuteScriptAsync`. Its scripts ran under the
CSP, but they ran with a user gesture: with the 500 ms poll running, the
canary's one-second meta refresh reached the navigation policy as a
person's click and raised the external-link bar. `Runtime.evaluate` with
no gesture keeps page-initiated navigations page-initiated. It works with
`AreDevToolsEnabled` off, since that setting only hides the DevTools UI.

## Docs

Done in the implementing branch; see the status lines in each.

## Out of scope

- Saving the locator and restoring it on reopen, and showing restore
  reasons (T12.2).
- Keeping the point across theme and font changes (T09.2, T14.3; they can
  reuse the resize re-apply).
- Estimated progress labels (T12.3).
- HTML guides with more than one document (S21).

## Implementation notes

Planning refinements, from the
[implementation plan](t09-3-html-locator-plan.md):

- **P1. The restore choice is in Core.** The `Restore(plan)` script became
  `Find`, which only measures (is the quote at the saved offset, and where
  else does it occur, inside the `ElementId` element and in the whole walk),
  and `ScrollToOffset` and `ScrollToFraction`, which only scroll.
  `HtmlLocationRules.Resolve` picks the step and the offset, so the tie rule
  is a unit test. `Outcome` maps Core's own step; there is no restore reply
  to parse.
- **P2. Capture measures text boxes.** The capture script binary-searches
  the walk for the first character whose box bottom is below the viewport
  top, instead of `caretRangeFromPoint` and an 8 px step-down. Images, gaps,
  padding and body margins don't affect it, and restore scrolls that
  character's box top to the viewport top, so a capture after a restore
  returns the same offset.
- **P3. Exact needs a quote.** A page without text captures offset 0 and no
  quote; its restore uses the fraction.
- **P4.** A fraction restore of unchanged bytes is `Approximate` without a
  reason, because "The guide changed" would be false.
- **P5.** `RestoreLocationAsync` takes a `ReaderLocation`, as
  `IReaderSession` defines it. `HtmlLocationRules.Decode` sends it through
  the codec's `Serialize` and `Deserialize`, so it gets the checks a stored
  locator gets.
- **P6.** An unimported link is recorded as a denied navigation:
  `HtmlDenyReason.UnimportedPage` with context `Navigation`.
- **P7.** The smoke reads the page's top line independently of the app's
  capture: it walks the page's UI Automation tree for the `MARK-` line
  elements and takes the one whose bounding box starts within 30 px of the
  page's top. `TextPattern` returned nothing for the WebView2 page, and
  `ElementFromPoint` returns the WinUI hosting bridge, not page content.
  The 30 px rule keeps "the top is not a MARK line" true for a page at its
  start whose MARK lines are visible lower down.
- **P8.** `html-position` is its own seed (`seed-html-position`, game "Web
  Position Game") and smoke mode, so the canary passes' counts are
  unchanged. The page-tree helpers moved to the shared reader scope.
- **P9.** `HtmlNavigationPolicy.IsEntryDocument` is the one entry
  comparison, used for a capture's `href` and the session's origin check.
- **Script transport.** With scripts off (T07.3), `ExecuteScriptAsync`
  doesn't run, so the session evaluates its measuring and scrolling scripts
  through DevTools `Runtime.evaluate` with `userGesture` false. T07.3's
  settings and the CSP are unchanged; the guide's own scripts still don't
  run.
- **Fixture lines.** `html-long` wraps each MARK line in its own `<span>`,
  so the smoke can find lines in the page tree. Its `<pre>` sets
  `overflow-anchor: none`: otherwise the spans give Chromium a scroll
  anchor that keeps the point across a resize by itself, and the re-apply
  would go untested against a guide that is one long text node.
- **Page start.** A position at the walk's first text scrolls to 0, not to
  that text's box top, so a re-apply at a page's start doesn't nudge the
  page by its top margin.
- **Fixture image.** The lazy image above the target is its own file,
  `images/route.png`. A second `<img>` with `map.png`'s URL is answered
  from Chromium's memory cache.
- **No image wait.** With scripts off, Chromium ignores `loading="lazy"`
  and loads every image eagerly, and the entry's load (`NavigationCompleted`)
  waits for all of them, including those the test gate delays by 1 s. No
  image is still loading when a restore runs (run 37200461420: `route.png`
  complete and 462 px tall at the restore's first scroll), so the planned
  image wait was dropped. `position-restore-late-images` stays as the check
  that a restore after late images is `Exact` with the line on top. The
  scroll scripts' pending count remains only as their reply.
- **Diagnostics.** The installer saves each `html-position` pass's
  diagnostics in a `finally`, so a failing pass still leaves them.

## Verification

- `HtmlLocationRulesTests` cover reply parsing (every type, cap and origin
  row, and a titled entry's literal sub-delimiters), capture through the
  T12.1 codec, a plan per decode status, `Resolve` including the tie, and
  every outcome row. `HtmlNavigationPolicyTests` and
  `HtmlSessionDiagnosticsTests` cover the `Unavailable` kind and the new
  counts.
- CI run 37203750673, `html-position` on `html-long`, light [dark]:

  | Phase | Result |
  | --- | --- |
  | `position-fragment` | MARK-0420 on top, offset 29056 [29056] |
  | `position-resize` | 600, 1100, 1500 px: same offset, MARK-0420 on top |
  | `position-restore-exact` | `Exact` at 1100 px |
  | `position-restore-late-images` | `Exact`, MARK-0420 on top; the open waited for the late images |
  | `position-restore-changed` | `Approximate` through the text context |
  | `position-restore-invalid` | `Unavailable`, page at its start |
  | `position-unimported-link` | bar shown twice, offset unchanged, 2 [2] denied navigations |

  The values are from
  [light](evidence/t09-3-html-locator/html-position-light.json) and
  [dark](evidence/t09-3-html-locator/html-position-dark.json).
  Screenshots: restored page
  [light](evidence/t09-3-html-locator/html-restore-light.png),
  [dark](evidence/t09-3-html-locator/html-restore-dark.png); unavailable
  link [light](evidence/t09-3-html-locator/html-unavailable-link-light.png),
  [dark](evidence/t09-3-html-locator/html-unavailable-link-dark.png).
- Not covered: saving and restoring on reopen, and showing restore reasons
  (T12.2); the point across theme and font changes (T09.2, T14.3); a
  `target="_blank"` link to an unimported page, which takes the same
  `RaiseUnavailableLink` path as a plain link and is classified by the
  `HtmlNavigationPolicyTests` rows only.
