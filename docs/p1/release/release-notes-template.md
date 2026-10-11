<!-- {{VERSION}}, {{COMMIT}}, {{MSIX}}, {{PORTABLE}}, {{THUMBPRINT}} and
     {{RUNTIME}} are filled in by the release workflow. -->
# Desktop Guides {{VERSION}}

A pre-release for testing. Built from {{COMMIT}}.

## Install

1. Install the prerequisites, if you don't have them:
   - [Windows App Runtime {{RUNTIME}} or later](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
   - [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (for web page guides; most Windows 11 PCs already have it)
2. Trust the release certificate once. In an administrator PowerShell, in
   the folder where you downloaded `desktop-guides-release.cer`:

   ```powershell
   Import-Certificate -FilePath .\desktop-guides-release.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

   The certificate's thumbprint is `{{THUMBPRINT}}`.
3. Open `{{MSIX}}` and choose **Install**. Later releases upgrade it in
   place and keep your library.

## Portable build

`{{PORTABLE}}` holds one `DesktopGuides.Production.exe` that runs without
installing. It keeps its library in `%LOCALAPPDATA%\DesktopGuides`,
separate from the installed app's. It needs the WebView2 Runtime for web
page guides.

## Limits

- x64 only.
- Tested on Windows 11. Clean-machine and Windows 10 support aren't claimed yet.
- Back up your library from **Settings → Back up library** before testing a
  pre-release.

Check each download against its `.sha256` file and `release-manifest.json`.
