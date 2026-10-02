# T08.2 Virtualized TXT view design

Status: design approved in brainstorming on 1 October 2026;
implementation planned in [t08-2-txt-view-plan.md](t08-2-txt-view-plan.md)
and verified in CI run
[36946245355](https://github.com/ilya-slalom/desktop-guides/actions/runs/36946245355)
(see the [verification record](#t082-verification-record)).
Prerequisite T08.1 is merged (PR #28, merge commit `da1e975`).

## Intent

Opening an imported TXT guide shows its text in the Reader:

- **Fixed-width and unwrapped.** Consolas, no wrapping, with horizontal
  scrolling for long lines, so ASCII maps, columns and diagrams keep their
  shape.
- **Whitespace kept.** Spaces are never collapsed (TR08.1). Tabs expand to
  8-column stops and C0 control characters other than tab, plus DEL, display
  as one space. Both rules change only the displayed line; the decoded text
  and its offsets are untouched.
- **Bounded UI.** The guide is held once as one normalized string and its
  line-start index. The Reader realizes only the visible lines and never
  keeps one persistent control or string per source line (TR08.2).
- **P0-level response.** The 10 MiB `txt-long` fixture meets the P0
  reference checks: first text within 3 s, no repeated UI-thread gap above
  500 ms while scrolling, and a realized item count that stays bounded.
- **Explicit failures.** A guide that can't be loaded shows one plain
  sentence and no partial text.

Traces: the T08.2 row of [implementation-plan.md](implementation-plan.md)
(TR08.1, TR08.2), [work-breakdown.md](../work-breakdown.md) S08 and T08.2,
[p1-technical-design.md](../p1-technical-design.md) §7 S08, and the TXT
thresholds in [p0-technical-design.md](../p0-technical-design.md) and
[p0/results.md](../p0/results.md).

Decisions made during brainstorming:

- **Approach A.** A `ListView` over a lazy line source, the control the P0
  spike proved (902 ms first text and 98 realized items for `txt-long` on
  x64). Rejected: `ItemsRepeater` (no list semantics for UI Automation
  without custom peers, and no P0 evidence) and a custom pool of line
  controls (most code, custom accessibility).
- **Tabs** expand to the next multiple of 8 columns.
- **C0 controls** other than tab and newline, and DEL, display as a space.
- **No selection** in T08.2. The session doesn't report `SelectableText`;
  copy is a later task designed around the single text buffer.
- **CI is the performance gate.** See [Reference host](#reference-host).

## Already working (kept as-is)

- `ManagedTextGuideLoader.LoadAsync` returns `TextGuideLoaded(Document,
  ContentChanged)` or `TextGuideLoadFailed(Error)` (T08.1).
- `TextGuideDocument` holds the normalized `Text` and `LineStarts`, with
  `LineAtOffset`, `Capture` and `Restore`.
- `IReaderSession`, `ReaderCapabilities` and `ReaderCommandPolicy` (T11.2),
  and `ReaderToolbar.SetSession`, which shows only supported commands.
- The `ReaderRoute` render path, guarded by `renderGeneration` (T05.3).

## Components

### TextLineView (Core/Text/TextLineView.cs)

Pure functions over a `TextGuideDocument` and a zero-based line index:

- `DisplayText(document, line)` returns the line without its `\n`. A tab
  becomes spaces up to the next multiple of 8 columns; U+0000–U+001F other
  than tab, and U+007F, become one space; every other UTF-16 code unit
  passes through.
- `DisplayColumns(document, line)` returns the length of `DisplayText`
  without building it.
- `SourceOffset(document, line, column)` maps a display column to an offset
  in `document.Text`, clamped to the line. A column inside an expanded tab
  maps to the tab. T08.3 uses it for capture and restore.

A column is one UTF-16 code unit, matching T08.1's offsets.

### TextLineList (Core/Text/TextLineList.cs)

A read-only `IReadOnlyList<TextLineItem>` and non-generic `IList` (which
`ListView` needs for virtualization) over one document:

- `Count` is the number of source lines. The empty line after a trailing
  `\n` is not counted, so a file ending in a newline has no extra blank
  row. An empty document has 0 rows.
- The indexer returns a new `TextLineItem(int Index, string Text)` built
  with `DisplayText`. Nothing is cached; the only line strings alive are
  those of realized rows.
- `IndexOf` and `Contains` use `TextLineItem.Index`, because each access
  returns a new item and `ListView` looks items up by value.
- Mutating members throw `NotSupportedException`.

### TextLineMetrics (Core/Text/TextLineMetrics.cs)

`MaxColumns(document, token)` scans every line once with `DisplayColumns`
and returns the widest, checking the token periodically. The shell runs it
on the thread pool.

### TextReaderView (Production/Reading/TextReaderView.xaml)

A `UserControl` wrapping a `ListView` (`AutomationId` `ReaderTextLines`):

- `SelectionMode=None`, `IsItemClickEnabled=False`, with item hover,
  pressed and selection visuals removed; a virtualizing `ItemsStackPanel`;
  horizontal scrolling `Auto`, vertical `Auto`.
- The item template is a `TextBlock`: Consolas at the default size,
  `TextWrapping=NoWrap`, `IsTextSelectionEnabled=False`, foreground from
  theme resources, on the existing reading-surface brush.
- **Fixed extent.** Row width is `MaxColumns × character width` and row
  height is one line, both measured once with a hidden Consolas `TextBlock`.
  The horizontal and vertical scroll ranges are therefore exact from the
  first frame and don't change as wide lines come into view.
- **Accessible names.** Each `ListViewItem` gets its line text as its
  automation name, so Narrator reads the line rather than the item type. A
  blank line is named "Blank line".

### TextReaderSession (Production/Reading/TextReaderSession.cs)

Implements `IReaderSession` and exposes `View`:

- `Format` is `Txt`; `Capabilities` is `Scroll`. `PageNavigation` arrives
  with T08.3 and `TextSize` with T14.1.
- `OpenAsync` is not used by the shell, which builds the session from a
  loaded document; it throws `NotSupportedException`.
- `GetLocationAsync` returns the first visible line's start through
  `TextGuideDocument.Capture`. T08.3 owns capture and restore;
  `RestoreLocationAsync` returns an unsupported outcome until then.
- `ExecuteAsync` accepts only scroll actions. `ApplyAppearanceAsync` is a
  no-op; theme brushes follow the app theme.
- `DisposeAsync` clears the items source.

## Data flow

Inside the `ReaderRoute` case of `ShellWindow.RenderCurrentAsync`, after the
heading, game and format load:

1. A non-TXT guide keeps today's placeholder and no toolbar session.
2. A TXT guide shows "Loading guide…" and calls
   `textLoader.LoadAsync(guide, token)`. The token belongs to the Reader and
   is cancelled when the route changes.
3. On `TextGuideLoaded`, `MaxColumns` runs on the thread pool; then the
   shell builds the session and view, sets `ReaderSurface.Content`, calls
   `ReaderActions.SetSession(session)` and shows "Guide ready." Focus does
   not move; T16.1 owns keyboard routing.
4. After every `await` the `renderGeneration` check runs. A stale document
   or session is dropped and disposed.
5. `ContentChanged` is ignored; T12.3 owns the hash comparison.

Leaving the Reader cancels the load token, calls `SetSession(null)`,
restores the placeholder and disposes the session. Reopening reloads from
disk; nothing is cached between visits.

The composition root in `ShellWindow` adds
`new ManagedTextGuideLoader(paths)` next to the publisher and remover.

Memory for `txt-long`: the file bytes are released after decoding, the text
is about 20 MB of UTF-16, and the line starts about 0.8 MB.

## Error handling

A load failure shows one sentence on the reader surface and the same
sentence as a warning status; the toolbar gets no session and no partial
text is shown. T15.1 adds recovery actions.

| Error | Message |
| --- | --- |
| Missing | "This guide's file is missing from the library." |
| TooLarge | "This guide is larger than the 64 MB limit for text files." |
| Unreadable | "This guide's file can't be opened. Close any app that's using it, then open the guide again." |
| InvalidMetadata | "This guide's saved details are damaged, so it can't be opened." |
| NotUtf8 | "This guide isn't valid UTF-8 text, so it can't be opened." |
| Undecodable | "This guide can't be read with its saved encoding." |

Cancellation shows nothing. Any other exception follows the shell's
existing render error path.

## Testing

### Core tests

- `TextLineViewTests`: tabs at columns 0, 3, 7 and 8; consecutive tabs; a
  tab after a long run; form feed, U+001A, ESC and DEL as one space;
  leading, trailing and internal spaces kept; `DisplayColumns` equals
  `DisplayText().Length`; `SourceOffset` round-trips, and a column inside a
  tab maps to the tab; out-of-range columns clamp.
- `TextLineListTests`: count with and without a trailing newline; an empty
  document has 0 rows; the same index gives equal text; `IndexOf` finds a
  freshly built item; mutation throws.
- `TextLineMetricsTests`: widest line including tab expansion; a cancelled
  token throws.

### Installed smoke

A new `txt-reader` mode in `tools/p1/windows_shell_ui_smoke.ps1`, run by
`production-shell-ui` through `windows_shell_install.ps1`:

- `ShellSeed` seeds TXT guides from fixture files, writing the managed
  copy, fingerprint and code page as publication does.
- `txt-ascii`: the first lines' UIA names equal the fixture's lines; the
  2048-column line makes the reader horizontally scrollable; the
  placeholder is gone and the toolbar shows no commands. Light and dark
  screenshots.
- New fixture `tests/fixtures/p1/txt-tabs.txt` (a tab-aligned table and a
  form feed): the names show the 8-column expansion and a space.
- `generated/txt-long.txt` with the P0 method: Open-to-first-ListItem time,
  realized ListItem count after opening and after eight `LargeIncrement`
  scrolls, and the `WM_NULL` response probe during scrolling. The script
  asserts first text ≤ 3 s, realized items ≤ 300 at both points, and no
  repeated response gap above 500 ms, and writes the numbers to the result
  JSON.
- A seeded TXT guide whose managed file is deleted shows the Missing
  message and no toolbar commands.
- Back from the Reader restores the placeholder; reopening renders again.
- `production-shell-ui` gains a `python tools/p0/make_fixtures.py` step.
- Existing modes that assert `ReaderPlaceholder` or "commands without an
  adapter" for a seeded TXT guide assert the text view instead.

### Reference host

The P1 design asks for a comparison with P0 on the Windows 11 x64 reference
host. Ruling: the CI run is the gate and records the numbers against the
thresholds; a hosted runner is generally slower than the reference host, so
passing there is conservative. `pcsx2-win` is used only if CI fails or a
measure is within 20% of its threshold.

### Traceability

- TR08.1: `TextLineViewTests` and the `txt-ascii` and `txt-tabs` checks.
- TR08.2: `TextLineListTests` and the `txt-long` realized-item checks.

## Known limits

- A character outside the Basic Multilingual Plane counts as 2 columns.
- An East Asian wide character takes two cells but counts as 1 column, so
  alignment after it can drift; the text stays readable.

## Out of scope (follow-ups)

- Page Up/Down, Home/End and offset/context capture and restore: T08.3.
- Text size: T14.1. Theme setting: T14.2.
- Selection and copy: a later task; Find: S20.
- Recovery actions for load failures: T15.1.
- Narrator audit: T16.2.

## Documentation

- This design and its plan, `t08-2-txt-view-plan.md`.
- T08.2 notes in [p1-technical-design.md](../p1-technical-design.md) S08
  and [work-breakdown.md](../work-breakdown.md) S08.
- A TXT reader row in [e2e-testing.md](e2e-testing.md).
- After merge: `progress.md`, `implementation-plan.md` and this status line.

## PR outcome

The PR names T08.2, its prerequisite T08.1 (merged, PR #28), and the
outcome: TXT guides open in a virtualized fixed-width Reader view with
light and dark screenshots, and the `txt-long` measures beside P0's.

## T08.2 verification record

- **Unit tests.** On `pcsx2-win`, Core 371/371 and Infrastructure 438/438
  passed, and the Production build had 0 warnings.
- **Installed.** CI run
  [36946245355](https://github.com/ilya-slalom/desktop-guides/actions/runs/36946245355)
  on `0042155` passed every job, `production-shell-ui` included:
  - `txt-reader`, light and dark: `txt-ascii`, `txt-tabs`, `txt-legacy`,
    `txt-long`, `txt-missing`, `html-placeholder` and `txt-reopen`. The
    full 2,064-character row names on txt-ascii matched, so no truncation
    was seen.
  - `txt-long` against P0 (902 ms to first text, 98 rows realized):

    | Measure | Light | Dark | Limit |
    |---|---|---|---|
    | First text | 133 ms | 138 ms | 3,000 ms |
    | Rows realized after open | 35 | 26 | 300 |
    | Rows realized after 8 large scrolls | 56 | 56 | 300 |
    | Slowest `WM_NULL` response | 3 ms | 0 ms | under 2 over 500 ms |
    | Response timeouts | 0 | 0 | 0 |

  - Back during a held load: `txt-load-paused`, `txt-back-during-load` and
    `txt-load-released`. Back reached the Game page while the load was
    held at the `TextLoad` gate, and the released load reported nothing.
- **Not seen.** The gated back-during-load modes were never run against
  the code before `7888504`, so their failure on the old queueing is
  argued, not observed.
- **CI fixes.** Run
  [36885091223](https://github.com/ilya-slalom/desktop-guides/actions/runs/36885091223)
  failed because the smoke window showed only two or three of the six
  seeded guides and Tab Table Guide was off screen; `0042155` scrolls each
  guide into view before opening it.
- **Evidence.** ASCII Map Guide:
  [light](evidence/t08-2-txt-view/txt-reader-light.png) and
  [dark](evidence/t08-2-txt-view/txt-reader-dark.png).
- **Follow-up.** Rows clip after a Windows text-size change until the
  guide is reopened: issue #29.
