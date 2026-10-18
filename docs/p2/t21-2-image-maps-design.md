# T21.2 image maps: design

Status: approved in conversation, 19 October 2026.

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
