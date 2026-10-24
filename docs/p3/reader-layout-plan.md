# T14.5 and T18.3 (reader layout) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two reader changes the user asked for during P3's live checks.
- **T14.5:** text guides sit centered in the reader. ASCII art and diagrams keep their alignment.
- **T18.3:** the Contents pane stays open across restarts, and opens on its own for multi-page HTML guides until the reader chooses.

**Architecture:**
- **Core:** `ContentsPanePolicy.ShowOnOpen`, a pure rule for whether the pane shows when a guide opens. `AppSettings` gains `ContentsPaneOpen`.
- **Infrastructure:** the setting is stored in the `Settings` key/value table. There is no schema change.
- **Production:**
  - `TextReaderView`: the original view centers its rows;
  - `TextReflowView`: the reflow view caps and centers its column;
  - `ShellWindow.Contents.cs` follows the policy and stores the reader's choice.

**Tech Stack:** .NET 10, WinUI 3, xUnit, PowerShell UI Automation.

**Spec:** the user's decisions in chat on 23 and 24 October 2026, recorded as T14.5 and T18.3 in [the work breakdown](../work-breakdown.md):
- **centering:**
  - text guides are always centered, with no setting;
  - the original view centers the block of lines, each row as wide as the widest line;
  - the reflow view keeps prose to about 80 characters and centers it;
- **Contents pane:**
  - remember whether it's open;
  - until the reader has opened or closed it, open it on its own for multi-page HTML guides, and only when the window is at least 720 px wide;
- **delivery:** its own PR after #148 and #149.

Built on [T18.2's contents design](../p2/t18-2-contents-design.md) and [T19.2's reflow design](../p2/t19-2-reflow-design.md).

## Global Constraints

- **Alignment:**
  - In the original view, every row keeps `MinWidth = rowWidth`, the widest line's display columns times the cell width. Rows share one left edge, so columns line up.
  - A guide wider than the window scrolls sideways as today; centering only applies when the block is narrower than the window.
