# T11.5 colorful icons design

Status: design approved in brainstorming; spec awaiting review.
Prerequisites T11.4 and T14.4 are merged. T11.5 is the last open task in M2,
and T16.2 depends on it.

## Intent

Users can turn on colourful icons in Settings. The navigation, the title bar,
the two empty states and the Settings cards then use Fluent UI System Icons
`*_color` SVGs (MIT) instead of the monochrome Segoe Fluent glyphs. The
monochrome glyphs stay the default, and high contrast always uses them.

Traces: the T11.5 rows in [implementation-plan.md](implementation-plan.md)
(TR11.3) and [work-breakdown.md](../work-breakdown.md) T11.5.

Decisions made during brainstorming:

- **Scope:** the navigation items, the title bar, the empty states and the
  Settings cards. The reader toolbar, search boxes, dialogs and taskbar icon
  stay monochrome.
- **Approach:** a Core catalog maps each place to its glyph and its colour
  asset. The shell sets every icon source from that catalog and applies
  them again when the setting or high contrast changes. This was chosen over
  XAML theme resources or per-control bindings because it gives one place
  for each choice that tests can check.

## Behaviour

- **Setting:**
  - A `SettingsCard` in Settings → Appearance, below Window background.
  - Header "Colorful icons". Description "Show navigation and Settings icons
    in colour." and, on a second line, "Icons: Fluent UI System Icons, MIT
    licence."
  - The control is a `ToggleSwitch` named `ColorfulIconsToggle`.
- **Default and storage:**
  - Off by default.
  - Stored as a `ColorfulIcons` key in the Settings table through
    `AppSettings`, `ReadSettings` and `WriteSettings`. No migration; a
    missing key reads as off.
- **Applying:**
  - The change takes effect immediately; no restart.
  - If saving fails, the toggle reverts and the existing settings-error
    status appears.
- **High contrast:**
  - High contrast always uses the monochrome glyphs.
  - While it is on, the toggle is disabled and the card shows "High contrast
    uses standard icons."
  - When high contrast turns off, the saved choice applies again.
  - The app listens to `AccessibilitySettings.HighContrastChanged`.
- **Themes:** the colour icons are the same in light and dark.
- **Accessibility:** the icon elements stay `AccessibilityView=Raw`, so the
  UIA names don't change.

## Icon mapping

All assets are `ic_fluent_<name>_24_color.svg`.

| Place (`AppIcon`) | Current glyph | Colour asset |
| --- | --- | --- |
| `NavLibrary` (Library nav item) | `Home` symbol (E80F) | `library` |
| `NavSettings` (built-in Settings item) | built-in gear (E713) | `settings` |
| `TitleBar` | E82D | `library` |
| `EmptyLibrary` | E82D | `book_open` |
| `EmptySearch` | E721 | `search_sparkle` |
| `ThemeCard` (App theme) | E771 | `paint_brush` |
| `MaterialCard` (Window background) | E790 | `image` |
| `BackupCard` (Back up) | E896 | `document_folder` |
| `RestoreCard` (Restore) | E777 | `history` |
| `ProvidersCard` (Game data) | E721 | `globe` |

The plan confirms each current glyph against the XAML before moving it into
the catalog. The glyph each place shows today stays unchanged.

## Assets and licence

- **Assets:**
  - The ten SVGs come from `microsoft/fluentui-system-icons` at a pinned
    commit.
  - They go in `src/DesktopGuides.Production/Assets/Icons/Color/` with their
    upstream names.
  - Both the MSIX and the portable build package them as content.
  - One 24 px source is used at every size, from the 16 px navigation icon
    to the 48 px empty-state icon.
- **Source record:** `Assets/Icons/Color/SOURCE.md` records the upstream
  commit, the file list and the total asset size.
- **Licence:**
  - `THIRD-PARTY-NOTICES.txt` at the repo root holds the MIT notice for
    Fluent UI System Icons (Copyright (c) 2020 Microsoft Corporation).
  - Both builds package it.
- **Rendering check:**
  - The first plan task renders all ten SVGs through `SvgImageSource` on
    pcsx2-win and takes screenshots.
  - Any icon that renders wrongly, for example a missing gradient, gets 1×
    and 2× PNGs at 24 px for that icon only.
  - `SOURCE.md` records which icons use PNGs.

## Code

- **Core:**
  - An `AppIcon` enum lists the ten places in the mapping table.
  - `AppIconCatalog.For(AppIcon icon, bool colorful, bool highContrast)`
    returns an `AppIconChoice`: either a glyph (a Segoe Fluent code point) or
    an asset name.
  - It returns the asset only when `colorful` is true and `highContrast` is
    false.
  - `AppIconCatalog` also exposes every asset name, so a test can check the
    packaged files.
- **Production:**
  - An `AppIcons` helper turns an `AppIconChoice` into a `FontIconSource` or
    an `ImageIconSource` over `SvgImageSource`.
  - The URI is `ms-appx:///Assets/Icons/Color/...` in the MSIX and a file
    URI under the exe folder in the portable build, using the existing
    packaged/portable path logic.
- **Shell:**
  - `ApplyIcons()` sets every host:
    - the `NavigationViewItem` icons, using `IconSourceElement`;
    - the built-in Settings item, through `NavigationView.SettingsItem`
      after load;
    - the `TitleBar` `IconSource`;
    - the empty-state icons;
    - the `SettingsCard.HeaderIcon`s, including `ProviderSettingsCard`.
  - It runs at startup, when the toggle changes and when high contrast
    changes. The high-contrast event is handled on the UI thread.
- **High-contrast input:** the shell reads high contrast through a small
  input that tests can set, with `AccessibilitySettings.HighContrast` as the
  production value.

## Tests and verification

- **Core unit tests:**
  - Every `AppIcon` has both a glyph and an asset.
  - All four combinations of colourful and high contrast are covered; only
    colourful with high contrast off returns the asset.
- **Packaging guard:** every catalog asset name has a file under
  `Assets/Icons/Color/`.
- **Infra round trip:**
  - `ColorfulIcons` saves and loads.
  - A missing key reads as off.
  - Theme, window background and the export reminder keep their values.
- **Production shell tests (existing UI shards):**
  - **Startup:** the toggle is off and every host shows a glyph.
  - **Toggle on and off:** on switches all ten hosts, including the
    built-in Settings item, to image sources; off switches them back.
  - **Restart:** the saved choice is applied.
  - **Failed save:** the toggle reverts and the settings-error status
    appears.
  - **High contrast** (through the test input):
    - while it's on, the toggle is disabled, the note shows and glyphs are
      forced;
    - when it turns off, the saved choice returns.
  - **Accessibility:** the icon elements stay `AccessibilityView=Raw`, and
    nav item names are unchanged.
  - These tests check what app code controls, not how WinUI draws images.
- **Installed check on pcsx2-win** (per [e2e-testing.md](e2e-testing.md)):
  - In the MSIX, take screenshots of the navigation, the empty library and
    Settings in light and dark with colour on, and one with it off.
  - Start the portable exe once with colour on, to confirm its icons load.
  - The screenshots go in the PR.
- **Not automated:** a real system-wide high-contrast switch, because it
  changes the user's whole desktop. The test input covers the behaviour. The
  real high-contrast screenshot comes in T16.2's high-contrast pass, which
  already saves and restores the theme.

## Out of scope

- Reader toolbar, search box, dialog and taskbar icons.
- Different colour icons per theme.
- A user-chosen icon set beyond on and off.
