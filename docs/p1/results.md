# P1 implementation results

Status: P1 work is merged into `main` through
[PR #57](https://github.com/ilya-slalom/desktop-guides/pull/57) (a T14.4 harness fix,
merge commit `0f10a1c`) on 8 October 2026. The [merged PR summary](#merged-pr-summary)
lists every PR since M0 with its CI run and test counts. The P1 first usable
release remains in progress: the [dependency plan](implementation-plan.md)
defines all task exit gates, and this file records only checks actually run.

M0 merged through [PR #3](https://github.com/ilya-slalom/desktop-guides/pull/3)
on 26 September 2026, including the SQLite initialization review fix.
T07.1 merged through PR #9 on 27 September 2026 at
`7039aef8127f4fcf45f1547344b4e88dad849b59`; see the
[static-asset result](t07-static-assets-results.md). T07.2 merged through
PR #10 the same day at `72f43785fad2b01b3f79b739017f0623d46b6665`; see the
[static-boundary result](t07-static-boundary-results.md). T04.1 merged through
PR #11 on 28 September 2026 at `e18964f1d253746a3e6cdc0d51c659a71a531bc3`.

On 28 September 2026, requirements review added T04.4 (provider-backed,
search-first game addition with a cached offline snapshot, and T04.1 as the
manual fallback) and UI planning added T11.4, T05.4, and T14.4. T11.4 merged
in PR #13 and T04.4, using IGDB and SteamGridDB, in PR #14; T05.4 merged in
PR #16. T14.4 merged in PR #54.

## Merged PR summary

One row per PR merged into `main` after M0, through PR #57. The *Record* column
links the task's own verification section, which has the full evidence.

- *Final CI run* is the run the PR cites as decisive. Where no CI run is
  cited, it says *Host only* or *—*. Every cited run passed every job it ran
  unless the caveat says otherwise.
- *Core* and *Infra* are the Core.Tests and Infrastructure.Tests pass counts.
  *(host)* marks counts from a unit-test run on `pcsx2-win`, not CI. *—* means
  the PR states no count.
- PRs record `shell-scope` only from #38 onward, when the input was added.
  From #47 a full run splits `production-shell-ui` into four shards.
- Tasks for #33 (a T08.3 harness follow-up) and #37 are inferred from the PR
  titles. #15, #21, #38, #43, #47, #53, #55 and #56 aren't task PRs.

| PR | Merged | Commit | Task | Final CI run | Core | Infra | Record | Caveats |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [#13](https://github.com/ilya-slalom/desktop-guides/pull/13) | 2026-09-29 | `23e0694` | T11.4 | Host only | 73 | 98 | [t11-design-language-plan](t11-design-language-plan.md#verification-record) | Signed interactive install on `pcsx2-win`, no CI run cited. High contrast and 200% scaling deferred to T16.2. |
| [#14](https://github.com/ilya-slalom/desktop-guides/pull/14) | 2026-09-29 | `3e490de` | T04.4 | Host only | 150 | 221 | [below](#m2-t044-provider-search--implementation-check-29-september-2026) | Installed provider pass and blocked-network run on `pcsx2-win`. The offline run used a package from before the thumbnail fix. |
| [#15](https://github.com/ilya-slalom/desktop-guides/pull/15) | 2026-09-29 | `a4bb44c` | Portable build | Host only | 150 | 233 | — | Portable scenario suite on `pcsx2-win`. The signed-MSIX harness path wasn't run end to end. |
| [#16](https://github.com/ilya-slalom/desktop-guides/pull/16) | 2026-09-29 | `515440a` | T05.4 | [36577997048](https://github.com/ilya-slalom/desktop-guides/actions/runs/36577997048) | 165 | 233 | [t05-4-catalog-components-design](t05-4-catalog-components-design.md#t054-verification-record) | Counts come from the earlier run [36572000282](https://github.com/ilya-slalom/desktop-guides/actions/runs/36572000282); the later commit was docs-only. |
| [#17](https://github.com/ilya-slalom/desktop-guides/pull/17) | 2026-09-30 | `f359ca7` | T06.1, T06.2 | [36658419215](https://github.com/ilya-slalom/desktop-guides/actions/runs/36658419215) | 198 | 294 | [t06-1-import-preview-design](t06-1-import-preview-design.md#t061--t062-verification-record) | Every job passed, including x64 and ARM64 packages. |
| [#19](https://github.com/ilya-slalom/desktop-guides/pull/19) | 2026-09-30 | `494cb02` | T06.3 | [36669062806](https://github.com/ilya-slalom/desktop-guides/actions/runs/36669062806) | 198 | 333 | [t06-3-import-publication-design](t06-3-import-publication-design.md#t063-verification-record) | Push run. The pull-request run [36669095974](https://github.com/ilya-slalom/desktop-guides/actions/runs/36669095974) failed `native-arm64-ui` on the known P0 `pdf-short` probe. |
| [#20](https://github.com/ilya-slalom/desktop-guides/pull/20) | 2026-09-30 | `81e9eb7` | T06.4 | [36675695341](https://github.com/ilya-slalom/desktop-guides/actions/runs/36675695341) | 198 | 349 | [t06-4-duplicate-import-design](t06-4-duplicate-import-design.md#t064-verification-record) | — |
| [#21](https://github.com/ilya-slalom/desktop-guides/pull/21) | 2026-09-30 | `b54d28d` | CI | — | — | — | — | CI-only change. Checked only by the absence of a push run for the branch. |
| [#22](https://github.com/ilya-slalom/desktop-guides/pull/22) | 2026-09-30 | `1d36a26` | T15.3 | [36689994514](https://github.com/ilya-slalom/desktop-guides/actions/runs/36689994514) | 210 (host) | 383 (host) | [t15-3-guide-deletion-design](t15-3-guide-deletion-design.md#t153-verification-record) | Earlier run [36688055406](https://github.com/ilya-slalom/desktop-guides/actions/runs/36688055406) failed `native-arm64-ui` on the known P0 flake. |
| [#23](https://github.com/ilya-slalom/desktop-guides/pull/23) | 2026-09-30 | `d94df92` | T05.1 | [36705946660](https://github.com/ilya-slalom/desktop-guides/actions/runs/36705946660) | 235 (host) | 395 (host) | [t05-1-guide-rows-design](t05-1-guide-rows-design.md#t051-verification-record) | — |
| [#24](https://github.com/ilya-slalom/desktop-guides/pull/24) | 2026-09-30 | `b774fd4` | T05.2 | [36726600007](https://github.com/ilya-slalom/desktop-guides/actions/runs/36726600007) | — | — | [t05-2-library-search-design](t05-2-library-search-design.md#t052-verification-record) | All nine jobs passed. The later [36730472882](https://github.com/ilya-slalom/desktop-guides/actions/runs/36730472882) passed `production-shell-ui` but failed `native-arm64-ui` on P0 `pdf-short`. |
| [#25](https://github.com/ilya-slalom/desktop-guides/pull/25) | 2026-10-01 | `739212e` | T04.2 | [36827722176](https://github.com/ilya-slalom/desktop-guides/actions/runs/36827722176) | 276 (host) | 407 (host) | [t04-2-game-actions-design](t04-2-game-actions-design.md#t042-verification-record) | Live IGDB and SteamGridDB checks aren't run in CI. |
| [#26](https://github.com/ilya-slalom/desktop-guides/pull/26) | 2026-10-01 | `d7a4e77` | T04.3 | [36841135926](https://github.com/ilya-slalom/desktop-guides/actions/runs/36841135926) | 280 (host) | 429 (host) | [t04-3-game-removal-design](t04-3-game-removal-design.md#t043-verification-record) | The re-prompt, failed restore, and pending cleanup aren't in the installed smoke. |
| [#27](https://github.com/ilya-slalom/desktop-guides/pull/27) | 2026-10-01 | `8dd9cd5` | T05.3 | [36857845252](https://github.com/ilya-slalom/desktop-guides/actions/runs/36857845252) | 301 (host) | 429 (host) | [t05-3-stable-navigation-design](t05-3-stable-navigation-design.md#t053-verification-record) | No smoke phase covers a stale metadata refresh. |
| [#28](https://github.com/ilya-slalom/desktop-guides/pull/28) | 2026-10-01 | `da1e975` | T08.1 | — | 323 (host) | 438 (host) | [t08-1-txt-decoding-design](t08-1-txt-decoding-design.md#t081-verification-record) | No CI run cited at merge, and no installed smoke. |
| [#30](https://github.com/ilya-slalom/desktop-guides/pull/30) | 2026-10-02 | `1294e4b` | T08.2 | [36946245355](https://github.com/ilya-slalom/desktop-guides/actions/runs/36946245355) | 371 | 438 | [t08-2-txt-view-design](t08-2-txt-view-design.md#t082-verification-record) | Earlier run [36885091223](https://github.com/ilya-slalom/desktop-guides/actions/runs/36885091223) failed: the smoke window showed too few guides. |
| [#31](https://github.com/ilya-slalom/desktop-guides/pull/31) | 2026-10-02 | `3157a19` | T08.3 | [36967638178](https://github.com/ilya-slalom/desktop-guides/actions/runs/36967638178) | 397 | — | [t08-3-txt-position-design](t08-3-txt-position-design.md#t083-verification-record) | `native-arm64-ui` passed on a re-run after a transient P0 PDF probe failure. |
| [#33](https://github.com/ilya-slalom/desktop-guides/pull/33) | 2026-10-02 | `e447597` | T08.3 follow-up | [36978593823](https://github.com/ilya-slalom/desktop-guides/actions/runs/36978593823) | — | — | [t08-3-txt-position-design](t08-3-txt-position-design.md#t083-verification-record) | Test harness only. The `txt-resize` flake fix held across 4 of 4 re-runs. |
| [#34](https://github.com/ilya-slalom/desktop-guides/pull/34) | 2026-10-03 | `3339fc7` | T07.3 | [37084631667](https://github.com/ilya-slalom/desktop-guides/actions/runs/37084631667) | 488 | 469 | [t07-3-webview2-policy-design](t07-3-webview2-policy-design.md#verification) | Counts come from [37081967040](https://github.com/ilya-slalom/desktop-guides/actions/runs/37081967040). Run [37083514707](https://github.com/ilya-slalom/desktop-guides/actions/runs/37083514707) failed on a harness race. |
| [#35](https://github.com/ilya-slalom/desktop-guides/pull/35) | 2026-10-03 | `9f2ad26` | T09.1 | [37125967628](https://github.com/ilya-slalom/desktop-guides/actions/runs/37125967628) | — | — | [t09-1-html-adapter-design](t09-1-html-adapter-design.md#verification) | Earlier run [37123660786](https://github.com/ilya-slalom/desktop-guides/actions/runs/37123660786) timed out; the cause is unknown. |
| [#36](https://github.com/ilya-slalom/desktop-guides/pull/36) | 2026-10-04 | `e2b9b8c` | T10.1 | [37169058467](https://github.com/ilya-slalom/desktop-guides/actions/runs/37169058467) | — | — | [t10-1-pdf-adapter-design](t10-1-pdf-adapter-design.md#verification) | PDF smoke runs on x64 only; ARM64 PDF is untested. |
| [#37](https://github.com/ilya-slalom/desktop-guides/pull/37) | 2026-10-04 | `541c245` | T10.3 | [37179558877](https://github.com/ilya-slalom/desktop-guides/actions/runs/37179558877) | 611 | 514 | [t10-3-pdf-locator-design](t10-3-pdf-locator-design.md#verification) | Counts come from the earlier run [37178208319](https://github.com/ilya-slalom/desktop-guides/actions/runs/37178208319). |
| [#38](https://github.com/ilya-slalom/desktop-guides/pull/38) | 2026-10-04 | `b4e31ae` | CI | [37202551075](https://github.com/ilya-slalom/desktop-guides/actions/runs/37202551075) | — | — | — | Adds the `shell-scope` and `dev-fast` dispatch inputs. [37202214399](https://github.com/ilya-slalom/desktop-guides/actions/runs/37202214399) checked `shell-scope=html` with `dev-fast`. |
| [#39](https://github.com/ilya-slalom/desktop-guides/pull/39) | 2026-10-04 | `dee44e4` | T09.3 | [37209771443](https://github.com/ilya-slalom/desktop-guides/actions/runs/37209771443) | — | — | [t09-3-html-locator-design](t09-3-html-locator-design.md#verification) | `shell-scope=all` on the merge with `main`. [37206866197](https://github.com/ilya-slalom/desktop-guides/actions/runs/37206866197) passed before that merge. |
| [#40](https://github.com/ilya-slalom/desktop-guides/pull/40) | 2026-10-05 | `17bdf48` | T12.2 | [37256476218](https://github.com/ilya-slalom/desktop-guides/actions/runs/37256476218) | 702 | 515 | [t12-2-progress-coordinator-design](t12-2-progress-coordinator-design.md#verification) | The installed shell job passed on its second attempt after an `html-position-dark` miss. |
| [#41](https://github.com/ilya-slalom/desktop-guides/pull/41) | 2026-10-05 | `a36b058` | T12.3 | [37274977026](https://github.com/ilya-slalom/desktop-guides/actions/runs/37274977026) | 723 | 520 | [t12-3-progress-estimates-design](t12-3-progress-estimates-design.md#verification) | `shell-scope=all`. `native-arm64-ui` passed on its second attempt after a P0 `pdf-short` timeout. |
| [#42](https://github.com/ilya-slalom/desktop-guides/pull/42) | 2026-10-05 | `0807d5a` | T13.2 | [37286991639](https://github.com/ilya-slalom/desktop-guides/actions/runs/37286991639) | 728 | 528 | [t13-2-completion-service-design](t13-2-completion-service-design.md#verification) | `shell-scope=core`, full matrix. |
| [#43](https://github.com/ilya-slalom/desktop-guides/pull/43) | 2026-10-05 | `070250b` | Docs | — | — | — | — | Docs only: adds this summary through PR #42. No CI run cited; its PR run [37297728233](https://github.com/ilya-slalom/desktop-guides/actions/runs/37297728233) passed every job. |
| [#44](https://github.com/ilya-slalom/desktop-guides/pull/44) | 2026-10-05 | `72baaeb` | T13.1 | [37315084947](https://github.com/ilya-slalom/desktop-guides/actions/runs/37315084947) | 737 | 528 | [t13-1-completion-actions-design](t13-1-completion-actions-design.md#verification) | `shell-scope=completion`, full matrix; PR run [37311359432](https://github.com/ilya-slalom/desktop-guides/actions/runs/37311359432) passed `shell-scope=all`. Six Minor final-review findings are deferred in the PR body. |
| [#46](https://github.com/ilya-slalom/desktop-guides/pull/46) | 2026-10-06 | `303a0ee` | T10.2 | [37432552738](https://github.com/ilya-slalom/desktop-guides/actions/runs/37432552738) | 788 | 540 | [t10-2-pdf-controls-design](t10-2-pdf-controls-design.md#verification) | Full matrix, passed on its first attempt. An unexplained `Next page` lookup failure hit two earlier runs and passed on rerun. Ctrl+wheel zoom is deferred to [#45](https://github.com/ilya-slalom/desktop-guides/issues/45). |
| [#47](https://github.com/ilya-slalom/desktop-guides/pull/47) | 2026-10-06 | `974c46d` | CI | [37439569037](https://github.com/ilya-slalom/desktop-guides/actions/runs/37439569037) | — | — | — | Splits `production-shell-ui` into four shards and adds `shell-pass` and the docs-only skip; the full run took 12.0 min, down from 29.5. The docs-only skip and the cancelling of older runs weren't exercised. |
| [#48](https://github.com/ilya-slalom/desktop-guides/pull/48) | 2026-10-06 | `5ab11f0` | T14.2 | [37465745451](https://github.com/ilya-slalom/desktop-guides/actions/runs/37465745451) | 807 (host) | 540 (host) | [t14-2-theme-setting-design](t14-2-theme-setting-design.md#verification) | High contrast isn't switched on in CI (T16.2). The uncited PR-head run [37468100338](https://github.com/ilya-slalom/desktop-guides/actions/runs/37468100338), on a docs-only last commit, failed the `core` and `pdf` shards; the cause wasn't recorded. |
| [#49](https://github.com/ilya-slalom/desktop-guides/pull/49) | 2026-10-06 | `a330d39` | T09.2 | [37484041874](https://github.com/ilya-slalom/desktop-guides/actions/runs/37484041874) | 839 (host) | 540 (host) | [t09-2-html-theme-style-design](t09-2-html-theme-style-design.md#verification) | The review fix `368a223` came later: [37486250072](https://github.com/ilya-slalom/desktop-guides/actions/runs/37486250072) checked it with `shell-scope=html` and `dev-fast`, and the PR run [37486234386](https://github.com/ilya-slalom/desktop-guides/actions/runs/37486234386) passed every job. Dark changing image and inline SVG colors is deferred to [#50](https://github.com/ilya-slalom/desktop-guides/issues/50). |
| [#51](https://github.com/ilya-slalom/desktop-guides/pull/51) | 2026-10-07 | `6567c81` | T14.1 | [37561318073](https://github.com/ilya-slalom/desktop-guides/actions/runs/37561318073) | 888 | 540 | [t14-1-text-size-design](t14-1-text-size-design.md#verification) | The `pdf` shard passed on a rerun after three failed progress saves in `progress-changed`, which looks like a slow-runner flake. The uncited PR-head run [37564016374](https://github.com/ilya-slalom/desktop-guides/actions/runs/37564016374), on a docs-only commit, failed `native-arm64-ui`. |
| [#52](https://github.com/ilya-slalom/desktop-guides/pull/52) | 2026-10-07 | `a350d7e` | T14.3 | [37590262774](https://github.com/ilya-slalom/desktop-guides/actions/runs/37590262774) | 899 | 540 | [t14-3-appearance-restore-design](t14-3-appearance-restore-design.md#verification) | Run after the final-review fixes; [37583360621](https://github.com/ilya-slalom/desktop-guides/actions/runs/37583360621) passed before them. The fix for a restyle landing during a restore's capture has no failing-first test. |
| [#53](https://github.com/ilya-slalom/desktop-guides/pull/53) | 2026-10-07 | `5795a24` | Docs | — | — | — | — | Docs only: status pages brought up to PR #52. No CI run started: #47's docs-only skip held. |
| [#54](https://github.com/ilya-slalom/desktop-guides/pull/54) | 2026-10-08 | `3ba3d72` | T14.4 | [37710298596](https://github.com/ilya-slalom/desktop-guides/actions/runs/37710298596) | 906 | 540 | [t14-4-reader-settings-design](t14-4-reader-settings-design.md#verification) | The `html` shard passed on attempt 2 after a one-off WebView2 open hang before the first offline session. The host `-ProviderOnly` pass and a UI Automation notification record (the Narrator pass) ran on VEGA; nobody listened to the audio, and high contrast, text scale and 200% display scale are T16.2's. |
| [#55](https://github.com/ilya-slalom/desktop-guides/pull/55) | 2026-10-08 | `f7c22e3` | Docs | — | — | — | — | Docs only: records the T14.4 merge (PR #54). No CI run started: #47's docs-only skip held. |
| [#56](https://github.com/ilya-slalom/desktop-guides/pull/56) | 2026-10-08 | `aef9d81` | Portable build | [37736088360](https://github.com/ilya-slalom/desktop-guides/actions/runs/37736088360) | 906 | 540 | [host check](#portable-single-file-release--host-check-8-october-2026) | The `core` shard passed on attempt 2 after `text-size-steps` lost a race with the closing overflow, fixed in #57. The earlier head `bb74c70` passed every job in [37730052864](https://github.com/ilya-slalom/desktop-guides/actions/runs/37730052864). It also plans T17.4 from the [size spike](#portable-size-spike--8-october-2026). |
| [#57](https://github.com/ilya-slalom/desktop-guides/pull/57) | 2026-10-08 | `0f10a1c` | T14.4 (harness) | [37738588719](https://github.com/ilya-slalom/desktop-guides/actions/runs/37738588719) | 906 | 540 | — | Harness only: `Assert-ResetEnabled` now waits for the overflow to close, and `text-size-steps` recorded closes of 42, 38 and 51 ms. |

## M2 T04.1 game editor — implementation check, 27 September 2026

The [game editor plan](t04-game-editor-plan.md) defines the shared Add/Edit
dialog and repository contract. On the Windows 11 x64 host (build
`10.0.26200.0`, .NET SDK `10.0.401`), the branch was staged under
`E:\work\desktop-guides\t04-game-editor-20260927`. Locked Core,
Infrastructure, and Production restores passed; Release Core tests passed
**73/73**, Infrastructure tests **90/90**, and an unsigned Release x64
Production MSIX build passed. The host's existing `mspdbcmf.exe`
symbols-package warning remains. Both changed PowerShell UI/installer
scripts passed a Windows PowerShell parser check.

The signed installed `production-shell-ui` job passed on the Windows CI
runner (Windows build `10.0.26100.0`, x64, .NET SDK `10.0.401`) for source
`76a762724b260d3b028a94691ae259f673894455`, in
[push run 36331946181](https://github.com/ilya-slalom/desktop-guides/actions/runs/36331946181).
The [game editor UI trace](evidence/ci/game-editor/game-editor.json) shows
invalid-title and canceled drafts left an empty library, keyboard submission
created a trimmed Unicode title and optional fields, canceled Edit preserved
the game, an ID-bound edit cleared optional fields, and two selectable
duplicate-title games appeared. The
[relaunch trace](evidence/ci/game-editor/game-editor-persisted.json) found both
games again. The signed test package and temporary trust were removed after
the run. The [Add game screenshot](evidence/ci/game-editor/add-game.png) and
[Edit game screenshot](evidence/ci/game-editor/edit-game.png) show the installed
WinUI form. T04.1's exercised Windows x64 checks passed. The retryable
repository-write failure still needs installed fault injection; the complete
P1 workflow and Windows 10/ARM64 runtime checks remain later gates.

### T04.1 review fixes — Windows 11 x64 check, 28 September 2026

PR #11 review fixes reject queued Add/Edit work after shutdown begins and
recheck closing state after the Edit metadata read. The dialog disables its
originating action until the request finishes. Normalized field lengths now
control validation without truncating a valid boundary value surrounded by
whitespace. Game notes render in a selectable, vertically scrollable
96-DIP region so the guide area retains the remaining height. T20.2 restore
design now validates managed game-artwork references as well as guide files.

On the Windows 11 x64 host, both changed PowerShell scripts passed parser
checks, Core passed 73/73, Infrastructure passed 90/90, and the unsigned
Release x64 production MSIX built with only the existing missing-symbols-tool
warning. The installed [game-editor trace](evidence/review-fixes/game-editor.json)
passed the trimmed 160-character input, 2,000-character note, duplicate-title,
cancel, and update checks. The
[long-note screenshot](evidence/review-fixes/long-notes.png) shows the bounded
metadata area with the guide region still available.

The installed shutdown fixture prepared Game view and blocked Edit's metadata
read in process 58520, as recorded by the
[prepare trace](evidence/review-fixes/prepare-game-editor-close.json) and
[queued-edit trace](evidence/review-fixes/queue-game-editor.json). Close
drained that process and the controller launched process 11748 for the next
scenario, proving the editor did not reopen and hold shutdown. The later
[normal trace](evidence/review-fixes/normal.json) stopped at an unchanged
background-window pointer click on the local host. The
[install report](evidence/review-fixes/signed-install.json) records the
limitation and confirms no package or temporary certificate remained. Full
installed regression remains a PR CI gate.

## M0 task results

| Task | Implemented output | Verification and remaining scope |
| --- | --- | --- |
| T03.1 | Portable Game, Guide, ReadingState, ReaderPreferences, and Settings records; repository contract; SQLite v1 schema and repository. IDs are generated, timestamps use an injected UTC clock, foreign keys are enabled on each connection, and a stored guide root must match its ID. `TextCodePage` is stored only for TXT. | Windows integration tests reopen two guides under one game with independent locators, estimates, completion timestamps, and preferences; reject an orphan state and a mismatched guide root. An interrupted first initialization can retry when the version-0 database has no schema objects, passes integrity check, and has no other application ID. Existing unknown schema or another application ID is preserved and rejected. The v1→v2 migration and public guide publication belong to T03.2 and T06.3. |
| T03.3 | `ILibraryPaths`, strict forward-slash managed relative paths, and an injected-root resolver under generated guide IDs. | Windows tests accept a nested CSS asset and reject traversal, absolute/UNC paths, percent escapes, symlinks, and an NTFS junction. |
| T11.2 | Portable reader session, typed actions and capability policy, plus the WinUI view adapter contract. | Fake-reader tests show supported commands dispatch and unsupported commands stop at the policy. Production TXT/HTML/PDF adapters and shell command controls are later M3 work. |
| T12.1 | Versioned bounded JSON codecs for TXT, HTML, and PDF, with fingerprint checks and exact/context/approximate restore candidates. | Core tests cover round trips, changed bytes, future versions, invalid JSON/numbers, duplicate fields, oversized text, wrong format, and HTML entry-document mismatch. The codecs do not alter completion. Actual readers use them in M3. |
| T10.0 | [Native hybrid PDF decision](pdf-decision.md) with restricted WebView2 comparison, license check, fixture traces, keyboard/UIA selection, password retry, app-specific outbound-blocked run, and 23 repeated page turns. | The prototype passed its tested decision gates on Windows 11 x64. It is not a production PDF adapter. Narrator speech, complex reading order, installed offline use, and cache/endurance limits remain T10.1–T10.3, T16.3, and T17.3 gates. |

## Windows M0 verification

These checks were recorded for the initial M0 implementation on 25 September 2026.

- Host: Windows 11 x64, build `10.0.26200.0`; source staged under
  `E:\work\desktop-guides`; .NET SDK `10.0.401`. Locked restores passed for
  Core tests, Infrastructure tests, the WinUI app, and the PDF tool.
- Release headless tests: **61/61 Core and 10/10 Infrastructure passed**,
  including the two-guide completion-state assertion and Windows reserved
  device-name path cases.
- Unpackaged WinUI x64 publish and `PdfTextSpike` Release build passed, with
  zero PDF-tool build warnings or errors. An unsigned x64 MSIX build passed;
  its sole warning was that `mspdbcmf.exe` was unavailable, so no symbols
  package was generated. The
  [interactive native trace](evidence/pdf-ui-candidate.json) and
  [outbound-blocked trace](evidence/pdf-ui-candidate-offline.json) passed on
  the published assembly SHA-256
  `36ffc1af5c5d6ed4541f4dd292cc9ed8ed6773f2b60152f3c053a9a14c9452c9`.
  The [WebView2 comparison](evidence/pdf-web-candidate.json) did not expose
  tagged document text or a validated locator in its restricted prototype.
- After testing, no candidate firewall rule, scheduled task, or app process
  remained. The stale temporary directory from the earlier junction test
  was removed.

## Review follow-up — 26 September 2026

The review found that an interrupted first initialization could leave an
existing SQLite file at `user_version = 0`. The repository previously rejected
it solely because the file existed. Initialization now retries only if the
database has no schema objects, passes `integrity_check(1)`, and has
`application_id = 0`. Unknown schema objects or a different application ID
still require recovery instead of replacement.

On the Windows 11 x64 host under `E:\work\desktop-guides`, the three focused
tests first reproduced the issue against the old code (one failure, two
passes), then passed with the fix (three passes). The full locked Release run
passed **61/61 Core and 13/13 Infrastructure** tests. The PR checks for this
follow-up are tracked on [PR #3](https://github.com/ilya-slalom/desktop-guides/pull/3).

## PR CI evidence

[PR CI run 36157085242](https://github.com/ilya-slalom/desktop-guides/actions/runs/36157085242)
passed all five jobs for code head `6c6deb8527e271e4398d6d833d3bb77698776e22`
before the 26 September review follow-up:
locked Core/Infrastructure tests on Windows x64, native Windows 11 ARM64
Core/Infrastructure tests, x64 and ARM64 unsigned MSIX builds, and the
installed ARM64 P0 fixture regression. GitHub's `pull_request` checkout used
synthetic merge commit `c6b09d302d2c9a15bfb2417a6ecd1c674616dbd2`,
recorded by the [native environment](evidence/ci/native-arm64-environment.json).

The retained [x64 Core](evidence/ci/core-tests.trx) and
[Infrastructure](evidence/ci/infrastructure-tests.trx) results report
**61/61** and **10/10** passes. The [native ARM64 Core](evidence/ci/native-arm64-core-tests.trx)
and [Infrastructure](evidence/ci/native-arm64-infrastructure-tests.trx)
results report the same counts, with OS build `10.0.26200.0`, native `Arm64`
process, and SDK `10.0.401`.

The [installed ARM64 record](evidence/ci/native-arm64-signed-install.json)
reports **14/14** P0 fixture workflows passed in interactive session 2 and
confirms that its test package and trust certificate were removed. The
[suite traces](evidence/ci/native-arm64-ui-suite/suite.json) are retained.
This verifies the diagnostic P0 package on ARM64; the M0 PDF text candidate
was exercised interactively on Windows 11 x64, and the production P1 reader
has not been installed or tested.

[Final PR CI run 36206500420](https://github.com/ilya-slalom/desktop-guides/actions/runs/36206500420)
passed all five jobs for review-fix head
`39daad9f768249415bc7b434221ccad5c3cbad59`. Both x64 and native ARM64
ran **61/61 Core** and **13/13 Infrastructure** tests; both unsigned MSIX
builds and the installed native ARM64 **14/14** P0 fixture regression passed.

P0's 14-fixture installed result remains [P0 evidence](../p0/results.md);
these M0 prototype results do not claim that the production shell, import,
resume coordinator, PDF reader, or release package is complete.

## M1 T03.2 migration result — 26 September 2026

The first M1 change on `feat/p1-m1-migrations` applies schema v1 and v2 in
order for a new library and upgrades a populated v1 library to v2. Before
upgrading existing data, it creates a consistent SQLite backup under the
app-data `.recovery` directory. Upgrade scripts and the version change run
in one transaction. Initialization checks schema objects, database integrity,
and foreign keys; it rejects newer or incomplete schemas without replacing
their data. An injected failure after creating the v2 index rolls back to a
usable v1 database and retains the v1 recovery copy.

The tests first failed against the v1-only repository (two failures, five
passes). On the Windows 11 x64 host, build `10.0.26200.0`, under
`E:\work\desktop-guides` with .NET SDK `10.0.401`, the completed locked run
passed **61/61 Core** and **19/19 Infrastructure** tests. The Release x64
WinUI build passed with zero warnings and errors. Tests cover the populated
v1 backup, failure rollback and retry, newer-schema and orphaned-row
rejection, incomplete v2 detection, and linked recovery-root rejection.
Production installed-app migration remains a later M1 shell check.

The PR #4 review follow-up added three Windows regression tests. Before the
fix, all three failed: a linked `library.sqlite` upgraded an external v1
database, a changed v1 `CHECK` constraint reached backup and migration, and
a v2 database with `Games.Notes` renamed to `Memo` passed initialization.
The resolver now rejects a linked database before SQLite opens it, including
repository reads. Initialization compares app-owned table and index
definitions against the version's migration scripts before backup or use;
unrecognized definitions stop with their existing data intact.

With the fix on the Windows 11 x64 host under `E:\work\desktop-guides`, locked
restore and Release tests passed **61/61 Core** and **22/22 Infrastructure**.
The unsigned Release x64 WinUI MSIX build succeeded with zero errors. It
reported one host tooling warning: `mspdbcmf.exe` was unavailable, so no
symbols package was generated. The installed production-app migration gate
remains open.

A second PR #4 review found two more path cases. Disposable Windows 11 x64
probes confirmed that a linked `library.sqlite-shm` modified an outside file
and an NTFS hard link at `library.sqlite` let initialization upgrade an
outside v1 database. Two regression tests failed against that PR head. The
resolver now checks SQLite sidecar paths and rejects Windows files whose
link count is not one before opening SQLite. The two focused tests passed
after the change. Locked restore, **61/61 Core** and **24/24 Infrastructure**
tests passed on the same host, including the active-WAL-writer migration
test. The unsigned Release x64 WinUI MSIX build succeeded with zero errors
and the same host symbols-tool warning.

[Final M1 migration PR CI run 36211490767](https://github.com/ilya-slalom/desktop-guides/actions/runs/36211490767)
passed all five jobs for `26ad3c3e119c7d264bd80b345a4a3f25510fead3`
before merge through [PR #4](https://github.com/ilya-slalom/desktop-guides/pull/4).
Both x64 and native ARM64 passed **61/61 Core** and **24/24
Infrastructure** tests; both unsigned MSIX builds and the installed native
ARM64 **14/14** P0 fixture regression passed. The installed test record
confirms package and certificate cleanup. The production P1 app and its
startup recovery remain separate M1 gates.

## M1 T15.2 startup reconciliation — 26 September 2026

On `feat/p1-m1-reconciliation`, `InitializeAsync` validates or migrates SQLite
before preflighting every pending `FileOperations` row. The v1 manifest
contains canonical guide IDs and exact generated content/staging/trash paths.
Prepared imports without a Guide remove only their named staged and moved
content roots. Prepared deletions restore named trash roots while retaining
guide metadata; committed deletions remove named trash roots. Each journal
row is cleared after its filesystem work, allowing interrupted work to retry.
The startup report counts resolved operations and untracked content/staging/
trash entries for later `Review orphan` UI. Unknown entries are retained.

The first eight recovery tests failed against the merged T03.2 repository.
The focused review exposed a prepared-import collision: staging and content
both existed. Its regression failed against the first PR head; the preflight
now stops recovery and retains both directories and the journal row for
review. After that fix, **15/15 focused recovery tests** passed on the
Windows 11 x64 host, build `10.0.26200.0`, under `E:\work\desktop-guides`
with .NET SDK `10.0.401`. Locked restore and the full Release suites passed
**61/61 Core** and **39/39 Infrastructure**. Tests include stage-only and
moved imports, prepared and committed deletions, partial two-guide restore,
unknown directories, malformed and overlapping manifests, nested links,
an NTFS junction, conflicting paths, and retry after a locked-file deletion
failure. The unsigned Release x64 WinUI MSIX build passed with zero errors
and the host's existing `mspdbcmf.exe` symbols-package warning. Native
ARM64 headless CI on PR #5 passed **61/61 Core** and **39/39 Infrastructure**
tests at implementation head `7a54a58`; the x64 headless lane passed the
same counts. Both unsigned MSIX package jobs passed. The installed ARM64
P0 diagnostic regression passed **14/14** fixture workflows, and its record
shows package and certificate cleanup. Installed production-app recovery
remains a separate check; no M2 file mutation is wired yet.

A second review found that a schema-valid uppercase `Guides.Id` was missed by
the case-sensitive committed-guide lookup. A prepared import could then
remove that guide's content directory. The regression failed on the first
PR head; recovery now compares parsed GUIDs, rejects malformed or duplicate
logical Guide IDs before cleanup, and counts content orphans by GUID. Locked
Windows 11 x64 Infrastructure tests passed **41/41** after this fix. PR #5
checks record CI for its current head.

PR #5 merged into `main` on 26 September 2026 as
`47c9e906c01e85eb9ce9f9759f6359390d809e52`. Its final x64 and native
ARM64 headless lanes passed **61/61 Core** and **41/41 Infrastructure**
tests; both unsigned MSIX packages built. The installed ARM64 P0 fixture lane
passed **14/14** after a transient first-attempt probe-status lookup failure
on `pdf-short`; its successful artifact records package and certificate
cleanup.

## M1 T11.1 production shell — 26 September 2026

`feat/p1-m1-shell` adds a typed Library/Game/Reader/Settings route coordinator,
an ID-based Back stack, and a separate WinUI production project with a
provisional `DesktopGuides.Preview` identity. The packaged shell opens a
persistent library under its package local data, shows empty and populated
routes, offers an explicit Resume action for a valid last-guide ID, and
returns to Library when a current route has a stale ID. The Reader route is a
placeholder until M3 adds the format adapters. The P0 diagnostic project
stays available for its fixture regression lane.

On the Windows 11 x64 host (build `10.0.26200.0`, .NET SDK `10.0.401`,
`E:\work\desktop-guides`), locked Release tests passed **69/69 Core** and
**41/41 Infrastructure**. Unsigned production x64 and ARM64 MSIX builds
passed with zero errors; each reported the existing missing `mspdbcmf.exe`
symbols-tool warning. Package inspection found the production assembly and
no P0 diagnostic assembly, fixture files, or probe controls. The test-only
metadata seeder successfully set valid and stale last-guide IDs.

Two temporary self-signed x64 install attempts
([first record](evidence/production-shell-host-install-1.json),
[retry record](evidence/production-shell-host-install-2.json))
passed signature verification but `Add-AppxPackage` failed with `0x80070005`
while initializing Windows Process Lifetime Manager. The deployment log also
records access failures when Windows attempted cleanup under
`C:\Program Files\WindowsApps\Deleted` for unrelated WhatsApp and Clipchamp
packages. Both attempts removed the temporary certificate and left no
preview app installed. Neither attempt exercised the installed shell.

After the user retried the signed x64 copy from an interactive desktop,
Windows installed `DesktopGuides.Preview_0.1.0.0_x64` and the user reported
that it launched successfully. A [host check](evidence/production-shell-host-interactive-install.json)
confirmed the installed package reports `Status: Ok` and the same signed
copy has a valid signature. The app was closed when checked, so launch is a
user observation rather than an automated local UI trace. The interactive
result narrows the earlier failure to the install context or host state; it
does not establish why the two SSH-driven attempts failed at PLM. The
temporary development signer remains in the host's `TrustedPeople` store
while this manual install is being evaluated and must be removed afterward.
This test certificate expires on 27 September 2026.

A [controlled fresh-install retest](evidence/production-shell-host-ssh-reinstall.json)
on the same host used the same signed MSIX after verifying its signature and
publisher trust. The existing package was removed only after three package
data files were backed up on `E:\work\desktop-guides` and checked by SHA-256.
`Add-AppxPackage` from SSH session 0 again failed with `0x80070005`. This
deployment's log confirms successful signature validation before `Failed to
initialize PLM` and `PackagesInUseClosed` failure events. An interactive
scheduled task then installed the same MSIX in desktop session 1. The library
file was restored and matched its backup; the package reported `Status: Ok`
and launched in session 1. The app was closed after the check, all temporary
tasks were removed, and the backup remains on the host. This rules out an
already installed preview package and missing signer trust as sufficient
explanations for the SSH failures. The exact PLM access denial remains
undetermined; WindowsApps cleanup warnings are present in the deployment
log, but their relation to the failure has not been established.

Future local-host installed runs follow the
[interactive E2E procedure](e2e-testing.md).

An [earlier production-shell UI job](https://github.com/ilya-slalom/desktop-guides/actions/runs/36217602539)
passed on the PR #6 Windows 11 x64 runner, build `10.0.26100.0`, in
interactive session 2. The
[signed install record](evidence/ci/production-shell/signed-install.json)
shows unsigned input SHA-256
`E78CC2C24856A0DD094C78006B7B4CDBD5FC511B3B9F59CC27C9CE43BC57D6E4`
and installed signed MSIX SHA-256
`60ECD7C582B74F4974F4DF61BA68E8D1AC7E26AFBE0FEA73E7CF7E873C5C5A14`
and successful package/certificate cleanup. The
[empty-Library trace](evidence/ci/production-shell/empty.json) verifies
fresh startup before seeding. The
[normal UIA trace](evidence/ci/production-shell/normal.json) verifies
Library, explicit Resume, Reader → Game → Library, Settings → Library,
and Library → Game → Reader → Game. The
[stale-ID trace](evidence/ci/production-shell/stale.json) verifies that a
missing last-guide ID does not expose Resume after relaunch. The smoke test
also checks that the P0 fixture picker is absent. This verifies T11.1's
installed routing behavior; reading the guide remains M3 work.

The duplicate push workflow exposed a smoke-test race after Settings → Library:
the test selected a game while the Library list was still loading
([failure trace](evidence/ci/production-shell/smoke-race-before-fix.json)).
A route-ready wait alone did not resolve the race in the next two CI runs:
the smoke test still could not find the row immediately after the status
became ready. The smoke script now polls for a visible row that supports
selection. The combined empty → seeded → stale installed test passed with
this change. The expanded route suite also checks that a stale Game or
Reader route clears its Back history.

The M1 review follow-up added single-instance activation and a normal-close
drain check. An initial custom `async Task Main` build crashed inside WinUI
during the first installed UI Automation query. The entry point now uses a
synchronous STA `Main` and waits for duplicate activation redirection while
remaining on the STA. [PR CI run 36225502945](https://github.com/ilya-slalom/desktop-guides/actions/runs/36225502945)
passed the signed installed x64 shell job at code head `88bf060`. Its
[diagnostic install record](evidence/ci/production-shell/signed-install-single-instance.json)
shows a second launch redirecting to the original window process, a
responsive empty Library afterward, all seeded and stale route checks, a
normal window close, and removal of the test package and certificate. The
lifecycle trace in that record was used for diagnosis; the production code
no longer writes it.

The next review pass tightened the installed gate and package cleanup. The
running window now acknowledges a redirected launch through an optional
test-created event; starting the scheduled task alone cannot pass the gate.
The runner removes only the package identity it recorded after its install
and uses per-run task names. Closing unregisters the instance key before
draining navigation, so another launch can open a new window.
[PR CI run 36227524454](https://github.com/ilya-slalom/desktop-guides/actions/runs/36227524454)
passed the signed installed x64 shell job at code head `ade1a59`. Its
[install record](evidence/ci/production-shell/signed-install-blocked-handoff.json)
shows the redirect acknowledgment and a new window while the closing
process remained alive. The
[queued-guide trace](evidence/ci/production-shell/queue-guide.json)
confirms that the old window began a guide action while a separate test
process held a SQLite write lock. After lock release, the old process exited,
the new window passed the seeded route smoke, and the test package and
certificate were removed. This is M1 shell evidence; the complete P1
installed workflow remains T17.2 work.

The next review follow-up at code head `464f7c8` holds an exclusive
package-local library lease through repository disposal. A new window can
open during close, but waits for the old session before loading the library.
The activation path retries if it selected an instance that starts closing.
The installed test now binds UI checks and process cleanup to recorded
processes in the interactive session, and queues a different guide from the
seeded Resume guide.

[PR CI run 36229239568](https://github.com/ilya-slalom/desktop-guides/actions/runs/36229239568)
passed the signed Windows 11 x64 installed-shell job. Its
[install record](evidence/ci/production-shell/review-followup/signed-install.json)
shows two different window processes during close; the
[waiting-window trace](evidence/ci/production-shell/review-followup/waiting-handoff.json)
observed the new window waiting for the library lease while the old one was
still alive. After the test released the SQLite write lock, the old window
exited and the new Library resumed with the guide selected in the
[pending-guide trace](evidence/ci/production-shell/review-followup/queue-guide.json).
The same run paused another launch after target selection, closed that
target, and verified that the launch opened a new window. The package and
temporary certificate were removed. The Windows 11 x64 host separately
passed 42/42 Infrastructure tests, the unsigned x64 production MSIX build,
and the shell-seed build. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36229242647)
passed 8/8 jobs, including the native ARM64 P0 fixture regression. These
checks remain M1 shell evidence.

The close-boundary follow-up at code head `1581bde` makes a redirected
secondary wait for the first window's UI callback to accept activation.
The installed runner now launches the packaged executable through an
interactive task helper and records its returned process ID and start time;
UI checks and process cleanup use that specific launch record. Process
exit during cleanup no longer stops package and certificate cleanup.

[PR CI run 36230900475](https://github.com/ilya-slalom/desktop-guides/actions/runs/36230900475)
passed the signed Windows 11 x64 installed shell job. The
[install record](evidence/ci/production-shell/activation-ack/signed-install.json)
shows accepted redirection, the library lease wait during the blocked
guide write, and a new window after both a pre-redirection target close
and a close while activation was queued on the UI thread. The
[launch trace](evidence/ci/production-shell/activation-ack/launch.json)
records the interactive task's package executable, process ID, and session.
The package and temporary signer were removed. On the Windows 11 x64 host,
PowerShell parsing, 42/42 Infrastructure tests, the unsigned production MSIX,
and the shell-seed build passed. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36230903529)
passed 8/8 jobs, including the native ARM64 P0 fixture regression. The
complete P1 installed workflow remains T17.2 work.

The next review fix at code head `625d3c3` replaces the process-wide
activation acknowledgment with a same-user pipe connection for each launch.
An accepted reply stays accepted after the old window closes; a delayed
reply on an abandoned connection cannot acknowledge a later launch. The
installed runner now waits for each verified test-owned process to exit
before seeding metadata or removing the MSIX, and retains ownership if
that wait times out.

The signed Windows 11 x64
[install record](evidence/ci/production-shell/activation-pipe/signed-install.json)
from [push run 36233298067](https://github.com/ilya-slalom/desktop-guides/actions/runs/36233298067)
shows acceptance before close without reopening, a new window when close
precedes acceptance, and the queued-activation and library-lease handoffs.
It also records removal of the temporary package and signer. The
[launch trace](evidence/ci/production-shell/activation-pipe/launch.json)
identifies the installed executable and interactive process. On the
Windows 11 x64 host, PowerShell parsing, 45/45 Infrastructure tests, the
delayed/already-exited/timed-out cleanup checks, and the unsigned x64
production MSIX build passed. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36233300630)
passed 8/8 jobs, including the native ARM64 P0 fixture regression. The
complete P1 installed workflow remains T17.2 work.

The installed-runner handle follow-up at code head `12d21c2` makes the
interactive launch helper retain its child until the installer verifies a
second handle and acknowledges a per-launch token. A failed result write or
handoff stops the child through the helper's original handle. The installer
checks exact creation time, session, and image through its retained handle,
and cleanup terminates and waits through that handle after draining both
launch tasks. On the Windows 11 x64 host, harmless child-process checks passed
for rejected identity, an unrelated decoy PID, already-exited and timed-out
cleanup, failed result publication, accepted handoff, and stale
acknowledgment. PowerShell 5.1 parsing of the installer also passed.

The signed Windows 11 x64
[install record](evidence/ci/production-shell/handle-handoff/signed-install.json)
from [push run 36235737097](https://github.com/ilya-slalom/desktop-guides/actions/runs/36235737097)
shows the redirect and close handoffs, no process cleanup error, and removal
of the temporary package and certificate. The
[launch trace](evidence/ci/production-shell/handle-handoff/launch.json)
records the interactive session and handoff token. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36235738707)
passed 8/8 jobs, including the native ARM64 P0 UI regression. These remain
M1 shell checks; the complete P1 installed workflow is still T17.2 work.

The next review follow-up at code head `92011f9` rejects an existing
`DesktopGuides.Preview_*` profile before the M1 shell test prepares or
installs a package, even if the package was previously unregistered. The
handoff test retains its launched child's handle for failure cleanup, and
activation waits for a reply through the remaining deadline. On the Windows
11 x64 host, PowerShell parsing, fresh-profile and process-handoff checks,
and 47/47 locked Infrastructure tests passed.

The signed Windows 11 x64
[install record](evidence/ci/production-shell/profile-boundary/signed-install.json)
from [push run 36237540783](https://github.com/ilya-slalom/desktop-guides/actions/runs/36237540783)
shows successful activation and removal of the temporary package and
certificate without a process cleanup error. Both the push and
[PR workflow](https://github.com/ilya-slalom/desktop-guides/actions/runs/36237542985)
passed 8/8 jobs, including the native ARM64 P0 UI regression. This closes
the M1 review findings; the complete P1 installed workflow remains T17.2
work.

The next review fix at code head `b5e8aec` waits for the registered
installed UI smoke task to reach `Ready` after its result is written,
before another scenario reuses the task name. The first empty-library
smoke keeps its helper alive for two seconds after publishing its result;
the next empty-library smoke starts immediately after the task becomes
ready. Both changed scripts passed Windows PowerShell 5.1 parsing on the
Windows 11 x64 host.

The signed Windows 11 x64
[install record](evidence/ci/production-shell/smoke-idle/signed-install.json)
from [push run 36238917270](https://github.com/ilya-slalom/desktop-guides/actions/runs/36238917270)
records successful delayed and follow-up smokes, with the test package
and temporary certificate removed. The complete P1 installed workflow
remains T17.2 work.

The next review follow-up at code head `a1f7ee5` transfers foreground
permission from a second user launch to the existing window, rejects
stale smoke results by invocation and process identity, and checks that
all test-owned scheduled tasks are removed. On the Windows 11 x64 host,
PowerShell 5.1 parsing, a read-only result check, a retained-task check,
an unsigned x64 production MSIX build, and production-package inspection
passed. The signed Windows 11 x64
[install record](evidence/ci/production-shell/foreground-result-cleanup/signed-install.json)
from [push run 36240550121](https://github.com/ilya-slalom/desktop-guides/actions/runs/36240550121)
shows a separate test window in the foreground before a duplicate
launch and the original shell in the foreground afterward. The
[focus trace](evidence/ci/production-shell/foreground-result-cleanup/second-launch.json.foreground.json)
records the two window handles and the duplicate's exit. The installed
run passed with no test package or temporary certificate left behind.
The complete P1 installed workflow remains T17.2 work.

The parallel
[PR run 36240553671](https://github.com/ilya-slalom/desktop-guides/actions/runs/36240553671)
for `a1f7ee5` exposed a duplicate-process exit between its image-path query
and the handle's exit signal. Code head `5bd2821` waits up to 500 ms for
that exact handle only after a process-information query fails; a still-live
process remains an error. The Windows 11 x64 host passed the handle and
launch-handoff checks, including a post-exit ownership check. The signed
Windows 11 x64
[install record](evidence/ci/production-shell/foreground-exit-transition/signed-install.json)
from [push run 36241173720](https://github.com/ilya-slalom/desktop-guides/actions/runs/36241173720)
passed the background-window activation and reports package, certificate,
and process cleanup. The
[focus trace](evidence/ci/production-shell/foreground-exit-transition/second-launch.json.foreground.json)
again records the test window before launch and the original shell after
the duplicate exited.

## M1 T11.3 reader shell — 26 September 2026

The `feat/p1-m1-reader-shell` branch adds a guide title/game/format header,
in-reader Back, a reading-surface host, and a capability-driven CommandBar.
The preview shell has no reader adapter yet, so it offers no reader
commands. Returning to Game retains the selected guide and restores focus
to its row; activating the selected row can reopen it.

On the Windows 11 x64 host, build `10.0.26200.0`, with source staged under
`E:\work\desktop-guides`, locked production restore and the unsigned Release
x64 MSIX build passed. The MSIX SHA-256 is
`1b20ada10646f124dd6e2f716df9fcf6ee8c84a7d9e411bbd95569fb2be6e293`.
The build reported only the existing missing-`mspdbcmf.exe` symbols warning.
The updated installed-smoke script passed Windows PowerShell 5.1 parsing.
The host retains an installed Preview package, so its fresh-profile M1
installer was not run there.

The signed installed Windows 11 x64
[push run 36244519302](https://github.com/ilya-slalom/desktop-guides/actions/runs/36244519302)
passed its shell job for code head `2187a0492aa0c1aa5218510289ffc8a8458d292f`.
The [install record](evidence/ci/production-shell/reader-shell/signed-install.json)
reports Windows build `10.0.26100.0`, signed MSIX SHA-256
`11f6bff9f38bccdf62cc5a3c911d972dbcdce37ece9b96ef207b4e5fb5d45827`,
all normal and close-handoff route smokes, stale Resume, and graceful exit.
The [normal trace](evidence/ci/production-shell/reader-shell/normal.json)
includes Reader → Game with selected-row focus, Enter to reopen the same
guide, and Settings round trips. The
[Reader screenshot](evidence/ci/production-shell/reader-shell/normal.reader.png)
shows the compact title/game/format header, collapsed pane, and honest
unavailable-reading message. The install record confirms that the temporary
package and signer were removed. Both x64 and ARM64 production package
builds, Core/Infrastructure tests, and the native ARM64 P0 installed
fixture regression passed in the eight-job run. The ARM64 result is a
diagnostic P0 check, not the complete P1 installed workflow.

The installed checks exposed two smoke assumptions that were corrected
before the passing run: reselecting an already selected guide does not
activate it, and the blocked-write handoff temporarily changes the Resume
guide. The final smoke activates a selected row with Enter and explicitly
returns to the route guide before later relaunches. Dynamic toolbar actions
remain an M3 adapter integration check; this M1 preview has no adapter.

The T11.3 review follow-up at code head
`920324342d0947bc3c04237f783dbfee479f2339` passed the signed installed
Windows 11 x64 shell job in [PR run 36247566880](https://github.com/ilya-slalom/desktop-guides/actions/runs/36247566880)
and [push run 36247564409](https://github.com/ilya-slalom/desktop-guides/actions/runs/36247564409).
The [install record](evidence/ci/production-shell/reader-shell/review-followup/signed-install.json)
reports Windows build `10.0.26100.0`, AMD64, success, and removal of the
temporary package and certificate. The [normal trace](evidence/ci/production-shell/reader-shell/review-followup/normal.json)
checks that Reader closes the navigation pane, its toggle opens and closes
it, pointer Back restores guide selection and focus, pointer input reopens
the selected row, and keyboard Back and Enter reopen it again. Screenshot
capture now fails the smoke if it cannot save a nonempty image; the retained
[Reader screenshot](evidence/ci/production-shell/reader-shell/review-followup/normal.reader.png)
shows the closed pane and unavailable-reading preview. The first review
follow-up run failed because WinUI's pane toggle did not expose a UI
Automation clickable point; using its visible bounds for the physical click
passed the installed rerun.

### T11.3 focused-row review follow-up — 26 September 2026

At code head `e81db1a81a23ac6bf13562a8daabfe9415b3a49d`, Enter now opens
the guide row that received the key even when another row remains selected.
The Windows 11 x64 host build under `E:\work\desktop-guides` passed the
unsigned Release MSIX build, and Windows PowerShell 5.1 parsed the updated
smoke script. The signed installed shell jobs passed in
[push run 36249362118](https://github.com/ilya-slalom/desktop-guides/actions/runs/36249362118)
and [PR run 36249364872](https://github.com/ilya-slalom/desktop-guides/actions/runs/36249364872).
The retained [install record](evidence/ci/production-shell/reader-shell/focused-enter/signed-install.json)
reports Windows build `10.0.26100.0`, AMD64, success, and removal of the
temporary package and signer. Its four normal route scenarios all include
the [focused-row trace](evidence/ci/production-shell/reader-shell/focused-enter/normal.json):
Ctrl+Arrow moves focus to the other guide without changing selection, Enter
opens that focused guide, and the test restores the original Resume guide
before later route checks. The [Reader screenshot](evidence/ci/production-shell/reader-shell/focused-enter/normal.reader.png)
was retained.

The T11.3 exit check now covers guide selection and focus. Library query
retention remains required and is assigned to T05.3 after T05.2 adds search.
In the PR run, the first native ARM64 P0 diagnostic attempt failed when
`pdf-short` could not read its startup status; the unchanged P0 lane passed
all 14 fixtures in the push run and in [PR attempt 2](https://github.com/ilya-slalom/desktop-guides/actions/runs/36249364872/attempts/2).

### T11.3 queued guide and loading-state review follow-up — 27 September 2026

At code head `ce6e823c879781325b53f4c41a3751d975a18a37`, Game-view guide
actions retain the later selection when an earlier settings write is delayed.
The [queued-action trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/queue-later-guide.json)
selects guide B while A is still opening. The
[result trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/later-guide-result.json)
confirms B opens, Back selects and focuses B, and Library offers Resume B.

The [preparation trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/switch-game-prepare.json)
leaves the previous game's selected guide in the shell. With the next
game's read held by an exclusive SQLite lock, the
[loading trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/switch-game-loading.json)
confirms the previous guide row and selected-guide action are unavailable
while the guide list is disabled. After release, the
[two-game trace](evidence/ci/production-shell/reader-shell/queued-guide-loading/switch-game.json)
confirms the second game's guide and Reader context.

The signed installed Windows 11 x64
[PR run 36286711186](https://github.com/ilya-slalom/desktop-guides/actions/runs/36286711186)
passed the production-shell UI job. Its
[install record](evidence/ci/production-shell/reader-shell/queued-guide-loading/signed-install.json)
reports OS build `10.0.26100.0`, AMD64, success, and removal of the
temporary package and certificate. On the local Windows 11 x64 host,
Windows PowerShell 5.1 parsed both changed scripts, the locked fixture
restore and Release fixture build passed, and the unsigned Release x64
production MSIX built. A separate disposable database probe confirmed
that the fixture's exclusive read lock blocks another read and releases
normally. The host's existing Preview package and profile precluded a
fresh-profile installed run there.

The first installed attempt at `aad96a9` stopped in the smoke runner:
its later-guide continuation started from Reader but still applied the
Library precheck. The `ce6e823` test correction removed that precheck,
and the installed scenario passed.

### T11.3 close admission and Resume review follow-up — 27 September 2026

At code head `8839d15a4a47d03866914ea1b203a921c032fde3`, Game-view guide
actions stop registering a new selection once Close has stopped the
navigation queue. Resume is saved after the selected Reader route renders,
and a failed settings save leaves that Reader visible with an error. The
installed selection test now holds the first guide's database read until
the later guide is selected; it no longer relies on a 300 ms delay.

The signed installed Windows 11 x64
[PR run 36289417891](https://github.com/ilya-slalom/desktop-guides/actions/runs/36289417891)
passed all nine jobs, including the signed shell job and the retained native
ARM64 P0 regression. The
[late-close traces](evidence/ci/production-shell/reader-shell/close-resume-review/late-guide-after-close.json)
select a second guide after Close is requested, while the first accepted
guide is held by a read lock; the
[install record](evidence/ci/production-shell/reader-shell/close-resume-review/signed-install.json)
includes the successful relaunch check that expects the first guide as
Resume after drain. The
[later-selection trace](evidence/ci/production-shell/reader-shell/close-resume-review/queue-later-guide.json)
and [result](evidence/ci/production-shell/reader-shell/close-resume-review/later-guide-result.json)
confirm the later guide's Reader, Back focus, and persisted Resume. The
[failed-later-guide trace](evidence/ci/production-shell/reader-shell/close-resume-review/later-guide-failed-result.json)
confirms that a removed later guide opens no Reader and leaves Resume
empty instead of saving the superseded guide. The existing close/relaunch
scenario now waits for the Reader to appear while its Resume write is held,
as recorded in the
[write-lock trace](evidence/ci/production-shell/reader-shell/close-resume-review/queue-guide-write.json).

The install record reports OS build `10.0.26100.0`, AMD64, success, and
removal of the temporary package and certificate. The Windows 11 x64 host
under `E:\work\desktop-guides` passed Windows PowerShell 5.1 parsing,
the locked ShellSeed restore and Release build, an unsigned Release x64
production MSIX build, and an isolated invalid-guide fixture probe. Its
existing Preview package and profile prevented a fresh-profile installed
run there. An earlier CI attempt at `2f3c740` used the old `"Opening
guide..."` expectation for the held-write scenario; it was updated to
observe the rendered Reader before the passing run.

### T11.3 render, cleanup, and focus review follow-up — 27 September 2026

At code head `9c4457f`, `RenderCurrentAsync` reports whether its requested
route finished loading. `OpenGuideAsync` saves Resume only when that result
is successful and the matching Reader remains current. The toolbar
installer now stops a timed-out test task, waits for it to leave `Running`,
then stops the test-owned app process and removes the test-owned MSIX
before checking for leftover package, process, task, and certificate.
Go to page and Find in guide reopen the CommandBar overflow after their
dialogs and restore keyboard focus to the command when it is still
available.

The Windows 11 x64 host, build `10.0.26200.0`, under
`E:\work\desktop-guides` passed Windows PowerShell 5.1 parsing, locked
restores, and unsigned Release x64 builds for the production shell and
linked toolbar test app. Each build reported only the host's existing
missing-`mspdbcmf.exe` symbols warning. The
[interactive host record](evidence/host/reader-toolbar-render-cleanup/review-result.json)
ran in desktop session 1. Its
[forced-timeout record](evidence/host/reader-toolbar-render-cleanup/timeout-install.json)
confirms that the parent stopped the still-running toolbar app and
removed its package after stopping the scheduled task. The following
[normal install](evidence/host/reader-toolbar-render-cleanup/normal-install.json)
passed with no test package, process, task, or certificate left behind.
The [UI trace](evidence/host/reader-toolbar-render-cleanup/toolbar-ui.json)
includes keyboard focus returning to both overflow dialog commands.
An initial host attempt exposed a smoke assumption: WinUI names the open
overflow toggle `Less app bar`; the passing test closes it by its
automation ID. The Reader render-error path was checked in code and built
on Windows; the host fixture did not inject a second-read exception.

The review-fix evidence head `fdffd08` passed all nine jobs in
[push run 36291334420](https://github.com/ilya-slalom/desktop-guides/actions/runs/36291334420)
and [PR run 36291336239](https://github.com/ilya-slalom/desktop-guides/actions/runs/36291336239).
The [CI timeout record](evidence/ci/reader-toolbar-render-cleanup/timeout-install.json)
confirms that the parent stopped the running test app and removed its
package; the following
[signed toolbar install](evidence/ci/reader-toolbar-render-cleanup/signed-install.json)
and [focus trace](evidence/ci/reader-toolbar-render-cleanup/toolbar-ui.json)
passed with cleanup. The
[signed production shell](evidence/ci/production-shell/reader-shell/render-cleanup-review/signed-install.json)
passed its route and Resume checks and removed its package and
certificate. Both architecture package builds, x64 and native ARM64
headless tests, and the retained native ARM64 P0 installed regression
passed. The native ARM64 installed result remains a P0 diagnostic check.

### T11.3 Reader render fault and toolbar inspection cleanup — 27 September 2026

At code head `41511d3`, the installed shell fixture pauses after the Reader
route opens and before its second guide metadata read. The disposable
fixture clears Resume and stores an invalid guide format, then releases the
read. The shell smoke requires the Reader loading error and, after restoring
the guide format and returning to Library, no Resume action. This checks
the render-success gate through the repository's actual read error. The
toolbar timeout fixture simulates a process exit during cleanup inspection
and requires removal of the confirmed test-owned package despite the
inspection error, followed by a successful normal install.

The Windows 11 x64 host, build `10.0.26200.0`, under
`E:\work\desktop-guides` passed PowerShell 5.1 parsing, locked restores,
and unsigned Release x64 builds of Production and the toolbar test app,
plus a Release ShellSeed build. Each MSIX build had only the existing
missing-`mspdbcmf.exe` symbols warning. In a separate disposable
database, ShellSeed changed the route guide from TXT to invalid format,
cleared Resume, and restored TXT while keeping Resume empty. The
[interactive host record](evidence/host/reader-render-fault-inspection/review-result.json)
reports session 1 and a passing timeout-to-reinstall sequence. Its
[timeout record](evidence/host/reader-render-fault-inspection/timeout-install.json)
shows the simulated inspection error, process stop, package removal, and
no remaining package, process, certificate, or task. The subsequent
[normal install](evidence/host/reader-render-fault-inspection/normal-install.json)
passed and cleaned up. The host's existing Preview package and profile
precluded a fresh-profile installed Reader run there.

The signed installed Windows 11 x64 shell in
[push run 36292903300](https://github.com/ilya-slalom/desktop-guides/actions/runs/36292903300)
passed all nine jobs at `41511d3`. Its
[route-open trace](evidence/ci/reader-render-fault-inspection/queue-reader-render-error.json),
[render-error trace](evidence/ci/reader-render-fault-inspection/reader-render-error-observed.json),
and [Resume result](evidence/ci/reader-render-fault-inspection/reader-render-error-result.json)
confirm the second-read error left Reader visible and did not save
Resume. The [production install record](evidence/ci/reader-render-fault-inspection/production-signed-install.json)
reports package and certificate cleanup. The
[toolbar timeout record](evidence/ci/reader-render-fault-inspection/toolbar-timeout-install.json)
reports the simulated inspection error and successful package removal;
the subsequent
[normal toolbar install](evidence/ci/reader-render-fault-inspection/toolbar-signed-install.json)
and [UI trace](evidence/ci/reader-render-fault-inspection/toolbar-ui.json)
passed. The parallel PR run `36292904629` passed the new toolbar gate but
stopped in the older later-guide Back check because a name-only UIA search
selected an element without an Invoke pattern. The smoke helper now
prefers the back button automation ID and requires an Invoke pattern.

At test-fix head `c94f54d`, all nine jobs passed in
[push run 36293452654](https://github.com/ilya-slalom/desktop-guides/actions/runs/36293452654)
and [PR run 36293454522](https://github.com/ilya-slalom/desktop-guides/actions/runs/36293454522).
The final PR
[later-guide trace](evidence/ci/reader-render-fault-inspection/final/later-guide-result.json)
passed Reader → Game → Library with guide selection, keyboard focus,
and Resume restored. Its
[Reader route](evidence/ci/reader-render-fault-inspection/final/queue-reader-render-error.json),
[read-error](evidence/ci/reader-render-fault-inspection/final/reader-render-error-observed.json),
and [empty-Resume](evidence/ci/reader-render-fault-inspection/final/reader-render-error-result.json)
traces repeated the new regression. The
[signed production install](evidence/ci/reader-render-fault-inspection/final/production-signed-install.json)
passed and removed its temporary package and certificate. The signed
toolbar job and retained native ARM64 P0 installed suite passed in both
runs. Windows 10 and the complete P1 ARM64 workflow remain deferred.

### T11.3 toolbar install ownership review follow-up — 27 September 2026

The toolbar installer now holds an exclusive per-user file lock before
its package-absent preflight and through all cleanup. A concurrent
invocation exits before creating a task, certificate, or result directory
and cannot classify the first run's package as its own. The installed
timeout fixture holds the first package and process while invoking a
second installer, then releases the first and runs a normal install.

The Windows 11 x64 host under `E:\work\desktop-guides` parsed the
PowerShell scripts and passed locked restores and unsigned Release x64
production and toolbar MSIX builds. The
[interactive result](evidence/host/reader-toolbar-exclusive-install/review-result.json)
reports desktop session 1 and a passing overlap-to-reinstall sequence.
Its [overlap result](evidence/host/reader-toolbar-exclusive-install/overlap-result.json)
confirms the second invocation was rejected while the first package and
process remained present. The
[first timeout cleanup](evidence/host/reader-toolbar-exclusive-install/timeout-install.json)
removed the package after the simulated process-inspection error, and
the [normal install](evidence/host/reader-toolbar-exclusive-install/normal-install.json)
passed with no test package, process, task, or certificate left behind.
An initial host attempt exposed that asynchronous `Start-Process` in
Windows PowerShell 5.1 returned no exit-code property; the helper now
uses the first installer's signed report to verify its expected timeout
and cleanup.

At code head `8b6008a`, all nine jobs passed in
[push run 36300190195](https://github.com/ilya-slalom/desktop-guides/actions/runs/36300190195)
and [PR run 36300193536](https://github.com/ilya-slalom/desktop-guides/actions/runs/36300193536).
The signed PR
[overlap trace](evidence/ci/reader-toolbar-exclusive-install/overlap-result.json)
records a rejected second installer, the first package and process
still present, and verified first-run cleanup. Its
[timeout install record](evidence/ci/reader-toolbar-exclusive-install/timeout-install.json)
shows removal after the simulated inspection error. The
[normal install](evidence/ci/reader-toolbar-exclusive-install/normal-install.json)
and [toolbar UI trace](evidence/ci/reader-toolbar-exclusive-install/toolbar-ui.json)
passed with no test package, process, task, or certificate remaining.
The signed production shell and retained native ARM64 P0 installed
suite also passed.

The first CI overlap attempt launched Windows PowerShell 5.1 from the
`pwsh` CI controller and exited before setup because `Get-FileHash`
could not be resolved, as recorded in its
[diagnostic trace](evidence/ci/reader-toolbar-exclusive-install/ci-startup-failure.json).
The controller now starts child installers with its own PowerShell
executable, matching both the CI and local-host environments.

### T11.3 toolbar process-scope review follow-up — 27 September 2026

The toolbar test task now writes a fresh process handoff after launching
its app. Parent cleanup validates the invocation, package, PID, creation
time, session, and executable before opening an owned process handle.
Only that handle can be terminated or counted as a leftover. The installed
overlap fixture keeps an unrelated process with the same name alive through
timeout cleanup and requires it to survive.

The Windows 11 x64 host under `E:\work\desktop-guides` parsed all three
changed PowerShell scripts. Its
[interactive result](evidence/host/reader-toolbar-process-scope/review-result.json)
reports desktop session 1, matching timeout and normal handoffs, and a
passing timeout-to-reinstall sequence. The
[overlap trace](evidence/host/reader-toolbar-process-scope/overlap-result.json)
records that the contender was rejected while the first app survived,
and that the unrelated same-name process survived the first cleanup.
The [timeout handoff](evidence/host/reader-toolbar-process-scope/timeout-process.json)
identifies the stopped process. The
[timeout cleanup](evidence/host/reader-toolbar-process-scope/timeout-install.json)
removed its package, task, and temporary trust entry after the simulated
inspection error. The
[normal install](evidence/host/reader-toolbar-process-scope/normal-install.json)
and [toolbar UI trace](evidence/host/reader-toolbar-process-scope/toolbar-ui.json)
passed with no test-owned package or process left; its
[handoff](evidence/host/reader-toolbar-process-scope/normal-process.json)
matches the install invocation.

At code head `c6a5aa3`, all nine jobs passed in
[PR run 36301957589](https://github.com/ilya-slalom/desktop-guides/actions/runs/36301957589)
and in [push run 36301955579, attempt 2](https://github.com/ilya-slalom/desktop-guides/actions/runs/36301955579).
The signed PR
[overlap trace](evidence/ci/reader-toolbar-process-scope/overlap-result.json)
records the rejected contender and surviving unrelated process. Its
[timeout handoff](evidence/ci/reader-toolbar-process-scope/timeout-process.json)
matches the process stopped in the
[timeout install record](evidence/ci/reader-toolbar-process-scope/timeout-install.json).
The [normal install](evidence/ci/reader-toolbar-process-scope/normal-install.json)
and [toolbar UI trace](evidence/ci/reader-toolbar-process-scope/toolbar-ui.json)
passed, and the [normal handoff](evidence/ci/reader-toolbar-process-scope/normal-process.json)
matches the install invocation. The signed production shell and retained
native ARM64 P0 suite passed.

Push attempt 1 failed in the unchanged production-shell UI smoke while
waiting for `ReaderHeading` during the rapid Settings path. The same path
passed in the parallel PR run and push attempt 2. No shell source changed
in this follow-up.

### T11.3 toolbar handoff and overlap review follow-up — 27 September 2026

The overlap fixture starts an unrelated same-name process before the first
install and identifies the first app through its verified process handoff.
The toolbar parent now opens and retains the app's verified process handle
before acknowledging the handoff token. The child keeps its launch handle
until it receives that acknowledgment, so the parent does not reopen a PID
after the child has released it.

The Windows 11 x64 host under `E:\work\desktop-guides` parsed all three
changed PowerShell scripts. Its
[interactive result](evidence/host/reader-toolbar-handoff/review-result.json)
reports a successful session 1 overlap, timeout cleanup, and normal
reinstall. The [overlap trace](evidence/host/reader-toolbar-handoff/overlap-result.json)
shows that the decoy was present before installation and survived cleanup.
The [timeout handoff](evidence/host/reader-toolbar-handoff/timeout-process.json)
matches the app stopped in the
[timeout install record](evidence/host/reader-toolbar-handoff/timeout-install.json).
The [normal install](evidence/host/reader-toolbar-handoff/normal-install.json)
records an acquired parent handle, and its
[UI trace](evidence/host/reader-toolbar-handoff/toolbar-ui.json)
records child acknowledgment. The
[normal handoff](evidence/host/reader-toolbar-handoff/normal-process.json)
matches both records. No test package, process, scheduled task, or temporary
trust entry remained.

At code head `4690078`, all nine jobs passed in
[PR run 36303623358](https://github.com/ilya-slalom/desktop-guides/actions/runs/36303623358)
and [push run 36303620766, attempt 2](https://github.com/ilya-slalom/desktop-guides/actions/runs/36303620766).
The signed PR
[overlap trace](evidence/ci/reader-toolbar-handoff/overlap-result.json)
shows the pre-existing decoy, rejected contender, and verified first-app
cleanup. The [timeout handoff](evidence/ci/reader-toolbar-handoff/timeout-process.json)
matches the process stopped in the
[timeout install record](evidence/ci/reader-toolbar-handoff/timeout-install.json).
The [normal install](evidence/ci/reader-toolbar-handoff/normal-install.json),
[process handoff](evidence/ci/reader-toolbar-handoff/normal-process.json),
and [toolbar UI trace](evidence/ci/reader-toolbar-handoff/toolbar-ui.json)
record parent handle acquisition, child acknowledgment, all toolbar phases,
and final cleanup.

Push attempt 1 failed in the retained native ARM64 P0 installed suite
because its app opened no interactive window, as recorded in the
[failure report](evidence/ci/reader-toolbar-handoff/arm64-first-attempt.json).
The parallel PR job and push attempt 2 passed on the same code head.
No P0 source changed in this follow-up.

### T11.3 toolbar receipt and bounded-wait review follow-up — 27 September 2026

The interactive child now writes an atomic, invocation-bound package
receipt after installation. Parent cleanup requires that receipt and the
installed package's full identity before removal. A missing, stale,
malformed, or different-package receipt is rejected by the
[headless ownership check](../../tools/p1/test_windows_reader_toolbar_install_receipt.ps1).
The parent waits separately for installation and process handoff. The
overlap contender has bounded process and output waits and is stopped
before the first installer is released.

The Windows 11 x64 host under `E:\work\desktop-guides` parsed the changed
PowerShell scripts and passed the receipt check. Its
[interactive result](evidence/host/reader-toolbar-install-receipt/review-result.json)
records a session 1 overlap, simulated timeout cleanup, and delayed normal
reinstall. The [overlap trace](evidence/host/reader-toolbar-install-receipt/timeout/overlap-result.json)
records the rejected contender and surviving unrelated same-name process.
The [timeout install record](evidence/host/reader-toolbar-install-receipt/timeout/signed-install.json)
and [receipt](evidence/host/reader-toolbar-install-receipt/timeout/toolbar-install-receipt.json)
show that the parent removed only the package installed by that invocation.
The [normal install record](evidence/host/reader-toolbar-install-receipt/normal/signed-install.json)
reports a 31-second artificial pre-install delay and a 33.07-second
receipt wait, beyond the former 30-second cutoff. Its
[receipt](evidence/host/reader-toolbar-install-receipt/normal/toolbar-install-receipt.json)
matches the [UI trace](evidence/host/reader-toolbar-install-receipt/normal/toolbar-ui.json);
all eight toolbar phases passed. Both installed runs removed their temporary
packages, processes, scheduled tasks, and certificate trust entries.

At code head `1d1e614`, all nine jobs passed in
[push run 36306328232](https://github.com/ilya-slalom/desktop-guides/actions/runs/36306328232)
and [PR run 36306330461](https://github.com/ilya-slalom/desktop-guides/actions/runs/36306330461).
The signed PR [overlap result](evidence/ci/reader-toolbar-install-receipt/timeout/overlap-result.json)
confirms the bounded contender's lock rejection and the unrelated process's
survival. The [timeout install](evidence/ci/reader-toolbar-install-receipt/timeout/signed-install.json)
and [receipt](evidence/ci/reader-toolbar-install-receipt/timeout/toolbar-install-receipt.json)
confirm receipt-backed package and process cleanup. The
[normal install](evidence/ci/reader-toolbar-install-receipt/signed-install.json)
records a 36.18-second receipt wait after the 31-second artificial delay;
its [receipt](evidence/ci/reader-toolbar-install-receipt/toolbar-install-receipt.json)
and [UI trace](evidence/ci/reader-toolbar-install-receipt/toolbar-ui.json)
confirm identity, all eight toolbar phases, and final cleanup. The signed
production shell and native ARM64 UI regression jobs also passed.

## M1 T17.1 packaging groundwork — 27 September 2026

The production package remains on the `DesktopGuides.Preview` identity while
the public signing certificate Subject is undecided. The
[release procedure](release-packaging.md) records the public identity and
version rules, the external signing boundary, Windows App Runtime and WebView2
online/offline delivery, and the remaining release gates. CI now validates
the packed identity, x64/ARM64 architecture, version, Windows App Runtime
framework dependency, and P0 fixture isolation before uploading an unsigned
production package. Its build manifest records the package identity and
runtime minimum with the SHA-256.

On the Windows 11 x64 host (build `10.0.26200.0`, .NET SDK `10.0.401`), a
locked restore and fresh Release x64 MSIX build from the merged source passed
with zero errors. The existing host symbols-tool warning prevented only a
symbols package. The packed MSIX verifier found Preview version `0.1.0.0`,
architecture `x64`, and `Microsoft.WindowsAppRuntime.2` minimum `2.5.1.0`.
It also verified the previously built ARM64 package's static metadata; this
does not establish an ARM64 installed release result. Deliberately wrong
identity, changed upgrade Publisher, and non-increasing version checks
failed as intended.

The [signing record](evidence/host/p1-package-signing.json) shows that the
fresh x64 package passed SignTool signing and trust verification with a
temporary, non-exportable code-signing certificate. It was not timestamped
and is not a public candidate. The signed test output and its temporary
certificate were removed without changing the installed Preview package.

The [interactive upgrade record](evidence/host/p1-package-upgrade.json)
used a separate `DesktopGuides.PackageUpgradeTest` identity and a temporary
certificate. Its scheduled task ran in desktop session 1, installed version
`1.0.0.0`, seeded two guide files and a SQLite library, installed `1.0.1.0`
over it without uninstalling, confirmed the package family and database
hash stayed equal, and launched the upgraded app. It removed the test
package, profile, task, and temporary trust. The host's existing Preview
package remained at version `0.1.0.0`. The first attempt failed because the
check looked for SQLite at `LocalState\library.sqlite`; it cleaned up, and
the passing rerun checked the actual `LocalState\library\library.sqlite`.

T17.1 still requires the final public Name and certificate Subject, protected
public signing integration, and an upgrade of public candidate versions.
The user-deferred runtime-free VM and Windows 10 checks remain T17.3 gates.

## M2 T11.4 design language — implementation check, 28 September 2026

T11.4 adds app-level semantic resources for theme colors, spacing, shape,
typography, surfaces, lists, buttons, and status presentation. Library, Game,
Reader, Settings, the game editor, and the reader toolbar consume those
resources. The representative routes use responsive page padding, bounded
metadata, native WinUI heading semantics, directed empty states, a native
`TitleBar`, a Mica system backdrop, and full-width shell content. Routine
ready messages use a top `InfoBar` and close after three seconds; loading
remains visible until replaced, and warnings/errors remain dismissible. The
[design plan](t11-design-language-plan.md) records the chosen quiet field-guide
direction and the WinUI Gallery revision and component inventory. A follow-up
pins MIT-licensed
`CommunityToolkit.WinUI.Controls.SettingsControls` `8.2.251219` and replaces
the representative local-storage surface with an adaptive Toolkit
`SettingsCard`; Windows App SDK remains `2.5.1`.

On the Windows 11 x64 host at `E:\work\desktop-guides`, 73/73 Core tests,
90/90 Infrastructure tests, and all seven PowerShell harness checks passed.
Fresh Release production packages built for x64 and ARM64. The x64 package
SHA-256 was
`DCA67246134FF1BD305B6AFEA3B8B40B2C25D92AA8A0C09B0D5266EFD9811C14`;
the ARM64 package SHA-256 was
`6C3B7C01635552A8446B9B91A8BE63256C4C96F295A4C9EC23E6E2FF0B5DA8BE`.
The host's missing optional `mspdbcmf.exe` produced only the existing
symbols-package warning.

The final interactive signed-install run passed the complete existing shell
regression and all four design-language modes. Each mode checked Library,
Game, Reader, and Settings at wide and narrow widths, semantic headings,
focus, UIA names, title-bar Back and pane actions, full-width bounds, automatic
routine-status dismissal, the Toolkit Settings-card name and bounds, long
metadata, and control overlap. Light and dark screenshot luminance measured
238.56 and 39.74. That run also passed a high-contrast mode, which changed
flags `126` to `127` with `High Contrast Black` and restored them; the window
materials follow-up below moves high contrast to T16.2. The original
light-app preference was also restored.
The package, package profile, temporary certificate trust, and scheduled
tasks were absent after cleanup.

The exact-source final run passed every shell and design phase. Shell
automation reads a sequenced status marker from the raw UI Automation view
rather than requiring the transient user-facing `InfoBar` to remain visible
or exposing a dismissed message through normal accessibility views.

The linked reader-toolbar package includes the shared design token dictionary
and retains the native command label position so wide layouts keep `Zoom in`
visible. Its installed Windows run passed overlap ownership, simulated timeout
cleanup, all toolbar commands, and the 31-second delayed install/receipt path.

The run used the host's current 96 DPI / 100% display scale. The user deferred
200% display-scaling verification to T16.2. Windows 10 and installed ARM64
behavior also remain unverified; the local ARM64 result is a package build
only.

### Window materials follow-up

User review asked for a seamless backdrop, a working dark Acrylic, and dialogs
that match the window. The window backdrop now runs unbroken behind the title
bar, navigation pane, and route background, because the `NavigationView`
content layer and its border are transparent. A Settings `Window background`
card stores Mica (default), Acrylic, or Solid in SQLite `AppSettings` through
an atomic read-modify-write; unsupported materials fall back to Solid with a
warning. Acrylic uses the Base `DesktopAcrylicBackdrop` with the default
system backdrop configuration, so it follows the app theme and turns solid
while the window is inactive. The reader page uses the opaque
`SolidBackgroundFillColorQuarternaryBrush`. In Acrylic mode, `ContentDialog`
uses in-app acrylic; in Mica and Solid it keeps the default dialog.

Earlier material evidence was captured without translucency. From `279dfe1`
to `8bc7e5d`, the harness recreated the `Personalize` registry key when it
switched the app theme, which deleted the host's transparency setting and its
other values. Those screenshots showed backdrop fallback colors, so they did
not prove that Mica or Acrylic let the desktop through. `8bc7e5d` fixed the
harness; the runs below kept every `Personalize` value. With real translucency,
Thin Acrylic in light theme over a dark window measured 2.4:1 for secondary
text and 3.0:1 for body text, so Acrylic now uses Base acrylic.

From a snapshot of `eec9782` on the same host, with transparency
effects on: locked restores, 73/73 Core tests, 98/98 Infrastructure tests,
and all nine PowerShell harness checks passed, and Release packages built for
x64 and ARM64. The x64 package SHA-256 was
`0E96D957ACE66139CB3F1FA960CED667E855D4A33BE6650E425C0E82C0E7100E`;
the ARM64 package SHA-256 was
`510FEAA3E4F40B874D5E473D4E1D24E770365B5802781A4BCED937C115C54FCB`.
The full signed-install regression passed. It ran the system, light, and dark
design-language modes and six material passes: light and dark × Solid,
Acrylic, and Mica, plus a switch-and-relaunch check. A strip across the
pane/content boundary had a channel range of 0 for Solid, so no seam was
visible. Mica showed the wallpaper tint (light 249/241/235 and dark 35/31/28,
against Solid 243 and 32). Acrylic measured 214 in light and 42 in dark. These
checks cover the app code only: Acrylic fails only if it repeats the Solid
fill within one level on every channel, which is what one of our layers
covering the backdrop would show. Mica is not checked this way, because with
transparency off Windows draws it in the Solid fill color. How Windows tints
either material is recorded, not asserted.
Light Acrylic text measured 5.46:1 for secondary text and 12.34:1 for body
text. A 160×4 strip in the Acrylic edit-game command band showed the in-app
acrylic noise (channel range 4, Solid 0); the check accepts that noise or the
fallback color, because live in-app acrylic takes its tint from the content
behind it. The report records the dialog difference for each theme.

A second interactive run switched the Windows app theme while the app stayed
open. Library luminance followed from 46 to 209 and back to 46, and with Edit
game open from 41 to 168 and back to 41. With transparency effects turned off,
Mica matched Solid and Acrylic showed its fallback color (249 light, 44 dark);
the run then restored the setting. After each run, the package, certificate
trust, and scheduled tasks were gone, and the `Personalize` values, the
high-contrast flags (`126`), and the active `Custom.theme` were unchanged.
On a snapshot of `fb3a393`, the reader-toolbar installed smoke passed all
eight phases, including focus restoration after the page and find dialogs; its
code and harness have not changed since.

High contrast moves to T16.2: enabling it makes Windows rewrite the active
theme to `Custom.theme`, which this harness cannot restore. Optional colorful
icons from the MIT Fluent UI System Icons `*_color` set are T11.5; WinUI
Gallery has no colored icon set.

Selected installed evidence includes the
[wide](evidence/t11-design-language/design-light.library-wide.png) and
[narrow](evidence/t11-design-language/design-light.library-narrow.png) light
Library, the [light Acrylic Library](evidence/t11-design-language/material-light-acrylic.library.png), [dark Acrylic Library](evidence/t11-design-language/material-dark-acrylic.library.png),
[dark Acrylic Reader](evidence/t11-design-language/material-dark-acrylic.reader.png),
[dark Acrylic Edit game](evidence/t11-design-language/material-dark-acrylic.edit-game.png),
[light Solid Library](evidence/t11-design-language/material-light-solid.library.png),
and the [Window background card](evidence/t11-design-language/material-light-mica.settings.png).
The sanitized
[result record](evidence/t11-design-language/windows-11-x64-result.json)
lists every material pass and the package and screenshot hashes.

## M2 T04.4 provider search — implementation check, 29 September 2026

The [T04.4 provider design](t04-provider-search-design.md) selects IGDB
metadata with user-supplied Twitch credentials and SteamGridDB artwork with
a user-supplied key. Neither service is scraped; no proxy or shared secret
is used. New Core test classes are `GameMetadataNormalizerTests`,
`GameMetadataPresentationTests`, `ProviderGameImporterTests`,
`ProviderMessagesTests`, and `FallbackArtworkSourceTests`. New
Infrastructure test classes are `IgdbClientTests`, `TwitchTokenSourceTests`,
`ArtworkSourceTests`, `ProviderHttpTests`, `ProviderCredentialBlobTests`,
`ArtworkValidatorTests`, `ManagedArtworkStoreTests`,
`ProviderThumbnailLoaderTests`, and `GameMetadataJsonTests`;
`SqliteLibraryRepositoryTests` and `ManagedPathResolverTests` gained
provider-link and artwork-path cases.

On the Windows 11 x64 host at `E:\work\desktop-guides`, source commit
`0c05d8c` plus the final-review fix working tree, Core/Infrastructure tests
passed **150/150 Core** and **221/221 Infrastructure**, and the
credential-helper checks passed. The fixes map a connection that drops while
a provider body is read to Unavailable, keep the startup artwork sweep from
blocking library open when an empty game folder cannot be deleted, and load
search thumbnails through `ProviderHttp` and `ArtworkValidator` instead of
letting WinUI fetch them. The unsigned Release x64 production package built
with SHA-256
`18960E2E7AB5124A1C7879BB48F06BB8554890100FA168C30DAF59278031FE07`. The
host's missing optional `mspdbcmf.exe` caused only the existing
symbols-package warning.

Code-review fixes on top of `f94fc15` then passed **150/150 Core** and
**228/228 Infrastructure** on the same host, plus the credential-helper
checks and a Release x64 production build with no warnings. They re-enable
Refresh metadata when a refresh finishes on another page, treat a
`Retry-After` date in the past as retry now, and stop an existing but
locked `providers.bin` from counting as "not configured": the load isn't
cached, Settings reports that it couldn't read the saved credentials and
disables Save until its own reload succeeds when Settings is opened again. The installed E2E below was not
re-run for these fixes.

The installed E2E ran on `pcsx2-win` (OS build `10.0.26200.0`, AMD64, .NET
SDK `10.0.401`, Windows App Runtime `2.2.0.0/2.3.1.0/2.4.0.0/2.5.1.0`) on
29 September 2026. The provider pass without the firewall rule used the
package above, signed afresh with SHA-256
`D48D1D849B05C4D02B84ABBF8F2C3174EA92A1E68E32AB363D4D9C98F4BE532D`. The
blocked-network run with the elevated controller and
`-AllowOfflineFirewallRule` used the earlier package
`97D3A0FE3FED6137C5AB593784BFC344CD2FC539C488371D7B88F8164D3225F9`
(source `cb513ea` plus the Task 12 working tree), signed as
`A7206117B1D8385C47297940EEC7B9D00AD381F8F6F803747D81465393B24817`. The
user authorized the firewall rule before that run. It was not repeated: the
thumbnail fix does not touch offline paths, because an offline search fails
before any result or thumbnail is shown.

The primary [offline result](evidence/t04-provider-search/windows-11-x64-offline-result.json)
(runId `d4c7fe163ae64ae1a01664d8816d7f92`, 2026-09-29T06:55Z) reports
`success: true`, all provider scenarios (`none`, `offlineWithoutCredentials`,
`settings`, `live`, `blockedNetwork`, `remove`) with their phases,
`fixtureFields` "IGDB accepted 22 fixture fields", search result count 19
(1–20 allowed), attribution "Metadata from IGDB. Artwork from SteamGridDB.",
refresh status "Metadata refreshed.", `cancelDuringSearch: observed` (1
attempt), both leak scans with empty `filesWithCredentialValues`, and
`packageStillInstalled: false`. The
[controller log](evidence/t04-provider-search/offline-controller.json)
records rule `DesktopGuides-P1-ProviderOffline-d4c7fe163ae64ae1a01664d8816d7f92`,
`outcome: done`, and `ruleRemoved: true`. The
[provider result](evidence/t04-provider-search/windows-11-x64-result.json)
(runId `e0bfbd4329cb411b97ab2b8ce22320ad`, 2026-09-29T07:35Z) reports
`success: true`, the `none`, `offlineWithoutCredentials`, `settings`, `live`,
and `remove` scenarios with their phases, `fixtureFields` "IGDB accepted 22
fixture fields", search result count 19, the same attribution and refresh
status, `cancelDuringSearch: observed`, both leak scans empty, and
`blockedNetwork` recorded as not run without the switch. Its
[search results](evidence/t04-provider-search/provider-live.search-results.png)
show the cover thumbnails loaded through `ProviderHttp`.

Both runs removed the temporary package, certificate trust, and scheduled
tasks. The final-state `CredentialBlobExists: false` confirmed `providers.bin`
was deleted. The controller's re-scan on the host of all copied evidence found
0 files with credential values (10 files, 3 values). Screenshots were also
inspected by eye. No pre-existing Preview package or profile existed; no backup
was needed.

An **app change** made while getting this green: the game route's UIA tree
now exposes GameFacts, because an `AutomationGroup` wrapper reports the
`MetadataControl` text to UIA. Harness fixes: provider modes no longer wait
for a second `Library ready.` status. Row-count assertions wait for visible rows.
Close-AddGameSearch uses the dialog's Close button instead of Esc. The first
blocked-network attempt failed on the row-count race before it requested a
rule, so none was created. The second created its rule, then failed when Esc
did not close Add game; the controller removed that rule (`outcome: done`,
`ruleRemoved: true`). The recorded run is the third. For the provider pass, the smoke now waits
5 seconds before the search-results screenshot, because thumbnails load one
at a time after the rows appear and are raw in the UIA tree; the first
attempt with the fixed package captured the rows before any thumbnail had
loaded. An earlier attempt in the same run folder stopped before install
work because the seed tool had not been restored there.

Selected evidence includes
[Add game with no credentials](evidence/t04-provider-search/provider-none.add-game-no-credentials.png),
[seeded offline game](evidence/t04-provider-search/provider-offline-none.linked-game-notconfigured.png),
[Settings saved](evidence/t04-provider-search/provider-settings.provider-settings-saved.png),
[Add game search](evidence/t04-provider-search/provider-live.add-game-search.png),
[search results](evidence/t04-provider-search/provider-live.search-results.png),
[linked live game](evidence/t04-provider-search/provider-live.linked-game-live.png), and
[offline search](evidence/t04-provider-search/provider-offline-blocked.search-offline.png).

## Portable single-file release — host check, 8 October 2026

The portable release is now one `DesktopGuides.Production.exe` in a zip,
not a 544-file folder. The publish is single-file with full self-extraction,
symbols are embedded, and `tools/p1/package_portable_release.ps1` builds the
zip, its `.zip.sha256` file and a JSON manifest; see
[Portable build](e2e-testing.md#portable-build). It merged through
[PR #56](https://github.com/ilya-slalom/desktop-guides/pull/56), merge commit `aef9d81`.

On the Windows 11 x64 host (build `10.0.26200.0`, .NET SDK `10.0.401`), from
a zip staging of `80d2647`, the locked MSIX, ShellSeed and portable restores
passed, publish gave 0 warnings, and the packaging test passed. The
[release manifest](evidence/portable-single-file/release-manifest.json)
records a 92,034,870-byte zip (SHA-256 `F9FABCAF…E14C`) holding a
244,004,113-byte exe (SHA-256 `1016FDC1…086E`). The multi-file release from
`f7c22e3` was 544 files in a 97,082,441-byte zip.

Every run below used the exe unpacked from that zip, with the same SHA-256,
in an interactive scheduled task without elevation:

- `core` passed ([report](evidence/portable-single-file/core-html-run.json)).
  The same run then failed `html-position`, as described below.
- `html` passed in full, both `html-position` passes included
  ([report](evidence/portable-single-file/html-run.json)).
- `pdf`: `pdf-reader`, `pdf-zoom`, `pdf-jump` and `pdf-locked` passed in light
  and dark ([report](evidence/portable-single-file/pdf-run.json)).
  `pdf-keys` failed.

Two checks fail on this host for the `f7c22e3` multi-file portable build as
well, so the single-file change doesn't cause them. Hosted CI runners pass
both.

- `pdf-keys`: after Ctrl+G, `Expected keyboard focus on 'ReaderCommandInput',
  found 'PdfPreviewScroller'`. It failed twice on the single exe and once on
  the multi-file build.
- `position-fixed-header`: after the size step back to 100%, the top line was
  MARK-0023 instead of MARK-0020 (page top 552). It failed twice on the single
  exe and once on the multi-file build, and passed in the `html` run above.

The first run's `html` failure came from staging. Windows `tar` read the UTF-8
names in the tar stream with the OEM code page, so the Guide B fixture no
longer matched its companion folder. Staging from a zip fixed it. The `html`
run above used a staging copy of `windows_shell_ui_smoke.ps1` with an
environment-variable guard meant to skip `position-fixed-header`. The smoke
runs as its own scheduled task and never saw the variable, so the guard never
applied and the phase ran unchanged. The staging copy was restored afterwards.

[Launch times](evidence/portable-single-file/launch-times.json), from start to
a visible main window: 1.63 s for the first launch, which unpacks 551 files
(233,305,709 bytes) to `%TEMP%\.net\DesktopGuides.Production`; 0.48 s for
later launches; 0.47 s for the multi-file build.

`%LOCALAPPDATA%\DesktopGuides` already existed. It was renamed to
`DesktopGuides.bak-20261008` for the runs and renamed back afterwards. It held
the P0 `P0-WebView` cache and a library created at 12:27 that day, before the
move, and nothing in it changed. The extraction folders the runs created under
`%TEMP%\.net` were removed. No scheduled task or app process was left behind.

## Portable size spike — 8 October 2026

A throwaway spike on the Windows 11 x64 host measured how far the portable
build can shrink. It built size variants from `80d2647` in scratch copies of
the staging and kept no code. The size work is scheduled as T17.4, a final
pass before the T17.3 candidate runs.

| Variant | Exe | First-launch extraction | Cold / warm launch | Result |
| --- | --- | --- | --- | --- |
| `80d2647` | 244.0 MB | 551 files, 233.3 MB | 2.2 s / 0.5 s | passes |
| V1: unused Windows App SDK components excluded | 181.8 MB | 485 files, 171.2 MB | 1.5 s / 0.5 s | passes |
| V2: V1 with `PublishTrimmed`, `TrimMode=partial` | 94.3 MB | 355 files, 84.0 MB | 2.9 s / 0.64 s | crashes |
| V2c: V2 with `EnableCompressionInSingleFile` | 41.7 MB | 355 files, 84.0 MB | 3.0 s / 0.65 s | crashes |
| V2f: V1 with `TrimMode=full` | 89.3 MB | — | — | not run |

- **What V1 removes.** The app uses only Foundation (file pickers, app
  lifecycle), WinUI and WebView2. The `Microsoft.WindowsAppSDK` 2.5.1
  metapackage also brings AI, ML (`onnxruntime.dll` 21.7 MB, `DirectML.dll`
  18.7 MB), Search, Widgets and DWrite. The Community Toolkit packages depend
  on the metapackage, so V1 keeps it and adds direct references to those
  components and `Microsoft.Windows.AI.MachineLearning` with
  `ExcludeAssets="all"`. A reference to only the components the app uses made
  NuGet resolve the Toolkit's metapackage at 1.6, whose build files collide
  with the 2.x components.
- **V1 build checks.** It published with 0 warnings. The MSIX still built and
  declared `Microsoft.WindowsAppRuntime.2` 2.5.1.0, and the MSIX restore
  accepted the updated lock file. Both builds load only the system
  `DWrite.dll`, never `DWriteCore.dll`, so text rendering doesn't change.
- **V1 scenario runs.** These groups passed on the V1 exe: core, txt,
  text-size, game-actions, design, catalog, pdf (all but the host-only
  `pdf-keys`), progress, theme, completion and import. `html-position` failed
  in both html runs at different points: once "Larger text" wasn't visible,
  and once the 110% restore never reported. The `80d2647` build also fails
  `html-position` in 3 of 4 runs on this host, so these runs neither show nor
  rule out a V1 effect. `provider` wasn't run, because it needs credentials.
- **Trimmed builds crash.** V2 and V2c crash with a stowed exception
  (`0xc000027b`) in `Microsoft.UI.Xaml.dll`: in core at game-editor
  "edit-by-id-and-clear-optional-fields", and early in the design, pdf,
  progress and html groups. Compression isn't the cause, because V2 fails the
  same way.
- **Trim warnings to fix.** A publish with `-p:CsWinRTAotWarningLevel=2
  -p:TrimmerSingleWarn=false` lists 20 warnings:
  - CsWinRT1028 (4): `GameSearchItem`, `GuideRowItem`, `LibraryGameItem` and
    `ProviderServices` need `partial`;
  - CsWinRT1030 (9): generic WinRT interfaces, for example `Catalog`,
    `List<MetadataItem>` and `byte[]`, need `AllowUnsafeBlocks` for generated
    code;
  - IL2026 (7): reflection-based `System.Text.Json` calls in
    `HtmlPositionScripts.cs`, `ExternalLinkLaunchers.cs`,
    `HtmlReaderSession.cs` and `ShellWindow.Progress.cs`.

  The default build also reports IL2104 for AngleSharp, the Windows SDK
  projection and `WinRT.Runtime`. Trimming drops the framework's ReadyToRun
  code, which probably explains the slower start.
- **Compression** shrinks the exe, not the extraction, so it pays off only
  together with trimming.

`%LOCALAPPDATA%\DesktopGuides` was renamed aside for the runs and renamed
back; its 291 files matched the pre-run list by path, size and write time.
The runs switched the Windows app theme and restored it (dark). The
extraction folder and the spike copies were removed afterwards.
