# T05.4 catalog and workflow components design

Status: implemented and verified 29 September 2026. Prerequisites T04.4 and
T11.4 are merged.

## Intent

T05.4 extracts the presentation patterns that T04.4 proved into shared
resources, so T05.1 (Library and Game rows), T04.2 (rename and remove) and
T06.1 (import) reuse one interaction language. It also switches the Library
game list to the shared artwork row. The virtualization, missing-artwork and
offline checks then run against a real list, not just a rule on templates.

Traces: TR05.3, TR11.3.

Constraints taken from the plan and the work breakdown:

- Use a style or data template. Don't wrap a standard control when a
  style or template is enough.
- Extract only proven patterns. `HeaderedContentControl` stays with T06.1,
  because no current workflow uses it.
- Add no new packages. `CommunityToolkit.WinUI.Controls.MetadataControl`
  `8.2.251219` is already centrally pinned.
- Catalog rendering makes no provider request.

## Proven patterns being extracted

| Pattern | Current location | Shared form |
| --- | --- | --- |
| Artwork row: a 45×60 tile with a placeholder, title, summary and detail line, and one accessible name | Add game result template | `DesktopGuidesArtworkRowTemplate` over `ArtworkItem` |
| Detail cover: a 120×180 frame | Game detail | `DesktopGuidesCoverFrameStyle` |
| Artwork tile | Add game result template | `DesktopGuidesArtworkTileStyle` |
| `MetadataControl` facts inside the `AutomationGroup` host | Game detail | Keep the host; `DesktopGuidesFactsStyle` for spacing and text |
| Empty-state surface with an icon, title and hint | Library and Game empty states | `DesktopGuidesEmptyStateStyle` |
| Busy row: ring, live text and Cancel | Add game | `DesktopGuidesBusyRowStyle` |
| Status `InfoBar` with actions | Add game, game editor, Settings | `DesktopGuidesStatusInfoBarStyle` |

## Approach

We considered three approaches:

| Approach | Decision |
| --- | --- |
| A shared resource dictionary with code-behind, holding keyed styles and compiled `x:Bind` templates | **Choose.** Controls stay native. Bindings are compiled and suit virtualization. |
| Small user controls (`ArtworkTile`, `EmptyState`, `BusyRow`) | Reject. This is the wrapping the task rules out, and it adds an element tree per item and UIA peer work. |
| `{Binding}` templates without code-behind | Reject. They rely on reflection and fail silently. |

We also considered three ways for Library rows to load their artwork:

| Approach | Decision |
| --- | --- |
| Phased `ContainerContentChanging` loading: decode on realization, clear on recycle | **Choose.** This is the standard WinUI virtualization pattern, and it reuses the existing stream loader. |
| `BitmapImage.UriSource` file paths | Reject. URI caching can show stale art after a refresh. The behavior hasn't been verified in both the MSIX and portable builds. |
| Load every thumbnail up front | Reject. It decodes the whole library and ignores virtualization. |

## Components

### Shared resources

A new `src/DesktopGuides.Production/Styles/Catalog.xaml` has a code-behind
file, `Catalog.xaml.cs`. WinUI needs the code-behind for `x:Bind` templates
defined in a dictionary. `App.xaml` merges it after `Controls.xaml`.

- **`DesktopGuidesArtworkTileStyle`** (`Border`)
  - 45×60 with 4-DIP corners, filled with `ControlFillColorSecondaryBrush`.
  - The row template puts a placeholder `FontIcon` behind the `Image`.
    A null or failed image therefore still shows a deliberate tile.
  - The image and glyph use `AccessibilityView="Raw"`.
- **`DesktopGuidesCoverFrameStyle`** (`Border`)
  - 120×180, with the same corners, fill and placeholder rule.
  - Game detail uses it.
- **`DesktopGuidesArtworkRowTemplate`** (`DataTemplate`, `x:DataType="local:ArtworkItem"`)
  - Layout: the tile, then a text column.
  - The text column holds the title, then an optional `Summary`, then an optional `Detail`.
  - The title is one line with `CharacterEllipsis`.
  - `Summary` and `Detail` use the metadata style, trim the same way, and collapse when null.
  - There is no nested items host and no per-item Toolkit control, so the element count per row stays fixed.
  - The row height isn't fixed. The tile sets a 60-DIP minimum, so text scaling still works.
- **`DesktopGuidesFactsStyle`**: spacing and text for `MetadataControl`. The `AutomationGroup` host stays at each use site.
- **`DesktopGuidesEmptyStateStyle`** (`Border`)
  - Based on `DesktopGuidesElevatedSurfaceStyle`, with top alignment.
  - The icon, title and hint stay in each view's XAML.
- **`DesktopGuidesBusyRowStyle`** (`StackPanel`): horizontal orientation and 12-DIP spacing.
- **`DesktopGuidesStatusInfoBarStyle`** (`InfoBar`): shared layout defaults.
  - Each use site keeps its names, `AutomationId`s, live settings and closing behavior.

