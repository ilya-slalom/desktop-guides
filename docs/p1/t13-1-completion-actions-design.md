# T13.1 completion actions design

Status: implemented with the native `RadioButtons` fallback; CI run
37302867776 passed `shell-scope=completion` and the full matrix.
Prerequisites: T05.1 (Library and Game rows, PR #23), T11.3 (the Reader
shell, PR #7), and T13.2 (the completion service, PR #42, merge commit
`0807d5a`) are merged.

## Intent

A guide is complete only when the user says so. T13.1 adds the controls
that say so: one on the Game page for the selected guide, and one in the
Reader. Both call T13.2's `GuideCompletionService`, show the committed
state, and announce the result. Reading to the last line or page changes
nothing.

Success means:

- From the keyboard, the user chooses `In progress` or `Complete` on the Game
  page or in the Reader. The control and the guide's row then show the
  committed state, and the result is announced.
- Returning to in progress keeps the saved place and shows the saved
  estimate again.
- The state survives a relaunch (TR13.2).
- Reaching the final TXT line or PDF page leaves the guide not complete
  (TR13.1).
- A failed write puts the choice back, explains the error, and never shows
  a state that wasn't stored.

Traces: the T13.1 row of [implementation-plan.md](implementation-plan.md)
(TR13.1, TR13.2), [work-breakdown.md](../work-breakdown.md) S13 T13.1, and
[p1-technical-design.md](../p1-technical-design.md) §8 S13 T13.1.

Decisions made during brainstorming:

- **Toolkit `Segmented` behind a UIA gate.** The work breakdown asks for
  `Segmented` when UI Automation exposes its selected state, with native
  radio buttons as the fallback. T13.1 is the first task to use
  `Segmented`, so its installed run checks the selected state first. If
  that check fails, the control switches to `RadioButtons` in the same
  branch, and this file and §8 of the technical design record why.
  Going straight to radio buttons was rejected: T14.2 would then have to
  answer the same question.
- **Full installed evidence.** The installed run covers the keyboard toggle
  on both surfaces, a relaunch, the last line and page, and a forced
  storage error. Leaving the error case to headless tests was rejected,
  because the error display is app code that only the installed app shows.
- **One shared control and one write path.** A `GuideCompletionChoice`
  control appears on both surfaces, and only `ShellWindow.Completion.cs`
  calls the service. Two alternatives were rejected:
  - Inline `Segmented` markup on both surfaces, with handlers in
    `ShellWindow`. It would duplicate the markup and the revert logic, and
    a fallback would touch both places.
  - Command buttons in the Reader toolbar and the Game header. They
    contradict the documented two-item choice, and the toolbar is gone
    when a guide fails to load.

## Core: `GuideCompletionPresentation`

`DesktopGuides.Core/Library/GuideCompletionPresentation.cs` is pure and
unit-tested, beside `GuideRemovalPresentation`:

- The item labels are `In progress` and `Complete`.
- `IsComplete(DateTimeOffset? completedUtc)` selects `Complete` only when a
  completion time is stored. A guide without one shows `In progress`,
  including one whose row fact reads `Not started`. The estimate and the
  open time play no part.
- `ChoiceName(title)` is the control's accessible name:
  `Completion for <title>`.
- `Announcement(title, completedUtc)` is `<title> marked complete.` or
  `<title> marked in progress.`.
- `SaveFailed(title)` is `Could not update completion for <title>. Try
  again.`. It names the failed operation and offers one recovery action,
  following the T11.4 copy rules.
- `Removed` is `This guide is no longer in your library.`, the text the
  Reader already shows for a missing guide.

Titles are used verbatim; they're user data, not markup.

## App: `GuideCompletionChoice`

`DesktopGuides.Production/GuideCompletionChoice.xaml(.cs)` is a
UserControl around Toolkit `Segmented`. It has two `SegmentedItem`s with
AutomationIds `CompletionInProgress` and `CompletionComplete`.

