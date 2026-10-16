# P2 implementation plan and exit gates

Status: M7 complete, 15 October 2026 (T18.1: #113, #116 and #117). M8 complete, 16 October 2026 (T18.2: #118 and #119). M9 in progress: T19.1 PR a merged (#126); PR b (the Bookmarks tab) in review. P1 is complete (54 of 54 tasks; v1.0.1
published). This plan orders the **3 P2 stories and 6 tasks** in the
[work breakdown](../work-breakdown.md#p2--reader-depth-and-local-ownership).

- **The architecture:** the [P2 technical design](../p2-technical-design.md)
  defines the reader-contract extensions, the data, and the behaviour.
- **The detail for each task:** like P1, each task gets its own design note
  (`docs/p2/t<id>-*-design.md`) and code-level plan (`docs/p2/t<id>-*-plan.md`)
  before implementation.
- **Results:** they are recorded in `docs/p2/results.md`, created with the
  first merge.

## Delivery rules

P1's [delivery rules](../p1/implementation-plan.md#delivery-rules) apply
unchanged. In summary:

1. **Small reviewed changes.** Each passes a locked restore and the
   relevant tests. Tests for locators, storage, migrations and security come
   first (TDD).
2. **Windows verification.** Core and Infrastructure tests run headless.
   Installed UI checks run in Windows CI and, where needed, on the Windows
   host through the interactive scheduled task, following the
   [E2E procedure](../p1/e2e-testing.md).
3. **What makes a task done:** its output exists, and its exit check has a
   recorded result. A passing unit test doesn't close a task that needs
   installed UI or UI Automation evidence.
4. **The PR description** names the task, its prerequisites and their
   status, and the outcome, with a screenshot for any UI change.

P2-specific rules:

5. **Fixtures are authored, never copied from real guides.** Real guides
   are used only for the survey's observations (T21.1) and for triage, as
   AGENTS.md asks.
6. **New stored data** (bookmarks, and the TXT view preference) ships with
   its migration, backup export, archive verification and restore tests in
   the same PR.
7. **New installed scenarios** go in the `navigation` scenario group and CI
   shard, which T18.1 creates. Every shard stays within about 10 minutes.

## M7: in-guide search

**Exit:**
- TXT, HTML and PDF guides each find next and previous matches, with
  counts.
- Searching never changes the stored reading place.
- PDF image-only pages are reported.
- Search and its controls appear only for guides that support it.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T18.1 | P1 complete (T08.3, T09.3, T10.3, T11.3, T12.3) | `IFindableReader` and `FindQuery`, `FindResult` and `FindOutcome` in Core. TXT search over the normalized text; HTML search through an injected text walker, highlighting with the CSS Custom Highlight API; PDF background page scanning through `PdfPageTextSource`, with partial counts and image-only reporting. The find bar: Ctrl+F, F3 and Shift+F3, Enter and Shift+Enter, Escape, "Back to my place", and announcements. **Exit:** Core matching tests (case, whitespace runs, line breaks); Infrastructure tests of PDF scanning (cancellation, a broken page, image-only counts); the `navigation` group's `find-txt`, `find-html` and `find-pdf` modes on both lanes, each asserting counts, the match position, and the stored locator unchanged before and after searching. | TR18.1 |

**Optional M7 follow-ups:** #45 (Ctrl+wheel PDF zoom) and #50 (Dark style
changes image and SVG colours). Each is its own PR, with its own triage
against the AGENTS.md prevalence rule.

## M8: table of contents

**Exit:**
- HTML headings and in-page contents lists, PDF outlines, and detected TXT
  sections each form a navigable contents tree.
- A guide with no sections shows the fallback.
- Failing to detect sections never blocks opening or scrolling.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T18.2 | T18.1 | `IOutlineReader` and `Outline`, `OutlineSource` and `OutlineEntry` in Core. HTML heading and in-page contents collection; PDF outline mapping through PdfPig; Core `TextSectionDetector` (search codes, underlined headings, numbered headings, a threshold of three sections, a 2 s budget). The left pane's Contents tab, with a native `TreeView` (fixed width, so no `GridSplitter`), Ctrl+Shift+C, the current entry selected, and the "No sections found" fallback linking to Search and Bookmarks. **Exit:** detector tests over authored fixtures, including the cases where it must find nothing (ASCII art, a short guide, prose without headings); PDF outline tests; `contents-txt`, `contents-html` and `contents-pdf` modes asserting the tree, navigation, the saved place, focus, and the fallback on a guide with no sections. | TR18.1 |

