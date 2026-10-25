# Releasing

How a maintainer cuts a Desktop Guides release. A release is a signed
draft pre-release on GitHub, built by [`release.yml`](../.github/workflows/release.yml)
from a `vX.Y.Z` tag. A human tests the draft's own assets and then
publishes it.

## Package lanes

One project builds two package identities. `-p:PackageLane` picks one; the
default is Preview.

| Lane | Name | Publisher | Use |
| --- | --- | --- | --- |
| Preview | `DesktopGuides.Preview` | `CN=DesktopGuides Development` | Development and CI. Its package family and data are separate from the public app's. |
| Public | `IlyaLissoboi.DesktopGuides` | `CN=359962D5-3801-484E-BE5B-39D0D498BEA4` (reserved in Partner Center) | Signed releases and in-place upgrades. |

- The Public lane builds from `Package.Public.appxmanifest` and
  `app.public.manifest`. Lane-dependent names (single-instance key, events,
  pipe) come from `AppLaneNames` in Core, so nothing is edited by hand.
- The Public manifest's Publisher must equal the release certificate's
  Subject. Name, Publisher and architecture never change between releases.
- Moving from Preview to Public is a new install. Data moves only through
  backup and restore.
- `sign_production_package.ps1` refuses to sign a Preview or test identity,
  or the development publisher, as a public release.
- Only x64 is released. ARM64 stays out of release output until its full
  native workflow passes.

## Versioning

- Tags are `vX.Y.Z`. `release_version.ps1` turns the tag into the package
  version `X.Y.Z.0`.
  - The major version is at least 1.
  - Each field is at most 65535.
  - The fourth field is always 0, for Store-compatible numbering.
- Each release must be higher than the last published one. The workflow
  passes the previous release's MSIX to `verify_production_package.py`
  (`--previous-package`), which fails on a lower or equal version or a
  changed identity.
- Releases are linear. The "previous release" is the most recent published
  (non-draft) release, so don't publish an out-of-order hotfix for an older
  version.
- `stamp_package_version.ps1` writes the version into the Public manifest's
  Identity at build time. The committed manifest isn't edited.

## The release certificate

The public package is signed with one self-signed code-signing certificate.

- **The pin:** [`release/release-certificate.json`](release/release-certificate.json)
  holds its Subject, thumbprint (`BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6`)
  and `notAfter` (11 October 2031).
- **The public half:** [`release/desktop-guides-release.cer`](release/desktop-guides-release.cer).
  Users compare its thumbprint before trusting it.
- **The private key:** the PFX (base64) and its password are the repository
  secrets `RELEASE_SIGNING_PFX` and `RELEASE_SIGNING_PASSWORD`. The owner
  keeps a copy in a password manager. The key is never in the repository,
  logs or artifacts.
- **In the job:** the PFX is written to a temp file, imported into
  `Cert:\CurrentUser\My`, and the file and both secrets are removed from the
  environment. `assert_release_certificate.ps1` refuses to sign unless the
  thumbprint, Subject and expiry match the pin. The key is deleted from the
  store afterwards, even on failure.

**Rotation** (expiry or loss):
1. Create a new certificate with the same Subject using
   `tools/p1/new_release_certificate.ps1`. It prints only the thumbprint,
   the dates and the `gh secret set` commands.
2. Update both secrets, `release-certificate.json` and the committed `.cer`.
3. Tell users to trust the new `.cer` once. The identity is unchanged, so
   installs still upgrade in place.

The release job's upgrade step trusts only the current `.cer`, so it can't
install the previous release after a rotation. For that first release,
either trust the old `.cer` too in that step for the run, or skip the step
and run the upgrade check by hand.

## Before tagging

1. **Merge to `main`** and wait for its `windows-ci` push run to pass.
2. **Update the release notes template,**
   [`release/release-notes-template.md`](release/release-notes-template.md):
   the "What's new" section and the "Limits" list. Leave the `{{VERSION}}`,
   `{{COMMIT}}`, `{{MSIX}}`, `{{PORTABLE}}`, `{{THUMBPRINT}}` and
   `{{RUNTIME}}` placeholders; the workflow fills them and fails if any
   `{{` is left. This is a docs change, so it can land after the green run.