```csharp
public sealed partial class GuideCompletionChoice : UserControl
{
    // Shows the committed state without raising CompletionRequested.
    public void Show(Guid guideId, string title, bool complete);
    public void SetBusy(bool busy);
    // Raised only by a user change of selection.
    public event EventHandler<GuideCompletionRequest>? CompletionRequested;
}

// The title travels with the request for the announcement and error text.
public sealed record GuideCompletionRequest(Guid GuideId, string Title, bool Complete);
```

- Tab moves focus into the choice, and the arrow keys move between items.
- `Show` sets the selection behind a flag, so a programmatic change never
  raises a request.
- The control holds no storage logic. A `RadioButtons` fallback replaces
  only this control's markup and selection code.

Package: `CommunityToolkit.WinUI.Controls.Segmented` at `8.2.251219`,
the version of the other Toolkit pins. It goes in
`Directory.Packages.props`, the Production project, and
`src/DesktopGuides.Production/packages.lock.json`.

### Placement

- **Game page.** The choice goes in a new `Auto` column of
  `GameGuidesHeader`, before *Open selected guide*. It shows when a guide
  of this game is selected, under the rule `UpdateOpenSelectedGuideAction`
  uses, and is hidden otherwise. It shows the selected guide's committed
  state. `GuideRowItem` keeps the summary's `CompletedUtc` for this. No row
  gains a control, so virtualization and the row template are unchanged.
- **Reader.** The header grid's `Auto` column becomes a horizontal stack:
  the choice, then the format badge. The Reader render reads
  `GetReadingStateAsync` after the guide's metadata loads and before the
  format adapter opens, then calls `Show`. The choice stays usable when the
  TXT, HTML, or PDF content fails to load, because it isn't part of the
  session-bound `ReaderToolbar`.

## Write flow: `ShellWindow.Completion.cs`

`InitializeCoreAsync` creates
`new GuideCompletionService(repository, TimeProvider.System)`. Only
`ShellWindow.Completion.cs` calls it.

For a `CompletionRequested(guideId, complete)` from either surface:

1. If a close has begun or a completion write is already running, show the
   last committed state again and stop.
2. Set `completionRequested` and call `SetBusy(true)` on both choices.
   `Segmented` has already moved its selection. Until the write commits it
   ignores pointer input, and a keyboard change snaps back to the pending
   choice. It isn't disabled, because disabling the focused item would
   move focus away.
3. Inside `RunNavigationAsync`, so the write is serialized with route
   changes as T15.3 and T04.2 are, call `MarkCompleteAsync` or
   `MarkInProgressAsync`.
4. On success:
   - **Game page, same game still shown:** `RenderCurrentAsync()`. The
     anchor keeps the guide selected, and its row fact becomes `Completed`
     or goes back to `~N%` or `In progress`, and
     `UpdateOpenSelectedGuideAction` shows the selected row's stored state
     in the choice. The render collapses the Game panel, which drops focus,
     so the shell then focuses the choice's selected item.
     `pendingGuideFocus` isn't set.
   - **Reader, same guide still shown:** `Show(committed)`. Library and
     Game rows show the change on their next render; Back always renders.
   - Then `ShowTransientStatus(Announcement(...))`. It follows the render,
     so it replaces `Game ready.`. It reaches screen readers through the
     shell `InfoBar`'s `LiveSetting="Polite"` and the `ItemStatus` sequence
     the harness reads.
   - **Route changed during the write:** leave the UI alone. The state is
     committed, and the next render shows it.
5. On `ReadingStateMissingException`, a removed guide:
   `RenderCurrentAsync()`. The Game page drops the row and the anchor moves
   to the next guide, and the Reader's existing check returns to the
   Library. Then `ShowWarningStatus(Removed)`.
6. On any other failure (SQLite, I/O, a held write lock): put the choice
   back to the last committed state and call
   `ShowErrorStatus(SaveFailed(title))`. The error stays until it's
   dismissed. The choice is re-enabled so the user can retry; it never
   shows a state that wasn't stored.
7. In `finally`, clear `completionRequested` and call `SetBusy(false)`.

