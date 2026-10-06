# T14.2 theme setting design

Status: design approved; implementation pending.
Prerequisites: T03.2 (versioned migrations, PR #4) and T11.1 (the
production shell, PR #6) are merged. The `Theme` key and
`ThemePreference` (`System`, `Light`, `Dark`, default `System`) have been
part of `AppSettings` since the M0 storage foundation. They are stored and
validated, but no code applies them yet.

## Intent

Desktop Guides follows the Windows app theme today and offers no choice.
T14.2 adds a global *App theme* setting: System, Light or Dark, with
System as the default. The choice applies at once, survives a restart, and
never overrides Windows high contrast. It uses the Toolkit `Segmented`
control that the work breakdown asks for. T13.1 fell back to native
`RadioButtons` for the completion choice; once `Segmented` passes the UI
Automation gate here, that choice moves to the same control.

Success means:

- In Settings, the user picks System, Light or Dark from the keyboard. The
  shell, both readers' native chrome, dialogs and the title bar caption
  buttons switch at once, and the status bar confirms the change.
- The choice survives a relaunch. System follows the Windows app theme
  again.
- With Windows high contrast on, the app uses the high-contrast system
  colors whatever the stored choice.
- Launch, change and restart make no non-loopback network connection
  (TR14.2: theme assets are local).
- A failed save puts the previous theme and choice back and explains the
  error.
- UI Automation reads the choice as a list of items with a selected state
  that follows the arrow keys. This is the gate T13.1's `Segmented` failed.
  Once it passes, the completion choice uses the same control and keeps its
  behavior.

Traces: the T14.2 row of [implementation-plan.md](implementation-plan.md)
(TR14.2), [work-breakdown.md](../work-breakdown.md) S14 T14.2, and
[p1-technical-design.md](../p1-technical-design.md) §8 S14 T14.2 and the
Toolkit control mapping.

## Decisions

Decisions made during brainstorming:

- **One shared Segmented subclass, bound to `ItemsSource`.** T13.1 found
  both `SegmentedItem`s declared in XAML as `ListItem`s, but
  `GetCurrentPattern(SelectionItemPattern)` threw `Unsupported Pattern`.
  The likely cause is that items that are their own containers get no
  data-item peer. `BoundedChoice` binds its options as data, so each item
  is a generated container. Two alternatives were rejected:
  - Inline `Segmented` in Settings, with completion converted separately.
    That puts any automation fix in two places.
  - A custom automation peer from the start. That adds peer code before
    there's evidence it's needed.
- **Custom peer as the fallback.** If the `ItemsSource`-bound control still
  fails the gate, `BoundedChoice` gains item peers that implement
  `ISelectionItemProvider`, and the gate runs again. It doesn't fall back
  to `RadioButtons`. (The user chose this over stopping or keeping radio
  buttons for theme.)
- **Gate first.** The first installed run checks only the theme card's
  automation. The rest of the work starts after that answer.
- **High contrast wins.** While Windows high contrast is on, the app
  applies `ElementTheme.Default` whatever the stored choice, and it
  applies the theme again when high contrast changes.
- **HTML guide content is out of scope.** T09.2, which depends on this
  task, adds the fixed local HTML style for the effective theme. Until
  then, HTML pages render as authored, in both themes.
- **Tests assert the app's choices.** The installed checks read the theme
  the app reports applying to the root, the dialogs and the title bar.
  They don't measure rendered pixels or re-test WinUI theme resources.
  Screenshots are for review only.

## Core: `ThemePresentation`

`DesktopGuides.Core/Library/ThemePresentation.cs` is pure and unit-tested,
beside `GuideCompletionPresentation`:

- The options in order: `System`, `Light`, `Dark`, with labels `System`,
  `Light` and `Dark` and AutomationIds `ThemeSystem`, `ThemeLight` and
  `ThemeDark`.
- `AppliedTheme Resolve(ThemePreference preference, bool highContrast)`
  returns `FollowSystem`, `Light` or `Dark`. High contrast always gives
  `FollowSystem`. An undefined preference gives `FollowSystem`.
- The status text: `App theme set to Dark.`; the save-failure text:
  `Could not save the app theme: <message>`.

`AppliedTheme` is a Core enum, so Core stays free of WinUI types; the
shell maps it to `ElementTheme` and `TitleBarTheme`.

## Control: `BoundedChoice`

`DesktopGuides.Production/BoundedChoice.cs` derives from Toolkit
`Segmented`:

```csharp
public sealed record BoundedChoiceOption(string Label, string AutomationId);

public sealed partial class BoundedChoice : Segmented
{
    // Sets ItemsSource; DisplayMemberPath is Label.
    public void SetOptions(IReadOnlyList<BoundedChoiceOption> options);
}
```

`PrepareContainerForItemOverride` sets the generated container's
`AutomationProperties.Name` to the label and `AutomationId` to the
option's id. Selection stays the `Segmented` `SelectedIndex`, so callers
keep the selection logic they have today.

The gate found two Toolkit behaviors that `BoundedChoice` corrects:

- `Segmented`'s Left and Right keys move keyboard focus only.
  `BoundedChoice` handles the arrow keys first and moves the selection
  with focus, as in a radio group. Tab and programmatic focus don't
  change the selection.
- `Segmented.OnApplyTemplate` resets `SelectedIndex` to the first index
  it ever held. `BoundedChoice` keeps the selection it had before the
  template pass.

Callers listen to `ChoiceChanged`, which is raised when the selected
option changes. It isn't raised by the template pass or by a cleared
selection that `BoundedChoice` puts back.

Package: `CommunityToolkit.WinUI.Controls.Segmented` at `8.2.251219`, the
version of the other Toolkit pins, in `Directory.Packages.props`, the
Production project and its lock file.

If the gate fails, the fallback is local to this file: item peers that
implement `ISelectionItemProvider` (`IsSelected`, `Select`,
`SelectionContainer`) over the container's selection. The plan checks
which Toolkit hook allows that (a `SegmentedItem` subclass from
`GetContainerForItemOverride`, or a peer supplied by the container).

## Shell

### Settings card

An *App theme* `SettingsCard` (`AppThemeSettingsCard`) goes above
*Window background*, with the description *Choose light or dark, or
follow Windows.* It holds a `BoundedChoice` (`AppThemeChoice`, automation
name `App theme`). The choice is disabled until startup has read the
settings, as the material selector is.

### Applying a theme

`ShellWindow.Theme.cs` owns one method:

```csharp
private void ApplyTheme(ThemePreference requested);
```

It resolves the applied theme with the current high-contrast state
(`ThemeSettings.HighContrast` from `Microsoft.UI.System`) and then:

- sets the root element's `RequestedTheme`, `Default` for `FollowSystem`;
- sets `AppWindow.TitleBar.PreferredTheme` (`UseDefaultAppMode`, `Light`
  or `Dark`), so the caption buttons match;
- records the theme for dialogs. `DialogSurface.Apply` gains a theme
  argument and sets `dialog.RequestedTheme`, because a `ContentDialog` in
  the popup root doesn't inherit the root's theme. Every dialog already
  goes through `DialogSurface.Apply` or `ReaderActions.DialogMaterial`;
  the plan lists each call site and covers the Reader's dialogs too;
- selects the requested option without raising a save, as
  `ApplyWindowMaterial` does with `applyingMaterialSelection`;
- reports the applied theme as the choice's `ItemStatus`, for example
  `Dark` or `System (Light)`, where the part in brackets is the root's
  `ActualTheme`.

`ThemeSettings.Changed` calls `ApplyTheme` again with
the stored preference.

### Startup and changes

Startup reads `GetSettingsAsync()` once for both the material and the
theme, applies the theme before the first render, and then enables the
choice. An invalid stored theme already raises `InvalidDataException`, and
`RenderCurrentAsync` reports it; the app then applies `System`.

A change of selection:

1. Applies the new theme at once.
2. Calls `UpdateSettingsAsync(s => s with { Theme = requested })`.
3. On success, shows `App theme set to <Theme>.` as a transient status.
4. On failure, applies the previous theme, which also selects it again,
   and shows the save-failure text as an error status.

The Mica and Acrylic backdrops follow the root's theme. That's WinUI
behavior and isn't asserted; the screenshots show it.

## Completion choice

After the gate passes, `GuideCompletionChoice` hosts a `BoundedChoice`
with options `In progress` (`CompletionInProgress`) and `Complete`
(`CompletionComplete`) in place of `RadioButtons`. Its public surface
doesn't change: `Show`, `SetBusy`, `FocusSelection` and
`CompletionRequested`. Busy still ignores pointer input and snaps a
keyboard change back to the pending choice, and focus is still restored
after a Game-page render. Its smoke checks go back to the `ListItem` and
`SelectionItem` checks that T13.1 planned, and the 768x519 header check
stays.

## Installed checks

A new `theme` scenario group runs after `completion` and before `import`.
The CI matrix puts it in the `pdf` shard (`pdf,progress,theme`). Windows
app-theme changes use the harness's existing `Set-AppThemePreference` and
restore the user's value afterwards.

| Mode | Checks |
| --- | --- |
| `theme-segmented` (first) | At 768x519, Settings shows `AppThemeChoice` with three `ListItem`s named `System`, `Light` and `Dark`. `SelectionItem.IsSelected` is true for System only and follows Right and Left. A failure stops the group; it's the gate for the fallback above. |
| `theme-change` (Windows light, Windows dark) | Keyboard only: with Windows light, choose Dark. The status reads `App theme set to Dark.`; `ItemStatus` reads `Dark`; an opened Edit game dialog reports a Dark theme. With Windows dark, choose Light and check the inverse. Screenshots of Library, Reader and the dialog. |
| `theme-restart` | Relaunch with Dark stored: the choice and `ItemStatus` read `Dark`. Choose System and relaunch: `ItemStatus` reads `System (<Windows theme>)`. |
| `theme-offline` | Across `theme-change` and `theme-restart`, the app's process has no non-loopback TCP connection, with the same check `pdf-offline` uses. |
| `theme-error` | Hold the write lock and choose Light. `ShellStatus` shows the save-failure text, and the choice and `ItemStatus` return to the previous theme. Release the lock; a retry saves. |

How the dialog reports its theme is settled in the plan, for example its
`ActualTheme` in an automation property set by `DialogSurface`. High
contrast isn't switched on in CI: it changes the user's theme, and the
plan leaves that pass to T16.2 with its restore rules. The Core
`Resolve` test and the `ThemeSettings.Changed` path cover it here.

Runs: the `theme` group on CI with `shell-scope=theme` (gate first); the
`completion` group after the completion switch; then a full run. Host runs
on `pcsx2-win` follow [e2e-testing.md](e2e-testing.md) only to debug a CI
failure. Evidence goes in `docs/p1/evidence/t14-2-theme-setting/`.

## Docs

In the implementing branch:

- the implementation notes and verification in this file, including the
  gate result;
- the `theme` group and the `pdf` shard in
  [e2e-testing.md](e2e-testing.md), and `theme` in the workflow's
  `shell-scope` choices;
- the T14.2 lines in [implementation-plan.md](implementation-plan.md) and
  [work-breakdown.md](../work-breakdown.md). The plan row's "accessible
  native-radio fallback" becomes the custom-peer fallback;
- the Toolkit control mapping in
  [p1-technical-design.md](../p1-technical-design.md) §8, and the T13.1
  implementation note, once completion uses `Segmented`.

## Implementation notes

- **The gate needed the custom peer.** The `ItemsSource`-bound `Segmented`
  still lacked `SelectionItem` (run 37447327137, patterns
  `ScrollItemPatternIdentifiers.Pattern` on `ListItem`s named System, Light
  and Dark); `BoundedChoiceItem` supplies a peer that implements
  `ISelectionItemProvider` (run 37448537276, patterns SelectionItem and
  ScrollItem).
- **Selection follows the arrow keys.** The Toolkit's arrow keys move focus
  only. `BoundedChoice` notes an arrow key in `PreviewKeyDown` and, once
  `Segmented` has moved focus, selects the focused item. Selecting inside
  the key handler moved focus first, so `Segmented` skipped an item (runs
  37462702749 and 37463448838). The first version selected any item that
  got keyboard focus, which let a Tab choose an item; `theme-error` now
  tabs out and back in after the revert and checks the choice is
  unchanged. Putting a choice back moves focus to it.
  `Segmented.OnApplyTemplate` resets `SelectedIndex` to the first index it
  ever held, so `BoundedChoice` keeps the selection through it and raises
  `ChoiceChanged` instead of `SelectionChanged` for callers. The gate
  passes with both (run 37464169606).
- **The items panel names `BoundedChoice`.** The Toolkit's panel finds its
  control by exact type, so a subclass lost the control's alignment and
  stretched across the Game page. An implicit `BoundedChoice` style in
  `Styles/Controls.xaml` repeats the Toolkit panel with
  `AncestorType="local:BoundedChoice"`.
- `ThemePresentation` in Core holds the labels, ids, `Resolve`, and the
  status and failure texts.
- The applied theme goes on `ShellRoot.RequestedTheme`, dialogs get it
  through `DialogSurface.Apply`, and the title bar through
  `PreferredTheme`. The tests read `ItemStatus` on `AppThemeChoice`
  (`<Label>` or `System (<Windows theme>)`), `AppTitleBar` and each
  dialog. UIA shows a `ContentDialog` through its `Popup` host, which
  takes the dialog's AutomationId but not its ItemStatus, so
  `DialogSurface` sets the status on the open `Popup` too.
- The saves are serialized, and a failed save reverts only when no newer
  choice is pending.
- **High contrast** comes from `ThemeSettings.CreateForWindowId`; the
  status reads `<Label> (high contrast)`. Subscribing to
  `AccessibilitySettings.HighContrastChanged` threw `0x80070490` in a WinUI
  3 desktop window and closed the app at launch. The high contrast pass
  itself is deferred to T16.2.
- `DisplayMemberPath` isn't used. The container's `Content` is set to the
  label in `PrepareContainerForItemOverride`.
- Completion moved to `BoundedChoice`, and only `BoundedChoice` restores a
  cleared selection.
- The theme-change popup screenshot is the open Window background
  drop-down: the TXT design guide's Reader overflow menu is empty.

## Risks

- **The gate fails even with the custom peer.** Then the user decides; no
  control is chosen without them.
- **Popups that don't inherit the root theme.** Flyouts, tooltips and
  menus outside `ContentDialog` may keep the Windows theme. The theme-change
  screenshots include the open Window background drop-down (the TXT
  Reader's overflow menu is empty); any popup that needs it gets the theme
  from the same applied value.
- **Existing light and dark passes.** They change the Windows app theme
  and rely on the app following it. Each group starts from an empty data
  root, so the stored theme is System there and nothing changes for them.
