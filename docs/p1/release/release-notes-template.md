<!-- {{VERSION}}, {{COMMIT}}, {{MSIX}}, {{PORTABLE}}, {{THUMBPRINT}} and
     {{RUNTIME}} are filled in by the release workflow. -->
# Desktop Guides {{VERSION}}

A pre-release for testing. Built from {{COMMIT}}.

## What's new in 1.1

- **Search inside a guide** (Ctrl+F) for text, web page and PDF guides, with
  a match count and Next and Previous (F3, Shift+F3).
- **Contents** (Ctrl+Shift+C): a guide's sections or headings in a side
  pane, including a PDF's own outline. Choosing one goes there.
- **Bookmarks:** add one at the current place (Ctrl+D), name it, and come
  back to it from the Bookmarks tab (Ctrl+Shift+B). Bookmarks are included
  in backups.
- **A reflow view for text guides**, which rewraps prose to the window
  while keeping tables and ASCII art as they are.
- **Web guides of several pages:** a guide saved as several pages imports
  as one guide. Links between its pages work offline, and contents, search
  and bookmarks cover every page.
- **Image maps:**
  - import a game's PNG and JPEG maps, several at once, into their own
    Maps section;
  - open a map to zoom and pan, even on maps far larger than the screen;
    it reopens where you left it;
  - glance at a map from any guide with **Maps** (Alt+M), then go back to
    reading without losing your place.

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
- Image maps are PNG or JPEG files, at most 256 MiB and 32,768 pixels on a
  side. A large map is prepared in the background after import; opened
  before that, it shows "Preparing the map…" first.
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
