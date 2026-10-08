# T14.4 Reader and Settings design language design

Status: merged through PR #54 on 8 October 2026 (merge commit `3ba3d72`);
full CI run 37710298596 (see [Verification](#verification)).
Prerequisites: T05.4 (catalog components, PR #16), T08.3 (TXT position,
PR #31), T09.2 (HTML theme style, PR #49), T10.2 (PDF controls, PR #46),
T11.3 (reader shell, PR #7) and T14.2 (theme setting, PR #48) are merged.

## Intent

T11.4 set the design language and T05.4 applied it to the catalog. The
Reader and Settings were built feature by feature after that, and they
show it:

- the shared status bar pushes every page down about 53 px for 3 s after
  each route opens, and again after a text-size or completion click, so
  the toolbar moves under the pointer;
- the Reader has two Back controls (the title bar's and an in-page
  button), a format pill off the spacing scale, a flat grey command band,
  ambiguous ↑ / ↓ glyphs for Go to start and Go to end (#32) and +/−
  text-size glyphs that look unlike PDF's zoom;
- an HTML guide in Dark sits in a frame inside a frame;
- Settings is one flat list, and two three-option settings use different
  controls;
- many sizes, margins, colors and fonts are literal values, and one Toolkit
  package is used directly without a central pin.

T14.4 applies the design language to the production Reader (TXT, HTML,
PDF) and Settings. Success means:

- Opening any route, changing text size, marking completion and changing
  the theme or window background move nothing on screen. Narrator still
  hears each of them.
- The Reader has one Back (the title bar's, plus Alt+Left), a metadata
  line that carries the format, grouped left-aligned commands with
  unambiguous glyphs, and one reading frame in every format and theme.
- Settings groups its cards under Appearance, Library and Game data, and
  App theme and Window background are both drop-downs.
- No literal margin, padding, width, font or color remains in the changed
  Reader and Settings XAML, and every Toolkit package used directly is
  centrally pinned.
- Installed TXT, HTML, PDF and Settings screenshots, keyboard, theme and
  narrow-width checks pass.

Traces: the T14.4 row of [implementation-plan.md](implementation-plan.md)
(TR11.3, TR14.3) and its UI adoption sequence;
[work-breakdown.md](../work-breakdown.md) S14 T14.4;
[p1-technical-design.md](../p1-technical-design.md) §8 S14 T14.4 ("Keep
content dominant, retain capability-based command overflow, and use
settings/status/teaching patterns only where they clarify an action");
the [design-language note](t11-design-language-plan.md); issue
[#32](https://github.com/ilya-slalom/desktop-guides/issues/32).

## Decisions

Decisions made during brainstorming, with before/after mockups:

- **Scope: all four kinds of change.** A conformance pass, Settings
  structure, Reader layout and status-bar behaviour.
- **Status bar: quiet, in place.** The shared bar only shows what the page
  doesn't already show, and only problems stay. Overlay and bottom-toast
  bars were rejected: the first covers the title and completion choice,
  the second the guide's last lines.
- **App theme becomes a drop-down, like Window background.** This reverses
  T14.2's Toolkit `Segmented` presentation at the user's request.
  `Segmented` (`BoundedChoice`) stays for the completion choice.
- **High contrast, Windows text scale and 200% display scale are deferred
  to T16.2**, the final accessibility pass, as T11.4 and T14.2 did. T14.4
  still gives every new brush a high-contrast mapping. T14.4's exit row
  records the deferral.
- **One PR, layered commits**: shared resources and the Toolkit pin,
  status behaviour, Reader, Settings, then harness, screenshots and docs.
  Each layer gets a scoped CI run.
- **The in-page Back button goes.** The title-bar Back calls the same
  `GoBackAsync`; Alt+Left is added so the keyboard keeps a Back that
  doesn't depend on tab order.
- **The PDF page-text box keeps its `TextBox` chrome.** Restyling it risks
  the UI Automation `TextPattern` that T10.0 proved; T16.3 owns PDF
  accessibility.
- **TXT keeps Consolas.** A different monospace font would change T08.3's
  line metrics; the font only moves into a resource.

## Status behaviour

One `ShellStatusInfoBar` sits above every route, and the shell's
`ShowStatus` opens it for every message. T14.4 sorts the messages:

| Kind | Messages | Today | T14.4 |
| --- | --- | --- | --- |
| Route loading | `Loading library…`, `Loading game…`, `Loading guide…`, `Opening guide...` | bar until ready | No bar. An indeterminate `ProgressBar` (`RouteProgress`) overlays the top edge of the content without taking layout space. The Reader card shows `ReaderLoading`: a `ProgressRing` and `Opening guide…`, replacing the "Reading this guide is unavailable in this preview." placeholder that showed between sessions. The Library keeps its own loading view. |
| Route ready | `Library ready.`, `Game ready.`, `Guide ready.`, `Settings ready.` | bar for 3 s | Announced only. |
| A confirmation the control already shows | `TextSizeSteps.Status` (text size), the completion announcement, `ThemePresentation.Saved`, `Window background set to …`, the Library search counts | bar for 3 s | Announced only. |
| Approximate place | `ApproximateRestoreMessage` ("Opened near your last place. …"), the T14.3 shifted text size | shell bar; the first stays, the second closes after 3 s | `ReaderNotice`, an informational, closable `InfoBar` inside the reading card above `ReaderSurface`. It stays until closed or until the guide closes; a newer notice replaces its text. |
| Other confirmations | `… is already in your library.`, guide and game removal, `Metadata refreshed.` | bar for 3 s | Unchanged. |
| Warnings, errors and long actions | failed saves and opens, `UnavailableRestoreMessage`, `Refreshing metadata…`, `Waiting for previous window…` | bar, closable or busy | Unchanged. |

**Mechanism.** A new `AnnounceStatus(message)` does what `ShowStatus` does
to the test probe (`ShellContent`'s `ItemStatus` becomes
`<sequence>|<message>`) and raises a UI Automation notification from the
shell content's automation peer, with `ImportantMostRecent` processing so
rapid text-size steps don't queue. It doesn't touch the bar: a warning
already showing stays. Route loads call a `ShowRouteProgress(message)`
that shows `RouteProgress` and announces the message; `RouteProgress`
hides when that render finishes, fails or is superseded. The busy-status `StatusShowsProblem` guard keeps its
meaning.

**Shifted text size.** The card notice says `Your place may have shifted.`
(a new `TextSizeSteps.ShiftedNotice`); the announcement stays
`TextSizeSteps.ShiftedStatus` ("Text size 110%. Your place may have
shifted.") so Narrator hears both facts.

**Effects.** The Reader viewport no longer changes size when a route opens
or a confirmation arrives. A `ReaderNotice` opening shrinks it once; the
resize paths of T08.3, T09.3 and T10.3 keep the place, as they do for the
T07.3 link bars today. The Library and Game also lose their ready and
search-count bars, because the bar is shared. T12.2's "a restore that
isn't exact says so in the status bar" becomes "in the reading card".

## Reader

### Header

- `ReaderBackButton` is removed with its `ReaderBackClicked` handler. A
  shell `KeyboardAccelerator` for Alt+Left (`VirtualKey.Left` with
  `Menu`) calls the same navigation as `TitleBarBackRequested`, on every
  route where Back is enabled. T16.1 lists it with the other shortcuts.
- The format pill (`Border Padding="10,4"`) is removed. The metadata line
  is a horizontal `StackPanel` of three `MetadataStyle` text blocks:
  `ReaderGameName`, a `·` separator hidden from automation, and
  `ReaderFormat`, which keeps its AutomationId and its `TXT` / `HTML` /
  `PDF` text.
- At or below `DesktopGuidesNarrowBreakpoint` (640), the completion choice
  moves from the header's right column to its own row under the metadata
  line, and the reading surface uses `DesktopGuidesSurfacePaddingNarrow`
  (16) instead of 20. Both switch in the existing `ShellContentSizeChanged`
  handler, next to the page-padding switch.

### Commands

`ReaderToolbar.xaml`:

| Command | Today | T14.4 |
| --- | --- | --- |
| Go to start / Go to end | `FontIcon` `E74A` / `E74B` (↑ / ↓) | `Symbol.Previous` / `Symbol.Next` (end-bar glyphs), closing #32 |
| Previous / Next page | `Symbol.Back` / `Symbol.Forward` | unchanged |
| Smaller / Larger text | `Symbol.Remove` / `Symbol.Add` | `Symbol.FontDecrease` / `Symbol.FontIncrease` |
| Zoom out / in | `Symbol.ZoomOut` / `Symbol.ZoomIn` | unchanged |
| Go to page, Fit to width, Reset text size, Find (overflow) | no icon | `Symbol.Page`, `FontIcon` `E9A6` (FitPage), `Symbol.Font`, `Symbol.Find`; the `E9A6` glyph is checked in the WinUI Gallery icon list before use |

- Labels, tooltips, accelerators, `AutomationProperties.AcceleratorKey`
  values and the `ReaderCommands`, `ReaderStart`, `ReaderEnd` and
  `TextSizeValue` ids don't change.
- The `CommandBar` takes `DesktopGuidesReaderCommandBarStyle`: transparent
  background, left alignment, dynamic overflow on. "More" sits after the
  last command, as native layout puts it.
- An `AppBarSeparator` (`ReaderGroupSeparator`) sits between the movement
  and size/zoom groups. It shows only when a format has both, which
  `ReaderCommandPolicy` decides (see Core).
- `DynamicOverflowOrder` is explicit: Go to start and Go to end leave
  first (1), the size or zoom group next (2, the label's container with
  its buttons), Previous and Next last (3). That keeps T11.3's "essential
  reader movement remains available".
- `TextSizeValue` keeps `VerticalAlignment="Center"` inside a container
  stretched to the button height, and its 44 px becomes
  `DesktopGuidesTextSizeValueMinWidth`.

### Reading surface

- **HTML.** While an HTML session is shown, the reading card has no
  padding and its background is `HtmlReaderStyle.PageColor` (`#FFFFFF` in
  Light, `#1E1E1E` in Dark), so the page fills the card and there is one
  frame. In high contrast `PageColor` is null and the card keeps
  `DesktopGuidesReadingSurfaceBrush`. Other formats restore the style's
  padding and brush.
- **Loading and notices.** `ReaderLoading` and `ReaderNotice` as in
  [Status behaviour](#status-behaviour).
- **Link bars.** `ReaderExternalLinkBar` and `ReaderUnavailableLinkBar`
  take spacing tokens for their margins; Open in browser takes
  `DesktopGuidesPrimaryActionButtonStyle` and Dismiss
  `DesktopGuidesSecondaryActionButtonStyle`.
- **PDF.** `PdfReaderView` margins become tokens. The 720 px split stays
  as `DesktopGuidesPdfSplitBreakpoint`, so its behaviour doesn't change.
  `Image-only page; OCR is unavailable` gains its period. `PdfUnlockError`
  uses `DesktopGuidesDestructiveBrush`, `PdfUnlockButton` the primary
  action style, and `PdfUnlockPanel`'s 360 px becomes
  `DesktopGuidesPdfUnlockMaxWidth`.
- **TXT.** `TextReaderView`'s two `Consolas` values become
  `DesktopGuidesReaderMonoFontFamily`, still Consolas. Its row padding
  becomes a named thickness.

### Copy

Across the shell and the Core presentation classes, `...` becomes `…`
(three strings) and `Could not` becomes `Couldn't` (17 strings), matching
the messages that already use contractions. Harness messages that aren't
app text stay as they are.

## Settings

The Settings `StackPanel` gets three section headings in
`DesktopGuidesSettingsSectionStyle` (BodyStrong, `HeadingLevel` 2,
`DesktopGuidesSpacing24` above and `DesktopGuidesSpacing8` below):

| Section | Rows |
| --- | --- |
| Appearance | App theme, Window background |
| Library | Library storage (T20.2 adds Export and Restore here) |
| Game data | Game data providers |

**App theme.** `AppThemeChoice` (`BoundedChoice`) is replaced by a
`ComboBox`, `AppThemeSelector`, whose items keep the `ThemeSystem`,
`ThemeLight` and `ThemeDark` AutomationIds from
`ThemePresentation.Options`. `ApplyTheme`, the save gate, the revert on a
failed save and the `ItemStatus` text (`ThemePresentation.Status`, for
example `System (Light)`) are unchanged; only the control and its
selection handler change. Both drop-downs use
`DesktopGuidesSettingsChoiceMinWidth` (160).

**Game data providers.** A successful save ("Provider credentials
saved.") and a successful connection test close their `InfoBar` after
3 s, like other routine status (TR11.3); warnings and errors stay until
closed. Save, Test connection and Remove take the DesktopGuides primary
and secondary action styles. The 260 px credential inputs and the 20 px
progress ring become `DesktopGuidesCredentialInputMinWidth` and
`DesktopGuidesInlineProgressSize`.

Library storage's text, the material fallback description and the
expander's contents don't change.

## Shared resources and Toolkit lock

| Resource | Dictionary | Used by |
| --- | --- | --- |
| `DesktopGuidesReaderCommandBarStyle` | new `Styles/Reader.xaml` | `ReaderToolbar` |
| `DesktopGuidesTextSizeValueMinWidth` (44) | `Styles/Reader.xaml` | `TextSizeValue` |
| `DesktopGuidesReaderMonoFontFamily` (Consolas) | `Styles/Typography.xaml` | `TextReaderView` |
| `DesktopGuidesSettingsSectionStyle` | `Styles/Typography.xaml` | Settings headings |
| `DesktopGuidesSettingsChoiceMinWidth` (160), `DesktopGuidesCredentialInputMinWidth` (260), `DesktopGuidesInlineProgressSize` (20), `DesktopGuidesPdfSplitBreakpoint` (720), `DesktopGuidesPdfUnlockMaxWidth` (360) | `Styles/DesignTokens.xaml` | Settings, providers, PDF |

`App.xaml` merges `Reader.xaml`. The `ReaderToolbarSmoke` project links it
beside `DesignTokens.xaml` and `Dialogs.xaml`, because it compiles
`ReaderToolbar` on its own.

**Literal-value rule.** After T14.4 the Reader panel and Settings panel in
`ShellWindow.xaml`, `ReaderToolbar.xaml`, `TextReaderView.xaml`,
`PdfReaderView.xaml` and `ProviderSettingsCard.xaml` contain no literal
margin, padding, width, font family or color, apart from 0 and 1 px
borders; every brush is a `ThemeResource` with a high-contrast entry. The
plan checks this with a grep. The Library and Game XAML are out of scope
apart from the shared status changes.

**Toolkit lock.** `Styles/Controls.xaml` uses
`CommunityToolkit.WinUI.FrameworkElementExtensions`, which comes from the
transitive `CommunityToolkit.WinUI.Extensions` package. T14.4 adds a
central `PackageVersion` for it at 8.2.251219, the locked Toolkit
release, and a direct `PackageReference` from the Production project, and
regenerates `packages.lock.json` on Windows (CI restores in locked mode).
The other transitive Toolkit packages (Helpers, Triggers, Common) aren't
used directly and stay transitive. No new Toolkit control is added:
`SettingsExpander` is in the already pinned SettingsControls package.

## Core

- `ReaderCommandPolicy.ShowsGroupSeparator(IReadOnlyList<ReaderCommand>)`
  is true when the visible commands include `PageTurn` or `PageEdge` and
  also `TextSize` or `Zoom`. Tests cover TXT (true), HTML (false), PDF
  (true) and an empty list (false).
- `TextSizeSteps.ShiftedNotice` is `Your place may have shifted.`
- The Core presentation strings that say `Could not` (`TextSizeSteps`,
  `ThemePresentation`, `GuideCompletionPresentation` and the rest) change
  to `Couldn't`; their tests change first.

## Installed checks

Each assertion names the app code that would have to regress for it to
fail; none measures WinUI or Windows rendering.

| Area | Check | App code it guards |
| --- | --- | --- |
| Route status | After each `… ready.` probe update, `ShellStatus` is closed | `AnnounceStatus` doesn't open the bar |
| Confirmations | A text-size step and a completion change leave `ReaderCommands`' top edge where it was, and `ShellStatus` closed | no bar opens above the Reader |
| Approximate place | An approximate restore and a shifted text size show `ReaderNotice` with their text; `ShellStatus` stays closed | the card notice replaces the bar |
| Loading | During the existing gated guide load, `ReaderLoading` is visible and `ShellStatus` closed | in-page loading |
| Back | `ReaderBackToGame` is absent; the title-bar Back and Alt+Left each return to the Game with its selection and focus | the accelerator and the removed button |
| Header | `ReaderFormat` shares `ReaderGameName`'s line; at 600 px the completion choice is below the metadata line and overlaps neither the heading nor the title bar | the metadata row and the narrow switch |
| Overflow | In the toolbar smoke at its narrow width, Previous and Next are still primary after Go to start and Go to end have moved to overflow | `DynamicOverflowOrder` |
| Settings | The Appearance, Library and Game data headings appear in that order above their cards; the theme passes select through `AppThemeSelector` with `Select-ComboItem` and keep their `ItemStatus` checks | the sections and the drop-down |
| Providers | A saved-credentials message closes by itself | the provider status timer |

Harness changes that follow: the theme Segmented passes become drop-down
passes; the `ReaderBackToGame` steps and keyboard traces move to the
title-bar Back or Alt+Left; the newly quiet messages join `Wait-Status`'s
probe-only list; the checks that waited for the bar to close stay and pass
at once.

Glyphs, the HTML single frame and the label's vertical centering can't be
asserted through UI Automation without re-testing WinUI rendering; they're
checked in screenshot review.

**Screenshots.** The `design` mode adds TXT, HTML and PDF Reader shots
(seeded like `seed-text-size`) and Settings, at 1500 × 720 and 600 × 720
in light and dark, plus one open overflow menu per theme (T14.2 left the
menu's popup theme unchecked). Evidence goes to
`docs/p1/evidence/t14-4-reader-settings/`, and the PR includes the shots.

**CI.** Each layer gets a scoped `dev-fast` dispatch of the groups it
touches; the branch ends with one full run.

**Deferred to T16.2:** high contrast, Windows text scale, 200% display
scale, and listening to the new announcements with Narrator.

## Docs

- [t11-design-language-plan.md](t11-design-language-plan.md): the status
  rule becomes the table above, and a short icon rule records the
  end-bar glyphs and the font-size glyphs that differ from zoom.
- [e2e-testing.md](e2e-testing.md): the design, theme and status rows.
- [implementation-plan.md](implementation-plan.md): the T14.4 row ("the
  App theme drop-down" for "the T14.2 `Segmented` choice", and the T16.2
  deferral) and its note; the T14.2 row's presentation.
- [work-breakdown.md](../work-breakdown.md): T14.2 (now a drop-down,
  changed by T14.4 at the user's request) and T14.4.
- [p1-technical-design.md](../p1-technical-design.md): the component rows
  that map `Segmented` to System/Light/Dark now map it to the completion
  choice only, and the approximate-restore notice is in the reading card.
- [results.md](results.md) and [progress.md](../progress.md): the PR's
  row.

## Implementation notes

Rulings from the [plan](t14-4-reader-settings-plan.md), one line each:

1. **Copy count.** 14 strings changed (3 Core, 11 Production); the P0
   diagnostic app keeps its own text.
2. **A card notice also updates the probe**, so `Wait-Status` sees it.
3. **`StatusShowsProblem` is gone**: the notice no longer shares the bar.
4. **Startup** begins with the bar closed and `RouteProgress` visible;
   `Waiting for previous window…` keeps the bar.
5. **`ShowReaderSurface(loading:)`** shows `ReaderLoading`; the three
   format starts pass `true`.
6. **A failed render stops loading** in the card and the progress line.
7. **`RouteProgress` hides** in `RenderCurrentAsync`'s `finally` and in
   `ShowErrorStatus` / `ShowWarningStatus`.
8. **The completion row moves by code**; the header's row spacing is 0
   wide and 12 narrow.
9. **Alt+Left** lives on `ShellRoot`; it may not fire while focus is in
   the WebView2 page (T16.1).
10. **Drop-down keys**: Alt+Down, arrows, Enter.
11. **`theme-segmented` became `theme-selector`.**
12. **Provider `Success` and `Informational` messages close after 3 s.**
13. **`design-readers`** runs in the `design` group from `seed-text-size`.
14. **The shifted notice** shows in the card; the announcement names the
    size and the shift.
15. **`ShowsGroupSeparator` takes `IReadOnlyCollection<ReaderCommand>`.**
16. **Narrator** isn't observable from the harness; see Verification.
17. **The metadata line keeps the body size** of the approved mockup.
18. **No label strip**: `DefaultLabelPosition="Collapsed"` centers the
    size label on the icons.

Found while running the checks:

- **A route load clears the previous route's bar.** The old busy message
  replaced any bar on navigation; with quiet ready messages a warning about
  the last guide stayed on the next page. `ShowRouteProgress` now calls
  `HideStatus()`; quiet announcements still never touch the bar.
- **The PDF place passes scroll to 0.2, not 0.3.** Without the Back row the
  PDF viewport is taller, so at 1500×720 page 121 scrolls only 0.262 of its
  height. `pdf-resize`, `pdf-zoom` and `progress-flush` share `$pdfPoint`.
- **`progress-flush` waits for the scroll before Back**, polling under the
  1 s quiet delay; once in two runs Back had flushed the page top.
- **The burst bound follows T12.2's ceiling of 2.** A 4 s deadline can fire
  just after the last press while its page turn is still arriving; the
  bound is `min(2, floor((seconds + 1) / 4) + 1)`.
- **Overflow order at 180 px.** Equal `DynamicOverflowOrder` values move as
  a group, and Previous and Next don't fit beside More at 180 px, so every
  primary command overflows there. The toolbar smoke checks the order at
  180 px (Go to start leaves first) and page movement at a new 320 px
  step. At 180 px the open menu is taller than the test window and clips
  its last rows (no UI Automation scroll moved it), so the narrow step
  invokes the moved Zoom in through its Invoke pattern. A stretched-bar
  experiment showed the alignment wasn't the cause, so the bar stays left.
- **`E9A6` is FitPage**, and the other glyphs match the Segoe Fluent Icons
  reference.

## Verification

- **Unit tests.** CI run 37710298596 `core-tests`: Core 906/906 (899 before;
  five `ShowsGroupSeparator` rows and two `ShiftedNotice` facts) and
  Infrastructure 540/540.
- **Red, then green, per task** (dispatched CI runs):

  | Task | Red | Green |
  | --- | --- | --- |
  | 3–4 status | 37694299304 (`The quiet status 'Game ready.' opened the shell status bar.`) | 37696710787 (core, html, design), 37705125647 and 37705133458 (progress), 37702111258 (pdf) |
  | 5 header and Back | 37695137819 (`'ReaderBackToGame' was visible.`) | 37696710787 |
  | 6 commands | 37696767891 (overflow order) | 37705118073 (`reader-toolbar-ui`, `medium-toolbar-keeps-page-movement`) |
  | 7 surface | 37698490991 (the image-only period) | 37702111258 |
  | 8 Settings | 37700375217 (`AppThemeSelector`), 37700386770 (section headings) | 37702186578 (theme), 37702197554 (design), 37708996963 (provider card in view) |
  | Review: Settings clears the bar | 37709606391 (`Expected 'ShellStatus' to hide.`) | 37710298596 (core) |

- **Full run** 37710298596 at `6f6b8c1`: `core-tests`, both package builds
  for x64 and ARM64, native ARM64 Core and UI, `reader-toolbar-ui`, and the
  `core`, `pdf` and `design` shards passed. The `html` shard's first attempt
  stopped in `html-reader-offline` before its first session started (the
  probe stayed at `Loading guide…` for 15 s), the WebView2 open hang T09.1
  and T14.3 also recorded; it passed on attempt 2 of the same run.
- **Screenshots** in [evidence/t14-4-reader-settings](evidence/t14-4-reader-settings/):
  TXT, HTML and PDF Readers at 1500 and 600 px in light and dark, the open
  PDF overflow menu in both themes, Settings wide and narrow, and the
  `theme-selector` gate at 768×519. Reviewed: one Back; "Game · format";
  end-bar and font-size glyphs; the separator on TXT and PDF only; one HTML
  frame in dark (WebView2's square corner isn't distinguishable from the
  card fill, so no inset was added); the menu in the window's theme; the
  Settings sections and drop-downs. The text-size label still sits about
  4 px below the icons' center line (a deferred minor).
- **Literal check.** The plan's grep over the Reader and Settings XAML
  finds nothing.
- **Final review** (fresh reviewer): ready with fixes. Fixed: Settings
  didn't clear the previous route's bar. Not reproduced: Alt+Left under an
  open dialog (a dialog's popup is outside `ShellRoot`, so the accelerator
  doesn't fire); the guard and the check stay. Added: progress-line absence
  checks after failed and finished loads. Deferred minors: the HTML and PDF
  ring hides before the page shows; a card notice survives a later reader
  failure; marking completion on the Game page re-renders it; the hidden
  ring stays active outside the Reader; an empty page-colored card on an
  unexpected HTML open failure; the label offset; small nits.
- **Host provider pass** on VEGA (Windows 11 x64), 8 October 2026, with the
  user's approval: `-ProviderOnly` through a one-off elevated interactive
  task, with an MSIX built on the host from the branch's sources. Every
  provider phase passed, including `saved-status-closes` and
  `notice-opens-expanded-provider-settings`; both credential scans found no
  file holding a credential value; the package, the temporary certificate and
  the task were removed. The blocked-network pass wasn't run (no firewall rule).
  Result: [provider-settings-host.json](evidence/t14-4-reader-settings/provider-settings-host.json).
- **Narrator pass** on VEGA, 8 October 2026, at the user's request. A small
  UI Automation client recorded the notification events Narrator speaks,
  while the installed `html` and `text-size` groups ran
  ([uia-notifications-host.tsv](evidence/t14-4-reader-settings/uia-notifications-host.tsv)).
  The app raised 283 announcements, all `ImportantMostRecent`. They
  included every quiet message: the ready messages; the loading and
  opening messages; "Text size N%." (49); "Text size N%. Your place may have
  shifted." (8); and the approximate-restore message. Warnings and errors
  were announced by the shell `InfoBar` itself. The card notices are
  announced twice: once by the `InfoBar` opening and once by
  `ShowReaderNotice`. That is a deferred minor. A first run with Narrator on
  stopped at the first guide open: Narrator Home takes focus at start and
  Narrator intercepts the keys the harness sends. So the scenarios ran with
  the listener only, and nobody listened to the audio; T16.2's Narrator pass
  covers listening. High contrast, text scale and 200% display scale are
  T16.2's.

## Risks

- **WebView2 corners.** WebView2 may not clip to the card's 8 px corner
  radius, leaving square corners at the card's edge. If the screenshots
  show it, the HTML card keeps a 1–2 px inset.
- **Left-aligned dynamic overflow.** A left-aligned `CommandBar` sizes to
  its content; dynamic overflow still needs the available width to shrink
  it. The toolbar smoke's narrow-width check covers this; a stretched bar
  with commands pushed left would be the fallback.
- **Overflow re-layout flake.** T10.2 saw an intermittent "no visible
  Next page" during dynamic-overflow re-layout. Changing the overflow
  order touches the same path; the plan reruns the PDF group to check.
- **Announcements.** The UI Automation client in the PowerShell harness
  doesn't observe notification events, so tests check the probe, not what
  Narrator says. A basic Narrator check runs on the host before the PR;
  the full pass is T16.2.
- **Card notice resize.** A `ReaderNotice` opening shrinks the viewport
  once. TXT, HTML and PDF keep their place across a resize, but HTML's
  resize settle has had ordering bugs (T14.1, T14.3); the approximate
  restore and shifted-size passes cover it.
- **Shared bar.** Library and Game lose their ready and search-count bars
  too. That is intended, but those pages' checks change in this PR.
