# S22 PR 5 (VGMaps) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- In Find online, a reader picks **VGMaps** and searches the atlas for a game.
- They open it and see its maps, each with its area, size, format and ripper.
- They save the maps they pick, or all of them, as image maps of the game. A map saved again replaces the one saved from the same address.

**Architecture:**
- **Core:**
  - VGMaps in `OnlineSources`, behind `IncludeVgMaps`.
  - Map rows on the game page gain the source fact.
- **Infrastructure:**
  - `VgMapsIndex`: the atlas index, fetched, cached for 30 days, and searched locally.
  - `VgMapsSystemPage`: reads a game's map table from a system page.
  - `VgMapsSaver`: fetches each chosen map and saves it through the existing image-map import and replacement. A GIF becomes PNG through an `IMapImageConverter` the caller supplies.
- **Production:**
  - VGMaps' panel in Find online: the game search, the map list with check boxes, and Save.
  - `WinRtMapImageConverter`, using `Windows.Graphics.Imaging`.

**Tech Stack:** .NET 10, WinUI 3, AngleSharp, xUnit, PowerShell UI Automation.

**Spec:**
- [The P3 technical design](../p3-technical-design.md):
  - 5.1: VGMaps results list the game's maps with area, size and ripper; the user picks maps, or all of them;
  - 5.3, **VGMaps**: the index on first search, kept 30 days, with **Refresh**; system pages only when a game opens; maps one at a time; PNG and JPEG as they are, GIF to PNG, SWF and other formats listed as not saveable;
  - 5.5: saved again, a map replaces the one with the same source URL in that game;
  - 5.7: build switches;
  - 5.9: tests.
- [The source review](source-review.md), VGMaps section:
  - there are no terms, and the footer credits the copyright holders;
  - attribution is each map's ripper, the game's copyright line and the page URL;
  - a courtesy note to the site's owner, which design decision 9 lets ship without waiting for.

**Structure,** recorded on 24 October 2026 from the live index and the PSX system page. Nothing from either is copied into fixtures.
- **The index:** `/Atlas/index.htm` (1.9 MB, windows-1252).
  - Each entry is `<a href="{System}/index.htm#{Anchor}">`.
  - A game's own entry is blue text. A grey entry in parentheses, like "(NES)", continues the title above it.
  - Red single letters (`#A`…`#Z`) are headings, not games.
- **A system page:** `/Atlas/{System}/index.htm` (about 0.9 MB).
  - Each game is a table that starts with a row holding `<a NAME="{Anchor}">` (the title image).
  - Next comes a row with "{Game} Maps" and the copyright line (for example "© 1997 Square"), and sometimes a full-width note row.
  - Then come the map rows: a link to the file (relative, for example `FinalFantasyVII-World.png`), the area, "W x H", the file size ("4.82 MB", "544 kB"), the format, the method, and the credit.
  - The credit is link text whose `href` is often a Cloudflare email-protection link, so only the text is kept.
- **Size:** PSX has 83 games and 806 map rows, almost all PNG. Some maps are past the image-map limits (`MapImageLimits`): one is 854 MB, another 30682 × 21794 px.

## Global Constraints

- **Requests:**
  - They go to `vgmaps.com` only (added to `ProviderHttp.AllowedHosts`), with the Steam client's user agent.
  - One at a time, and at least 1 s apart.
  - An HTTP 429 stops the step, with "VGMaps is limiting requests. Try again in a few minutes."
  - The index is at most 16 MB, a system page at most 16 MB, and a map at most `MapImageLimits.MaxBytes`.
- **The index cache:**
  - It's `{cache}/vgmaps-index.json`, holding `{ fetchedUtc, games: [[title, system, systemPath, anchor], …] }`.
  - It's fetched again when it's older than 30 days, or on **Refresh**.
  - A fetch that fails keeps nothing, and a cache that can't be written is still used, as `SteamAppIndex` does.
  - It's read, parsed and written off the UI thread.
- **Search:**
  - It runs locally, with `SteamAppIndex`'s normalizing and ranking: exact, then prefix, then all-words, at most 30 results.
  - A result reads "{Title} ({System})".
- **System pages:** fetched when a game opens, and kept in memory for the session.
- **Saveable:**
  - PNG and JPEG are saved as they are.
  - A GIF becomes PNG through the converter, using its first frame.
  - Anything else (SWF, ZIP, HTM) is listed as "Not saveable ({format})".
  - A map whose listed size or pixels exceed `MapImageLimits` is listed as "Too large to save" and isn't fetched. The import also checks the real file.