### Code

- **`ArtworkItem`** (Production, `INotifyPropertyChanged`)
  - Properties: `Title`, `Summary?`, `Detail?`, `AccessibleName`, and a notifying `Thumbnail`.
  - The `Thumbnail` setter is internal.
- **`GameSearchItem`** derives from `ArtworkItem`. Its behavior and its network thumbnail loader stay the same.
- **`LibraryGameItem`** also derives from `ArtworkItem`.
  - It wraps a `Game` and exposes `Game` and `ArtworkRelativePath`.
  - Its strings come from the Core presentation helper below.
- **`LibraryGamePresentation`** (Core, next to `GameMetadataPresentation`)
  - The summary is the trimmed platform, or null when it's blank.
  - `Detail` is null for now; T05.1 adds facts.
  - The accessible name is the title. The existing harness finds Library rows by title, so they keep working.
- **`ArtworkLoadTicket`** (Core)
  - A small, testable rule that pairs a container with an item and a version.
  - A decoded thumbnail may be applied only if the ticket is still current for that container.
- **`ArtworkListLoader`** (Production) attaches to a `ListView` through `ContainerContentChanging`.
  - It sets the container's `AutomationProperties.Name` to the item's `AccessibleName`.
    UIA reads the name from the `ListViewItem`, not from the template root.
  - In phase 1 it starts the thumbnail delegate with a per-container cancellation token.
  - When a container is recycled, it clears the thumbnail and cancels the token.
  - Add game uses only the naming part, because its thumbnails arrive from the provider loader.
- **`ShellWindow`** changes:
  - `GameList` drops `DisplayMemberPath` and uses the row template. Its `ItemsSource` becomes `LibraryGameItem`s built from `ListGamesAsync`.
  - `GameSelected` and `GameFromRow` read `LibraryGameItem.Game`.
  - The cover loader takes a decode width. Library rows decode at 90 px wide (2× the tile), and Game detail keeps 240.
  - The loader reads only managed artwork through `ManagedArtworkStore.ResolveFile`. It never references provider services.

### Out of scope

- Sorting, per-row facts, last-opened time and guide rows (T05.1).
- Search states (T05.2).
- `HeaderedContentControl` (T06.1).
- Reader and Settings adoption (T14.4).
- New packages.

## Data flow

1. The Library route loads the games and maps each one to a `LibraryGameItem`, with `Thumbnail` null.
2. The `ListView` realizes only the containers in the viewport. Phase 0 renders the title, the summary and the placeholder tile.
3. Phase 1 creates a ticket and decodes that game's managed artwork on the UI thread's async path.
4. If the ticket is still current when the decode finishes, the thumbnail is set. Otherwise the result is dropped.
5. On recycling, the old container's ticket is cancelled and its thumbnail is cleared before the container is reused.

The Library route never causes a network request.

## Errors and edge cases

- **Missing, unreadable or corrupt artwork.** The loader returns null, and the placeholder tile stays.
  - No per-row error or status appears.
  - A game with no artwork path looks the same.
- **Recycling races.** Tickets prevent a stale cover appearing on a reused container.
  - A cancelled decode is ignored.
- **Library re-render.** New items replace the old ones. Loads still in flight are cancelled, or they fail the ticket check.
- **Long and localized text.** The title is trimmed to one line; the full title stays in the UIA name.
  - There is no culture-specific formatting.
  - Arabic titles take their direction from the inherited flow direction.
- **Narrow width.** The tile has a fixed width and the text column is `*`, so rows never scroll horizontally.

## Testing

### Unit tests (TDD, `DesktopGuides.Core.Tests`)

- `LibraryGamePresentation` covers these cases:
  - summary with a platform, a blank platform and a null platform;
  - accessible name for long and non-ASCII titles.
- `ArtworkLoadTicket` covers these cases:
  - a current ticket applies;
  - a ticket superseded on the same container doesn't apply;
  - a cancelled ticket doesn't apply.

The WinUI glue stays thin. The installed checks below verify it.

### Installed checks: the new `catalog` scenario

- **Seeding.** `DesktopGuides.ShellSeed` gets a new `seed-catalog` mode, which writes 500 games:
  - every third game has valid managed artwork: small PNGs generated in memory and stored through `ManagedArtworkStore`;
  - ten games point to an artwork file that was deleted;
  - some titles are 200 characters long;
  - some titles are in Japanese, in German with diacritics, and in Arabic.
- **Harness switches.** The scenario runs as part of the full `windows_shell_install.ps1` run, and alone with a new `-CatalogOnly` switch.
- **Virtualization.**
  - At the top of the list, fewer than 80 realized `ListItem`s are under `GameList`.
  - The same bound holds after scrolling to the end with `ScrollPattern`, and the last seeded game is reachable.
