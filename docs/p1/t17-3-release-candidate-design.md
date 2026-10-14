# T17.3 release-candidate runs design

Status: complete. PR 1 (#110) is merged, and the v1.0.1 candidate run
passed on 14 October 2026. Publishing the v1.0.1 draft is the user's
step.

## Intent

Produce the evidence behind the first public release's claim: the signed
release candidate installs, upgrades from the published release, and runs
the whole journey on Windows 11 x64. The result is a published tested
matrix and accurate release-note limits.

Traces:
- T17.3 in [implementation-plan.md](implementation-plan.md) (M6) and
  [work-breakdown.md](../work-breakdown.md);
- TR17.1 (no network needed to read an imported guide) and TR17.2 (no OS
  or architecture advertised without a recorded install and reader
  result);
- the [installed E2E procedure](e2e-testing.md).

## Scope

- **In scope:**
  - the release-signed Public MSIX on pcsx2-win (Windows 11 Enterprise
    build 26200, AMD Ryzen 7 7800X3D, x64);
  - the tested matrix;
  - the release-note limits, including scanned PDFs.
- **Out of scope:**
  - **The clean-install claim:** it waits for the runtime-free VM check,
    T17.8 in the final milestone. P1 doesn't claim it.
  - **Windows 10 x64 and Windows 11 ARM64:** recorded as untested.
  - **The portable build's own release gates:** first run without
    installation, data location, and replacing the exe stay defined
    separately. Its scenario groups already run in CI.

## The candidate's bytes

The run tests exactly what will be published:

1. **Tag the candidate.** With the user's OK, `v1.0.1` is pushed.
   `release.yml` builds, signs, runs its upgrade check, and creates a
   **draft** pre-release. Nothing is public yet.
2. **Download and check.** The draft's MSIX, `desktop-guides-release.cer`
   and `release-manifest.json` are downloaded to the host. Before
   anything is installed:
   - the MSIX SHA-256 matches the manifest;
   - the certificate's thumbprint matches
     `docs/p1/release/release-certificate.json`;
   - the package's signer is that certificate.
3. **Run the journey,** below.
4. **Publish, or not.** If every stage passes, the user publishes the
   draft. The record names the tested hashes, and they match the published
   assets byte for byte. If a stage fails, the draft is deleted, and the
   fix ships as `v1.0.2`, which gets its own candidate run.

The previous release, v1.0.0, is downloaded from its GitHub release and
checked against its own manifest the same way.

## The journey: the `rc` group

One installed run on pcsx2-win, through `start_installed_task.ps1`, as a
new `rc` scenario group (`-RcOnly`). It runs these stages, each its own
launch:

1. **`rc-install-previous`:** install v1.0.0, trusting the release
   certificate for the run only. Check its version and signer.
2. **`workflow-build`** on v1.0.0:
   - add a game, and import TXT, HTML and PDF through the real file
     picker and Import guide dialog, from the four scratch originals the
     workflow group already uses;
   - the installer then deletes the TXT, HTML and long PDF originals.
3. **`rc-upgrade`:** `Add-AppxPackage` the candidate over v1.0.0. Then
   check, reusing `windows_package_upgrade.ps1`'s identity and data checks:
   - the version increased;
   - the name, publisher and family name are unchanged;
   - the game, its three guides, their managed files and the database
     are intact.
4. **`workflow-read`** on the candidate: place each guide at a distinct
   position.
5. **`workflow-resume`,** after a restart: each guide resumes at its own
   place, and completion is set explicitly.
6. **`rc-offline`:**
   - **The block:** the existing elevated controller
     (`-AllowOfflineFirewallRule`) adds a temporary outbound block rule for
     the package's executable only. The user authorizes it at run time.
   - **The check:** the app is relaunched, all three guides open, and no
     non-loopback TCP connection is made.
   - **The restore:** the rule is removed, its removal is reported, and
     the app reaches the network again.
7. **`workflow-export`:** back up to a scratch `.zip`.
8. **`workflow-restore`:**
   - remove the game, which also deletes its guides;
   - restore the backup, and check that the game, the guides, each place
     and the completion state are back.
9. **Cleanup:** remove the package, the certificate trust, the firewall
   rule if one is left, and the scratch folders, as the other groups do.

**Pass rule:** every stage passes. A stage that is skipped, for example
offline without authorization, means the run doesn't pass, and the matrix
row isn't written.

**Offline method.** T17.3 uses the app-scoped firewall rule, not the
physical adapter disconnect that `e2e-testing.md` describes. The rule
proves TR17.1 for the app without cutting the host's network, and with it
the SSH session coordinating the run. `e2e-testing.md` is updated to
match.

## Harness changes

- **`-Lane Public`** (installer and smoke):
  - the package name becomes `IlyaLissoboi.DesktopGuides`;
  - the event prefix becomes `DesktopGuides`, from `AppLaneNames`;
  - the window title becomes `Desktop Guides`.

  The default stays Preview, so CI is unchanged. Test gates work in Public
  builds under the Public prefix. Only the fixture providers are compiled
  out, and the `rc` group doesn't use them.
- **`-ReleaseSigned`:**
  - **No re-signing:** it installs packages that are already signed,
    instead of re-signing with the test certificate.
  - **Trust:** it trusts `docs/p1/release/desktop-guides-release.cer` in
    `LocalMachine\TrustedPeople` for the run, and removes it in cleanup.
  - **Signer check:** it fails if a package's signer thumbprint isn't the
    pinned one.
- **`-PreviousPackage <msix>`** and **`-PackagePath <candidate msix>`**
  name the two packages. The `rc` group requires `-Lane Public` and
  `-ReleaseSigned`.
- **The evidence checklist** gains three lines:
  - "Upgrade: A → B, same family, library intact";
  - "Release signer: matches the pin";
  - "Offline relaunch: rule added, no connection, rule removed".

## The record

- **`docs/p1/release/tested-matrix.md`:** a new file, with one section per
  release. Each tested target is a row:
  - the Windows edition, build and CPU, and the architecture;
  - the Windows App Runtime and WebView2 Runtime versions installed;
  - the package version, the MSIX SHA-256 and the signer thumbprint;
  - the run date, and each stage's result.

  The untested rows are stated as such: Windows 10 x64, Windows 11 ARM64,
  a runtime-free clean machine (T17.8), and the portable build's own
  release gates.
- **Release notes template:** its "Limits" section gains two lines.
  - "Tested on Windows 11 x64; see the tested matrix." This links to the
    file on `main`, so the draft's notes need no edit after the run.
  - "Scanned (image-only) PDF pages show the page image only, marked
    'Image-only page; OCR is unavailable.' They have no text to select,
    search or read aloud."

  The existing "Clean-machine and Windows 10 support aren't claimed yet"
  stays.
- **Evidence on the host:** the run's evidence checklist and stage
  results go to `E:\work\desktop-guides\artifacts\p1-e2e\<run-id>`, as
  `e2e-testing.md` prescribes. Only a sanitized summary reaches the repo,
  in `results.md` and the matrix: hashes, versions, results and durations.
  No guide contents, no data paths and no package data.

## Delivery

1. **PR 1, the harness and the docs:**
   - `-Lane Public`, `-ReleaseSigned`, `-PreviousPackage` and the `rc`
     group;
   - the evidence lines;
   - the release-notes lines, the matrix skeleton and the `e2e-testing.md`
     offline update.

   It's verified three ways:
   - the Preview-lane CI shards still pass;
   - script tests cover the new switches' refusals and the signer pin;
   - a host rehearsal of the `rc` group runs on a release dry run's
     artifact against v1.0.0.
2. **The tag, with the user's OK:** `v1.0.1`, which creates the draft.
3. **The candidate run** on pcsx2-win, with offline authorized by the user.
4. **PR 2:** the matrix row and the `results.md` summary. Then the user
   publishes the draft.

## Risks

- **v1.0.0's UI differs from what the smoke expects.** `workflow-build`
  then fails on v1.0.0. The upgrade source would then be populated by
  `windows_package_upgrade.ps1`'s seed instead, and that is recorded.
- **A Public test gate behaves differently from Preview.** The host
  rehearsal in PR 1 finds that before the tag.
- **The draft's artifact expires.** The run artifact lasts a day, but the
  draft's assets stay until it is published or deleted.

## PR 1 verification record

See the [PR 1 plan](t17-3-rc-plan.md).

- **Script tests:**
  - **The new tests failed first:** the lane and signer helpers (not
    defined yet), the parameter mirror (the lists differed) and the
    evidence lines ("The signer line is missing").
  - **Then they passed on the host,** under Windows PowerShell 5.1. CI's
    `core-tests` runs them under pwsh 7.
- **Two harness bugs, each now caught by `test_windows_scripts_parse.ps1`:**
  1. **`$lane = Get-ShellLane $Lane` assigned the lane object to the
     validated `-Lane` parameter.** PowerShell names are
     case-insensitive. Every installed shard of dispatch 38550598097
     failed with "cannot be validated". The guard now fails any assignment
     to a validated parameter. It also found the smoke's `$windowsTheme`,
     which was latent and is now renamed, with no change in behaviour.
  2. **Script-relative parameter defaults are empty under Windows
     PowerShell 5.1 `-File`.** `start_installed_task.ps1` failed on the
     host without `-Installer`. The guard now flags any parameter default
     that uses `$PSScriptRoot`. It found three scripts, whose defaults
     moved into the body.
- **CI:** dispatch
  [38552852527](https://github.com/ilya-slalom/desktop-guides/actions/runs/38552852527),
  after the lane fix, passed all 21 jobs, including all 10 installed
  shards on both lanes. Preview behaviour is unchanged.
- **The host rehearsal** on pcsx2-win passed on its third attempt, with
  the offline block authorized by the user.
  - **The earlier attempts:** the first failed on bug 2. The second failed
    because the seed tool wasn't restored on the fresh stage, an
    environment step that CI does separately; that run still cleaned up
    fully.
  - **The packages:** the candidate came from release dry run 38550652711
    (`DesktopGuides-1.0.1.0-x64.msix`). The previous release was v1.0.0.
    Both hashes matched their manifests.
  - **The evidence was all ✓:**
    - the release signer matches the pin;
    - the upgrade from 1.0.0.0 to 1.0.1.0 kept the family, and the
      library was intact;
    - offline relaunch: the rule was added, there was no connection, and
      the rule was removed;
    - the stages: build 11.4 s, read 15.9 s, resume 11.3 s, offline
      11.8 s, export 4.7 s, restore 15.4 s;
    - cleanup complete, with 0 crash entries.
  - **The target:** Windows build 10.0.26200.0 (AMD64), Windows App
    Runtime 2.5.1.0, WebView2 155.0.4283.45.
  - **Afterwards:** no package, no certificate trust and no firewall rule
    remained on the host.
- **Rulings:**
  1. The upgrade checks are rewritten in the installer, as
     `Invoke-RcUpgrade`.
  2. Offline is a new smoke mode, `workflow-offline`.
  3. The firewall handshake is shared, as `Invoke-WithFirewallBlock`.
  4. A release-signed run's cleanup line names the release certificate.
  5. The two parse-test guards were added to pin the bugs above.

## v1.0.1 candidate run record

- **The tag:** `v1.0.1`, at `77f45f2` (the #110 merge), pushed with the
  user's OK after main's push run
  [38575975111](https://github.com/ilya-slalom/desktop-guides/actions/runs/38575975111)
  passed. Release run
  [38578894569](https://github.com/ilya-slalom/desktop-guides/actions/runs/38578894569)
  built and signed it, ran its upgrade check, and created the draft.
- **The assets, checked before the run:**
  - the MSIX SHA-256 `EAA2F6A0…B478F1` matches the draft's manifest;
  - the draft's `.cer` is byte-identical to the repo's;
  - the manifest's signer is the pin.

  v1.0.0 (`F3B75010…015B9`) matched its own manifest.
- **The run** (`rc` group, pcsx2-win, offline authorized by the user):
  every evidence line passed.
  - **Signing and upgrade:** the release signer matches the pin, and the
    upgrade from 1.0.0.0 to 1.0.1.0 kept the family and left the library
    intact.
  - **Offline:** the rule was added, no connection was made, and the rule
    was removed.
  - **The stages:** build 8.2 s, read 14.4 s, resume 10.3 s, offline
    10.5 s, export 4.1 s, restore 13.3 s.
  - **Cleanup:** the package was removed, the trust wasn't left behind, no
    tasks or processes remained, and 0 crash entries were logged.

  Afterwards the host had no package, trust, rule or task left.
- **The record:** the [tested matrix](release/tested-matrix.md) row and
  `results.md`.