- **Each saved map:**
  - Its title is "{Area}".
  - Its `GuideSource` is `(VgMaps, ItemId = the map's absolute URL, Url = {system page}#{anchor}, Author = ripper, Notice = the copyright line, SourceTitle = "{Game}: {Area}")`.
  - It's replaced by `FindSourcedGuideAsync(game, VgMaps, url)` and `ReplaceAsync`; one whose fingerprint is unchanged is up to date.
- **Failures:**
  - A failed map stops the run and names it ("map 3 of 7"). The maps saved before it stay.
  - Each map is published on its own, so no map is half saved (TR22.1).
- **The game page:** a map row shows "VGMaps · {ripper}" after its kind and size.

## Review Focus

1. A system page whose table has a note row, a map row missing a cell, or an SWF row still lists every readable map, and leaves out none it can read.
2. A map past the limits, by listed pixels or bytes, is never fetched. One whose real file is past them, despite a smaller listing, fails at the import with its own message.
3. A GIF converts to PNG; an animated one saves its first frame. A converter failure fails that map, with its name.
4. Saving the same maps again: unchanged ones are up to date, a changed one is replaced, and its view (zoom and pan) is kept.
5. An index entry for a system whose page has no table with that anchor says the game's maps couldn't be found, and doesn't crash.

## Host commands

`/tmp/s22d.sh <project> [filter]` syncs the branch to the host and runs a
test project. Installed runs use CI.

### Task 1: Core: the source, and the map row's source fact

**Files:**
- Modify:
  - `Directory.Build.props` and `.targets`: `IncludeVgMaps`;
  - `OnlineSources.cs`;
  - `CatalogPresentation.cs`.
- Test: `OnlineSourcesTests.cs`, `CatalogPresentationTests.cs`.

- [ ] **Step 1: Write the failing tests:**
  - the sources are GameFAQs, Neoseeker, Steam, then VGMaps;
  - `Find` matches `https://vgmaps.com/…`;
  - a map row with a VGMaps source reads `[kind, size, "VGMaps · Ripper"]`.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** The source's host is `vgmaps.com`, and its home is `https://vgmaps.com/Atlas/index.htm`.
- [ ] **Step 4: Run Core tests; they pass.**
- [ ] **Step 5: Commit** `feat(p3): VGMaps as a source; a saved map's row names it`.

### Task 2: Infrastructure: the index, the system page and the saver

**Files:**
- Create in `src/DesktopGuides.Infrastructure/Online/`: `VgMapsClient.cs`, `VgMapsIndex.cs`, `VgMapsSystemPage.cs` and `VgMapsSaver.cs`.
- Modify: `ProviderHttp.cs` (the host); `SteamAppIndex.cs`, whose `Normalize` and ranking become shared in `OnlineNames`.
- Fixtures in `tests/fixtures/p3/vgmaps/`, all synthetic:
  - `index.htm`, with blue entries, grey continuations, letter headings, and "Online Test Game" on two systems;
  - `system.htm`, whose game has:
    - a note row;
    - two PNG maps, a GIF map, a JPEG map and an SWF row;
    - a map listed at 40000 × 40000;
    - a row missing its size;
  - `map.png`, `map.gif` (two frames) and `map.jpg`.
- Test: `VgMapsIndexTests.cs`, `VgMapsSystemPageTests.cs` and `VgMapsSaverTests.cs`.

**Interfaces:**
- **`VgMapsGame(string Title, string System, string SystemPath, string Anchor)`.**
- **`VgMapsMap`:** `(Uri Url, string Area, int? Width, int? Height, long? Bytes, string Format, string? Ripper, VgMapsSaveable Saveable)`, where `VgMapsSaveable` is `Yes`, `Convert`, `NotSaveable` or `TooLarge`.
- **`VgMapsGamePage(VgMapsGame Game, string? Copyright, Uri PageUrl, IReadOnlyList<VgMapsMap> Maps)`.**
- **`VgMapsClient(ProviderHttp, Func<TimeSpan, CancellationToken, Task>? delay)`:** `GetIndexAsync`, `GetSystemPageAsync(path)` and `GetMapAsync(Uri)`, 1 s apart.
- **`VgMapsIndex(string cacheFile, TimeProvider)`:** `LoadAsync(fetch, refresh, token)`, `Search(query, max = 30)`, `IsFresh`.
- **`VgMapsSystemPage.Read(string html, Uri pageUrl, VgMapsGame game) → VgMapsGamePage?`.**
- **`IMapImageConverter.ToPngAsync(byte[] gif, CancellationToken) → byte[]`.**
- **`VgMapsSaver(VgMapsClient, SqliteLibraryRepository, GuideImportPublisher, GuideImportValidator, IMapImageConverter, string workRoot, TimeProvider)`:**
  - `SaveAsync(Guid gameId, VgMapsGamePage page, IReadOnlyList<VgMapsMap> maps, IProgress<(int, int)>?, CancellationToken)` returns `VgMapsSaveResult(int Added, int Replaced, int UpToDate)`;
  - it throws `VgMapsSaveException(ProviderErrorKind?, string Step, Exception)`.