## M9: bookmarks

**Exit:**
- Bookmarks can be added, renamed, deleted with undo, and followed, in all
  three formats.
- They survive a restart, and a backup and restore.
- Each restore outcome is shown honestly.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T19.1 | T18.2 (the shared left pane, and outline labels) | The `Bookmark` table and its migration (`guideId` deleted with its guide, a locator of at most 4 KiB, a label of 1 to 80 characters, at most 500 per guide); the repository; backup export, archive verification and restore with bookmarks. The Bookmarks tab, Ctrl+D and Ctrl+Shift+B, inline labels, F2, and Delete with a 10 s undo. Restore statuses for `Approximate` and `Unavailable`, and damaged bookmarks offering only Delete. **Exit:** repository, limit and migration tests; export, verification and restore tests with bookmark rows, including a backup from before P2 and a damaged locator; `bookmark-*` modes asserting add, rename, undo, follow, the persistence of the place, survival across a restart, and the `Unavailable` status after a content change. | — |

## M10: TXT reflow

**Exit:**
- TXT guides can switch between Original and Reflow, per guide.
- The place is kept across switches.
- Diagrams and tables keep their layout.
- The source bytes and locators never change.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T19.2 | T18.2 (`TextSectionDetector` headings), T19.1 | Core `TextReflowPlanner` (prose and preserved blocks, with source ranges; a 2 s budget). The reflow view: prose wraps, preserved blocks stay monospace and scroll horizontally, and text size applies. A Toolkit `Segmented` control, `Original` or `Reflow`, saved per guide in its preferences row (a migration, with backup coverage). Search, contents and bookmarks work in both views. **Exit:** planner tests over authored fixtures (ASCII art, maps, tables, lists and wrapped prose), with an offset round trip for every block; a check that the imported file's hash and the normalized text are unchanged; `reflow-*` modes asserting the top source offset kept across switches, the preference after a restart, and the "no prose to reflow" fallback. | TR19.1 |

## S21: selected local material formats

**Exit for S21:** each added format meets TR21.1 and the S21 acceptance.
That means a documented importer, reader controls, a locator, an offline
check and failure behaviour, all in place before it is marked supported.

| Task | Prerequisites | Output and verifiable exit | TR |
| --- | --- | --- | --- |
| T21.1 | None (research; can run alongside M7–M10) | `docs/p2/format-survey.md`. It samples representative guides from major public game-guide sites across genres and eras. For each candidate format (image maps, multi-page HTML, CBZ, EPUB, Markdown, and others found), it records frequency, whether the format can be read offline, import-security risk, locator needs, and the likely reader. The output is a ranked list with its uncertainty stated. No sample is committed. **Exit:** the survey is reviewed and its ranking approved by the user. | — |
| T21.2 | T21.1; for each format, its own design; T19.1 for backup coverage | One importer and reader adapter per format, in the survey's order. Each extends the backup tests and states its locator and offline behaviour. **Exit,** for each format: the importer refuses unsafe archives (no extraction outside the staging folder) and untrusted content; the format's reader modes pass; the offline check passes; and the format is listed as supported only after that. | TR21.1 |

## Verification

- **CI:** the `navigation` group joins the installed shard matrix on both
  lanes from T18.1. The release CI gate covers P2 automatically through the
  `windows-ci` push run.
- **Releases:** a P2 feature reaches users through the T17.3 candidate
  process. That's the `rc` group on the draft's assets, plus a new
  tested-matrix row.
- **Deferred:** the high-contrast, Narrator and full accessibility passes
  stay in the
  [final milestone](../work-breakdown.md#final-milestone--accessibility-audit-and-store-submission).
  P2 asserts names, focus and announcements for its own controls.

## Traceability

| Story | Tasks | TRs | Design |
| --- | --- | --- | --- |
| S18 search and contents | T18.1, T18.2 | TR18.1 | [Sections 3 and 4](../p2-technical-design.md#3-s18-search-t181) |
| S19 bookmarks and reflow | T19.1, T19.2 | TR19.1 | [Sections 5 and 6](../p2-technical-design.md#5-s19-bookmarks-t191) |
| S21 local formats | T21.1, T21.2 | TR21.1 | [Section 7](../p2-technical-design.md#7-s21-the-format-survey-t211) |
