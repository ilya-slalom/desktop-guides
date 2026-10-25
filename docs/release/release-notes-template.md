<!-- {{VERSION}}, {{COMMIT}}, {{MSIX}}, {{PORTABLE}}, {{THUMBPRINT}} and
     {{RUNTIME}} are filled in by the release workflow. -->
# Desktop Guides {{VERSION}}

A pre-release for testing. Built from {{COMMIT}}.

## What's new in 1.2

- **Find online** on a game's page saves guides from four sites for
  offline reading, credited to their authors:
  - **GameFAQs and Neoseeker:** browse the site in the app, with its ads as
    they are, and press **Save to library** on a guide. Its pages and images
    are saved one at a time, at least 2 s apart; a wiki walkthrough brings
    the articles it links to. If a site asks you to confirm you're a person,
    the save pauses until you do.
  - **Steam Community:** search a game's guides with your own Steam Web API
    key (Settings → Game data providers), or paste a guide's address, which
    needs no key. Find online picks the game's Steam app by name, and
    **Change** corrects it.
  - **The Video Game Atlas (VGMaps):** search the atlas, open the game, and
    save the maps you pick as image maps.
- **Saving again** replaces the copy saved before, and keeps your place,
  bookmarks and completion; an unchanged guide is up to date.
- **Saved web guides** get a clean built-in style, and the game page names
  each guide's source and author.
- **Text guides sit centered,** with ASCII art and tables kept aligned,
  and the **Contents** pane opens as you left it, and on its own for a
  guide of several pages.

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
- Find online depends on the sites as they are. A site that changes its
  pages can stop a save; your saved guides are unaffected. A guide is
  saved with at most 200 pages, and a longer one is marked as partial.
- Steam search needs your own Steam Web API key. Desktop Guides isn't
  affiliated with Valve or with any of the sites.
- Maps from VGMaps over the image-map limits are listed as too large to
  save, and SWF and other non-image maps as not saveable.
- Image maps are PNG or JPEG files, at most 256 MiB and 32,768 pixels on a
  side. A large map is prepared in the background after import; opened
  before that, it shows "Preparing the map…" first.
- Tested on Windows 11 x64; see the [tested matrix](https://github.com/ilya-slalom/desktop-guides/blob/main/docs/release/tested-matrix.md). Clean-machine and Windows 10 support aren't claimed yet.
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