The last line or page never completes a guide. The TXT, HTML, and PDF
sessions, `ProgressCoordinator`, and the estimate code don't reference the
service. The installed check shows this from the user's side.

## Untrusted input

The choice takes a guide ID and a boolean, and the time comes from the
clock. Guide content and reading position can't reach the write. Titles
appear in names, the announcement, and errors as plain text.

## Testing

Core (`GuideCompletionPresentationTests`):

- A null `CompletedUtc` selects `In progress`, and any stored time selects
  `Complete`.
- The announcement for each state, the save-failure text, and the removed
  text.
- `ChoiceName` and a long Unicode title, kept verbatim.

The shell flow needs a WinUI host, so the installed run proves it. T13.2's
repository tests already cover the transaction, repeats, and restart.

### Installed `completion` group

`windows_shell_install.ps1` gains `-CompletionOnly` and a `completion`
scenario group. The CI `shell-scope` input gains `completion`. The pass
uses `seed-progress` (Numbered TXT, Web, PDF, and Unopened, with no stored
estimates) and checks stored state through `describe-progress`
(`completedUtcMs` and the estimate) as well as the UI. Because no estimate
is seeded, the table's `~37%` and `~16%` stand for "the row's text before
the toggle": the harness captures it and requires the same text after
returning to in progress.

| Mode | Checks |
| --- | --- |
| `completion-segmented` (first) | The choice exposes two `ListItem`s named `In progress` and `Complete`. `SelectionItem.IsSelected` matches the stored state and follows the arrow keys. A failure triggers the `RadioButtons` fallback. |
| `completion-game` (light, dark) | Keyboard only: select Numbered, Tab to the choice, press Right. The row reads `Completed`, the announcement shows, `completedUtcMs` is set, and the locator, estimate, and open time are unchanged. Press Left: the row reads `~37%` and `completedUtcMs` is null. Screenshots. |
| `completion-reader` (light, dark) | Open Web and mark it complete in the Reader header. Back shows `Completed` on its row. Screenshots. |
| `completion-restart` | Relaunch: Web reads `Completed` and its choice shows `Complete`. Mark it in progress and relaunch: Web reads `~16%`. |
| `completion-last-page` | Move to the end of Numbered with Ctrl+End and to the last PDF page, wait past the five-second flush, and go Back. The rows show estimates, not `Completed`; `completedUtcMs` is null and the choice shows `In progress`. |
| `completion-error` | Hold the write lock and choose `Complete`. `ShellStatus` shows the save-failure text, the choice returns to `In progress`, and `completedUtcMs` stays null. Release the lock and retry; the write succeeds. |

`hold-write-lock` holds for up to 30 seconds, and Microsoft.Data.Sqlite's
default command timeout is also 30 seconds. The error mode passes a longer
hold so the app's write fails first, and the harness waits for the error
status before it releases the lock.

Runs: focused Core tests and the installed group on `pcsx2-win` first,
through an interactive scheduled task with the
[e2e-testing.md](e2e-testing.md) backup and cleanup rules; then CI with
`shell-scope=completion`; then a full CI run. Evidence goes in
`docs/p1/evidence/t13-1-completion-actions/`.

## Docs

In the implementing branch:

- the implementation notes and verification in this file, including the
  `Segmented` result;
- the `completion` group in [e2e-testing.md](e2e-testing.md);
- the T13.1 lines in [implementation-plan.md](implementation-plan.md),
  [work-breakdown.md](../work-breakdown.md), and §8 S13 of
  [p1-technical-design.md](../p1-technical-design.md).

## Out of scope

- A completion control in Library rows; the Library shows the fact only.
- A `Not started` item in the choice.
- Any change to row order. Completion doesn't touch `LastOpenedUtcMs`.
- Shortcut keys for completion (T16.1).

## Implementation notes

