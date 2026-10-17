# T21.1 format survey

Status: approved by the user, 17 October 2026, with T21.2 limited to ranks
1 and 2 (see [the decision](#the-decision)). This meets the
[T21.1 exit](implementation-plan.md#s21-selected-local-material-formats).

The survey asks which formats beyond TXT, HTML and PDF are common among
real game guides, and dependable and safe enough offline that T21.2
should add them, in what order. It follows
[section 7](../p2-technical-design.md#7-s21-the-format-survey-t211) of the
P2 design and S21 in the [work breakdown](../work-breakdown.md).

## The ranking

| Rank | Format | Why | Confidence |
| --- | --- | --- | --- |
| 1 | **Multi-page HTML**: a guide's saved pages imported as one guide | Every wiki walkthrough sampled spans 28–40 pages. GameFAQs' HTML guides, which are all of the newest game's, are split across pages too. Saved pages keep their text and images offline. Today each page is a separate guide, and the links between pages don't work. | Medium |
| 2 | **Image maps**: a standalone PNG, JPEG, GIF or WebP | 6 of 10 GameFAQs games have a chart, and VGMaps holds thousands of PNG maps. Today an image can't be imported on its own. Some maps are far larger than a plain image view can show. | Medium |
| 3 | **MHTML**: a page saved as one file | It's what Edge and Chrome offer as "Webpage, Single File", and it read offline as well as the "Complete" save. Import could turn it into today's HTML guide, with no new reader. How often readers use it wasn't measured. | Low |
| 4 | **CBZ**: page images in a ZIP | 19% of archive.org strategy guides offer a CBZ. Every one sampled also offers a PDF, which the app already reads, and the CBZ can be 11 times larger. It could reuse rank 2's image view. | Medium |
| 5 | **EPUB** | 61% of archive.org strategy guides offer an EPUB, but it's generated from OCR of the scan and repeats the PDF. Publisher EPUB guides couldn't be measured, and are probably rare and often DRM-protected. | Low |
| 6 | **Markdown** | Only four Markdown guides were found, all on GitHub. A single file reads acceptably as plain text. | Medium |

**Not recommended:**
- **CBR,** which needs a RAR decoder. It was seen once.
- **DjVu:** archive.org's DjVu files are mostly derived text, not a format readers download to view.
- **Interactive maps** on IGN and Fandom: they are scripted tiles with no static image to save.
- **Steam and Google Docs guides:** Steam guides can only be saved as a web page. A Google Doc can be exported as PDF, HTML or EPUB, which ranks 1–5 cover.

**The order's main uncertainty** is how often readers want rank 2 inside the app, compared with rank 1, rather than in an image viewer. The evidence shows both are common. Rank 1 was put first because a walkthrough split into 40 unlinked guides is broken reading, while a map in a separate viewer still works.

## The decision

Made by the user, 17 October 2026:

- **T21.2 adds two formats:** multi-page HTML, then image maps.
- **Plain text** is already supported and stays as it is.
- **Multi-page HTML must fix what saved pages lose** (TR21.2):
  - links between its pages open the imported pages, not the site;
  - every page reads styled, even when the site's stylesheets weren't
    saved with it.
- **MHTML, CBZ, EPUB and Markdown** wait until after the final milestone.
  They're listed in the work breakdown's
  [After the final milestone](../work-breakdown.md#after-the-final-milestone).

## Method

- **Sources:** GameFAQs, IGN, Neoseeker, StrategyWiki and a large Fandom wiki
  for written guides; VGMaps for image maps; archive.org and Replacement
  Docs for scanned manuals and official guides; GitHub, Steam Community,
  speedrun.com and Google Docs for community guides.
- **Automated reading** (17 October 2026) fetched single public pages and
  archive.org's metadata search. It downloaded no archives, scans or images.
- **Blocked:** GameFAQs (HTTP 403), Neoseeker (its robots.txt disallows AI
  fetchers), IGN (refused), StrategyWiki (403, and its robots.txt
  disallows AI agents), Fandom (402 and 403), speedrun.com (403) and Kobo
  (403). Nothing was tried that would get around them.
- **Sampling by hand:** for the blocked sites, the user sampled GameFAQs
  listings and four wiki walkthroughs in a browser on 17 October 2026,
  following a checklist. Saved copies were deleted afterwards.
- **No sample is committed.** The procedure for keeping a private copy for
  later work is in [guide-samples.md](guide-samples.md).

## Observations

### GameFAQs listings (by hand)

| Game | Plain text | HTML | A chart | The chart's size |
| --- | ---: | ---: | --- | --- |
| Super Mario Bros. | 18 | 0 | Yes | 3077 × 786 |
| The Legend of Zelda: A Link to the Past | 30 | 2 | Yes | 1040 × 734 |
| Final Fantasy VII | 79 | 3 | Yes | 750 × 655 |
| The Legend of Zelda: Ocarina of Time | 63 | 0 | Yes | 743 × 705 |
| Gran Turismo 2 | 23 | 0 | No | — |
| StarCraft | 35 | 0 | Yes | 300 × 400 |
| Pokémon Emerald | 46 | 0 | Yes | 1608 × 1192 |
| World of Warcraft | 27 | 3 | No | — |
| Portal 2 | 12 | 0 | No | — |
| Elden Ring | 0 | 8 | No | — |
| **Total** | **333** | **16** | **6 of 10** | |

- **Plain text** is 95% of these guides (333 of 349). The app reads it today.
- **HTML** is rare for older games but is all of Elden Ring's (8 of 8), so
  newer guides lean to HTML. GameFAQs' HTML guides are split across
  several pages.
- **Charts** are small: the largest is 2.4 megapixels.

### Wiki walkthroughs (by hand)

Each walkthrough page was saved with "Webpage, Complete" and opened with
the network off.

| Guide | Pages in its contents | Offline |
| --- | ---: | --- |
| IGN, Elden Ring | 40 | Text and images kept; links broken |
| StrategyWiki, A Link to the Past | 35 | Text and images kept; links and styling broken |
| Zeldapedia (Fandom), Ocarina of Time | 28 | Text and images kept; links and styling broken |
| Neoseeker, Final Fantasy VII | 1 | Text and images kept; a single page, no links |

- **One MHTML probe:** the IGN page saved as "Webpage, Single File" read
  offline like the "Complete" save, with links and styling broken.
- **None of the four offered an export of the whole guide.**

### Image maps (VGMaps, from its listings)

| Game | Era | The map |
| --- | --- | --- |
| 10-Yard Fight | NES | 4 PNGs, 216 × 963 |
| 1943: Battle of Midway | NES | 24 PNGs; 320 × 10531 |
| Addams Family: Pugsley's Scavenger Hunt | SNES | PNG strip, 22112 × 344 |
| The 7th Saga | SNES | PNG quadrants, 8704 × 6688, up to 2 MB |
| Castlevania: Circle of the Moon | GBA | PNG, 15456 × 10304, 16.1 MB |
| Alien Shooter 2: Reloaded | PC | PNG, 12809 × 9363, 72 MB; another 17096 × 560 |
| Dragon Quest V (PS2) | PS2 | PNG world map, 4097 × 4724, 23.2 MB |

- **The files:** nearly every map is a PNG, with occasional JPEGs and one
  PDF. A game has many separate images and no bundle.
- **The sizes:** two widths pass 16384 pixels, a common decoder and GPU
  texture limit. The largest decodes to about 640 MB of pixels. Only
  about a fifth of each platform listing was read, so larger maps likely
  exist.

### Scanned guides and manuals (archive.org metadata)

A title search for "strategy guide" among archive.org texts found 2,190
items. The search also matches books that aren't game guides.

| Format offered | Items | Note |
| --- | ---: | --- |
| PDF | nearly all | With a text layer, image-only, or both |
| EPUB | 1,333 (61%) | Generated by archive.org from OCR; some are lending-only and encrypted |
| CBZ | 415 (19%) | Large: 149 MB beside a 13 MB PDF for one item |
| CBR (RAR) | a few | Seen on one sampled item |

- **Image-only PDFs exist,** so search and text locators don't work on
  some scans. The app already reports such pages as unsearchable.
- **Replacement Docs:** one item was read. The site states PDF is its
  norm, and it shows dead links.

### Community guides

- **GitHub:** four Markdown guides: a 6-file walkthrough, a single
  68 KB README, a folder of docs, and a Jekyll site whose built HTML is
  the offline form.
- **Steam Community:** guides are web pages only, often mostly images.
- **speedrun.com:** blocked.
- **Google Docs:** its documented exports are PDF, DOCX, HTML (zipped),
  EPUB, Markdown and text. No public document was fetched.

## The formats

### 1. Multi-page HTML

- **What it is:** the pages of one guide, saved together, imported as one
  guide with one place and one completion state.
- **How often:** all three wiki walkthroughs were 28–40 pages, and
  GameFAQs' HTML guides are split across pages as well.
- **Offline:** saved pages keep text and images on all four sites.
  Styling broke on two, which the reader's own styles can absorb.
- **Risks:**
  - every page is untrusted HTML under today's policy;
  - links between pages must point at the imported copies, not the site;
  - pages share assets;
  - a guide's total size needs a limit.
- **Locator** (TR21.1): the page's path plus today's HTML locator, and a
  rule for progress across pages.
- **Reader:** today's WebView2 HTML reader, opening one page at a time.
  Contents, search and bookmarks would have to span pages.

### 2. Image maps

- **What it is:** an image imported on its own, as a guide.
- **How often:**
  - 6 of 10 GameFAQs games had a chart, all small;
  - VGMaps holds thousands of PNG maps, a few of them huge.
- **Offline:** yes; the image is the whole content.
- **Risks:**
  - decompression bombs and huge dimensions;
  - widths beyond 16384 pixels;
  - memory, at 640 MB decoded for the largest map seen.

  The importer needs pixel and size limits, and the reader needs tiled
  or downsampled decoding.
- **Locator:** the zoom and the centre point, as fractions of the image.
- **Reader:** a new image reader with pan and zoom. A game's many maps
  are many guides at first.

### 3. MHTML

- **What it is:** one `.mhtml` file holding a page and its assets.
- **How often:** not measured. It's a browser save option, not a site
  format.
- **Offline:** as good as "Webpage, Complete" in the one probe.
- **Risks:** the MIME parts are untrusted. Import would unpack them into
  today's HTML guide layout, under today's asset rules.
- **Locator and reader:** today's HTML ones.

### 4. CBZ

- **What it is:** page images in a ZIP.
- **How often:** 19% of archive.org strategy guides. In the sample, each
  one also had a PDF.
- **Offline:** yes.
- **Risks:**
  - extracting outside the staging folder (TR21.1);
  - ZIP bombs and nested archives;
  - huge page images.
- **Locator:** the page.
- **Reader:** rank 2's image reader, with page turns.

### 5. EPUB

- **How often:** 61% of archive.org strategy guides, but generated from
  OCR and repeating the PDF. Publisher EPUBs weren't measured.
- **Risks:**
  - a ZIP of untrusted XHTML;
  - DRM detection, with lending-only copies refused clearly.
- **Locator and reader:** like multi-page HTML, so it builds on rank 1.

### 6. Markdown

- **How often:** rare; four guides were found.
- **Today:** a single file reads as plain text after a rename. Accepting
  `.md` as text is a small step if it's wanted.
- **A real Markdown reader** (rendered headings and links) isn't
  justified by this evidence.

## What the survey doesn't show

- **Small samples:** 10 GameFAQs games, 4 walkthroughs, 7 VGMaps games,
  15 archive.org items read in detail, and 4 GitHub repositories.
- **The hand sampling missed two things:** two of the twelve planned
  games (Final Fantasy VI and Street Fighter II Turbo), and the number of
  charts per game. Page counts for GameFAQs' HTML guides weren't
  recorded.
- **archive.org percentages come from a title search,** so they include
  books that aren't game guides.
- **VGMaps sizes come from partial listings.**
- **Publisher EPUBs, speedrun.com and Google Docs weren't sampled.**
- **The survey counts what sites offer,** not what readers keep on disk.

## Next

T21.2 starts with its own design for multi-page HTML, including TR21.2.
Image maps follow with their own design.
