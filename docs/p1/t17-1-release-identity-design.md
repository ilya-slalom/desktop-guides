# T17.1 release identity and signing design

Status: PR a merged in #82; PR b in review.
Prerequisite T11.1 is merged. The groundwork from 27 September (the package
verifier, the public-mode signer and the release procedure) is merged.

## Intent

Desktop Guides gets one permanent public package identity, so every release
installs and upgrades in place and keeps the user's library. Releases are
signed in CI with a self-signed certificate, published as a draft GitHub
pre-release from a version tag, and ship with a portable build. The
development lane, `DesktopGuides.Preview`, stays exactly as it is for the
installed smokes.

Traces: the T17.1 row and sequence in
[implementation-plan.md](implementation-plan.md) (TR17.2),
[work-breakdown.md](../work-breakdown.md) S17 and T17.1,
[p1-technical-design.md](../p1-technical-design.md) S17, and the
[release procedure](release-packaging.md).

Decisions made during brainstorming:

- **Signing:** a self-signed code-signing certificate for now. Microsoft
  Store certification is a separate task once feature development is done
  (T17.5, below).
- **Identity:** reserved in Partner Center, so the self-signed builds and the
  later Store build share one identity:
  - Name `IlyaLissoboi.DesktopGuides`;
  - Publisher `CN=359962D5-3801-484E-BE5B-39D0D498BEA4`;
  - PublisherDisplayName `Ilya Lissoboi`.

  The signing certificate's Subject is exactly that Publisher.
- **Key storage:** the certificate is exported once as a password-protected
  PFX and kept as two GitHub Actions secrets, used only by the release job.
- **Release:** pushing a tag `vX.Y.Z` builds, signs and creates a draft
  GitHub pre-release. A person reviews and publishes it.
- **Prerequisites:** the release notes link Microsoft's downloads for the
  Windows App Runtime and WebView2, and the release manifest records the
  versions. Nothing is redistributed.
- **Approach A:** one project with two lanes chosen at build time, rather
  than switching the default manifest or adding a second project.

## Rulings against the plan

- **The upgrade gate runs on GitHub's hosted runners**, not on pcsx2-win
  through a scheduled task. The installed shell smokes already install MSIX
  packages there, and the gate needs no host data. AGENTS.md's scheduled-task
  rule applies to installed runs triggered over SSH, which this isn't.
- **The release doesn't re-run the installed shell smokes.** It requires the
  tagged commit's `windows-ci` push run on `main` to have passed. That run
  tests the same code on the Preview lane, and the lane changes only names.
- **Test gates stay in the public build.** They act only when a named event
  already exists in the user's own session, so they add no attack surface,
  and the upgrade gate then runs the code users get.
- **P1 grows to 56 tasks** with T17.5.

## PR a: two lanes in one project

### Build property

`DesktopGuides.Production.csproj` gains `PackageLane`, `Preview` by default.
With `-p:PackageLane=Public` it:

- uses `Package.Public.appxmanifest` as the `AppxManifest` instead of
  `Package.appxmanifest`;
- uses `app.public.manifest` as the `ApplicationManifest`;
- defines `PUBLIC_LANE`.

Any other value fails the build. The portable build (`-p:Portable=true`) has
no package identity, so the lane sets only its application manifest and
`PUBLIC_LANE`; the release builds it with `-p:PackageLane=Public` for the
"Desktop Guides" window title.

### Public manifests

`Package.Public.appxmanifest` is a copy of `Package.appxmanifest` with the
same capabilities, assets and dependencies, except:

- `Identity Name="IlyaLissoboi.DesktopGuides"`,
  `Publisher="CN=359962D5-3801-484E-BE5B-39D0D498BEA4"`,
  `Version="1.0.0.0"` (the release job stamps the real version);
- `Properties/DisplayName` and the tile `DisplayName` are "Desktop Guides";
- `PublisherDisplayName` is "Ilya Lissoboi".