- **Missing artwork.** Rows whose artwork is missing or deleted render and are named by title. The app stays responsive and shows no error status.
- **Long text.** A 200-character-title row is no taller than a short-title row, and its UIA name is the full title.
- **Keyboard.** Focus the list, then press Down, End and Enter. The Game view opens for the focused game.
- **Narrow width.** Each realized row's bounds lie within `GameList`'s width.
- **No provider traffic.**
  - The check runs after opening the Library and scrolling.
  - `Get-NetTCPConnection -OwningProcess <app>` must show no connections to a non-loopback address.
  - `describe-providers` must confirm that no credential blob exists.
  - The check uses no firewall rule.
- **Screenshots.** Library wide and narrow, in light and dark, saved to the result directory.
- **Add game.** The existing provider scenarios cover Add game after it moves onto the shared template.

## Gates

| Gate | Where | What it covers |
| --- | --- | --- |
| Headless tests and script guards | CI `core-tests` | Core and Infrastructure tests, including the new unit tests |
| Package build | CI `packages` and `production-packages` | x64 and ARM64 MSIX |
| Installed UI regression | CI `production-shell-ui`, the full harness run | Shell smoke, design-language, material and the new `catalog` scenario. Provider live runs skip because the runner has no credential files. The job uploads JSON results and screenshots. |
| Live provider regression | Windows host, `-ProviderOnly` | Add game on the shared template with the user's IGDB and SteamGridDB keys. Normal provider requests, no firewall rule. |

The Windows host isn't needed for the catalog or regression gates. CI is the
gate of record, and the PR links to its screenshot artifacts. Run the host
harness only to debug a CI failure, or for live-provider checks. Portable
build checks are not required, because T05.4 changes nothing in packaging.
The PR states this. Adding a portable CI job, a fake-provider CI lane and
the scheduled-task CI entry point is T17.2 work.

`docs/p1/e2e-testing.md` records this split. The display size of the hosted
runner is recorded in the `catalog` result. The wide and narrow window sizes
are checked against it the first time the scenario runs in CI.

## Documentation

- This design, and the implementation plan
  `docs/p1/t05-4-catalog-components-plan.md`.
- A T05.4 verification record at the end of this document once the checks pass.
- `docs/p1/implementation-plan.md`: T05.4 status.
- `docs/p1/e2e-testing.md`: the CI gate split and the `-CatalogOnly` switch.
- `docs/progress.md`:
  - correct the stale T11.4 status;
  - add T04.4, the portable build and T05.4.
- `docs/work-breakdown.md` and `docs/initial-design.md` need no requirement
  changes. T05.4's text already covers this scope.

## PR outcome

The PR targets **T05.4**. Prerequisites T04.4 and T11.4 are merged. The
outcome is a set of shared catalog and workflow styles and templates, adopted
by Add game, Game detail, the status surfaces and the Library game list. The
virtualization, missing-artwork, text, keyboard, narrow-width and no-traffic
checks pass in CI, with screenshots. This unblocks T05.1, T04.2 and T06.1.

## T05.4 verification record

- **Unit tests.** `core-tests` in CI run 36563413921: 165 Core and 233
  Infrastructure passes, including `LibraryGamePresentationTests` and
  `ArtworkLoadTicketsTests`.
- **Installed catalog scenario.** `production-shell-ui` in the same run, light and
  dark. Working area 1024×720 at 100%, so the 1500 px wide size clamps to the
  working area. Realized rows: 34 at the top and 21–22 at the end (limit 80),
  from CI run 36572000282. Long row 76 DIPs, short row 76. The long-title and
  short-title covers each matched 2672 pixels of their seeded colour; the corrupt
  and missing-art rows matched 0 and 29–38, under the 270-pixel limit. No status, no remote connections, no credential blob,
  10 missing-artwork games, 500 games.
- **Live provider regression.** Host `-ProviderOnly` on 29 September 2026 with the
  CI x64 MSIX: `provider-live` passed all nine phases and found 16 Half-Life
  result rows on the shared template, including `Half-Life, Main game, 1998`.
  Both credential leak scans found no files, and no firewall rule was used.
- **Rulings.** The long title is 160 characters, because `GameDetails.TitleLimit`
  is 160. The keyboard check uses Ctrl+Down and End instead of Down, because
  `GameList` selection follows focus and plain Down opens a game.
  `LibraryGamePresentation` lives in `Core/Library`. The first CI run counted
  7 realized rows, because it dropped rows with empty bounds and UIA reports
  empty bounds for cached off-screen rows. The count now keeps those rows. A
  negative control proved both checks: a `StackPanel` items panel with row
  artwork turned off failed with 500 realized rows and 0 cover pixels. It ran on
  the host because the control ran past CI's 120 s catalog timeout.
- **Not run.** Portable build checks: T05.4 changes nothing in packaging.