3. **Optional dry run.** It builds and signs, but publishes nothing, and
   may use any commit or branch:

   ```sh
   gh workflow run release.yml -f tag=vX.Y.Z [--ref <branch>]
   ```

   Download the `release-X.Y.Z.0` artifact and check `release-manifest.json`
   against the pin. The repository is public: the artifact (dry runs
   included) can be downloaded by any signed-in GitHub user, for one day.

## The release gate

On a tag push, the `gate` job runs `release_ci_gate.ps1`:

- the tagged commit must be on `origin/main`;
- among its last 50 first-parent commits, the nearest one with a completed
  `windows-ci` push run must have passed;
- only docs may change after that run: files under `docs/` or ending in
  `.md`. (`windows-ci` skips docs-only pushes, so they have no run of their
  own.)

A dry run skips the gate.

## Tag and build

```sh
git tag -a vX.Y.Z -m "Desktop Guides X.Y.Z" <sha>
git push origin vX.Y.Z
```

`release.yml` then, on `windows-2025`:

1. restores locked dependencies;
2. stamps the version and builds the unsigned x64 Public MSIX;
3. downloads the latest published release's MSIX and verifies the new
   package's identity and version against it;
4. signs the MSIX with the pinned certificate and a DigiCert timestamp;
5. upgrades the previous release to the new one in place
   (`windows_package_upgrade.ps1`), checking the family, version and
   library (skipped when there's no published release);
6. publishes the portable single-file exe (trimmed, compressed,
   ReadyToRun) and zips it;
7. fails on excluded Windows App SDK components in either build, and on any
   trim or C#/WinRT warning not in `tools/p1/trim-warning-allowlist.txt`;
8. measures the portable exe (size, first-launch extraction, cold and warm
   launch);
9. writes the checksums, `release-manifest.json` and the release notes.

The `publish` job then creates a **draft pre-release** titled
`Desktop Guides X.Y.Z`. It fails if a release for the tag already exists.

### Release assets

| Asset | What |
| --- | --- |
| `DesktopGuides-X.Y.Z.0-x64.msix` | The signed MSIX. |
| `DesktopGuides-X.Y.Z.0-x64.msix.sha256` | Its SHA-256. |
| `DesktopGuides-portable-x64-X.Y.Z.zip` | The portable exe. It is unsigned. |
| `DesktopGuides-portable-x64-X.Y.Z.zip.sha256` | Its SHA-256. |
| `desktop-guides-release.cer` | The release certificate's public half. |
| `release-manifest.json` | Version, tag, commit, identity, both files' sizes and hashes, measurements, signer thumbprint and expiry, Windows App Runtime minimum, Windows App SDK version, prerequisite links. |

The run artifact also holds `release-notes.md`, the portable build record
and, when a previous release exists, `upgrade.json`. These aren't attached
to the release.

## Release-candidate run

The draft's own assets are tested before anything is public.

1. **Download and check** the draft's MSIX, `.cer` and
   `release-manifest.json`, and the previous release's MSIX and manifest.
   Before installing:
   - each MSIX SHA-256 matches its manifest;
   - the `.cer` thumbprint matches the pin;
   - the package's signer is that certificate.
2. **Run the `rc` group** of the installed harness on Windows 11 x64; see
   [testing](testing.md). It installs the previous release, builds a
   library through the UI, upgrades in place to the candidate, reads,
   resumes and completes, relaunches with the app's network blocked, and
   exports, removes and restores. The offline stage needs a maintainer's
   authorization at run time.
3. **Pass rule:** every stage passes. A skipped stage is not a pass.
4. **Record it:** add a section to
   [`release/tested-matrix.md`](release/tested-matrix.md) with the target's
   Windows build, CPU, Windows App Runtime and WebView2 versions, the MSIX
   name, SHA-256 and signer, the date, the release run, and each stage's
   result. Only sanitized data: hashes, versions, results and durations.

## Publish

Publishing the draft is a human step. Review the draft's notes and assets,
then publish it on GitHub. The tested hashes in the matrix must match the
published assets byte for byte, so don't replace assets on the draft.

## If a stage fails

Delete the draft. Fix the problem on `main`, and ship the fix as the next
patch version (`vX.Y.Z+1`), with its own candidate run. Don't reuse a tag.

## What a release doesn't claim

- **Clean install:** no claim until T17.8 proves the runtime-free path in a
  VM; see the [roadmap](roadmap.md).
- **Windows 10** is not advertised, and **ARM64** is not released.
- **Microsoft Store:** submission is T17.5, the last roadmap item.
- The release notes' "Limits" must stay in line with these.
