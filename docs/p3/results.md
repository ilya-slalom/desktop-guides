# P3 results

Merged P3 work, in the [P2 results](../p2/results.md) format. P3 holds S22,
guides from supported online sources, delivered as the
[P3 technical design](../p3-technical-design.md#6-delivery) planned, plus two
follow-ups the live checks asked for.

| PR | Merged | Commit | Task | Final CI run | Core | Infra | Record | Caveats |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [#153](https://github.com/ilya-slalom/desktop-guides/pull/153) | 2026-10-25 | `452ccb0` | T22.2 (PR 5, VGMaps) | [40149358232](https://github.com/ilya-slalom/desktop-guides/actions/runs/40149358232) | 1584 | 1140 | [PR 5 record](../p3-technical-design.md#pr-5-verification-record) | The atlas's index, a game's maps, and saving them as image maps, GIF as PNG. The installed check found a late progress report replacing a save's outcome. The review found four Important issues, all fixed; among them, maps now stream to a file, timed by silence. Live check passed. |
| [#152](https://github.com/ilya-slalom/desktop-guides/pull/152) | 2026-10-24 | `7d484fb` | T22.2 (PR 4b, Steam game picker) | [40100561518](https://github.com/ilya-slalom/desktop-guides/actions/runs/40100561518) | 1578 | 1118 | [PR 4 record](../p3-technical-design.md#pr-4-verification-record) | From the Steam live check: a game is matched to its Steam app by name from Steam's game list, not by a pasted URL. The review found four Important issues, all fixed. One re-run: MSIX `provider-settings` (#138). Live check passed. |
| [#151](https://github.com/ilya-slalom/desktop-guides/pull/151) | 2026-10-24 | `646704c` | T22.2 (PR 4, Steam) | [40084110165](https://github.com/ilya-slalom/desktop-guides/actions/runs/40084110165) | 1578 | 1105 | [PR 4 record](../p3-technical-design.md#pr-4-verification-record) | Steam Community guides: search with the user's key, or a pasted URL; fetched and saved by the app. The installed checks found Steam images dropped (addresses with no type) and two site-picker races. The review, with a live check of six public guides, found five Important issues, all fixed. |
| [#150](https://github.com/ilya-slalom/desktop-guides/pull/150) | 2026-10-24 | `870f11c` | T14.5, T18.3 | [40067216378](https://github.com/ilya-slalom/desktop-guides/actions/runs/40067216378) | 1550 | 1059 | [reader layout plan](reader-layout-plan.md) | From the live checks: text guides sit centered, in both views, and the Contents pane is remembered and opens on its own for multi-page HTML. The review found the reflow column too narrow for 80-column art; fixed. Two re-runs (#138). |
| [#149](https://github.com/ilya-slalom/desktop-guides/pull/149) | 2026-10-24 | `77c6a13` | T22.2 (copy contents) | [40052479341](https://github.com/ilya-slalom/desktop-guides/actions/runs/40052479341) | 1544 | 1055 | [#149](https://github.com/ilya-slalom/desktop-guides/pull/149) | From the second live check: a saved copy's list of pages is hidden, and its title is no heading, so the Contents pane leads. |
| [#148](https://github.com/ilya-slalom/desktop-guides/pull/148) | 2026-10-24 | `d2bb70a` | T22.2 (PR 3b, Find online) | [39981619645](https://github.com/ilya-slalom/desktop-guides/actions/runs/39981619645) | 1544 | 1054 | [PR 3b record](../p3-technical-design.md#pr-3b-verification-record) | Find online, the in-app browser, and capture of GameFAQs and Neoseeker guides through it. Capture moved to the visible browser by the user's choice. The first live check found six issues, all fixed in [the live-check fixes](s22-live-check-fixes-plan.md), among them a library schema check that compared line endings. |
| [#147](https://github.com/ilya-slalom/desktop-guides/pull/147) | 2026-10-20 | `f083736` | T22.2 (PR 3a, capture core) | [39415801302](https://github.com/ilya-slalom/desktop-guides/actions/runs/39415801302) | 1522 | 1009 | [PR 3a record](../p3-technical-design.md#pr-3a-verification-record) | The two adapters, the offline copy, unfinished captures and saving. The review checked the adapters against archived pages. |
| [#146](https://github.com/ilya-slalom/desktop-guides/pull/146) | 2026-10-20 | `656dace` | T22.2 (PR 2, source fields) | [39402619411](https://github.com/ilya-slalom/desktop-guides/actions/runs/39402619411) | 1522 | 949 | [PR 2 record](../p3-technical-design.md#pr-2-verification-record) | Schema 9: a guide's source, and saving a guide again in place, safely. |
| [#145](https://github.com/ilya-slalom/desktop-guides/pull/145) | 2026-10-20 | `dcb5d93` | T22.1 | [39393529037](https://github.com/ilya-slalom/desktop-guides/actions/runs/39393529037) | 1513 | 922 | [source review](source-review.md) | The per-source terms review and the P3 design. |

## Live checks

Done by hand by the user on pcsx2-win (Windows 11 x64) with the portable
build from each PR's run, against the live sites. CI never reaches a live
site: its fixtures are synthetic.

| Date | Build | Checked | Found | Outcome |
| --- | --- | --- | --- | --- |
| 23 October 2026 | #148 | Starting the portable build on the user's library | "Your library database can't be read": the schema check compared stored SQL as text, line endings included, so a CI build (CRLF) refused a library an LF build made | Fixed in #148: the check ignores line endings, and C# sources are pinned to LF |
| 23 October 2026 | #148 | GameFAQs text FAQ and HTML guide; Neoseeker text FAQ and Final Fantasy VII Rebirth walkthrough | All saved and read. No styles. A same-site link opening a new tab left the app. Links to the walkthrough's side-quest articles stayed online. | Fixed in #148 ([live-check fixes](s22-live-check-fixes-plan.md)): built-in styles, linked articles one level deep, new tabs kept in the app, and two link fixes |
| 24 October 2026 | #148 | The same guides again | "Downloaded successfully and with all content." The in-page contents list took space; the Contents pane's outline had extra levels. | #149 (the copy) and #150 (the pane) |
| 24 October 2026 | #151 | Steam search and save | A game with one clear match worked; others asked for a store URL | #152 (pick the game by name), then "works as expected" |
| 25 October 2026 | #153 | VGMaps search, a game's maps, saving and saving again | — | "Everything is working as expected" |

## What P3 was tested on

| Source | What | Unit tests (x64; Core and Infrastructure also native ARM64) | Installed `online` group (MSIX and portable, x64) | Live check (portable, x64) |
| --- | --- | --- | --- | --- |
| GameFAQs | Text FAQ | Yes | Yes (`online-save`) | Yes |
| GameFAQs | HTML guide of several pages; pause, resume and saving again | Yes | Yes (`online-save`, `-resave`, `-pause`, `-resume`) | Yes |
| Neoseeker | Text FAQ | Yes | — | Yes |
| Neoseeker | Wiki walkthrough with linked articles | Yes | — (the article loop is unit-tested) | Yes |
| Steam | Search with a key, the game picker, a pasted URL without one, a refused image | Yes | Yes (`online-steam-limit`, `-nokey`, `-search`) | Yes |
| VGMaps | The atlas's index, a game's maps, PNG and GIF, saving again | Yes | Yes (`online-vgmaps`) | Yes |

**Not tested:**
- **MSIX live:** the live checks used the portable build.
- **ARM64 installed:** ARM64 isn't released.
- **A release candidate of P3's version:** the next tested-matrix row comes from that release's own run on its draft assets ([how a row is made](../p1/release/tested-matrix.md#how-a-row-is-made)).

## Deferred

Each PR's record lists the Minor findings it deferred. The ones a reader
could meet:
- **Steam:**
  - if a library save fails, saving the same guide again leaves a duplicate unfinished save;
  - the game list's wording when it fails, and its loads can't be cancelled.
- **VGMaps:**
  - a map row's cells are read by position; a differently laid-out row is listed as not saveable;
  - titles ending in parentheses are read as a variant;
  - an included game inside a compilation's table lists the compilation's maps.
- **The reader:**
  - a pane remembered open doesn't reappear when a narrow window is widened;
  - adding a bookmark counts as opening the pane.

#138 tracks the CI flakiness seen during P3, and #94 a save that times out.