- [ ] **Step 1: Write the failing tests.** Review Focus 1–5 are pinned here.
  - **Index:**
    - the fixture parses into games, with continuations titled "Online Test Game (NES)" and no letter headings;
    - search ranks as `SteamAppIndex` does;
    - the cache keeps a list for 29 days and fetches one 31 days old again; a failed fetch keeps nothing.
  - **System page:**
    - the game's table reads its copyright, its map rows in order with absolute URLs, areas, sizes, bytes, formats and rippers;
    - `Saveable` is Yes for PNG and JPEG, Convert for GIF, NotSaveable for SWF, and TooLarge for 40000 × 40000;
    - the row missing its size reads with null size, and is still saveable;
    - a missing anchor gives null.
  - **Saver:**
    - two maps are fetched 1 s apart, on a fake clock, and saved as image guides titled by area, with the source fields above;
    - a GIF goes through a fake converter and saves as PNG;
    - a second run: unchanged maps are up to date, and a changed map is replaced with its view kept;
    - a 429 on map 2 of 3 keeps map 1, saves no others, and throws with the step "map 2 of 3";
    - a TooLarge or NotSaveable map passed in is refused before any request.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Implement.** Parse with AngleSharp from the bytes, so `windows-1252` is decoded from the page's charset.
- [ ] **Step 4: Run Infrastructure tests; they pass.**
- [ ] **Step 5: Commit** `feat(p3): VGMaps' index, game tables and map saving`.

### Task 3: Production: VGMaps in Find online

**Files:**
- Modify: `ShellWindow.xaml` (`VgMapsPanel` beside `SteamPanel`) and `ShellWindow.Online.cs` (routing).
- Create: `ShellWindow.VgMaps.cs` and `WinRtMapImageConverter.cs`.
- Modify also: the smoke and install scripts, and the online fixture manifest.

**Visual direction:** the Steam panel's patterns.
- **Search:** a search box and **Search**, with **Refresh index**.
- **Results:** a games list ("{Title} ({System})").
- **A game:** its map list, showing:
  - for each map, a check box with the area and "W × H · size · format · by {ripper}";
  - for an unsaveable map, its reason, with the box disabled.
- **Actions:** **Save selected** and **Save all saveable**.
- **Progress:** "Saving map n of m…".

- [ ] **Step 1: Write the failing installed check** `online-vgmaps`, after the Steam modes:
  1. Pick VGMaps. The search for the game lists "Online Test Game (PSX)" and "Online Test Game (NES)".
  2. Opening the first lists 7 maps; the SWF and the 40000 × 40000 rows are disabled with their reasons.
  3. Check the first PNG and the GIF, then **Save selected**: "Saved 2 maps to Online Test Game."
  4. Run **Save selected** again: "2 maps are up to date."
  5. `describe-online` shows two Image guides with source VgMaps.
- [ ] **Step 2: Run the `online` group in CI; it fails.**
- [ ] **Step 3: Implement.** The browser is hidden in VGMaps mode, since the app fetches everything itself. `WinRtMapImageConverter` decodes the first frame with `BitmapDecoder` and encodes PNG.
- [ ] **Step 4: Run the full CI; it passes.** Take screenshots of the game results and the map list.
- [ ] **Step 5: Commit** `feat(p3): VGMaps in Find online`.

### Task 4: Records

- [ ] **Step 1:** Update the docs:
  - `docs/p1/e2e-testing.md`: the `online-vgmaps` row;
  - the P3 design: a PR 5 verification record.
- [ ] **Step 2:** Write the PR description: the target task, prerequisites (#151 and #152, merged), outcome, screenshots and live-check steps. Include a note that the courtesy message to the site's owner is the user's to send (design decision 9).
