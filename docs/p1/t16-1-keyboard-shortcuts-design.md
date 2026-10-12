# T16.1 keyboard shortcuts design

Status: in review in PR #88.
Prerequisites T05.2, T06.1, T08.3, T10.2 and T11.3 are merged. T16.2 depends
on this task.

## Intent

Every command a keyboard user needs has a shortcut that works in the right
place and nowhere else, never takes keys away from typing, and matches a
visible control. One list in Settings documents them. Reader shortcuts work
in HTML guides even while focus is inside the page.

Traces: T16.1 in [implementation-plan.md](implementation-plan.md) (TR16.1,
TR16.2), [work-breakdown.md](../work-breakdown.md) T16.1 and
[p1-technical-design.md](../p1-technical-design.md) S16.

Decisions made during brainstorming:

- **Documentation:** a "Keyboard shortcuts" expander in Settings, plus the
  key named in each matching control's tooltip.
- **HTML guides:** the app's shortcuts are forwarded from inside the page.
  The mechanism is chosen by a probe (see "HTML guides").

Existing behaviour this builds on:

- The reader toolbar handles Page Up/Down, Ctrl+Home/End, Ctrl+G and
  Ctrl+Plus/Minus/0, and its tooltips name the keys.
- The shell handles Alt+Left for Back on `ShellRoot`.
- Confirmation dialogs make Cancel the default, so Esc cancels.
- The only reader overlays are dialogs (Go to page, the PDF password
  prompt), which WinUI closes on Esc.
- T14.4 left one gap: Alt+Left may not fire while focus is inside the
  WebView2 page.

## Shortcuts

| Context | Shortcut | Command | Visible counterpart |
| --- | --- | --- | --- |
| Everywhere | Alt+Left | Back | Title-bar Back |
| Library | Ctrl+F | Focus the title search | The search box |
| Library search box | Esc | Clear the search; with no text, leave the box | Clear search button |
| Game page | Ctrl+O | Import guide… | Import guide button |
| Reader | Page Up / Page Down | Previous / next page | Toolbar buttons |
| Reader | Ctrl+Home / Ctrl+End | Go to start / end | Toolbar buttons |
| Reader | Ctrl+G | Go to page | Toolbar button |
| Reader | Ctrl+Plus / Ctrl+Minus / Ctrl+0 | Zoom or text size in, out, reset | Toolbar buttons |
| Dialogs | Esc | Close; focus returns to the control that opened it | Cancel / Close |

## Behaviour

- **Wrong context:** a shortcut does nothing outside its context. Ctrl+O on
  the Library page and Ctrl+F in the Reader are ignored: no error, no
  navigation, no status.
- **Typing wins:** while a text field has focus (the Library search, the
  PDF password box, dialog inputs), Ctrl+F, Ctrl+O and Page Up/Down go to the
  field and the command doesn't run.
- **Dialogs own the keyboard:** while any dialog is open, only the dialog
  sees keys.
- **Disabled commands:** Ctrl+O follows the Import guide button. While the
  button is disabled, for example while the library is busy, Ctrl+O does
  nothing.
- **Esc in Library search:**
  - With text, Esc clears it, as the Clear search button does.
  - With no text, Esc moves focus to the first game row, or to Add game when
    the library is empty.
  - AutoSuggestBox's own handling stays first, so an open suggestion list
    closes before the text clears.
- **Focus return (TR16.2):** when the Go to page or PDF password dialog
  closes, by Esc or Cancel, focus returns to the control that opened it.
  The confirmation dialogs (remove a game, remove a guide, replace the
  library) are checked for the same behaviour and fixed if needed.
- **Tooltips and automation:**
  - Tooltips name the key: "Import guide (Ctrl+O)", "Search (Ctrl+F)",
    alongside the existing reader tooltips.
  - Automation names don't change. `AutomationProperties.AcceleratorKey`
    reports the key on each control that has one.

## HTML guides

The reader shortcuts and Alt+Left must work while focus is inside an HTML
guide. Page Up/Down stay with the page, which scrolls natively.

HTML guides run with page scripts off (`IsScriptEnabled = false`) and web
messages off. The app reaches the page only through the DevTools protocol
(`Runtime.evaluate`). The plan's first task is a probe on pcsx2-win that
tries these mechanisms in order and keeps the first that works:

1. **Native routing:** WinUI's WebView2 may already pass Ctrl and Alt
   combinations to the app's keyboard accelerators. If so, no code is
   needed.
2. **A DevTools-protocol listener** in an isolated script world. It reports
   only the forwarded key combinations through `Runtime.addBinding`. The
   guide's own content still can't run scripts.