- **`Segmented` failed the UIA gate; the choice uses `RadioButtons`.** In CI
  run 37300830462, `completion-segmented` found both items as `ListItem`s
  named `In progress` and `Complete`, but reading their selected state threw
  `Exception calling "GetCurrentPattern" with "1" argument(s): "Unsupported
  Pattern."` for `SelectionItemPattern`. `GuideCompletionChoice` now holds a
  native `RadioButtons` (`MaxColumns="2"`, so Left and Right move between the
  items) with two `RadioButton`s that keep the names and AutomationIds. The
  smoke checks for `RadioButton` items, and the Segmented package is gone
  from `Directory.Packages.props`, the Production project and its lock file.
  The likely cause, not verified, is that `SegmentedItem`s declared in XAML
  are their own containers and get no data-item peer. An `ItemsSource`-bound
  `Segmented` wasn't tried; T14.2 can test it before choosing its control.
- The plan's rulings against this design:
  1. Focus is restored after the Game render. `RenderCurrentAsync`
     collapses the Game panel, which drops focus, so after a Game-page
     write the shell calls `GameCompletionChoice.FocusSelection()`.
  2. Busy doesn't disable the control, because disabling the focused item
     would move focus away. While busy the choice ignores pointer input,
     and a keyboard change snaps back to the pending choice.
  3. There is no `Show(committed)` after the Game render.
     `UpdateOpenSelectedGuideAction` shows the selected row's stored state,
     which also covers a selection change during the write.
  4. The request carries the title:
     `GuideCompletionRequest(Guid GuideId, string Title, bool Complete)`.
  5. Estimates are compared by capture, not by literal. `seed-progress`
     stores no estimates, so the harness captures a row's text before
     marking it complete and requires the same text after marking it in
     progress.
  6. `hold-write-lock` takes an optional hold time. The error mode holds
     the lock for 120 s, so the app's 30 s SQLite timeout fails first.
  7. The error check spans three smoke modes. The installer owns the lock
     helper, so it takes the lock between `completion-error-prepare` and
     `completion-error`, and releases it before `completion-error-retry`.

## Verification

- `GuideCompletionPresentationTests` (Core, 9 tests): the labels, both
  states from a null or stored time, the choice name, both announcements,
  the save-failure and removed text, and a long Unicode title kept verbatim.
- Installed `completion` group, CI run 37302205371 (`dev-fast`) and run
  37302867776 (full):
  - `completion-segmented`: the choice is named `Completion for Numbered
    Lines Guide`; two `RadioButton`s with their names; Shift+Tab from *Open
    selected guide* focuses `In progress`; Right selects and announces
    `Complete`, Left returns to `In progress`.
  - `completion-last-page`: after Go to end in Numbered Lines Guide and on
    page 200 of the PDF, the rows show estimates (the PDF `~100%`), not
    `Completed`; `completedUtcMs` stays null and the choice shows
    `In progress`.
  - `completion-game` (light, dark): from the keyboard the row reads
    `Completed`, the announcement shows and `completedUtcMs` is set, with
    the locator, estimate and open time unchanged; Left restores the
    captured row text and clears `completedUtcMs`.
  - `completion-reader` (light, dark): Long Web Guide is marked complete in
    the Reader header, and Back shows `Completed` on its row.
  - `completion-restart`, `completion-restart-after`: after a relaunch the
    row reads `Completed` and the choice shows `Complete`; marked in
    progress and relaunched, the row's estimate is back within 0.01.
  - `completion-error-prepare`, `completion-error`, `completion-error-retry`:
    with the write lock held, `Complete` shows `Could not update completion
    for Numbered Lines Guide. Try again.`, the choice returns to
    `In progress` and `completedUtcMs` stays null; after the release the
    retry succeeds.
- Screenshots and results are in
  [evidence/t13-1-completion-actions](evidence/t13-1-completion-actions/).
- CI run 37302867776 (`shell-scope=completion`, the full matrix) passed
  every job, including ARM64, packages, `production-shell-ui` and
  `reader-toolbar-ui`: Core.Tests 737, Infrastructure.Tests 528. CI run
  37302882954 (`shell-scope=progress`, the full matrix) passed, covering the
  shared `Wait-Status` and lock-helper changes.