- **Reflow:** every block, prose or preserved, is at most `ReflowMeasure` wide (40em of the body font at the guide's text scale, about 80 characters), and the blocks share one left edge. A preserved block wider than that scrolls sideways on its own, as today.
- **Pane state:**
  - The setting has three values: `null` means the reader hasn't chosen, `true` means open, and `false` means closed.
  - Only the reader's own open and close actions write it. An automatic open, and a close caused by following an entry in a narrow window, don't.
- **Narrow windows:** below 720 px the pane covers the guide. When a guide opens in a window that narrow, the pane stays closed, whether it was remembered open or would open on its own. The remembered value doesn't change.
- **Unchanged:** reading locations, find, bookmarks and the outline itself.

## Review Focus

1. A TXT guide exactly as wide as the window, give or take a pixel, doesn't flicker between centered and scrolled. The horizontal scroll position restores as today.
2. Changing the text size re-centers the block. The saved horizontal offset scaling in `TextReaderView` still holds.
3. In the reflow view, a short paragraph and a long one share their left edge, because the column width is fixed rather than set by each block's content.
4. Opening a single-page HTML, TXT or PDF guide with the setting still `null` doesn't open the pane. A multi-page HTML guide opens it once the outline host is ready, with no flash in a narrow window.
5. Installed modes that expect the pane closed, such as `contents-*` and `html-pages-*`, still start with it closed. Seeds that open multi-page HTML set the setting to `false` unless the mode tests this behavior.

## Host commands

`/tmp/s22d.sh <project> [filter]` syncs the branch to the host and runs a
test project. Installed runs use CI, or the scheduled-task route in
[p1/e2e-testing.md](../p1/e2e-testing.md).

### Task 1: The pane setting and its policy

**Files:**
- Create: `src/DesktopGuides.Core/Reading/ContentsPanePolicy.cs`
- Modify:
  - `src/DesktopGuides.Core/Library/LibraryModels.cs` (`AppSettings`)
  - `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (read and write `ContentsPaneOpen`)
- Test: `tests/DesktopGuides.Core.Tests/Reading/ContentsPanePolicyTests.cs`, `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `AppSettings(…, bool? ContentsPaneOpen = null)`;
  - `public static bool ContentsPanePolicy.ShowOnOpen(bool? remembered, bool multiPageHtml, bool narrow)`.

- [ ] **Step 1: Write the failing tests:**
  - `ShowOnOpen`:
    - `(null, true, false)` → true;
    - `(null, false, false)` → false;
    - `(true, false, false)` → true;
    - `(false, true, false)` → false;
    - `(true, true, true)` → false;
    - `(null, true, true)` → false;
  - repository:
    - a new library reads `ContentsPaneOpen` as null;
    - saving true, then false, round-trips across reopening;
    - a stored value other than `true` or `false` reads as null.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** The key is `ContentsPaneOpen`, and its values are `true` and `false`. Saving null deletes the row.
- [ ] **Step 4: Run Core and Infrastructure tests; they pass.**
- [ ] **Step 5: Commit** `feat(t18.3): remember the Contents pane's open state`.

### Task 2: The reader follows and stores the pane state

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.Contents.cs`, and the reader open path that calls `StartContents`.
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`, `tools/p1/windows_shell_install.ps1`, `tools/p1/DesktopGuides.ShellSeed/Program.cs`.

**Interfaces:**
- Consumes: Task 1's `ShowOnOpen` and `AppSettings.ContentsPaneOpen`.

- [ ] **Step 1: Write the failing installed checks:**
  - `contents-remember`: open a TXT guide, open Contents with Ctrl+Shift+C, close and restart the app, and open the guide again; `ReaderContentsHost` is visible. Close it, restart, and it's collapsed.
  - `contents-auto`: on a seed with the setting null, a multi-page HTML guide opens with the pane visible, and a TXT guide opens with it collapsed. Then, at 600 px, the multi-page guide opens with it collapsed.
  - Both join the `navigation` group, after `contents-*`.
- [ ] **Step 2: Run the group in CI; the two modes fail.**
- [ ] **Step 3: Implement.**
  - `contentsOpen` is loaded from settings at startup.
  - `StartContents` shows the host when `ShowOnOpen(setting, guide is HTML with more than one page, ContentsNarrow)` holds.
  - The open and close actions write the setting through `UpdateSettingsAsync`. The automatic open, and the narrow close after following an entry, don't.
  - Seeds whose modes expect a closed pane write `ContentsPaneOpen=false`. These are `seed-html-reader`, the multi-page seeds and `seed-navigation`, wherever an HTML guide has more than one page.
- [ ] **Step 4: Run the full installed set in CI; it passes.**
- [ ] **Step 5: Commit** `feat(t18.3): the Contents pane reopens as it was left and opens on its own for multi-page guides`.

### Task 3: Centered text guides

**Files:**
- Modify: `src/DesktopGuides.Production/TextReaderView.xaml(.cs)`, `TextReflowView.xaml(.cs)`, `tools/p1/windows_shell_ui_smoke.ps1`.

- [ ] **Step 1: Write the failing installed checks** in a new `txt-centered` mode, after `reflow-*` in its group:
  - **original view:** on a TXT guide narrower than the window, the rows' left edges are equal within 1 px. The gaps between the block and `ReaderTextLines`' left and right edges differ by at most 2 px, scroll bar excluded. On the 160-column fixture, the horizontal scroll bar is present and the first row starts at the list's left edge;
  - **reflow view:** prose blocks share a left edge within 1 px, no block is wider than `ReflowMeasure`, and the column is centered within 2 px.
- [ ] **Step 2: Run in CI; it fails.**
- [ ] **Step 3: Implement.**
  - **Original view:** the item container's `HorizontalContentAlignment` becomes `Center`; rows keep `MinWidth = rowWidth`. Expose `rowWidth` in the existing text diagnostics.
  - **Reflow view:** each block's root `Grid` gets `MaxWidth = ReflowMeasure` and `HorizontalAlignment="Stretch"`, and the container's `HorizontalContentAlignment` becomes `Center`. `ReflowMeasure` is recomputed when the text scale changes.
- [ ] **Step 4: Run the full installed set in CI; it passes.** Take screenshots of both views for the PR.
- [ ] **Step 5: Commit** `feat(t14.5): text guides sit centered, in both views`.

### Task 4: Records

- [ ] **Step 1:** Mark T14.5 and T18.3 implemented in the work breakdown, with the PR number.
- [ ] **Step 2:** Write the PR description: the target tasks, prerequisites (#148 and #149, merged), the intended outcome, and screenshots of the centered original view, the reflow view and the self-opened pane.
