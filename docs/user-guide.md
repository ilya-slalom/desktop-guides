# Desktop Guides user guide

Desktop Guides keeps your game guides together and ready offline. This
guide covers version 1.2.

- [Installing](#installing)
- [The library](#the-library)
- [Importing guides](#importing-guides)
- [Find online](#find-online)
- [Reading](#reading)
- [Offline use](#offline-use)
- [Backup and restore](#backup-and-restore)
- [Where your data lives](#where-your-data-lives)
- [Privacy](#privacy)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Troubleshooting](#troubleshooting)

## Installing

Download a release from the
[GitHub Releases page](https://github.com/ilya-slalom/desktop-guides/releases).
Releases are x64 only, and are tested on Windows 11 x64 (see the
[tested matrix](release/tested-matrix.md)). Each release has an MSIX
package and a portable build. The two keep separate libraries.

### Prerequisites

| Prerequisite | MSIX | Portable |
| --- | --- | --- |
| [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) 2.5.1 or later (the release notes name the version) | Needed | Not needed; it's built in |
| [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) | Needed for web page guides and Find online | Needed for web page guides and Find online |

Most Windows 11 PCs already have the WebView2 Runtime. Without it, text,
PDF and image map guides still open.

### The MSIX package

The package is signed with the project's own certificate, so Windows
needs to trust that certificate once before the first install.

1. Download `desktop-guides-release.cer` from the release, and **compare
   its thumbprint before trusting it**. In PowerShell, in the folder with
   the file:

   ```powershell
   (Get-PfxCertificate .\desktop-guides-release.cer).Thumbprint
   ```

   The output must equal the thumbprint in the release notes. For the
   current certificate it's `BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6`.
   If it differs, don't continue.
2. In an administrator PowerShell, trust it:

   ```powershell
   Import-Certificate -FilePath .\desktop-guides-release.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

3. Open the `.msix` file and choose **Install**.

Trusting the certificate trusts anything signed with its key. To remove
that trust later, in an administrator PowerShell:

```powershell
Remove-Item Cert:\LocalMachine\TrustedPeople\BAA638E4DCFA4E1C5A2F65E3DC838BA7B3B59EB6
```

If a later release changes the certificate, its release notes say so.
Trust the new `.cer` once, the same way.

**Uninstalling the MSIX deletes its library.** Back it up first (see
[Backup and restore](#backup-and-restore)).

### The portable build

The portable `.zip` holds one file, `DesktopGuides.Production.exe`. Unzip
it anywhere and run it; it doesn't install anything. It keeps its library
in `%LOCALAPPDATA%\DesktopGuides`, so deleting the exe leaves the library
in place.

The portable exe isn't signed, so Windows SmartScreen may warn you when
you first run it.

### Upgrading

- **MSIX:** open the new release's `.msix` and choose **Install**. It
  upgrades the app in place and keeps your library.
- **Portable:** replace the old exe with the new one. The library stays in
  `%LOCALAPPDATA%\DesktopGuides`.

A library opened by a newer version can't be opened by an older one.

Each download comes with a `.sha256` file and the release has a
`release-manifest.json`, which you can check the download against.

## The library

The **Library** page lists your games. Choose a game to open its page,
which lists its guides and maps.

### Game details from IGDB and SteamGridDB

Desktop Guides can look up a game's details and cover art, using your own
keys. Enter them in **Settings → Game data providers**:

| Setting | What it's for |
| --- | --- |
| **IGDB client ID** and **IGDB client secret** | Searching for games. They come from your Twitch developer application. |
| **SteamGridDB API key** | Optional. Used for cover art. |
| **Steam Web API key** | Optional. Used to search Steam Community guides in Find online. |

Choose **Save** to keep the keys, **Test connection** to check them, and
**Remove** to delete them. The keys are stored encrypted for your Windows
account, and each is sent only to its own service.

### Adding a game

1. On the Library page, choose **Add game**.
2. Type the game's title in **Search IGDB by title** and choose
   **Search**.
3. Pick the right result to add it, with its details and cover art.

Choose **Add manually** to add a game without searching: enter a
**Title**, and optionally a **Platform** and **Notes**. You can always add
games manually, including offline or without IGDB keys.

### Editing and removing a game

On a game's page:
- **Edit game** changes its title, platform and notes.
- **Refresh metadata** fetches its details from IGDB again, for a game
  added from IGDB. **View on IGDB** opens its IGDB page in your browser.
- **Remove game** removes the game, its guides and maps, and their reading
  progress, after you confirm. The original files you imported aren't
  affected.

### Searching the library

Type in **Search games and guides** at the top of the Library page
(Ctrl+F). It matches game titles and guide titles. A game that matches
only through a guide shows that guide's title in its row. Esc clears the
search.

### A game's guides

Each guide on a game's page shows its format and its reading state: **Not
started**, **In progress**, an estimate such as **~40%**, or
**Completed**, and when you last opened it. A guide saved through Find
online also shows the site it came from and its author.

Select a guide to **Open selected guide** or **Remove guide**. Removing a
guide deletes its copy in the library and its reading progress, after you
confirm. The original file you imported isn't affected.

## Importing guides

On a game's page, choose **Import guide** (Ctrl+O) and pick a file. The
import dialog checks the file and shows a preview first: its format,
size, and anything worth knowing, such as linked files or warnings. You
can change the guide's **Title** before you choose **Import**.

Desktop Guides copies the file into its library. Your original file is
never changed, and the guide still opens if you move or delete the
original.

| Format | Files | Notes |
| --- | --- | --- |
| Text | `.txt` | Choose the **Text encoding** if the preview looks wrong: UTF-8, Western (Windows-1252) or DOS (CP437). Up to 64 MB. |
| Web page | `.html`, `.htm` | Static, saved pages. The page's local images and stylesheets are copied with it. Scripts don't run. |
| PDF | `.pdf` | A password-protected PDF asks for its password, at import and each time you open it. |
| Image map | `.png`, `.jpg`, `.jpeg` | Up to 256 MiB and 32,768 pixels on a side. |

### Guides saved as several web pages

Some guides span several pages. Save each page from your browser into the
same folder (for example, with "Save as" and "Webpage, Complete"), then
import the first page. Desktop Guides finds the other pages it links to in
that folder and imports them as one guide, in reading order, up to 200
pages. The preview lists the pages it found. Links between the pages work
in the reader.

### Image maps

Choose **Import maps** on a game's page to import several PNG or JPEG
maps at once. Maps are listed under **Maps** on the game's page, where you
can **Open selected map** or **Remove map**. A large map is prepared in
the background after import; if you open it before that finishes, it shows
"Preparing the map…" first.

### Importing the same file twice

If the file is already in that game, the dialog says so ("This file is
already in … as …"). Choose **Open existing** to read the copy you have,
or **Import another copy** to add it again.

## Find online

**Find online** on a game's page saves guides from supported sites into
your library for offline reading. Choose the site under **Site**. You can
also paste a guide's address into **Address or guide URL**; an address
from a supported site selects that site. Other sites aren't supported, and
the app tells you to open them in your browser.

Each saved guide keeps its title, author, source, address and the date it
was saved, and the author's notice when there is one. The game page names
each guide's source and author. If a guide's notice limits copies, Desktop
Guides shows it and asks before saving ("Save for personal reading").
Desktop Guides isn't affiliated with any of these sites.

### GameFAQs and Neoseeker

These sites open in an in-app browser, as they are, ads included. Browse
to a guide and choose **Save to library** (Ctrl+S). Desktop Guides steps
through the guide's pages one at a time, at least 2 seconds apart, and
saves their text and images. A Neoseeker wiki walkthrough also brings the
same game's articles its pages link to.

- **Pausing:** if a site doesn't return a page, for example because it
  asks you to confirm you're a person, the save pauses with **Saving
  paused**. Choose **Show page** to deal with it in the browser, then
  **Resume**. You can also **Save what's captured** or **Discard**.
- **Unfinished saves:** if you close Find online or the app during a save,
  the pages saved so far are kept. The next Find online for that game
  offers to resume. An unfinished save is deleted after 7 days.
- **Long guides:** a guide is saved with at most 200 pages. A longer one
  is saved as partial, and its row shows which pages it has.

The in-app browser keeps cookies and sign-ins for these sites. **Settings
→ Find online → Clear online data** deletes them and signs you out of
every site.

### Steam Community

- **Searching** needs your own Steam Web API key (**Settings → Game data
  providers**). Find online matches the game to its Steam app by name. If
  it isn't sure, it asks **Which game is this on Steam?**; choose **Use
  this game**. **Change** corrects the match later.
- **Without a key,** paste a Steam guide's address instead. That works
  with no key.

Choose a guide in the results to see it, then **Save to library**.

### The Video Game Atlas (VGMaps)

Search the atlas under **Search the atlas**, open the game, then pick the
maps you want and choose **Save selected**, or **Save all saveable**. They
are saved as image maps on the game's page. GIF maps are converted to PNG.
Maps over the image map limits are listed as too large to save, and SWF
and other non-image maps as not saveable. **Refresh index** reloads the
atlas's list of games.

### Saving again

Saving a guide you've already saved from the same site replaces the old
copy and keeps your place, bookmarks and completion ("Replaced …. Your
place is kept."). If nothing changed, the guide is up to date. If the
guide's format changed, for example from text to a web page, Desktop
Guides asks first, because your place and bookmarks can't carry over:
choose **Replace** or **Keep both**. Maps saved again from VGMaps replace
the same maps.

A site that changes its pages can stop a save. Your saved guides aren't
affected.

## Reading

Open a guide from its game's page. The reader's toolbar holds the controls
for that format, and **Complete** or **In progress** for the guide.

### Text guides

Text guides sit centered, with spacing kept, so ASCII art and tables stay
aligned. Choose **Original** for the file as it is, or **Reflow** to wrap
long lines to the window. Use Page Up and Page Down to move a page, and
**Larger text**, **Smaller text** and **Reset text size** to change the
text size.

### Web page guides

Web page guides open in the reader with their images and styles, offline.
Guides saved through Find online get a clean built-in style. In a guide of
several pages, Ctrl+Page Up and Ctrl+Page Down move between its pages. A
link to a website opens in your browser only if you choose **Open in
browser**.

### PDF guides

Use **Previous page** and **Next page**, **Go to page** (Ctrl+G), **Zoom
in**, **Zoom out** and **Fit to width**. A scanned page with no text shows
"Image-only page; OCR is unavailable." It has no text to select or
search.

### Image maps

Use **Zoom in**, **Zoom out** and **Fit map**. From any guide, **Maps**
(Alt+M) lists the game's maps; point to one to preview it, and choose
**Open map** to open it.

### Contents, search and bookmarks

- **Contents** (Ctrl+Shift+C) lists the guide's sections, or its pages for
  a guide of several pages. The pane opens as you left it.
- **Find in guide** (Ctrl+F) searches the guide. F3 and Shift+F3 step
  through the matches, and **Back to my place** returns to where you were.
- **Add bookmark** (Ctrl+D) marks your place. **Bookmarks**
  (Ctrl+Shift+B) lists them. In the list, F2 renames a bookmark and
  Delete removes it.

### Theme

The reader follows the app theme. Choose it in **Settings → Appearance →
App theme**: System, Light or Dark. Windows high contrast takes precedence.
**Window background** chooses Mica, Acrylic or Solid.

### Your place and completion

Each guide remembers your place, and its text size or zoom, and reopens
there. If the guide changed since you were last there, it opens near your
place and says so.

Completion is your choice. Choose **Complete** in the reader or on the
game's page when you've finished a guide, and **In progress** to undo it.
Reading to the end never marks a guide complete, and the reading estimate
never changes it.

## Offline use

Every guide and map in your library opens with no network: the reader
never reaches the internet. Only adding games from IGDB, refreshing their
details, and Find online need a connection.

## Backup and restore

Your library lives in app data. For the MSIX, uninstalling the app
removes it, so keep a backup.

**To back up:** in **Settings → Library → Back up library**, choose
**Export…** and pick a folder outside the app's data, such as Documents
or a USB drive. The backup is a `.zip` file with your games, guides and
reading progress. Desktop Guides checks it after writing it.

**To restore:** in **Settings → Library → Restore from backup**, choose **Choose
backup…**. Desktop Guides shows when the backup was made, what it holds,
and what this library has. Choose **Replace library…**, then **Replace**,
or **Discard** to keep your library. A restore replaces the whole library
and can't be undone, so export the current library first if you want to
keep it.

Use backup and restore to move a library to another PC, or between the
MSIX and portable builds. Your provider keys aren't part of the library;
enter them again on the new PC.

## Where your data lives

| Build | Data folder |
| --- | --- |
| MSIX | The app package's `LocalState` folder, under `%LOCALAPPDATA%\Packages` |
| Portable | `%LOCALAPPDATA%\DesktopGuides` |

The library itself is in the `library` folder inside: `library.sqlite`
holds your games, guides and reading state, and the guide copies and
artwork sit beside it. Don't edit these files by hand. If the library
can't be opened, the error screen shows the data folder's path and has
**Open data folder**.

## Privacy

Desktop Guides sends nothing on its own. It has no telemetry or update
check. The only network requests are the ones you start:

- IGDB (through Twitch's sign-in) and SteamGridDB, when you search for or
  add a game, refresh its details, or test your keys;
- Steam's API, when you search Steam Community guides or test your key;
- the site you're using in Find online, and the pages and images of the
  guide you save;
- your browser, when you choose to open a link outside the app.

Your keys are sent only to the service they belong to.

## Keyboard shortcuts

**Settings → Help → Keyboard shortcuts** lists the same keys.

| Where | Keys | Does |
| --- | --- | --- |
| Everywhere | Alt+Left | Go back |
| Library | Ctrl+F | Search the library |
| Library search | Esc | Clear the search |
| Game page | Ctrl+O | Import a guide |
| Find online | Ctrl+L | Go to the address box |
| Find online | Ctrl+S | Save the guide to the library |
| Reader (TXT, PDF) | Page Up / Page Down | Previous / next page |
| Reader (TXT, PDF) | Ctrl+Home / Ctrl+End | Go to start / end |
| Reader (web guides of several pages) | Ctrl+Page Up / Ctrl+Page Down | Previous / next page |
| Reader (PDF) | Ctrl+G | Go to page |
| Reader | Ctrl+Plus / Ctrl+Minus | Zoom in / out (PDF), or larger / smaller text |
| Reader | Ctrl+0 | Fit to width (PDF), or reset text size |
| Reader | Ctrl+F | Search in the guide |
| Reader | F3 / Shift+F3 | Next / previous match |
| Reader | Ctrl+Shift+C | Show contents |
| Reader | Ctrl+D | Add bookmark |
| Reader | Ctrl+Shift+B | Show bookmarks |
| Reader | Alt+M | Show the game's maps |
| Bookmarks list | F2 / Delete | Rename / remove the selected bookmark |
| Dialogs | Esc | Close the dialog |

In a web page guide, the reader keys work while focus is on the reader's
controls rather than inside the page.

## Troubleshooting

### The library can't be opened

If the library can't be opened, Desktop Guides shows why instead of the
Library page, and stops before changing anything. The screen shows the
data folder's path, with **Try again** and **Open data folder**.

| Title | What to do |
| --- | --- |
| Library can't be read | The library database is damaged. Your guide files are still in the data folder; keep the folder. |
| Library database is missing | Put `library.sqlite` back in the data folder's `library` folder, then choose **Try again**. |
| Update Desktop Guides | The library was saved by a newer version. Install that version or later. |
| Library upgrade failed | The screen shows where a copy of the library from before the upgrade was kept. |
| Library is in use | Close the program using it, then choose **Try again**. |
| Library can't be accessed | Check the data folder's permissions, then choose **Try again**. |
| Not enough disk space | Free up some space, then choose **Try again**. |
| Restore didn't finish | Your previous library is in the `.recovery` folder inside the data folder. Close other programs that might be using it, then choose **Try again**. |

### A guide won't open

- **"File missing"** or **"File damaged"** on a guide's row, or "This
  guide's file is missing from the library": remove the guide, then import
  the original again.
- **"Web page guides need the Microsoft Edge WebView2 Runtime."** Install
  the WebView2 Runtime (the reader links to it). Text, PDF and map guides
  still open.
- **"This PDF needs a password."** Enter it and choose **Unlock**.
- **"Opened near your last place. The guide changed since you were
  here."** The guide was saved again or changed; your place is close but
  may not be exact.

### Game search

- **"Add IGDB credentials in Settings to search."** Add your IGDB client
  ID and secret (choose **Open Settings**), or choose **Add manually**.
- **"IGDB rejected the client ID or secret."** Check them in Settings and
  choose **Test connection**.
- **"IGDB is busy. Try again in a moment."** Wait, then choose **Retry**.

### Find online

- **"Steam search needs your Steam Web API key."** Add it in **Settings →
  Game data providers** (**Get a key** opens Steam's key page), or paste a
  guide's address, which needs no key.
- **"Steam didn't accept the key. Check it in Settings."** Check the key
  and choose **Test connection**.
- **"Steam is limiting requests. Try again in a few minutes."** and
  **"VGMaps is limiting requests. Try again in a few minutes."** The site
  is refusing requests for now. Wait a few minutes, then try again.
- **"Saving paused"** on GameFAQs or Neoseeker: the site may be asking you
  to confirm you're a person. Choose **Show page**, deal with it, then
  **Resume**.
- **"This page isn't a guide Desktop Guides can save."** Open the guide
  itself, not a list of guides or the game's page.
- **"The in-app browser couldn't start. Repair the WebView2 Runtime, then
  try again."** Repair or reinstall the WebView2 Runtime.

### Backup and restore

- **"Choose a folder outside the app's data…"**: backups saved in app data
  are removed with the app. Pick another folder.
- **"Some guide files are missing or damaged…"**: remove or re-import the
  guides named, then export again.
- **"This backup is from a newer version of Desktop Guides."** Update the
  app, then restore.
- **"The backup couldn't be opened, so your previous library was kept."**
  Nothing changed. Try another backup file.