`app.public.manifest` is a copy of `app.manifest` with the assembly identity
name `DesktopGuides`.

### Lane names

A new `AppLaneNames` record in `DesktopGuides.Core`:

```csharp
public sealed record AppLaneNames(string Prefix, string WindowTitle)
{
    public static AppLaneNames For(bool publicLane);
    public string InstanceKey { get; }            // Prefix + ".Main"
    public string TestGate(string name, int processId); // Local\<Prefix>.<name>.<pid>
}
```

- Preview: prefix `DesktopGuides.Preview`, title "Desktop Guides Preview".
- Public: prefix `DesktopGuides`, title "Desktop Guides".

A Production `AppLane` class exposes `AppLaneNames.Current`, chosen with
`#if PUBLIC_LANE`. Every hard-coded `DesktopGuides.Preview` string in
`src/DesktopGuides.Production` moves to it:

- the instance key and the `Local\DesktopGuides.Preview.*` event names in
  `Program.cs`;
- the window title in `ShellWindow.xaml.cs`;
- every test-gate name in the readers, progress, HTML, PDF and external-link
  code.

The portable build keeps `DesktopGuides.Portable.Main`. On the Preview lane
every name stays byte-identical, so the smoke harness is unchanged. A public
install and a Preview install can run side by side.

A public install keeps its library in its own package `LocalState`,
separate from Preview's. Moving between them uses T20.2's export and
restore.

### Tests

- **Core:** `AppLaneNames` for both lanes: prefix, title, instance key and
  test-gate format.
- **CI:** a step in `production-packages` (x64) also builds
  `-p:PackageLane=Public` and runs `verify_production_package.py` with the
  public Name and Publisher and `1.0.0.0`. The Preview package and its checks
  are unchanged.

## PR b: certificate, release workflow and upgrade gate

### Certificate setup (one time, by the user)

`tools/p1/new_release_certificate.ps1` runs on pcsx2-win in the user's
interactive session:

- `New-SelfSignedCertificate -Type CodeSigningCert` with Subject exactly
  `CN=359962D5-3801-484E-BE5B-39D0D498BEA4`, RSA 3072, SHA-256, valid for five
  years, in `Cert:\CurrentUser\My`;
- exports a PFX protected by a generated password, and the public `.cer`;
- prints only the thumbprint, the validity dates and two `gh secret set`
  commands that read the PFX (base64) and the password from files:
  `RELEASE_SIGNING_PFX` and `RELEASE_SIGNING_PASSWORD`;
- once the user confirms they saved the PFX and password in a password
  manager, deletes the temporary files and removes the certificate from the
  store.

The user runs the script from the PR b branch before it merges. The
thumbprint is then recorded in `release-packaging.md`, and the public
certificate is committed as `docs/p1/release/desktop-guides-release.cer`,
both in PR b. A
lost certificate is replaced by a new one with the same Subject. The package
identity is unchanged, so upgrades still work; users trust the new `.cer`
once.

### Version and stamping

- `tools/p1/release_version.ps1 -Tag vX.Y.Z` returns `X.Y.Z.0`. It rejects
  anything else: a tag without the leading `v`, more or fewer than three
  parts, leading zeros, a zero major, or any field over 65535.
- `tools/p1/stamp_package_version.ps1 -Manifest <path> -Version <v>` writes
  the Identity version. It refuses a manifest whose Name isn't
  `IlyaLissoboi.DesktopGuides`, and checks the result.

### Release workflow

`.github/workflows/release.yml` runs on tags `v*.*.*`:

1. **Gate** (ubuntu): resolve the version. The tagged commit must be on
   `main`, and its `windows-ci` push run must have concluded `success`.
