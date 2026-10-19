# T21.2 image maps: design

Status: approved in conversation, 19 October 2026. PR a (import maps and
the Maps section) merged (#136); see the [PR a verification record](#pr-a-verification-record).
PR b (the reader) in review; see the [PR b verification record](#pr-b-verification-record).

## Intent

A map supports a text guide. It doesn't replace one. A reader is partway
through a walkthrough and reaches something the text can't carry: an
area whose layout is hard to describe, or the exact spot of a secret or
hidden item. They look at the map for 5–10 seconds, then go back to the
text. Today that glance means leaving the guide, finding the map on the
game page, and coming back. Each switch takes several steps, and the
glance it serves takes only a few seconds.

So the map has to be one action away from whatever guide is open, and it
has to leave that guide exactly where it was. The app usually sits on a
second display beside the game, so the whole window is available for it.

This is the second format from the [format survey](format-survey.md)
(rank 2, approved 17 October 2026). Multi-page HTML, rank 1, is supported
(#133–#135).

## Goals

- **Import** a game's maps: PNG or JPEG files saved from VGMaps or
  GameFAQs, several at once. Each file is one map.
- **A Maps button on every reader's toolbar** opens a list of the game's
  maps. Hovering a name pops that map up, shrunk to fit, over the guide.
  The guide doesn't move.
- **A map opens on its own** for longer study, with pan and zoom.
- **Maps have their own section** on the game page, below Guides.
- **Maps survive a backup and restore** like any guide.

## Non-goals

- **Progress and completion.** A map has neither (the user's choice). It
  has no In progress or Complete control, no percentage and no "Not
  started".
- **Downloading maps** from VGMaps, GameFAQs or any other site. That
  waits for S22, which reviews each source's terms first (the user's
  choice, 19 October 2026).
- **Scripted interactive maps** (IGN, Fandom). They have no static image
  to save (the survey).
- **Annotations, markers, collected-item tracking, bookmarks, search and
  contents** on a map.
- **Maps inside a text guide's contents.** A map isn't part of a text
  guide's structure, and mixing maps in would overload its contents (the
  user's reason for choosing the toolbar).
- **Linking** a map to a place in a text guide.

## Anti-goals

These would make it a failure even if it works:

- **The list or the pop-up closing because of where the mouse goes.**
  Moving onto a large map image, across it, or between the list and the
  image must never dismiss them (the user's chief concern).
- **The pop-up being slow** to show a big map. The glance is only a few
  seconds long.
- **Opening a map moving the text guide's place,** or counting as reading
  progress.
- **A huge or malformed image** exhausting memory, hanging the app, or
  leaving a partial import.

## Starting point

- **Guides** have one of three formats, `Txt`, `Html` and `Pdf`, stored
  by name. A CHECK on `Guides.Format` enforces them, at schema 7.
- **A guide's title** is its own field. It's suggested from the file name
  (`GuideTitle.Suggest`), it can be edited in the import dialog, and it's
  stored apart from the file. `SourceLabel` keeps the original file name.
- **Import** picks one file (`PickSingleFileAsync`). It inspects the
  file, checks for a duplicate by fingerprint, publishes through staging,
  and only then writes rows. No import has a pixel or decompression
  limit.
- **The readers** are TXT, HTML (WebView2) and PDF behind
  `IReaderSession`. `ShellWindow` routes to them by format. Anything that
  isn't HTML or PDF falls through to TXT.
- **The reader toolbar** shows commands by capability flag
  (`ReaderCommandPolicy`).
- **The game page** has one flat guide list (`GuideList`). The library's
  game row reads "N guides", counting every guide.
- **Images:**
  - Artwork checks a PNG, JPEG or WebP header for its dimensions without
    decoding (`ArtworkValidator`), capped at 4096 pixels.
  - The UI decodes with `BitmapImage` and `DecodePixelWidth`.
  - PDF pages render at most 4096 pixels wide, within a 96 MiB budget.
  - Nothing decodes in tiles.
- **The maps the survey saw** (VGMaps listings):
  - nearly all PNG, with a few JPEGs;
  - a game often has many separate images;
  - sizes up to 22112 × 344 (a strip), 15456 × 10304, and 12809 × 9363
    at 72 MB. The largest decodes to about 640 MB of pixels.
  - Two widths pass 16384, a common decoder and GPU texture limit.

## The design

### Storage (Infrastructure)

- **A fourth format, `Image`.** Schema 8 rebuilds `Guides` to widen the
  `Format` CHECK, using the Version 7 pattern (create, copy, drop,
  rename). It keeps every table that references `Guides` and every
  index.
  - **The rebuild must not cascade.** Five tables reference `Guides`
    with `ON DELETE CASCADE`, and migrations run in one transaction with
    `foreign_keys=ON`. A plain `DROP TABLE Guides` would delete their rows.
    The migration has to avoid that on purpose, for example by switching
    foreign keys off before its transaction and running
    `foreign_key_check` before commit. It adds `PixelWidth` and `PixelHeight`, required for `Image`
  and null otherwise. *(My call: the game page row shows the size, and
  the reader plans from it before decoding.)*
- **The file** is stored byte for byte as `content/<guideId>/map.png` or
  `map.jpg`. It's the guide's only file, so backup, restore, deletion and
  the reconciler treat it as any single-file guide.
- **The title** is suggested from the file name, with `-` and `_` read as
  spaces (`castle-floor-1.png` → "castle floor 1"). It's editable before
  import and stored as the guide's `Title`.
  - The original file name stays in `SourceLabel`.
  - S22 will set the title from the source page instead, which says more
    than a file name (the user's choice).
- **The view place** (below) is stored in `ReadingStates.LocatorJson`.
  `EstimatedFraction` is null, and `CompletedUtcMs` stays null.
- **The fingerprint** is the file's SHA-256, so a duplicate is caught
  the same way as for TXT and PDF.

### Import (Infrastructure and Production)

- **Accepted files:** `.png`, `.jpg` and `.jpeg`, identified by their
  header, not only their extension. The VGMaps sample held only PNG and
  JPEG, and GameFAQs maps and charts are mostly the same (the user, 19
  October 2026). GIF and WebP can follow on demand.
- **Limits:**
  - **The header is checked before anything is decoded,** as artwork
    does today.
  - A file is at most 256 MiB.
  - Neither side may exceed 32768 pixels.
  - The image is at most 268 million pixels (16384²), about 1 GiB
    decoded.

  *(My call: the largest real map, 159 million pixels, fits with room
  to spare.)* A file over a limit is refused with a message giving its
  size and the limit.
- **The check** decodes the image once, off the UI thread, at a reduced
  size, to prove it decodes. A truncated or corrupt file is refused
  before anything is published.
- **Several at once:**
  - The game page's Maps section has an **Import maps** button, and its
    picker takes several files.
  - The dialog lists every file with its editable title, size and any
    problem, and imports the good ones together.
  - Each map is published on its own. One bad file is reported and
    doesn't stop the rest.
  - **Import guide** (Ctrl+O) keeps its single-file picker. A PNG or
    JPEG picked there imports as one map.

  *(My call: the guide import dialog stays as it is.)*
- **The preview** for a map shows its title, type, pixel size and file
  size, with a small thumbnail.

### The game page (Production)

- **A Maps section** sits below Guides, in the same scrolling page, with
  its own list and the **Import maps** button. With no maps it shows "No
  maps yet" and the button, so several maps can be imported from the
  start. *(My call.)*
- **Maps are ordered by title,** as in the Maps list. *(My call.)*
- **A map's row** shows its icon, title, type and pixel size (for
  example "PNG · 15456 × 10304"). There's no reading state.
- **Open, Remove and keyboard selection** work in the Maps list as in
  Guides. Focus moves between the two lists with Tab.
- **The game's completion choice** ignores maps.
- **The library's game row** reads "N guides, M maps", and leaves out
  "M maps" when there are none. Library search matches map titles too.
  *(My call.)*

### The Maps button and pop-up (Production)

- **The button:** every reader's toolbar gains **Maps** when the game has
  at least one map. A map opened on its own has it too. It opens a list
  of the game's maps, by title.
- **Hovering a name**, or moving keyboard focus onto it, shows that map
  in a pop-up beside the list.
  - The map is the whole image, shrunk to fit.
  - The pop-up is as large as the window allows, beside the list rather
    than over it.
  - Hovering another name swaps the map.
- **The pop-up shows the whole map only.** It has no pan or zoom: "a
  version small enough to fit on screen" is what the glance needs (the
  user's answer). Anything closer means opening the map.
- **What keeps it open** (the user's chief concern):
  - Pointer movement never closes the list or the pop-up. Moving onto
    the image, across it, or between the two keeps both open.
  - When the pointer leaves both, the last map stays up.
  - They close on Esc, on the Maps button again, on a click outside both,
    or when a map is opened.
- **Clicking a name** shows its map, the same as hovering, so touch and
  click work too. The pop-up has an **Open map** button that opens the
  map as its own guide.
  - The text guide's place is saved as on any guide switch.
  - Back returns to the game page, as from any reader.

  *(My call: an accidental click never swaps the guide that's open.)*
- **The keyboard:**
  - Alt+M opens the list, Up and Down move through the names, Enter
    opens the focused map, and Esc closes.
  - The pop-up's image has the map's title as its accessible name.
- **Speed:** each map has a **preview copy**, shrunk to at most 2048
  pixels on its longer side.
  - It's made when the map is imported, and again whenever it's
    missing.
  - It's a cache under the app's local cache folder, keyed by the file's
    hash, outside the library. It isn't backed up.
  - The pop-up shows the preview copy, so even the largest map appears
    at once.

### Reading a map (Production)

- **A new reader**, `ImageReaderSession`, sits behind `IReaderSession`.
  - Its capabilities are `Zoom` and `Scroll`. Text size, find, contents,
    bookmarks and the completion control are absent.
  - `ShellWindow`'s format routing names TXT explicitly, so no format
    falls through to it.
  - Opening a map skips `ReaderCompletionChoice` and progress tracking.
  - **Opening a map isn't reading activity.** It doesn't change the
    library's Resume guide (`LastActiveGuideId`), and it doesn't move the
    game or the map in recent-activity order. Resume still points to the
    walkthrough after a glance at a map. *(My call, from the intent: a
    map supports a text guide.)*
- **The view:**
  - The map opens fitted to the reader.
  - Drag or the arrow keys pan.
  - The wheel with Ctrl, Ctrl+Plus and Ctrl+Minus zoom about the
    pointer or the centre, from fit up to 4× the image's own pixels.
  - Ctrl+0 fits.
  - The toolbar shows Zoom out, Fit and Zoom in, plus Maps.
- **Large images:**
  - A map is shown from a few downsampled levels.
  - Zoomed past what the largest level holds, the visible region is
    decoded at full detail in tiles.
  - No surface is wider or taller than 4096 pixels, so maps beyond the
    16384-pixel texture limit display.
  - Decoded memory stays within a budget like PDF's: tiles off screen
    are dropped first.
  - The builder chooses the decoder (WIC via `BitmapDecoder` with a
    transform, or another) and the tile size. It records the choice
    here.

    **The choice (PR b):** WinRT `BitmapDecoder`, since a trimmed build
    rules out hand-written COM interop. The overview is one Fant-scaled
    decode. Then level 0 is decoded in bands of 512 rows through
    `BitmapTransform.Bounds`, into a 2×2 box pyramid of 512-pixel PNG
    tiles. For the 15456 × 10304 test map on the CI host, the overview
    took 0.4–0.8 s and its 875 tiles 6.1–9.3 s.
  - **A tile cache on disk is allowed.** PNG decodes from the top down,
    so a big map's levels and tiles would cost a full decode on every
    open. They may be saved once to the same local cache folder as the
    preview copies:
    - capped at 2 GiB, dropping the least recently used first;
    - removed with its map;
    - never backed up.

    *(My call.)*
  - **Opening time:** the map appears fitted within 1 second, from the
    preview copy, and full detail fills in as it decodes. A first open
    of the largest map may take longer to reach full detail, never to
    show the map. *(My call.)*
- **The view place** is the zoom and the centre point, as fractions of
  the image (the survey's locator).
  - `ImagePosition(Zoom, CenterX, CenterY)` is the fourth
    `ReaderLocationCodec` payload, checked against the file's hash.
  - It's saved like any place: when the reader closes, and after the view
    settles.
  - Reopening a map returns to it. A place that no longer decodes fits.
  - It's a convenience only and is never shown as progress.

### Backup and restore

- **A map is backed up and restored** like any single-file guide: its
  file, its row with the new columns, and its view place. The preview
  copies aren't backed up. They're rebuilt on demand.
- **A schema 7 backup** restores through the schema 8 migration. An
  older app refuses a schema 8 backup, as today.

### Tests

- **Core:**
  - `ImagePosition` round trip, and bounds checks for zoom and centre;
  - title suggestion from file names;
  - the limits;
  - the "N guides, M maps" fact;
  - toolbar commands for an image reader;
  - zoom and pan math: fit, zoom about a point, clamping.
- **Infrastructure:**
  - header checks for PNG and JPEG;
  - refusals for each limit, a truncated file, a mismatched extension,
    and a decompression bomb (tiny file, huge header);
  - schema 8 migration and expected schema;
  - publishing a map, duplicate detection;
  - backup and restore of a map, and restore of a schema 7 backup;
  - preview copy creation and rebuild.
- **Installed modes** (both lanes):
  - `maps-import`: several files at once, one refused, the Maps section
    and its rows;
  - `maps-reader`: open, zoom, pan, and reopen at the same view; a map
    over 16384 pixels wide displays;
  - `maps-popup`: from a TXT guide, open the list, hover two maps, and
    move the pointer onto and across the image and away. The list and
    pop-up stay open, Esc closes them, and the TXT place is unchanged.
    Open a map from the pop-up.
  - `maps-keyboard`: Alt+M, Up and Down, Enter, Esc.
- **Fixtures** are generated, not downloaded:
  - a small PNG and a JPEG;
  - a 20000 × 400 strip;
  - a PNG near the pixel limit, made at fixture time to keep the repo
    small;
  - a truncated PNG and a decompression bomb.

## Delivery

| PR | Content | Installed modes |
| --- | --- | --- |
| a | The `Image` format, schema 8, the limits and checks, Import maps (several files), the Maps section, backup and restore. A map opens fitted, with no zoom yet. | `maps-import` |
| b | The image reader: zoom, pan, levels and tiles, the view place | `maps-reader` |
| c | The Maps button and pop-up on every reader, the preview copies, the keyboard | `maps-popup`, `maps-keyboard` |

Image maps are listed as supported only after PR c.

## Deviations from the P2 design

1. **Three PRs for one format,** as for multi-page HTML.
2. **Maps have no progress or completion** (the user's choice), unlike
   every other guide.
3. **A derived cache** (the preview copies, and the reader's levels and
   tiles) lives outside the library.
   The originals stay untouched.
4. **Several files import at once,** but only through Import maps.

## PR a verification record

See the [PR a plan](t21-2-image-maps-import-plan.md).

- **Core,** each test failing first and then passing on the host:
  - `MapImageHeaderTests`: PNG and JPEG sizes from the header (a 64 KiB
    EXIF segment, a progressive JPEG, fill bytes, a stream that can't
    seek), and completeness: a PNG must reach IEND, a JPEG its end after
    its first scan, found by segment lengths so an EXIF thumbnail's own
    scan and end don't count;
  - `MapImageLimits`, `MapPresentationTests` (stored name, label, size
    fact, sort by title then id) and `MapBatchTests` (the button label,
    the summary, and Import waiting for every file's check);
  - map titles from file names, keeping a dot that isn't the extension;
    "N guides, M maps"; the map icon; `GamePageLayout` leaving a row of
    each list and the Maps header.

  The full suite passes, 1444/1444.
- **Infrastructure:**
  - schema 8: a populated schema 7 library keeps every row through the
    `Guides` rebuild, a failed migration stays at schema 7, deletes still
    cascade, and the pixel columns are required for maps only;
  - `MapImportTests`: each limit's refusal, a decompression bomb refused
    from its header before any decode, a truncated map that still decodes,
    a mismatched extension, upper-case extensions, duplicates by bytes and
    not by title, and publishing `content/<id>/map.png`;
  - a map backed up and restored, and a schema 7 backup restored.

  The full suite passes, 901/901.
- **Production:** it builds with 0 warnings, with warnings as errors.
- **Installed:** the `import` group passes on both lanes (runs
  39182497573, MSIX, and 39182499716, portable). `import-maps` picks four
  files at once; two import, and a damaged file and a 40,000 × 40,000
  header are refused with their reasons. A strip map imports by Ctrl+O,
  the Maps section lists all three by title with no reading state, and a
  map opens fitted. The library then holds 3 maps (640 × 480, 64 × 48 and
  20000 × 400) with no reading state, and Resume isn't a map.

  Screenshots: [import-maps](evidence/ci/t21-2/import-maps.png),
  [import-maps-section](evidence/ci/t21-2/import-maps-section.png) and
  [import-maps-open](evidence/ci/t21-2/import-maps-open.png).
- **Found by the installed runs:**
  - **A truncated PNG decoded** with its missing rows filled in, so it
    showed as ready. Completeness is now checked apart from decoding.
  - **Import maps stayed disabled** after a Ctrl+O import, and **the Maps
    list ran off a short window**. Both are fixed, and the smoke checks
    the button.
- **Found by the final review:** Import could start before every file was
  checked, a dotted file name lost its tail in the title, and a JPEG cut
  after its EXIF thumbnail counted as whole. Each is fixed with a test
  that failed first.
- **Deferred:** map removal and in-batch duplicate copy still say
  "guide"; focus after cancelling Remove map. Both are polish for PR c.

## PR b verification record

See the [PR b plan](t21-2-image-maps-reader-plan.md).

- **Core,** each test failing first and then passing on the host:
  - `ReaderLocationCodecTests`: `ImagePosition` round trips, refused
    bounds, never an estimate, and a changed map with no restore
    candidates;
  - `ReaderCommandPolicyTests`: a map shows Zoom and Fit map only;
  - `ProgressCoordinatorTests`: tracking without an open still saves;
  - `MapViewportTests`:
    - fit (at most 4×), zoom about a point, the 1.25 step, the 1% snap;
    - pan clamping, arrow steps, a strip centred vertically, and resize
      keeping the centre;
    - places round-tripping, the level, visible tiles, and a tile plan
      within the budget on a 4K view;
  - `MapPyramidTests`: levels stopping at the 2048 overview, a strip's
    single tile row, every tile emitted once, 2×2 averaging and odd
    edges;
  - `MapTileMemoryTests` and `MapSessionDiagnosticsTests`.

  The full suite passes, 1498/1498.
- **Infrastructure:**
  - `MapTileStoreTests`:
    - a committed build is complete;
    - an interrupted build, a missing level folder or overview, another
      size, or an unreadable manifest is incomplete;
    - a rebuild replaces the old cache;
    - prune keeps a hash another guide uses;
    - trim drops the least recently opened, never a kept map;
    - a bad hash builds no path;
  - `MapHashesAreThoseOfImageGuidesOnly`.

  The full suite passes, 917/917.
- **Production:** it builds with 0 warnings, with warnings as errors.
- **Installed:** the PR run [39231269680](https://github.com/ilya-slalom/desktop-guides/actions/runs/39231269680)
  passes on both lanes, with the new `maps-reader` mode:
  - the 15456 × 10304 map showed fitted in 764 ms on its first, uncached
    open;
  - eight zoom steps, then panning by the arrow keys and by a drag, drew
    15 tiles from level 2 within the budget;
  - Back and reopen showed the same view from the cache in 13 ms;
  - Ctrl+0 fitted, and the fitted view was saved;
  - the 20000-pixel strip displayed, its largest surface 1250 pixels;
  - removing it removed its cache.

  Afterwards no map had an open time or an estimate, and Resume was still
  the text guide.

  Screenshots: [maps-reader-zoomed](evidence/ci/t21-2/maps-reader-zoomed.png)
  and [maps-reader-strip](evidence/ci/t21-2/maps-reader-strip.png).
- **Found by the installed runs:** each was a fault in the mode, not the
  reader.
  - It sat inside the import block, so it ran no steps.
  - An integer `[Math]::Min(4, …)` expected a zoom of 0.
  - A map still selected after Back didn't reopen when selected again.
  - The short Maps list hadn't made its third row.
- **Found by the final review:**
  - Visible tiles on a 1440p or 4K display could exceed the tile budget
    and evict one another forever. The reader now gives up the margin,
    then detail, to stay within the budget.
  - The mode didn't check that panning moved the view. Once it did, it
    showed that the drag never panned: the probe moved the cursor with
    `SetCursorPos`, which sends the app no pointer moves. The probe now
    sends mouse moves, and the reader applies a drag's release point, so
    a drag whose moves were merged still pans. In the final runs the
    arrow keys and the drag each moved the view.

  All are fixed; the tile budget with tests that failed first.
- **Found by the PR runs:**
  - Removing a map while its cache was being built failed ("couldn't be
    removed"), because the build held the map's file open. Removal now
    stops the build first. A game's removal stops every build.
  - That stop then waited for the whole build (7–25 s): WinRT's decode
    and encode run to the end once started, and the band loop never
    checked its token. It now checks before each band and tile, and a
    mid-build removal took 0.3–0.7 s.

  `maps-reader` removes a second large map while its tiles build, and
  requires it within 5 s.
- **Deferred:**
  - a changed map's place restores exactly rather than fitting;
  - a part-deleted cache can read as complete;
  - a cache-build failure blames the original file;
  - one `XamlRoot.Changed` handler is left per open;
  - a restore's old build can overlap the new queue;
  - an open during a build reads the overview from `.building`;
  - the build's peak memory is about 150 MiB, not 64;
  - the smoke has no final `else`.
