# P1 MSIX packaging and prerequisite delivery

Status: T17.1 is merged (#82, #83); the first release, v1.0.0, was published
as a pre-release on 11 October 2026. T17.6 (#86) added the app icon, which
v1.0.0 predates. The public identity (`IlyaLissoboi.DesktopGuides`,
`CN=359962D5-3801-484E-BE5B-39D0D498BEA4`) is built with
`-p:PackageLane=Public`, and a `vX.Y.Z` tag produces a signed draft
pre-release.
This procedure does not establish a clean-install or release-support claim.

## Identity and version

| Lane | Package Name and Publisher | Use |
| --- | --- | --- |
| Development | `DesktopGuides.Preview`, `CN=DesktopGuides Development` | Installed shell and reader checks. Its package family and local data stay isolated. |
| Public | `IlyaLissoboi.DesktopGuides`, `CN=359962D5-3801-484E-BE5B-39D0D498BEA4` (reserved in Partner Center); build with `-p:PackageLane=Public` | Signed releases and in-place upgrades. Signed with the pinned release certificate below. Never use a provisional subject in this lane. |

The public manifest Publisher must exactly match the release signing
certificate Subject. Both values stay fixed across upgrades. A change from Preview to
the public identity is a new installation; carrying Preview data across needs
the explicit export/restore workflow in T20.2. The Public lane builds from
`Package.Public.appxmanifest` and `app.public.manifest`, and every
lane-dependent name (single-instance key, events, pipe) comes from
`AppLaneNames` in Core via `AppLane`, so nothing is edited by hand per
release. The Preview lane stays isolated from the public package.

Use `major.minor.patch.0` with a nonzero major component, increasing at least
one of the first three fields for each new public package. Each field must fit
in the MSIX version range.
The fourth field is held at zero for Store-compatible numbering. Build v1 and
v2 with the same Name, Publisher, and architecture, and pass v1 to the package
verifier as `--previous-package` before signing v2. This checks the metadata
relationship; the interactive install/upgrade check below proves Windows
deployment and user-data continuity.

## Release certificate

The public package is signed with one self-signed code-signing certificate,
pinned in `docs/p1/release/release-certificate.json`:

- Subject `CN=359962D5-3801-484E-BE5B-39D0D498BEA4` (equal to the manifest
  Publisher)
- Thumbprint `BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6`
- Valid until 2031-10-11T06:37:44Z (`notAfter`)

The public half is committed as `docs/p1/release/desktop-guides-release.cer`.

**Creation.** The owner ran `tools/p1/new_release_certificate.ps1` on
pcsx2-win on 11 October 2026 (RSA 3072, SHA-256, five years, AES256-protected
PFX). It prints only the thumbprint, the dates and the `gh secret set`
commands. The certificate is not in pcsx2-win's stores.

**Where the key lives.** The PFX (base64) and its password are the repository
secrets `RELEASE_SIGNING_PFX` and `RELEASE_SIGNING_PASSWORD`, and the owner
keeps a copy in a password manager. The release job writes the PFX to a
temporary file, imports it into `Cert:\CurrentUser\My`, deletes the file, and
refuses to sign unless the thumbprint, Subject and expiry match the pin
(`tools/p1/assert_release_certificate.ps1`). It removes the key afterwards.
Never put the PFX or password in the checkout, logs or artifacts.

**Rotation** (expiry or loss). Create a new certificate with the same Subject
using `new_release_certificate.ps1`, update both secrets, update
`release-certificate.json` and the committed `.cer`, and tell users to trust
the new `.cer`. The package identity is unchanged, so installs still upgrade
in place.

The release job's previous-release upgrade step trusts only the current
`.cer`, so it can't install the previous release's package after a rotation.
For the first release after a rotation, either trust the old `.cer` too in
that step for that run, or skip the step and run the upgrade check manually.

Only linear releases are supported: the "previous release" is the most
recently created published release, so don't publish an out-of-order hotfix
for an older version.

## Releasing

1. Merge to `main` and wait for its `windows-ci` run to pass. Docs-only
   commits are allowed on top of a green commit.
2. Optional dry run, which builds and signs but publishes nothing and may use
   any commit or branch: `gh workflow run release.yml -f tag=vX.Y.Z` (add
   `--ref <branch>`). Download the `release-X.Y.Z.0` artifact and check
   `release-manifest.json` against the pin.
3. Tag the green `main` commit and push the tag:
   `git tag -a vX.Y.Z -m "Desktop Guides X.Y.Z" <sha> && git push origin vX.Y.Z`.
   The version is `X.Y.Z.0`; it must be higher than the last release.
4. The workflow gates the tag (on `main`, `windows-ci` green), builds the
   x64 Public MSIX, verifies it (with `--previous-package` when a published
   release exists), signs it with a DigiCert timestamp, runs the
   previous-release upgrade, builds the portable zip, and writes checksums and
   `release-manifest.json`. The `publish` job then creates a **draft
   pre-release** `Desktop Guides X.Y.Z`.
5. Review the draft and publish it manually. The draft release stays private
   until it is published, but the run's `release-X.Y.Z.0` artifact (dry runs
   included) can be downloaded by any signed-in GitHub user for one day.

Release assets: `DesktopGuides-X.Y.Z.0-x64.msix`,
`DesktopGuides-portable-x64-X.Y.Z.zip`, a `.sha256` for each,
`desktop-guides-release.cer` and `release-manifest.json`. The run artifact
also holds `release-notes.md`, the portable build record
(`DesktopGuides-portable-x64-X.Y.Z.json`) and, when a previous release exists,
`upgrade.json`; these are not attached to the release. Keep ARM64 out of
release output until its complete native P1 workflow passes. Both the MSIX
and the portable exe carry `THIRD-PARTY-NOTICES.txt`, the MIT notice for
the Fluent UI System Icons used by the optional colorful icons (T11.5).

`release-manifest.json` also records `measurements` (T17.4): the MSIX size,
and for the portable exe its size, what its first launch extracts (files
and bytes), and the median cold and warm launch times on the release
runner, from start until the library view shows. Each cold launch starts
with an empty extraction folder; warm launches reuse one. Runner times are
noisier than a desktop's, so compare them with earlier releases, not with
host measurements.

The app icon (T17.6) is the source for every logo in
`src/DesktopGuides.Production/Assets`: the Square44x44 logo at five scales
and 14 target sizes (plain, unplated and light-unplated), Square150x150,
Wide310x150, StoreLogo and SplashScreen at five scales, and `AppIcon.ico`
(16 to 256 px) for the portable exe and its window. Sizes 16 to 32 px use a
separate small-size drawing of the icon, with a larger controller. `AppxBundle=Never` keeps the scale-qualified
logos in one `.msix` rather than a bundle of resource packages.
To change the icon, replace the two masters in `docs/p1/app-icon/` and run
`python tools/p1/generate_app_icon_assets.py src/DesktopGuides.Production/Assets`
(needs Pillow).

Record the commit, the MSIX SHA-256, identity, Windows App Runtime minimum
and signer thumbprint (all in `release-manifest.json`).

## User install steps

These are the steps in the release notes template
(`docs/p1/release/release-notes-template.md`):

1. Install the Windows App Runtime and the WebView2 Runtime if missing.
2. Download `desktop-guides-release.cer` and **compare its thumbprint before
   trusting it**. In PowerShell, in the folder with the file:

   ```powershell
   (Get-PfxCertificate .\desktop-guides-release.cer).Thumbprint
   ```

   The output must equal `BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6`. If it
   differs, do not continue.
3. In an administrator PowerShell, trust it once:

   ```powershell
   Import-Certificate -FilePath .\desktop-guides-release.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

4. Open the MSIX and choose **Install**. Later releases upgrade it in place
   and keep the library.

The portable zip holds one `DesktopGuides.Production.exe` that needs no
installation and keeps its library in `%LOCALAPPDATA%\DesktopGuides`.

The release notes list the same limits:

- Uninstalling the app deletes its library; back it up first from
  **Settings → Back up library**.
- The portable exe is unsigned, so Windows SmartScreen may warn.
- Trusting the `.cer` trusts anything signed with this key. Remove that trust
  later in an administrator PowerShell with
  `Remove-Item Cert:\LocalMachine\TrustedPeople\BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6`.
- After a certificate rotation, users trust the new `.cer` once.

The current x64 production MSIX declares
`Microsoft.WindowsAppRuntime.2` at minimum version `2.5.1.0`. The project
publishes its .NET runtime self-contained and uses the Windows App SDK as a
framework dependency. The CI production artifact currently contains the app
MSIX alone; it is a build artifact, not a complete offline installer.

## Prerequisite delivery

At candidate time, obtain the Windows App Runtime redistributable or installer
for the selected Windows App SDK channel and the x64 WebView2 Evergreen
Standalone Installer from Microsoft. Record their exact versions, Microsoft
signatures, source, SHA-256 hashes, and the selected app MSIX hash in one
candidate manifest. Do not use a runtime older than the MSIX
`PackageDependency` minimum. Keep the installers beside the signed MSIX in
the offline distribution. On an online setup path, acquire the same supported
installers from Microsoft and verify them before running.

Install Windows App Runtime and WebView2 Evergreen before the app MSIX.
WebView2's online bootstrapper requires network access; its Evergreen
Standalone Installer is the offline path. An installer or setup guide must
provide an actionable message for a missing prerequisite and retry after it
is installed. The actual missing-runtime failure and recovery observation
requires the disposable runtime-free Windows 11 x64 VM in T17.3, which the
user has deferred. Do not advertise clean-machine or offline-install support
until that gate passes.

## Installed upgrade gate

Two automated checks run `tools/p1/windows_package_upgrade.ps1`, which
installs an old package, seeds a library, installs the new package over it
without uninstalling, and checks the family, the higher version, the library
content and that the upgraded app opens its window:

- the `public-upgrade` job in `windows-ci` builds `1.0.0.0` and `1.0.1.0` from
  one commit, signs both with a throwaway certificate with the public Subject
  and runs on every main build;
- the release job runs the same gate from the previous published release's
  MSIX to the newly signed one (skipped when no release exists yet).

For a manual run, use a disposable logged-in Windows 11 x64 profile and the
[interactive scheduled-task procedure](e2e-testing.md). Sign both versions
with certificates whose Subjects match the same manifest Publisher. Install
v1, create a game and guide state, and record package family, version,
database and content hashes. Start the interactive task to install v2 over
v1 **without uninstalling v1**. Verify the package family is unchanged,
version increased, app relaunches, and the data and reading state survive.
Keep Preview installations and profiles out of this run. T17.3 repeats the
full three-format workflow offline on each advertised target.

## Microsoft deployment references

- [MSIX signing and certificate trust](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)
- [MSIX publisher mismatch diagnostics](https://learn.microsoft.com/en-us/windows/msix/msix-troubleshooting-guide)
- [Windows App SDK deployment for packaged apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)
- [Windows App SDK runtime downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- [WebView2 Evergreen online and offline distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [Microsoft Store package version numbering](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