2. **Build and sign** (windows-2025):
   - stamp the version; locked restore; build x64 with
     `-p:PackageLane=Public -p:GenerateAppxPackageOnBuild=true
     -p:AppxPackageSigningEnabled=false`;
   - if a published (non-draft) release exists, download its MSIX for
     `--previous-package`;
   - verify the package; import the PFX from the secret into
     `Cert:\CurrentUser\My` and delete the file at once; refuse if the
     thumbprint isn't the pinned one;
   - sign with `sign_production_package.ps1` in public mode, with
     `-TimestampUrl http://timestamp.digicert.com` and the previous package;
     remove the certificate in an `always()` step;
   - if a previous release exists, run the upgrade gate from it to the new
     package;
   - publish the portable build and package it with
     `package_portable_release.ps1 -Version X.Y.Z`;
   - write `release-manifest.json`: version, commit, MSIX and portable
     SHA-256, signer thumbprint and validity, timestamp status, the Windows
     App Runtime minimum from the packed manifest, the Windows App SDK version
     from `Directory.Packages.props`, and the prerequisite download links.
3. **Publish** (ubuntu, `contents: write` only here):
   `gh release create vX.Y.Z --draft --prerelease --title "Desktop Guides X.Y.Z"`
   with the signed MSIX, `desktop-guides-release.cer`, the portable zip, a
   `.sha256` for each, and `release-manifest.json`. The notes come from
   `docs/p1/release/release-notes-template.md`.

Any failure leaves no release and no partial assets. Secrets are masked; no
step echoes them; the password reaches `Import-PfxCertificate` as a
SecureString built from an environment variable.

### Release notes

The template covers:

- trusting the certificate (one admin PowerShell line into Local Machine →
  Trusted People), then installing the MSIX;
- Microsoft's Windows App Runtime and WebView2 Evergreen download links;
- the portable build and its data folder, `%LOCALAPPDATA%\DesktopGuides`;
- the limits: x64 only, tested on Windows 11, no clean-machine claim before
  T17.3.

### Upgrade gate

`tools/p1/windows_package_upgrade.ps1 -OldPackage -NewPackage -Certificate`
reuses `windows_shell_install.ps1`'s install and launch helpers:

1. refuses to run if `IlyaLissoboi.DesktopGuides` is already installed;
2. trusts the certificate and installs the old package;
3. seeds a library into that package's `LocalState` with the seed tool, and
   records the database and content hashes;
4. installs the new package over it without uninstalling, and checks the
   package family is unchanged and the version is higher;
5. launches the app, waits for "Library ready." and the window title
   "Desktop Guides", and closes it;
6. checks the hashes again;
7. in `finally`, removes the package, its data and the trust.

It runs in two places:

- **`windows-ci`**, on pushes to `main` and on dispatch: a `public-upgrade`
  job builds the public lane at `1.0.0.0` and `1.0.1.0` from one commit,
  signs both with a throwaway non-exportable certificate with the same
  Subject (valid one day) and runs the gate.
- **The release workflow**, when a published release exists: from the
  previous release's MSIX to the newly signed one.

### Tests

In `core-tests`, with the existing `test_*.ps1` pattern:

- `test_release_version.ps1`: valid tags and each rejection;
- `test_stamp_package_version.ps1`: stamps a copy, refuses a Preview
  manifest;
- `test_package_portable_release.ps1`: extended for `-Version`.

## T17.5: Microsoft Store certification and listing

A new task after T17.3 and every feature task:

- submit through Partner Center with the reserved identity;
- pass Store certification;
- write the listing;
- confirm the Store-signed package upgrades the sideloaded installs in place
  (same identity), and decide whether sideloaded releases continue.

## Rules that hold across both PRs

- The Preview lane's names, identity, data and smokes don't change.
- No private key material in the repository, logs or artifacts. Only the
  public `.cer` and the thumbprint are committed.
- Every public package has the same Name, Publisher and architecture, and a
  strictly higher `X.Y.Z.0` version than the previous published release.
- Only x64 is released. ARM64 waits for the native ARM64 P1 workflow.
- PowerShell scripts stay ASCII-only.

## Out of scope

- ARM64 release output.
- Clean-machine and Windows 10 claims (T17.3).
- The Store submission (T17.5).
- An auto-update feed (`.appinstaller`).

