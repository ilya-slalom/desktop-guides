# T11.5 colorful icons design

Status: in review in PR #85.
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

## T11.5 verification record

Implemented from [t11-5-colorful-icons-plan.md](t11-5-colorful-icons-plan.md)
on `feat/p1-t11-5-colorful-icons`, PR #85.

- **Unit tests** (pcsx2-win, .NET SDK 10.0.401):
  - Core: 1089 of 1089 pass, including 53 new in `AppIconCatalogTests` and
    `ColorfulIconsPresentationTests`.
  - Infrastructure: 773 of 773 pass, including 5 new in
    `ColorfulIconsSettingTests`.
- **Package:**
  - `verify_production_package.py` now requires the nine icons and
    `THIRD-PARTY-NOTICES.txt`. It failed before the notice was packaged,
    then passed.
  - The SDK already packaged the SVGs as content. The project file only
    adds `CopyToOutputDirectory` for the portable exe.
- **Assets:** nine SVGs, 20,355 bytes, from
  `microsoft/fluentui-system-icons` at
  `a347af95478f989038cccbf085978865264c048a` (`SOURCE.md`). No PNG
  fallbacks.
- **Installed MSIX:** CI `dev-fast` `design` run
  [38136321055](https://github.com/ilya-slalom/desktop-guides/actions/runs/38136321055)
  passed four icon scenarios:
  - off, switched on;
  - on after a restart, switched off;
  - dark theme with colour on;
  - the empty library with colour on.

  It also passed `design-language` with the new card's order checks. The
  full PR run is listed below.
- **Portable exe:**
  - The installed `design` group passed on Windows 11 x64 (pcsx2-win)
    through an interactive scheduled task, using `-PortableExecutable`.
  - The user's existing portable library at `%LOCALAPPDATA%\DesktopGuides`
    was renamed aside for each run, with the user's approval, and renamed
    back with all 301 files after each.
  - The colour icons load from the single-file exe's extraction folder.
- **Rendering check:**
  - All nine SVGs render in colour with their gradients through
    `SvgImageSource`, in light and dark, with no blank or black boxes.
  - The colour `settings` icon is a mostly grey gear upstream.
  - Fluent's 24 px art has built-in padding, so the colour icons look
    slightly smaller than the glyphs they replace; each mode is
    consistent.
- **Accessibility:**
  - The nav item, title bar and Settings card names are unchanged.
  - `ImageIcon` draws through an `Image` child that `AccessibilityView=Raw`
    on the icon doesn't reach, and the `TitleBar` builds its own from
    `IconSource`. The shell hides both images, and the smoke fails on any
    `Image` in the control view under `LibraryNavigation`, `AppTitleBar` or
    `AppThemeSettingsCard`.
- **Smoke fixes found on the way:**
  - The new card pushes Game data below the fold at 720 px, so
    `design-language` checks that section scrolled into view.
  - `Scroll-SettingsToTop` reaches the page's `ScrollViewer` through the
    raw view.
  - The toggle's confirmation is a quiet status.
- **Not run:** a real system-wide high-contrast switch. It is deferred to
  T16.2's high-contrast pass, which saves and restores the theme.
  `AppIconCatalog.For` and `ColorfulIconsPresentation` pin the behaviour.
- **Final review:** a fresh reviewer found two Important issues. Each was
  fixed after a check failed first:
  - The empty-state colour icons still leaked an `Image`. A whole-window
    check failed on the empty search. Now every colour-icon host waits on
    one layout handler until its inner image exists.
  - The toggle stayed enabled during an export. `export-cancel` failed in
    CI run
    [38137886129](https://github.com/ilya-slalom/desktop-guides/actions/runs/38137886129).
    The toggle now follows `ready` and the export/restore busy state, like
    the theme and material selectors.
- **Deferred minors:**
  - No automated check that the portable exe contains the icons.
  - The MIT notice sits only inside the portable exe.
  - `e2e-testing.md` doesn't list the icon scenarios.
  - The icon scenarios' cleanup resets the setting before restoring the
    Windows theme, as the material scenarios do.
- **Full PR CI:** run
  [38138192255](https://github.com/ilya-slalom/desktop-guides/actions/runs/38138192255)
  passed on `e1d1548`: all four installed shell shards, x64 and ARM64
  packages, the toolbar and native ARM64. The portable `design` group
  passed again with both fixes.

Rulings against this spec (details in the plan):

1. High contrast comes from the window's `ThemeSettings`, not
   `AccessibilitySettings`.
2. Ten places use nine files: `library` serves the Library item and the
   title bar.
3. The portable icons load from `AppContext.BaseDirectory`, the
   single-file extraction folder; the MSIX uses `ms-appx:///`.
4. High contrast and the failed save are unit-tested in Core, not in the
   installed smoke.
5. The rendering check ran in the installed runs, not as a first task.
6. The empty-state icons stay at 24 px.
7. The copy uses US spelling: "color", "license".
8. The credit line always shows; high contrast adds a third line.
9. The packaging guard has two parts: a Core test of the folder and the
   package verifier.
10. The smoke reads the applied icons from the toggle's `ItemStatus`.
11. The Colorful icons card has its own glyph, `E76E`.
12. The installed MSIX check is the CI `design` shard, the gate of record
    in [e2e-testing.md](e2e-testing.md); the user's Preview install on
    pcsx2-win was not touched.

Evidence (portable exe, pcsx2-win):
[light on, Settings](evidence/t11-5-colorful-icons/icons-light-on-settings.png),
[light on, Library](evidence/t11-5-colorful-icons/icons-light-on-library.png),
[dark on, Settings](evidence/t11-5-colorful-icons/icons-dark-on-settings.png),
[dark on, Library](evidence/t11-5-colorful-icons/icons-dark-on-library.png),
[off, Settings](evidence/t11-5-colorful-icons/icons-off-settings.png),
[empty library](evidence/t11-5-colorful-icons/icons-light-empty-library.png),
[empty search](evidence/t11-5-colorful-icons/icons-empty-search.png) and
[Game data](evidence/t11-5-colorful-icons/icons-game-data.png).
