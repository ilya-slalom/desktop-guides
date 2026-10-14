<!-- {{VERSION}}, {{COMMIT}}, {{MSIX}}, {{PORTABLE}}, {{THUMBPRINT}} and
     {{RUNTIME}} are filled in by the release workflow. -->
# Desktop Guides {{VERSION}}

A pre-release for testing. Built from {{COMMIT}}.

## Install

1. Install the prerequisites, if you don't have them:
   - [Windows App Runtime {{RUNTIME}} or later](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
   - [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (for web page guides; most Windows 11 PCs already have it)
2. Download `desktop-guides-release.cer` and **compare its thumbprint before
   trusting it**. In PowerShell, in the folder with the file:

   ```powershell
   (Get-PfxCertificate .\desktop-guides-release.cer).Thumbprint
   ```

   The output must equal `{{THUMBPRINT}}`. If it differs, do not continue.
3. In an administrator PowerShell, trust it once:

   ```powershell
   Import-Certificate -FilePath .\desktop-guides-release.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

4. Open `{{MSIX}}` and choose **Install**. Later releases upgrade it in
   place and keep your library.

## Portable build

`{{PORTABLE}}` holds one `DesktopGuides.Production.exe` that runs without
installing. It keeps its library in `%LOCALAPPDATA%\DesktopGuides`,
separate from the installed app's. It needs the WebView2 Runtime for web
page guides.

## Limits

- x64 only.
- Tested on Windows 11 x64; see the [tested matrix](https://github.com/ilya-slalom/desktop-guides/blob/main/docs/p1/release/tested-matrix.md). Clean-machine and Windows 10 support aren't claimed yet.
- Scanned (image-only) PDF pages show the page image only, marked "Image-only page; OCR is unavailable." They have no text to select, search or read aloud.
- Uninstalling the app deletes its library. Back it up from
  **Settings → Back up library** before uninstalling or testing a pre-release.
- The portable exe is unsigned, so Windows SmartScreen may warn when you run it.
- Trusting the `.cer` trusts anything signed with this key. To remove that
  trust later, in an administrator PowerShell:

  ```powershell
  Remove-Item Cert:\LocalMachine\TrustedPeople\{{THUMBPRINT}}
  ```

- After a release certificate change, trust the new `.cer` once, the same way.

Check each download against its `.sha256` file and `release-manifest.json`.