3. **Neither works with page scripts off:** stop and bring it back to the
   user. The likely answer is F6 to move focus out to the toolbar.

Whatever works ends in the same `ReaderToolbar.TryRunKey(key, modifiers)`
the native accelerators use, plus Back for Alt+Left. The forwarded set is
fixed to the reader shortcuts and Alt+Left, so the page can't trigger
anything else.

## Settings

- A new "Help" section heading at the end of Settings, with a "Keyboard
  shortcuts" `SettingsExpander` below it.
- One row per shortcut, grouped by context: the description on the left,
  the keys on the right.
- The rows are read-only. Each row's automation name is "Description,
  Keys", for example "Import a guide, Ctrl+O".
- The expander's description: "Keys for the Library, Game pages, the reader
  and dialogs."

## Code

- **Core:**
  - `KeyboardShortcuts`: the shortcut table as data. Each entry has a
    `Context` (Everywhere, Library, LibrarySearch, Game, Reader, Dialog),
    `Keys` (display text such as "Ctrl+O"), a command and a description.
    Settings builds its list from it, and the shell takes tooltip text from
    it.
  - `ShortcutRouting.Resolve(command, route, textInputFocused, dialogOpen)`
    returns Run or Ignore, following the behaviour rules above.
- **Production:**
  - `ShellWindow.Keyboard.cs` adds the Ctrl+F and Ctrl+O accelerators on
    `ShellRoot`, next to the existing Alt+Left. Each handler asks
    `ShortcutRouting`, then does what its button does. Ctrl+F focuses
    `LibrarySearchInput` and selects its text. Ctrl+O runs the Import
    guide button's action.
  - The Library search's Esc handling.
  - A focus-return helper: it records the invoking control before a
    dialog's `ShowAsync` and restores focus after it.
  - The HTML forwarding chosen by the probe.
  - The Settings expander, built from `KeyboardShortcuts`.
  - The reader toolbar's tooltip text comes from `KeyboardShortcuts`. Its
    key table and behaviour stay as they are.

## Tests and verification

- **Core unit tests:**
  - `KeyboardShortcuts`: every command appears once, the keys text uses
    Windows names ("Ctrl+O", "Page Down", "Esc"), every context has an
    entry, and the table matches the one above.
  - `ShortcutRouting`: for every command and context, the right page runs
    it, the wrong page ignores it, text focus wins for Ctrl+F, Ctrl+O and
    Page Up/Down, and an open dialog blocks everything.
- **HTML probe:** a throwaway smoke on pcsx2-win records which keys reach
  the XAML accelerators from inside the page. Its result picks the
  mechanism and goes in the verification record.
- **Installed smoke:** a new `keyboard` mode in the shell UI smoke, a
  keyboard-only trace with no clicks:
  - **Library:** Ctrl+F focuses the search box. Typing `zzqx` shows no
    results. Esc clears the text, and Esc again moves focus to the first
    game row.
  - **Game page:** Ctrl+O opens the Import dialog. Esc closes it, and focus
    returns to Import guide.
  - **Wrong context:** Ctrl+O on the Library page does nothing, and the
    status sequence doesn't change.
  - **Typing:** Ctrl+F while the PDF password box has focus doesn't move
    focus.
  - **TXT reader:** Page Down moves the reader. Ctrl+G opens Go to page, Esc
    closes it, and focus returns to the Go to page button. Alt+Left goes
    back.
  - **HTML reader, with focus inside the page:** Ctrl+G opens Go to page,
    Alt+Left goes back, and Page Down scrolls the page.
  - **PDF reader:** Page Down changes the page, and Ctrl+Plus zooms.
  - **Settings:** the Keyboard shortcuts expander lists every entry with
    its automation name. Screenshots in light and dark.
  - Every check reads app-owned state: focus, automation IDs and names,
    item status and the status sequence. None checks how WinUI draws.
- **Documentation:** the README gets a "Keyboard shortcuts" section copied
  from the table. The status pages are updated.
- **Gaps for T16.2:** anything the trace finds outside these shortcuts, such
  as tab order or Narrator, is listed in the verification record for T16.2
  rather than fixed here.

## Out of scope

- New commands, shortcut rebinding and find-in-guide.
- The full tab-order, focus-visual and Narrator audit (T16.2).
- PDF document-text accessibility (T16.3).

## T16.1 verification record

Implemented from [t16-1-keyboard-shortcuts-plan.md](t16-1-keyboard-shortcuts-plan.md)
on `feat/p1-t16-1-keyboard-shortcuts`, PR #88.

