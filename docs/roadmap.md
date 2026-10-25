# Roadmap

Open work only. Each PR names the roadmap item it targets, and a finished
item is removed from this page. Earlier plans and records are under the
`docs-history-2026-10-25` tag.

IDs: `S` is a story, `T` a task, `TR` a testable technical requirement.
IDs are stable.

## Final milestone: accessibility audit and Store submission

Runs once, after every feature task, so the audit and the Store review
cover the whole app. Order: T16.2 and T16.3, then T17.5 last. T17.8 and
T17.7 can run at any point before T17.5.

| ID | Task | Done when |
| --- | --- | --- |
| T16.2 | Accessibility audit across every feature | Keyboard order, focus restoration, AutomationProperties, touch target size, DPI scaling (including 200%) and high contrast are audited and fixed. The keys while focus is inside an HTML guide page are decided (#115). The high-contrast pass saves and restores the active `.theme` and wallpaper exactly, or runs in a disposable profile or VM. |
| T16.3 | PDF document-text access | The PDF engine's document-text access behavior is recorded, with an accurate user-facing limitation if needed. Closes S10. |
| T17.8 | Clean-install check in a VM | In a runtime-free Windows 11 x64 Hyper-V VM with a checkpoint, the signed MSIX fails clearly without its prerequisite and the offline installer recovers it, with the network disconnected for the offline steps. Needs Hyper-V on the Windows host, unavailable for now. Passing it allows the clean-install claim. |
| T17.7 (optional) | Rewrite the installed harness in C# | First measure the per-scenario PowerShell overhead and try the cheap wins (precompiled helpers, one test process per app session). If it goes ahead, port incrementally to MSTest and a UIA3 library, keep CI green throughout, and use no retries on release gates. |
| T17.5 | Microsoft Store submission (last) | Submitted with the reserved identity `IlyaLissoboi.DesktopGuides` after T16.2 and T16.3, certification passed, listing written, and the Store-signed package confirmed to upgrade sideloaded installs in place. Decides whether sideloaded releases continue, and whether the Store build includes GameFAQs and Neoseeker (a build switch; see [sources](sources.md)). |

TR16.1 (no essential flow depends on hover or a keyboard-only gesture),
TR16.2 (reader overlays return focus to the invoking control) and TR10.3
(a text-capable PDF exposes the tagged fixture's text through UI
Automation) get their full evidence in this milestone.

## Later candidates

No phase yet. Each needs its own design before it's scheduled.

### S23: sync library and progress across devices

Optional sync that never loses locally stored guides or places. Sync can be
disabled, offline edits stay available, and conflicting places and guide
revisions follow a documented, testable rule. It builds on backup and
restore.

- **T23.1** Choose the transport and identity model; document privacy and
  storage costs first.
- **T23.2** Define content IDs, change tracking, conflict UI and rules, and
  backup-based recovery tests.
- **TR23.1** Sync is opt-in, and can't silently replace a newer local guide
  or completion state with stale remote data.

### S24: expand desktop reading workflows

Optional controller navigation, second-window reading and map annotations.
Each keeps guide places independent, stays keyboard-accessible, and can be
turned off without affecting the base reader.

- **T24.1** Prototype controller mapping and multiple-window state
  ownership.
- **T24.2** Prototype annotations as overlays stored apart from imported
  originals.
- **TR24.1** Two windows can't overwrite each other's saved place without
  an explicit last-active-window rule.
- **TR24.2** Annotations never modify imported guide files.

### More guide formats

MHTML, CBZ, EPUB and Markdown, in that order. Each needs its own design, and
a documented importer, reader controls, locator, offline check and failure
behavior before it's marked supported. Archives can't extract outside their
staging root.

## Known gaps

- **ARM64 is not released.** It needs its own native full-workflow result
  first.
- **Windows 10 is not advertised.** It has no tested row.
- **No clean-install claim** until T17.8 passes.
- **MSIX not live-checked for online sources.** The live checks used the
  portable build; the MSIX runs the `online` group against fixtures only.
- **The portable build's own release gates** (first run without
  installation, data location, replacing the exe) aren't defined yet.

## Known issues worth tracking

- [#138](https://github.com/ilya-slalom/desktop-guides/issues/138):
  intermittent installed UI failures in CI.
- [#94](https://github.com/ilya-slalom/desktop-guides/issues/94): a reading
  progress save intermittently times out and is recorded as failed.
- Deferred minor findings a reader could meet:
  - **Steam:** if a library save fails, saving the same guide again leaves
    a duplicate unfinished save; the game list's failure wording, and its
    loads can't be cancelled.
  - **VGMaps:** map rows are read by cell position, so a differently laid
    out row is listed as not saveable; titles ending in parentheses are
    read as a variant; a game inside a compilation's table lists the
    compilation's maps.
  - **Reader:** a pane remembered open doesn't reappear when a narrow
    window is widened; adding a bookmark counts as opening the pane.
- **VGMaps courtesy note** to the site's owner: optional, the maintainer's
  to send.
