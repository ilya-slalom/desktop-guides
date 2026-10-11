# Colour icons (T11.5)

Source: [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons)
at commit `a347af95478f989038cccbf085978865264c048a`, MIT license. The
notice is in `THIRD-PARTY-NOTICES.txt` at the repo root.

The files keep their upstream names. Each comes from
`assets/<Folder>/SVG/<file>` at that commit.

| File | Upstream folder | Bytes |
| --- | --- | --- |
| `ic_fluent_apps_24_color.svg` | Apps | 1907 |
| `ic_fluent_book_open_24_color.svg` | Book Open | 1823 |
| `ic_fluent_document_folder_24_color.svg` | Document Folder | 2084 |
| `ic_fluent_document_text_24_color.svg` | Document Text | 2000 |
| `ic_fluent_globe_24_color.svg` | Globe | 2589 |
| `ic_fluent_history_24_color.svg` | History | 1335 |
| `ic_fluent_image_24_color.svg` | Image | 1473 |
| `ic_fluent_library_24_color.svg` | Library | 2328 |
| `ic_fluent_paint_brush_24_color.svg` | Paint Brush | 2078 |
| `ic_fluent_search_sparkle_24_color.svg` | Search Sparkle | 4413 |
| `ic_fluent_settings_24_color.svg` | Settings | 2232 |
| `ic_fluent_warning_24_color.svg` | Warning | 1306 |

Total: 25,568 bytes.

PNG fallbacks: none.

## Project icons (not from Fluent)

Fluent's colour set has no PDF document or game controller. These two were
generated in Fluent's colour style with an image model, outside the repo, on
11 October 2026. They were then cleaned up: background specks removed, edges
un-premultiplied, cropped, padded to a 9% margin and scaled to 96x96. They
belong to this project and fall under its licence, not Fluent's MIT licence.

| File | Use | Bytes |
| --- | --- | --- |
| `dg_document_pdf_color.png` | PDF guide rows | 7855 |
| `dg_game_controller_color.png` | Library game rows before artwork loads | 9131 |

To update, change the commit, download the same paths, and update the
table. `AppIconCatalogTests` checks that the folder matches the catalog.