- **Core** (pcsx2-win, .NET SDK 10.0.401): 1165 of 1165 pass, including:
  - 51 in `KeyboardShortcutsTests` and `ShortcutRoutingTests`;
  - 11 in `ReaderToolbarTooltipTests`. Removing a key from a tooltip made
    the matching test fail.
- **Installed keyboard-only trace** (CI, `dev-fast` dispatches):
  - `design` run
    [38207134518](https://github.com/ilya-slalom/desktop-guides/actions/runs/38207134518):
    - Library search: Ctrl+F, Esc clears, Esc again leaves the box;
    - wrong context: Ctrl+O on the Library does nothing;
    - import picker: Ctrl+O opens it, and focus returns to Import guide;
    - Remove game: Esc cancels, and focus returns to its button;
    - HTML from the toolbar: Ctrl+Plus and Alt+Left;
    - Settings: all 13 rows with their automation names.
  - `txt` run
    [38206362577](https://github.com/ilya-slalom/desktop-guides/actions/runs/38206362577),
    on the long guide: Ctrl+Home, Page Down landing on the same row as
    Next page, Ctrl+End (100% scroll), Ctrl+Plus and Alt+Left.
  - `pdf` run
    [38205679965](https://github.com/ilya-slalom/desktop-guides/actions/runs/38205679965):
    the existing PDF key smoke passes with the shell's new key handling.
- **Full PR CI:** pending.
- **Help card icon:** with Colorful icons on, it shows Fluent's
  `question_circle` in light and dark. Off, it shows the circled question
  mark `E9CE`. See the screenshots below.

Findings the trace surfaced, all fixed:

1. **Esc in the Library search:** it didn't clear, because the
   AutoSuggestBox's inner text box marks Esc handled. Esc is now taken in
   `PreviewKeyDown`, and skipped while the suggestion list is open.
2. **Keys swallowed by the focused control:** with focus on a reader
   toolbar button, Alt+Left didn't go back, and with focus in the TXT list,
   Ctrl+End didn't move. In WinUI the focused control handles a key before
   accelerators run. The shell root's `PreviewKeyDown` now takes Alt+Left,
   and the reader keys on the Reader route outside text fields. The
   accelerators stay as the fallback.

**HTML guides.** The probe (plan Task 3) found that WinUI's WebView2 doesn't
pass keys to the app's accelerators while the page has focus (run
[38195881210](https://github.com/ilya-slalom/desktop-guides/actions/runs/38195881210)).

A DevTools-protocol listener in an isolated script world was then built:
- It installed even with page scripts off. It sent its Ready signal in run
  [38199274878](https://github.com/ilya-slalom/desktop-guides/actions/runs/38199274878).
- Keys sent while the page had focus reached it once in four runs.
- Alt+Left never did. Chromium keeps it for itself.

With the user's agreement, the listener and an Esc-leaves-the-page key were
reverted. HTML guides get the reader keys from the reader toolbar.

Rulings (details in the plan and the ledger):

1. TXT and HTML reader keys were off; T16.1 turned them on.
2. Shortcuts follow each format: Ctrl+G is PDF-only, and HTML has only the
   text-size keys.
3. The PDF password prompt is inline, not a dialog.
4. Focus return: Go to page returns focus as T10.2 does. After the Ctrl+O
   picker, focus returns to Import guide, as the spec says. The plan's
   "where it was" was reversed.
5. PDF keys keep their existing `pdf` smoke.
6. No completion shortcut.
7. The toolbar tooltips stay in XAML, checked by `ReaderToolbarTooltipTests`.
8. The trace runs in `design`, and the TXT part in a `keyboard-txt` mode in
   the `txt` group. The design library's only TXT guide is 7 lines long,
   and its rows are named by their text.
9. The Help card's icon joined the T11.5 catalog (`question_circle`,
   `E9CE`).
10. Task 4 ran while Task 3's probe was in CI.
11. Key forwarding from inside HTML pages was reverted, as above.
12. Alt+Left and the reader keys are taken in the shell root's
    `PreviewKeyDown`.

**Gaps for T16.2:**
- Keys while focus is inside an HTML guide page: the reader keys and
  Alt+Left. Tab and Shift+Tab out of the page are not verified either.
- The Settings shortcut rows have a wide empty icon column; T16.2's visual
  audit should decide whether that's acceptable.

Evidence:
[Settings list](evidence/t16-1-keyboard-shortcuts/settings-shortcuts.png),
[Help card, colour off](evidence/t16-1-keyboard-shortcuts/settings-help-off.png),
[Help card, colour on, light](evidence/t16-1-keyboard-shortcuts/settings-help-on-light.png) and
[dark](evidence/t16-1-keyboard-shortcuts/settings-help-on-dark.png).