## Docs to update

Each PR updates this status line and its verification record. PR b also
updates `release-packaging.md` (the lanes table, the certificate setup, the
release steps, the user install steps and the pinned thumbprint), the T17.1
paragraph and row in `implementation-plan.md`, `progress.md`, and adds T17.5
to `implementation-plan.md` and `work-breakdown.md` with the task count at 56.

## T17.1 verification record

### PR a: two lanes

Commits `db7b6db` (lane names), `374b19b` (the Public lane in the shell
build), `87db610` and `9231770` (CI step and package description).

- **Core tests.** 1036 passed on pcsx2-win.
- **Host builds and verifier** (x64; the only warning is the pre-existing
  `mspdbcmf.exe` symbols-package warning):
  - Preview: `DesktopGuides.Preview`, `CN=DesktopGuides Development`,
    version 0.1.0.0.
  - `-p:PackageLane=Public`: `IlyaLissoboi.DesktopGuides`,
    `CN=359962D5-3801-484E-BE5B-39D0D498BEA4`, version 1.0.0.0.
  - `-p:PackageLane=Store` fails with "PackageLane must be Preview or
    Public, got 'Store'."
- **CI.** [Run 38105036523](https://github.com/ilya-slalom/desktop-guides/actions/runs/38105036523)
  (head `9231770`) is green. The new "Build and verify the public lane" step
  printed `{"architecture": "x64", "name": "IlyaLissoboi.DesktopGuides",
  "publisher": "CN=359962D5-3801-484E-BE5B-39D0D498BEA4", "version":
  "1.0.0.0", "windowsAppRuntimeMinimum": "2.5.1.0"}`. This step is the
  standing check that the Public package verifies on every build.
- **Rulings.**
  - Plan ruling 1: lane names take three forms (`Local\<prefix>.<name>`,
    `Local\<prefix>.<name>.<pid>`, and the pipe name `<prefix>.<name>.<pid>`),
    so `AppLaneNames` exposes `LocalEvent(name)`, `LocalEvent(name, pid)` and
    `PipeName(name, pid)` instead of a single `TestGate`.
  - The Public manifest Description no longer says "preview" (user-visible;
    the spec was silent).
  - Test gates stay reachable in the public build, as the spec's ruling
    accepts: they are session-local named events only.
  - `production-shell-ui (html)` failed once in this run and passed on
    rerun of the same code. Treated as a flake: a name regression would fail
    every shard.
- **Not run.** Signing, the release workflow, the certificate and the
  upgrade gate are PR b's.

### PR b: release

Commits on `feat/p1-t17-1-release`: `19b0e6f` (version scripts), `f8cb201` and
`5a0c791` (CI gate), `290a5b1` (portable packaging), `8649f31`, `a23481d`,
`6341c0f` and `798bd74` (upgrade gate), `283f8e8` and `42a1a4b` (certificate
script), `1138ad4` (pin), `c3f868b`, `38d811c`, `a2c612a` and `8a7e2b4`
(release workflow).

- **PowerShell tests.** The `core-tests` job runs the script tests under
  pwsh 7: [run 38113468830](https://github.com/ilya-slalom/desktop-guides/actions/runs/38113468830)
  passed (it covers the certificate script), and the final `windows-ci` run
  [38120109659](https://github.com/ilya-slalom/desktop-guides/actions/runs/38120109659)
  on `8a7e2b4` is green, including the certificate pin test. pcsx2-win has only
  Windows PowerShell 5.1; the scripts run in both.
- **Upgrade gate.** `public-upgrade` passed in run
  [38110708303](https://github.com/ilya-slalom/desktop-guides/actions/runs/38110708303)
  and, after the fix round, run
  [38112108659](https://github.com/ilya-slalom/desktop-guides/actions/runs/38112108659).
  `result.json` of the latter: package family
  `IlyaLissoboi.DesktopGuides_ybwptzv97781r`, `1.0.0.0` to `1.0.1.0`,
  `libraryPreserved` true, `oldAppOpenedLibrary` true, `upgradedAppLaunched`
  true, `cleanupError` null.
- **Release dry run.** [Run 38120091727](https://github.com/ilya-slalom/desktop-guides/actions/runs/38120091727)
  (`workflow_dispatch`, tag `v1.0.0`, head `8a7e2b4`): gate and build
  succeeded, publish skipped. Artifact `release-1.0.0.0`:
  `DesktopGuides-1.0.0.0-x64.msix` (68,633,653 bytes) and its `.sha256`,
  `DesktopGuides-portable-x64-1.0.0.zip` (92,160,813 bytes) and its `.sha256`,
  `DesktopGuides-portable-x64-1.0.0.json`, `desktop-guides-release.cer`
  (byte-identical to the committed one) and `release-manifest.json` (signer
  thumbprint equals the pin, timestamped, `windowsAppRuntimeMinimum` 2.5.1.0,
  `windowsAppSdk` 2.5.1, `previousRelease` false). The checksums match the
  downloaded files.
- **Host `signtool verify /pa /tw /v`** (pcsx2-win, Windows SDK 10.0.28000.0)
  of the dry-run MSIX: the chain shows the pinned certificate (SHA1
  `BAA638E4...`, expires 2031-10-11) and a DigiCert timestamp. It exits 1,
  "terminated in a root certificate which is not trusted by the trust
  provider", because the release certificate is deliberately not trusted on
  that host. The CI sign step's own `verify /pa /tw` passed with the
  certificate trusted on the runner.
- **Pinned certificate.** Thumbprint `BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6`,
  Subject `CN=359962D5-3801-484E-BE5B-39D0D498BEA4`, `notAfter`
  2031-10-11T06:37:44Z. Created by the owner on pcsx2-win on 11 October 2026
  with `new_release_certificate.ps1`; both secrets are set, and the
  certificate is not in pcsx2-win's stores.
- **Flakes.** `production-shell-ui (html)` failed once in
  [38105036523](https://github.com/ilya-slalom/desktop-guides/actions/runs/38105036523)
  (attempt 1) and in
  [38110158929](https://github.com/ilya-slalom/desktop-guides/actions/runs/38110158929)
  with no related change, and passed on rerun. Worth its own issue.
- **Rulings** (each with its cost if wrong):
  - 2. The CI gate accepts a docs-only tagged commit when its nearest
    ancestor with a green `windows-ci` push run differs only in docs paths.
    Cost: a docs path that changes behavior would release untested.
  - 3. The upgrade gate compares library content (`describe-library` and
    SHA-256 of `library/content` files), not raw database bytes. Cost: a
    database-only corruption the content check misses.
  - 4. The upgraded app is judged launched by its main window title
    "Desktop Guides" within 60 s, not "Library ready." Cost: a window that
    opens but fails to load the library passes the launch check (the library
    content check still runs).
  - 5. The release workflow has a dry run (`workflow_dispatch` with `tag`).
    Cost: a production-signed MSIX is a downloadable artifact for 90 days
    (retention to be shortened).
  - 6. The v1 install is launched once before seeding so Windows creates
    `LocalState`. Cost: a first-run-only difference from a real install.
  - 7. A dry run skips the CI gate. Cost: a dry run may build an unvetted
    commit, but it publishes nothing.
  - Also accepted: a temporary branch-push trigger (`38d811c`, reverted in
    `a2c612a`) to register the workflow for dispatch (two noisy commits); the
    csproj skips the public AppxManifest when `Portable=true` (none); the
    portable zip is built on the Public lane so its window title is "Desktop
    Guides" (a Preview title if wrong); the CI throwaway certificate and the
    release trust are removed only after use, and the signing key is deleted
    with `-DeleteKey`; repository secrets are readable by any branch workflow
    (accepted with no Environment).
- **Not run.** A real tag release (needs the owner's go-ahead after merge);
  the upgrade from a previous published release (none exists yet); ARM64;
  clean-machine.
