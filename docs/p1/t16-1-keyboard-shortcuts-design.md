# T16.1 keyboard shortcuts design

Status: design approved in brainstorming; spec awaiting review.
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
