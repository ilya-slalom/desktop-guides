# P1 MSIX packaging and prerequisite delivery

Status: T17.1 packaging groundwork, 27 September 2026. Public identity and
signing integration are pending the final publisher certificate subject.
This procedure does not establish a clean-install or release-support claim.

## Identity and version

| Lane | Package Name and Publisher | Use |
| --- | --- | --- |
| Development | `DesktopGuides.Preview`, `CN=DesktopGuides Development` | Installed shell and reader checks. Its package family and local data stay isolated. |
| Public | Set once from the release signing identity | Signed candidates and in-place upgrades. Never use a provisional subject in this lane. |

The public manifest Publisher must exactly match the release signing
certificate Subject. Select a stable package Name before building the first
candidate. Both values stay fixed across upgrades. A change from Preview to
the public identity is a new installation; carrying Preview data across needs
the explicit export/restore workflow in T20.2. When the public identity is
chosen, update the production package manifest, the `app.manifest` assembly
identity, and the production single-instance and event names together. Keep
the Preview test lane isolated from the public package.

Use `major.minor.patch.0` with a nonzero major component, increasing at least
one of the first three fields for each new public package. Each field must fit
in the MSIX version range.
The fourth field is held at zero for Store-compatible numbering. Build v1 and
v2 with the same Name, Publisher, and architecture, and pass v1 to the package
verifier as `--previous-package` before signing v2. This checks the metadata
relationship; the interactive install/upgrade check below proves Windows
deployment and user-data continuity.

## Windows 11 x64 candidate

1. Build from a clean, recorded commit on Windows with locked restore. Use
   `dotnet restore src/DesktopGuides.Production/DesktopGuides.Production.csproj
   --locked-mode -p:Platform=x64`, followed by `dotnet build` in Release with
   `--no-restore`, `-p:Platform=x64`,
   `-p:GenerateAppxPackageOnBuild=true`, and
   `-p:AppxPackageSigningEnabled=false`. Keep ARM64 out of the public output
   until its complete native P1 workflow passes.
2. Read `AppxManifest.xml` from the built MSIX. Run
   `tools/p1/verify_production_package.py` with `--name`, `--publisher`,
   `--version`, and `--architecture x64`. For an upgrade candidate, also pass
   `--previous-package` pointing to the prior signed MSIX. The check rejects
   a changed identity or architecture, a non-increasing version, a missing
   Windows App Runtime dependency, and accidental P0 diagnostic content.
3. On a protected signing runner, provision a trusted code-signing certificate
   in `Cert:\CurrentUser\My` outside the repository. Select it by thumbprint
   and run `tools/p1/sign_production_package.ps1` with the package path, a
   fresh output path, the exact expected identity, version, x64 architecture,
   and a trusted `-TimestampUrl`. The signer verifies its certificate Subject,
   private key, validity, code-signing purpose, signed package trust,
   timestamp presence, and packed identity. The `-DevelopmentTest` switch
   is only for disposable development certificates and accepts Preview or
   test identities without a timestamp. A public signing run rejects Preview
   and test package names and the development Publisher, and requires a
   timestamp. The final public Name and Publisher still need to be pinned
   when the signing identity is chosen.
4. Record commit, source and signed MSIX SHA-256, identity, architecture,
   Windows App Runtime minimum from the packed manifest, signer thumbprint
   and validity, and timestamp status. Never export a private key into the
   checkout or include it in logs or artifacts. Restrict access to signing
   credentials and signed-candidate promotion.

The current signer supports an externally provisioned certificate in the
protected runner's CurrentUser certificate store. Azure Artifact Signing
requires its own SignTool client and signing arguments; choose and integrate
that service separately if it is the final signing provider. Never assume the
certificate-store script signs through Azure Artifact Signing.

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

Use a disposable logged-in Windows 11 x64 profile and the
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
