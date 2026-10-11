# T11.5 Colorful Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Someone can turn on "Colorful icons" in Settings → Appearance.
The navigation, the title bar, the two empty states and the five Settings
cards then show Fluent UI System Icons colour SVGs. Off is the default, and
high contrast always shows the monochrome glyphs.

**Architecture:** Core gains `AppIcon`, `AppIconChoice`, `AppIconCatalog`
(one table: glyph and colour asset per place) and
`ColorfulIconsPresentation` (copy, toggle state, status). Infrastructure
stores a `ColorfulIcons` Settings key. The shell gains a
`ShellWindow.Icons.cs` partial. Its `ApplyIcons()` sets every icon host from
the catalog through a small `AppIcons` factory, and runs at startup, on a
toggle, when the settings load and on the existing high-contrast
`ThemeSettings.Changed` event.

**Tech Stack:** .NET 10, WinUI 3 (`ImageIcon`, `ImageIconSource`,
`SvgImageSource`), CommunityToolkit SettingsControls, Microsoft.Data.Sqlite,
xUnit, the PowerShell 5.1 UI Automation harness, Python 3.11 package
verifier.

**Spec:** `docs/p1/t11-5-colorful-icons-design.md`

**Target:** T11.5, in one PR on `feat/p1-t11-5-colorful-icons`.
**Prerequisites:** T11.4 and T14.4 (#54), both merged. T16.2 waits on this
task.

## Global Constraints

- Off by default. A missing or unknown `ColorfulIcons` value reads as off.
  No schema migration.
- High contrast always shows the glyphs. The saved choice is kept, and it
  applies again when high contrast turns off.
- Every icon element stays `AutomationProperties.AccessibilityView="Raw"`.
  No UIA name changes.
- The same colour asset is used in light and dark.
- Out of scope: the reader toolbar, search boxes, dialogs and the taskbar
  icon.
- Assets come from `microsoft/fluentui-system-icons` at commit
  `a347af95478f989038cccbf085978865264c048a`, with their upstream file
  names, under `src/DesktopGuides.Production/Assets/Icons/Color/`.
- `THIRD-PARTY-NOTICES.txt` (MIT, "Copyright (c) 2020 Microsoft
  Corporation") sits at the repo root and is packaged in both builds.
- Copy is verbatim from this plan (see ruling 7).
- New `AppSettings` parameters go at the end, with defaults.
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only.
- Never print, copy or log provider credential values.

## Visual direction

`ui-ux-pro-max` checked the visual direction at the plan stage. Its
database is web and mobile oriented, so the rules applied are its
quick-reference icon rules:

- **One icon family per layer** (`icon-style-consistent`). Every host in
  scope switches together, so colour and monochrome never mix in the
  navigation or the Settings cards. The reader toolbar is its own layer
  and stays monochrome.
- **Sizes come from the host.**
  - The navigation, the title bar and the card headers keep the size their
    control sets (16 or 20 px).
  - The empty states keep their current 24 px.
  - Every size uses the same 24 px source.
- **Icon contrast of at least 3:1 in both themes** (`Icon Contrast`,
  `dark-mode-pairing`). Checked by eye on the light and dark screenshots in
  Task 5.
- **Meaning never depends on colour** (`color-not-only`). Every icon sits
  beside a text label, which is unchanged.

## Rulings against the spec

Task 6 records these in the design's verification record.

1. **High contrast comes from the window's `ThemeSettings`**
   (`Microsoft.UI.System`), not `AccessibilitySettings`. `AccessibilitySettings`
   events need a CoreWindow, which this desktop window doesn't have
   (`ShellWindow.Theme.cs`). The theme code already reads
   `themeSettings.HighContrast` and listens to `themeSettings.Changed`, and
   the icons use the same source.
2. **Nine SVG files, not ten.** `library` serves both `NavLibrary` and
   `TitleBar`, so the ten places map to nine files.
3. **The portable file URI uses `AppContext.BaseDirectory`, not the exe
   folder.**
   - The single-file exe extracts its content under `%TEMP%\.net`, and
     `AppContext.BaseDirectory` points there, so that is where the icons
     are.
   - The MSIX uses `ms-appx:///`. `AppDataRoot.HasPackageIdentity()` picks
     between them, as the export does.
   - Task 5 confirms the portable icons load.
4. **High contrast and the failed save are tested in Core, not in the shell
   smoke.**
   - The production app has no test hooks. The theme's high-contrast path
     is tested the same way: `ThemePresentation.Resolve` in Core, plus code
     review.
   - The decisions are in `AppIconCatalog.For` and
     `ColorfulIconsPresentation`, which are unit-tested for every case.
   - The shell wiring is checked in code review against the existing
     theme handler.
   - The real high-contrast run is T16.2's high-contrast pass.
   - Cost if wrong: a wiring bug on `ThemeSettings.Changed` is found in
     T16.2 rather than here.
5. **The SVG rendering check runs in the installed run (Task 5),** not in a
   first task. It needs a WinUI host, which only exists once the shell is
   wired.
   - The catalog stores file names with their extension, so switching an
     icon to PNG changes data only: `AppIcons` picks `BitmapImage` for
     `.png`.
   - Cost if wrong: a few PNGs are added late, in Task 5.
6. **The empty-state icons stay at their current 24 px.** The spec's 48 px
   was wrong; the XAML uses `FontSize="24"`.
7. **US spelling throughout:**
   - "Show navigation and Settings icons in color." and "Icons: Fluent UI
     System Icons, MIT license."
   - The header "Colorful icons" is fixed by the task and Windows uses US
     English, so the spec's "colour" and "licence" would mix two
     spellings in one card.
   - Cost if wrong: a two-string copy change.
8. **The card description always shows the credit line,** and in high
   contrast it adds a third line, "High contrast uses standard icons." The
   spec said the note replaces the credit; keeping the credit keeps the
   licence notice visible in every mode.
9. **The packaging guard has two parts:**
   - a Core test that matches the catalog's file names against the source
     folder;
   - a check in `verify_production_package.py` that every file in that
     folder, plus `THIRD-PARTY-NOTICES.txt`, is in the MSIX.
10. **The smoke reads the applied icons from an `ItemStatus`.**
    `ApplyIcons()` counts the hosts it set and writes
    `images:<n> glyphs:<m>` to `ColorfulIconsToggle`'s `ItemStatus`. The
    hosts themselves are Raw.

11. **The new Colorful icons card has its own header icon, always a
    glyph:** `E76E` (Emoji2) in Segoe Fluent Icons.
    - Every other Appearance card has an icon, so a card without one would
      sit out of line.
    - The card isn't one of the spec's places, so it stays monochrome.
    - Task 4 confirms the code point against Microsoft's Segoe Fluent Icons
      list. If `E76E` isn't a face or palette there, Task 4 picks one that
      is and isn't used by another card, and records it here.

## Review Focus

1. **The built-in Settings item exists only after the `NavigationView`
   template loads.** `ApplyIcons()` runs again on `Navigation.Loaded`, and
   the status counts only the hosts that were set. The smoke expects
   `images:10 glyphs:0` (Task 5), so a missing Settings item fails it.
2. **The toggle is used before the settings load.** The toggle is disabled
   until the settings load, like the theme and material selectors. Pinned
   by `IsEnabled="False"` in the XAML and the `iconsReady` flag (Task 4);
   the smoke waits for it to be enabled.
3. **Quick double toggle while a save is pending.**
   - Saves are serialised through `iconSaveGate`.
   - A failed save reverts only if it is still the latest choice, the same
     pattern as the theme.
   - Checked in code review against `AppThemeSelectionChanged` (Task 4).
4. **High contrast turns on with the toggle on.**
   - The glyphs are forced and the toggle is disabled but still shows on.
   - When high contrast turns off, the icons are colour again.
   - Pinned by `ForEveryIconHighContrastWinsOverColorful` and
     `TheToggleIsDisabledInHighContrast` (Task 1).
5. **A stored value from a newer build, such as `"1"`, reads as off.**
   Pinned by `AnUnknownValueReadsAsOff` (Task 2).

## Host commands

The Mac has no dotnet, so every build and test runs on `pcsx2-win`:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t11-5) { Remove-Item -Recurse -Force E:\work\desktop-guides\t11-5 }; New-Item -ItemType Directory E:\work\desktop-guides\t11-5 | Out-Null"'
  COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t11-5'
}
```

Run `stage` from the repo root (`git -C` paths are fine; don't `cd`
elsewhere). The tar includes the gitignored `tests/fixtures/p0/generated/`
from the Mac checkout, which the Infrastructure tests need.

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t11-5 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- **Infrastructure tests:** `stage && s 'cd /d E:\work\desktop-guides\t11-5 && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'`
- **One class:** append `--filter "FullyQualifiedName~<Class>"` inside the
  quoted command.
- **Production build:** `stage && s 'cd /d E:\work\desktop-guides\t11-5 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'`
- **Production package:** the same command plus
  `-p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false`.
- **Seed build:** `stage && s 'cd /d E:\work\desktop-guides\t11-5 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'`
- **Installed group:** `gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t11-5-colorful-icons -f shell-scope=design`

---
### Task 1: Core icon catalog and the toggle's copy

**Files:**
- Create: `src/DesktopGuides.Core/Library/AppIconCatalog.cs`
- Create: `src/DesktopGuides.Core/Library/ColorfulIconsPresentation.cs`
- Test: `tests/DesktopGuides.Core.Tests/AppIconCatalogTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/ColorfulIconsPresentationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `DesktopGuides.Core.Library`):
  - `enum AppIcon { NavLibrary, NavSettings, TitleBar, EmptyLibrary, EmptySearch, ThemeCard, MaterialCard, BackupCard, RestoreCard, ProvidersCard }`
  - `sealed record AppIconChoice(string? Glyph, string? AssetFile)` with
    `bool IsAsset`, `static AppIconChoice FromGlyph(string)` and
    `static AppIconChoice FromAsset(string)`.
  - `AppIconCatalog.AssetFolder` (`"Assets/Icons/Color"`),
    `AppIconCatalog.All → IReadOnlyList<AppIcon>`,
    `AppIconCatalog.AssetFiles → IReadOnlyList<string>` (distinct, ordinal
    order), and
    `AppIconCatalog.For(AppIcon icon, bool colorful, bool highContrast) → AppIconChoice`.
  - `ColorfulIconsPresentation`, with:
    - the constants `Header`, `Description`, `Credit`,
      `HighContrastNote`, `ToggleName`;
    - `CardDescription(bool highContrast)`, `CardName(bool highContrast)`
      and `ToggleEnabled(bool ready, bool highContrast)`;
    - `Saved(bool on)`, `SaveFailed(string message)` and
      `Status(int images, int glyphs)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/DesktopGuides.Core.Tests/AppIconCatalogTests.cs
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class AppIconCatalogTests
{
    public static TheoryData<AppIcon> Icons()
    {
        TheoryData<AppIcon> data = [];
        foreach (AppIcon icon in Enum.GetValues<AppIcon>()) data.Add(icon);
        return data;
    }

    [Fact]
    public void TheCatalogListsTenPlaces()
    {
        Assert.Equal(10, AppIconCatalog.All.Count);
        Assert.Equal(Enum.GetValues<AppIcon>(), AppIconCatalog.All);
    }

    [Theory]
    [MemberData(nameof(Icons))]
    public void ByDefaultEveryIconIsItsGlyph(AppIcon icon)
    {
        AppIconChoice choice = AppIconCatalog.For(icon, colorful: false, highContrast: false);

        Assert.False(choice.IsAsset);
        Assert.False(string.IsNullOrEmpty(choice.Glyph));
        Assert.Null(choice.AssetFile);
    }

    [Theory]
    [MemberData(nameof(Icons))]
    public void ColorfulIconsUseTheirColorAsset(AppIcon icon)
    {
        AppIconChoice choice = AppIconCatalog.For(icon, colorful: true, highContrast: false);

        Assert.True(choice.IsAsset);
        Assert.Null(choice.Glyph);
        Assert.Matches("^ic_fluent_[a-z_]+_24_color\\.(svg|png)$", choice.AssetFile);
    }

    [Theory]
    [MemberData(nameof(Icons))]
    public void ForEveryIconHighContrastWinsOverColorful(AppIcon icon)
    {
        Assert.Equal(
            AppIconCatalog.For(icon, colorful: false, highContrast: false),
            AppIconCatalog.For(icon, colorful: true, highContrast: true));
        Assert.Equal(
            AppIconCatalog.For(icon, colorful: false, highContrast: false),
            AppIconCatalog.For(icon, colorful: false, highContrast: true));
    }

    [Theory]
    [InlineData(AppIcon.NavLibrary, "\uE80F", "ic_fluent_library_24_color.svg")]
    [InlineData(AppIcon.NavSettings, "\uE713", "ic_fluent_settings_24_color.svg")]
    [InlineData(AppIcon.TitleBar, "\uE82D", "ic_fluent_library_24_color.svg")]
    [InlineData(AppIcon.EmptyLibrary, "\uE82D", "ic_fluent_book_open_24_color.svg")]
    [InlineData(AppIcon.EmptySearch, "\uE721", "ic_fluent_search_sparkle_24_color.svg")]
    [InlineData(AppIcon.ThemeCard, "\uE771", "ic_fluent_paint_brush_24_color.svg")]
    [InlineData(AppIcon.MaterialCard, "\uE790", "ic_fluent_image_24_color.svg")]
    [InlineData(AppIcon.BackupCard, "\uE896", "ic_fluent_document_folder_24_color.svg")]
    [InlineData(AppIcon.RestoreCard, "\uE777", "ic_fluent_history_24_color.svg")]
    [InlineData(AppIcon.ProvidersCard, "\uE721", "ic_fluent_globe_24_color.svg")]
    public void EachPlaceKeepsItsGlyphAndGetsItsAsset(AppIcon icon, string glyph, string asset)
    {
        Assert.Equal(glyph, AppIconCatalog.For(icon, false, false).Glyph);
        Assert.Equal(asset, AppIconCatalog.For(icon, true, false).AssetFile);
    }

    [Fact]
    public void AssetFilesAreTheNineDistinctFiles()
    {
        Assert.Equal(9, AppIconCatalog.AssetFiles.Count);
        Assert.Equal(
            AppIconCatalog.All.Select(icon => AppIconCatalog.For(icon, true, false).AssetFile!)
                .Distinct().Order(StringComparer.Ordinal),
            AppIconCatalog.AssetFiles);
    }

    // Ruling 9: the catalog and the vendored folder agree, so no icon goes missing.
    [Fact]
    public void EveryAssetFileIsVendoredAndNothingElseIs()
    {
        string folder = Path.Combine(
            RepoRoot(), "src", "DesktopGuides.Production",
            AppIconCatalog.AssetFolder.Replace('/', Path.DirectorySeparatorChar));
        string[] vendored = Directory.GetFiles(folder)
            .Select(Path.GetFileName)
            .Where(name => name!.EndsWith(".svg", StringComparison.Ordinal) ||
                           name.EndsWith(".png", StringComparison.Ordinal))
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AppIconCatalog.AssetFiles, vendored);
        Assert.True(File.Exists(Path.Combine(folder, "SOURCE.md")));
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "THIRD-PARTY-NOTICES.txt")));
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DesktopGuides.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("DesktopGuides.sln not found.");
    }
}
```

```csharp
// tests/DesktopGuides.Core.Tests/ColorfulIconsPresentationTests.cs
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ColorfulIconsPresentationTests
{
    [Fact]
    public void TheCardDescriptionCreditsTheIconSet()
    {
        Assert.Equal(
            "Show navigation and Settings icons in color.\nIcons: Fluent UI System Icons, MIT license.",
            ColorfulIconsPresentation.CardDescription(highContrast: false));
    }

    [Fact]
    public void InHighContrastTheCardAddsTheNote()
    {
        Assert.Equal(
            "Show navigation and Settings icons in color.\nIcons: Fluent UI System Icons, MIT license.\n" +
            "High contrast uses standard icons.",
            ColorfulIconsPresentation.CardDescription(highContrast: true));
    }

    [Theory]
    [InlineData(false, "Colorful icons. Show navigation and Settings icons in color.")]
    [InlineData(true, "Colorful icons. Show navigation and Settings icons in color. High contrast uses standard icons.")]
    public void TheCardNameIsHeaderAndDescriptionWithoutTheCredit(bool highContrast, string expected)
    {
        Assert.Equal(expected, ColorfulIconsPresentation.CardName(highContrast));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void TheToggleIsDisabledInHighContrast(bool ready, bool highContrast, bool enabled)
    {
        Assert.Equal(enabled, ColorfulIconsPresentation.ToggleEnabled(ready, highContrast));
    }

    [Fact]
    public void TheCopyIsVerbatim()
    {
        Assert.Equal("Colorful icons", ColorfulIconsPresentation.Header);
        Assert.Equal("Colorful icons", ColorfulIconsPresentation.ToggleName);
        Assert.Equal("Colorful icons turned on.", ColorfulIconsPresentation.Saved(true));
        Assert.Equal("Colorful icons turned off.", ColorfulIconsPresentation.Saved(false));
        Assert.Equal("Couldn't save the icon setting: disk full",
            ColorfulIconsPresentation.SaveFailed("disk full"));
    }

    [Fact]
    public void TheStatusCountsImagesAndGlyphs()
    {
        Assert.Equal("images:10 glyphs:0", ColorfulIconsPresentation.Status(10, 0));
        Assert.Equal("images:0 glyphs:10", ColorfulIconsPresentation.Status(0, 10));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Core tests with `--filter "FullyQualifiedName~AppIconCatalog|FullyQualifiedName~ColorfulIconsPresentation"`
Expected: FAIL to compile. `AppIcon`, `AppIconCatalog` and
`ColorfulIconsPresentation` don't exist.

- [ ] **Step 3: Implement the catalog**

```csharp
// src/DesktopGuides.Core/Library/AppIconCatalog.cs
namespace DesktopGuides.Core.Library;

/// <summary>A place in the shell that shows an app icon (T11.5).</summary>
public enum AppIcon
{
    NavLibrary,
    NavSettings,
    TitleBar,
    EmptyLibrary,
    EmptySearch,
    ThemeCard,
    MaterialCard,
    BackupCard,
    RestoreCard,
    ProvidersCard
}

/// <summary>A Segoe Fluent glyph, or a colour asset file under <see cref="AppIconCatalog.AssetFolder"/>.</summary>
public sealed record AppIconChoice(string? Glyph, string? AssetFile)
{
    public bool IsAsset => AssetFile is not null;

    public static AppIconChoice FromGlyph(string glyph) => new(glyph, null);

    public static AppIconChoice FromAsset(string file) => new(null, file);
}

/// <summary>Each place's monochrome glyph and its Fluent UI System Icons colour asset.</summary>
public static class AppIconCatalog
{
    public const string AssetFolder = "Assets/Icons/Color";

    private static readonly IReadOnlyDictionary<AppIcon, (string Glyph, string Asset)> Table =
        new Dictionary<AppIcon, (string, string)>
        {
            [AppIcon.NavLibrary] = ("\uE80F", "ic_fluent_library_24_color.svg"),
            [AppIcon.NavSettings] = ("\uE713", "ic_fluent_settings_24_color.svg"),
            [AppIcon.TitleBar] = ("\uE82D", "ic_fluent_library_24_color.svg"),
            [AppIcon.EmptyLibrary] = ("\uE82D", "ic_fluent_book_open_24_color.svg"),
            [AppIcon.EmptySearch] = ("\uE721", "ic_fluent_search_sparkle_24_color.svg"),
            [AppIcon.ThemeCard] = ("\uE771", "ic_fluent_paint_brush_24_color.svg"),
            [AppIcon.MaterialCard] = ("\uE790", "ic_fluent_image_24_color.svg"),
            [AppIcon.BackupCard] = ("\uE896", "ic_fluent_document_folder_24_color.svg"),
            [AppIcon.RestoreCard] = ("\uE777", "ic_fluent_history_24_color.svg"),
            [AppIcon.ProvidersCard] = ("\uE721", "ic_fluent_globe_24_color.svg"),
        };

    public static IReadOnlyList<AppIcon> All { get; } = Enum.GetValues<AppIcon>();

    public static IReadOnlyList<string> AssetFiles { get; } =
        Table.Values.Select(entry => entry.Asset).Distinct().Order(StringComparer.Ordinal).ToArray();

    // High contrast always wins: its system colours apply only to glyphs.
    public static AppIconChoice For(AppIcon icon, bool colorful, bool highContrast)
    {
        (string glyph, string asset) = Table[icon];
        return colorful && !highContrast ? AppIconChoice.FromAsset(asset) : AppIconChoice.FromGlyph(glyph);
    }
}
```

- [ ] **Step 4: Implement the presentation**

```csharp
// src/DesktopGuides.Core/Library/ColorfulIconsPresentation.cs
namespace DesktopGuides.Core.Library;

/// <summary>The Colorful icons card's copy, toggle state, and automation status.</summary>
public static class ColorfulIconsPresentation
{
    public const string Header = "Colorful icons";
    public const string ToggleName = "Colorful icons";
    public const string Description = "Show navigation and Settings icons in color.";
    public const string Credit = "Icons: Fluent UI System Icons, MIT license.";
    public const string HighContrastNote = "High contrast uses standard icons.";

    public static string CardDescription(bool highContrast) =>
        highContrast ? $"{Description}\n{Credit}\n{HighContrastNote}" : $"{Description}\n{Credit}";

    public static string CardName(bool highContrast) =>
        highContrast ? $"{Header}. {Description} {HighContrastNote}" : $"{Header}. {Description}";

    public static bool ToggleEnabled(bool ready, bool highContrast) => ready && !highContrast;

    public static string Saved(bool on) => on ? "Colorful icons turned on." : "Colorful icons turned off.";

    public static string SaveFailed(string message) => $"Couldn't save the icon setting: {message}";

    public static string Status(int images, int glyphs) => $"images:{images} glyphs:{glyphs}";
}
```

- [ ] **Step 5: Vendor the assets, the source record and the notice**

`EveryAssetFileIsVendoredAndNothingElseIs` needs the files. Download each
asset into `src/DesktopGuides.Production/Assets/Icons/Color/` from
`https://raw.githubusercontent.com/microsoft/fluentui-system-icons/a347af95478f989038cccbf085978865264c048a/assets/<Folder>/SVG/<file>`:

| File | Folder | Bytes |
| --- | --- | --- |
| `ic_fluent_book_open_24_color.svg` | `Book%20Open` | 1823 |
| `ic_fluent_document_folder_24_color.svg` | `Document%20Folder` | 2084 |
| `ic_fluent_globe_24_color.svg` | `Globe` | 2589 |
| `ic_fluent_history_24_color.svg` | `History` | 1335 |
| `ic_fluent_image_24_color.svg` | `Image` | 1473 |
| `ic_fluent_library_24_color.svg` | `Library` | 2328 |
| `ic_fluent_paint_brush_24_color.svg` | `Paint%20Brush` | 2078 |
| `ic_fluent_search_sparkle_24_color.svg` | `Search%20Sparkle` | 4413 |
| `ic_fluent_settings_24_color.svg` | `Settings` | 2232 |

Check each file's byte count against the table (`wc -c`). Create
`src/DesktopGuides.Production/Assets/Icons/Color/SOURCE.md`:

```markdown
# Colour icons (T11.5)

Source: [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons)
at commit `a347af95478f989038cccbf085978865264c048a`, MIT license. The
notice is in `THIRD-PARTY-NOTICES.txt` at the repo root.

The files keep their upstream names. Each comes from
`assets/<Folder>/SVG/<file>` at that commit.

| File | Upstream folder | Bytes |
| --- | --- | --- |
| `ic_fluent_book_open_24_color.svg` | Book Open | 1823 |
| `ic_fluent_document_folder_24_color.svg` | Document Folder | 2084 |
| `ic_fluent_globe_24_color.svg` | Globe | 2589 |
| `ic_fluent_history_24_color.svg` | History | 1335 |
| `ic_fluent_image_24_color.svg` | Image | 1473 |
| `ic_fluent_library_24_color.svg` | Library | 2328 |
| `ic_fluent_paint_brush_24_color.svg` | Paint Brush | 2078 |
| `ic_fluent_search_sparkle_24_color.svg` | Search Sparkle | 4413 |
| `ic_fluent_settings_24_color.svg` | Settings | 2232 |

Total: 20,355 bytes.

PNG fallbacks: none.

To update, change the commit, download the same paths, and update the
table. `AppIconCatalogTests` checks that the folder matches the catalog.
```

Create `THIRD-PARTY-NOTICES.txt` at the repo root:

```text
Desktop Guides uses the following third-party material.

Fluent UI System Icons
https://github.com/microsoft/fluentui-system-icons
Used for the optional colorful icons in
src/DesktopGuides.Production/Assets/Icons/Color.

MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Compare the licence paragraphs with the upstream `LICENSE` at the pinned
commit (`gh api repos/microsoft/fluentui-system-icons/contents/LICENSE?ref=a347af95478f989038cccbf085978865264c048a --jq .content | base64 -d`);
they must match word for word.

- [ ] **Step 6: Run the tests to verify they pass**

Run: Core tests with the same filter.
Expected: PASS, 53 tests (`AppIconCatalogTests` 43,
`ColorfulIconsPresentationTests` 10, counting each theory row). Then run
the whole Core suite and expect it green.

- [ ] **Step 7: Commit**

```bash
git add src/DesktopGuides.Core/Library/AppIconCatalog.cs \
  src/DesktopGuides.Core/Library/ColorfulIconsPresentation.cs \
  tests/DesktopGuides.Core.Tests/AppIconCatalogTests.cs \
  tests/DesktopGuides.Core.Tests/ColorfulIconsPresentationTests.cs \
  src/DesktopGuides.Production/Assets/Icons/Color THIRD-PARTY-NOTICES.txt
git commit -m "feat(core): add the T11.5 icon catalog and colour assets"
```

### Task 2: The ColorfulIcons setting and its seed command

**Files:**
- Modify: `src/DesktopGuides.Core/Library/LibraryModels.cs` (`AppSettings`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (`ReadSettings`, `WriteSettings`)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`set-colorful-icons`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/ColorfulIconsSettingTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces:
  - `AppSettings(..., bool ExportReminderShown = false, bool ColorfulIcons = false)`;
  - the Settings key `ColorfulIcons`, stored as `"true"` or `"false"`;
  - the seed command `set-colorful-icons <dataRoot> true|false`.

- [ ] **Step 1: Write the failing tests**

The tests reuse `RemovalLibrary` (`CreateAsync`, `Repository`,
`RestartAsync`, `Scalar`, `Execute`) from the Infrastructure tests, as
`ExportReminderSettingTests` does.

```csharp
// tests/DesktopGuides.Infrastructure.Tests/ColorfulIconsSettingTests.cs
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class ColorfulIconsSettingTests
{
    [Fact]
    public async Task ColorfulIconsStartOff()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        Assert.False((await library.Repository.GetSettingsAsync()).ColorfulIcons);
    }

    [Fact]
    public async Task TheChoiceSurvivesARestart()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        await library.Repository.UpdateSettingsAsync(settings => settings with { ColorfulIcons = true });
        await library.RestartAsync();

        Assert.True((await library.Repository.GetSettingsAsync()).ColorfulIcons);
        Assert.Equal("true", library.Scalar("SELECT Value FROM Settings WHERE Key = 'ColorfulIcons'"));
    }

    [Fact]
    public async Task TurningItOffAgainStoresFalse()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        await library.Repository.UpdateSettingsAsync(settings => settings with { ColorfulIcons = true });
        await library.Repository.UpdateSettingsAsync(settings => settings with { ColorfulIcons = false });

        Assert.False((await library.Repository.GetSettingsAsync()).ColorfulIcons);
        Assert.Equal("false", library.Scalar("SELECT Value FROM Settings WHERE Key = 'ColorfulIcons'"));
    }

    [Fact]
    public async Task AnUnknownValueReadsAsOff()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();
        library.Execute("INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('ColorfulIcons', '1')");

        Assert.False((await library.Repository.GetSettingsAsync()).ColorfulIcons);
    }

    [Fact]
    public async Task TheToggleKeepsTheOtherSettings()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();
        await library.Repository.UpdateSettingsAsync(settings => settings with
        {
            Theme = ThemePreference.Dark,
            WindowMaterial = WindowMaterial.Acrylic,
            ExportReminderShown = true
        });

        await library.Repository.UpdateSettingsAsync(settings => settings with { ColorfulIcons = true });
        await library.RestartAsync();

        AppSettings stored = await library.Repository.GetSettingsAsync();
        Assert.Equal(ThemePreference.Dark, stored.Theme);
        Assert.Equal(WindowMaterial.Acrylic, stored.WindowMaterial);
        Assert.True(stored.ExportReminderShown);
        Assert.True(stored.ColorfulIcons);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~ColorfulIconsSettingTests"`
Expected: FAIL to compile. `AppSettings` has no `ColorfulIcons`.

- [ ] **Step 3: Add the setting**

In `LibraryModels.cs`:

```csharp
public sealed record AppSettings(
    ThemePreference Theme,
    Guid? LastActiveGuideId,
    WindowMaterial WindowMaterial = WindowMaterial.Mica,
    bool ExportReminderShown = false,
    bool ColorfulIcons = false);
```

In `ReadSettings`, declare `bool colorfulIcons = false;` beside
`reminderShown`, add this branch after the `ExportReminderShown` branch:

```csharp
            else if (key == "ColorfulIcons")
            {
                // Anything but "true", such as a value from a newer build, reads as off.
                colorfulIcons = value == "true";
            }
```

and return `new AppSettings(theme, lastGuide, material, reminderShown, colorfulIcons);`.

In `WriteSettings`, after the `reminder` command:

```csharp
        using (SqliteCommand icons = connection.CreateCommand())
        {
            icons.Transaction = transaction;
            icons.CommandText = """
                INSERT INTO Settings (Key, Value) VALUES ('ColorfulIcons', $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                """;
            icons.Parameters.AddWithValue("$value", settings.ColorfulIcons ? "true" : "false");
            icons.ExecuteNonQuery();
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: the same filter, then the whole Infrastructure suite.
Expected: 5 new tests pass. The whole suite is green, including the
existing `SqliteLibraryRepositoryTests` that compare `AppSettings` values,
because the new parameter defaults to false.

- [ ] **Step 5: Add the seed command**

In `tools/p1/DesktopGuides.ShellSeed/Program.cs`, after the
`set-material` block:

```csharp
if (args.Length == 3 && args[0] == "set-colorful-icons")
{
    if (args[2] is not ("true" or "false"))
    {
        Console.Error.WriteLine("Colorful icons must be true or false.");
        return 2;
    }
    await using SqliteLibraryRepository iconRepository =
        new(new ManagedPathResolver(args[1]));
    await iconRepository.InitializeAsync();
    await iconRepository.UpdateSettingsAsync(
        settings => settings with { ColorfulIcons = args[2] == "true" });
    return 0;
}
```

If the seed's usage text lists the commands, add
`set-colorful-icons <dataRoot> true|false` to it.

Run: Seed build (host commands).
Expected: build succeeds with 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Core/Library/LibraryModels.cs \
  src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs \
  tests/DesktopGuides.Infrastructure.Tests/ColorfulIconsSettingTests.cs \
  tools/p1/DesktopGuides.ShellSeed/Program.cs
git commit -m "feat(storage): store the T11.5 ColorfulIcons setting"
```

### Task 3: Package the icons and the notice

**Files:**
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj`
- Modify: `tools/p1/verify_production_package.py`

**Interfaces:**
- Consumes: Task 1's files in `src/DesktopGuides.Production/Assets/Icons/Color/`
  and `THIRD-PARTY-NOTICES.txt`.
- Produces:
  - MSIX entries `Assets/Icons/Color/<file>` and `THIRD-PARTY-NOTICES.txt`;
  - the same files in the build output, so the portable single-file exe
    extracts them;
  - the verifier error `Packaged files are missing: <names>.`

- [ ] **Step 1: Make the verifier require the files (RED)**

In `verify_production_package.py`, below the imports:

```python
REPO_ROOT = Path(__file__).resolve().parents[2]
ICON_DIR = REPO_ROOT / "src" / "DesktopGuides.Production" / "Assets" / "Icons" / "Color"


def required_entries() -> list[str]:
    """T11.5: every vendored colour icon and the third-party notice."""
    icons = sorted(
        path.name for path in ICON_DIR.iterdir() if path.suffix.lower() in (".svg", ".png"))
    if not icons:
        raise ValueError(f"No colour icons found in {ICON_DIR}.")
    return ["third-party-notices.txt"] + [f"assets/icons/color/{name.lower()}" for name in icons]
```

In `verify`, directly after the `"desktopguides.app.dll"` check:

```python
        missing = [name for name in required_entries() if name not in entries]
        if missing:
            raise ValueError(f"Packaged files are missing: {', '.join(missing)}.")
```

Build the package on the host (**Production package** in the host
commands), then run:

```bash
s 'cd /d E:\work\desktop-guides\t11-5 && powershell -NoProfile -Command "$p = Get-ChildItem src\DesktopGuides.Production\AppPackages -Recurse -Filter DesktopGuides.Production_*_x64.msix | Select-Object -First 1; python tools\p1\verify_production_package.py $p.FullName --name DesktopGuides.Preview --publisher ''CN=DesktopGuides Development'' --version 0.1.0.0 --architecture x64"'
```

Expected: FAIL with `Packaged files are missing: third-party-notices.txt,
assets/icons/color/ic_fluent_book_open_24_color.svg, ...`.

If `python` isn't on the host, record that in the ledger. The CI
`production-packages` job then sees this RED on the first push, and the
GREEN after Step 2.

- [ ] **Step 2: Package the files (GREEN)**

In the csproj's last `ItemGroup`, after the existing `Content` item:

```xml
    <!-- T11.5: colour icons (Fluent UI System Icons, MIT) and their notice.
         Copied to the output too, so the portable exe extracts them. -->
    <Content Include="Assets/Icons/Color/*.svg;Assets/Icons/Color/*.png"
             CopyToOutputDirectory="PreserveNewest" />
    <Content Include="../../THIRD-PARTY-NOTICES.txt"
             Link="THIRD-PARTY-NOTICES.txt"
             CopyToOutputDirectory="PreserveNewest" />
```

`SOURCE.md` isn't content, so it isn't packaged.

Run: the package build and the verifier command from Step 1 again.
Expected: the verifier prints the identity JSON and exits 0. Also check
that `src\DesktopGuides.Production\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Assets\Icons\Color`
(or the build's actual output folder) has the nine SVGs. The exact path
comes from the build log.

- [ ] **Step 3: Check the portable build**

Run on the host:
`s 'cd /d E:\work\desktop-guides\t11-5 && powershell -NoProfile -ExecutionPolicy Bypass -File tools\p1\test_package_portable_release.ps1'`
Expected: PASS, so the portable packaging still works with the new content.
Then build the portable exe as `package_portable_release.ps1` does, and
list its bundled entries if the script's output shows them. Task 5's
portable launch is the real check.

- [ ] **Step 4: Commit**

```bash
git add src/DesktopGuides.Production/DesktopGuides.Production.csproj tools/p1/verify_production_package.py
git commit -m "build(p1): package the T11.5 colour icons and notice"
```

### Task 4: The Colorful icons card and ApplyIcons

**Files:**
- Create: `src/DesktopGuides.Production/AppIcons.cs`
- Create: `src/DesktopGuides.Production/ShellWindow.Icons.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` (hosts, the new card)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (constructor, settings load)
- Modify: `src/DesktopGuides.Production/ShellWindow.Theme.cs` (the high-contrast event)
- Modify: `src/DesktopGuides.Production/ProviderSettingsCard.xaml`, `ProviderSettingsCard.xaml.cs`

**Interfaces:**
- Consumes: Task 1's `AppIcon`, `AppIconChoice`, `AppIconCatalog`,
  `ColorfulIconsPresentation`; Task 2's `AppSettings.ColorfulIcons`; the
  existing `themeSettings`, `repository`, `AnnounceStatus`,
  `ShowErrorStatus` and `AppDataRoot.HasPackageIdentity()`.
- Produces, for Task 5's smoke:
  - AutomationIds `ColorfulIconsSettingsCard` and `ColorfulIconsToggle`;
  - the toggle's `ItemStatus`, `images:<n> glyphs:<m>`;
  - the status messages `Colorful icons turned on.` and
    `Colorful icons turned off.`.

There is no unit-test project for Production. This task's RED is the
compile against Task 1's types, and its GREEN is the build plus Task 5's
smoke. The decisions it wires are already pinned by Task 1's tests
(ruling 4).

- [ ] **Step 1: Add the icon factory**

```csharp
// src/DesktopGuides.Production/AppIcons.cs
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopGuides.Production;

// T11.5: turns a catalog choice into WinUI icons. Colour assets are content
// files: ms-appx in the MSIX, and the single-file exe's extraction folder
// in the portable build.
internal static class AppIcons
{
    public static IconElement Element(AppIconChoice choice)
    {
        IconElement icon = choice.IsAsset
            ? new ImageIcon { Source = Image(choice.AssetFile!) }
            : new FontIcon { Glyph = choice.Glyph! };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        return icon;
    }

    public static IconSource Source(AppIconChoice choice) => choice.IsAsset
        ? new ImageIconSource { ImageSource = Image(choice.AssetFile!) }
        : new FontIconSource { Glyph = choice.Glyph! };

    public static ImageSource Image(string file)
    {
        Uri uri = AppDataRoot.HasPackageIdentity()
            ? new Uri($"ms-appx:///{AppIconCatalog.AssetFolder}/{file}")
            : new Uri(Path.Combine(
                AppContext.BaseDirectory,
                AppIconCatalog.AssetFolder.Replace('/', Path.DirectorySeparatorChar),
                file));
        // Ruling 5: a PNG fallback is a catalog change only.
        return file.EndsWith(".png", StringComparison.Ordinal)
            ? new BitmapImage(uri)
            : new SvgImageSource(uri);
    }
}
```

If `AccessibilityView` resolves from `Microsoft.UI.Xaml.Automation.Peers`
and that `using` is unused otherwise, keep it. If the build says it's
unused, remove it.

- [ ] **Step 2: Name the hosts and add the card in XAML**

In `ShellWindow.xaml`:

- **Title bar:** remove the `<TitleBar.IconSource>` block. The code sets it.
- **Library item:** remove `Icon="Home"` from `LibraryItem`. The code sets
  the icon.
- **Empty states:** replace each empty-state `FontIcon` with a glyph and
  image pair. They keep the accent foreground and the 24 px size (ruling
  6). `LibraryNoResultsState`:

```xml
                            <Grid HorizontalAlignment="Left">
                                <FontIcon x:Name="LibraryNoResultsGlyph"
                                          FontSize="24"
                                          Foreground="{ThemeResource DesktopGuidesAccentBrush}"
                                          AutomationProperties.AccessibilityView="Raw" />
                                <ImageIcon x:Name="LibraryNoResultsImage"
                                           Width="24"
                                           Height="24"
                                           Visibility="Collapsed"
                                           AutomationProperties.AccessibilityView="Raw" />
                            </Grid>
```

  and `LibraryEmptyState` the same, with `LibraryEmptyGlyph` and
  `LibraryEmptyImage`.
- **Card headers:** remove the `HeaderIcon` blocks from
  `AppThemeSettingsCard`, `WindowMaterialSettingsCard`,
  `ExportSettingsCard` and `RestoreSettingsExpander`. The code sets them.
- **The new card:** directly after `WindowMaterialSettingsCard`'s closing
  tag, add:

```xml
                        <toolkit:SettingsCard
                            x:Name="ColorfulIconsSettingsCard"
                            HorizontalAlignment="Stretch"
                            AutomationProperties.AutomationId="ColorfulIconsSettingsCard">
                            <toolkit:SettingsCard.HeaderIcon>
                                <!-- Ruling 11: this card's own icon is always a glyph. -->
                                <FontIcon Glyph="&#xE76E;"
                                          AutomationProperties.AccessibilityView="Raw" />
                            </toolkit:SettingsCard.HeaderIcon>
                            <ToggleSwitch x:Name="ColorfulIconsToggle"
                                          IsEnabled="False"
                                          Toggled="ColorfulIconsToggled"
                                          AutomationProperties.AutomationId="ColorfulIconsToggle" />
                        </toolkit:SettingsCard>
```

In `ProviderSettingsCard.xaml`, remove the expander's `HeaderIcon` block.
In `ProviderSettingsCard.xaml.cs`, add:

```csharp
    // T11.5: the shell sets this card's icon with the others.
    internal void SetHeaderIcon(IconElement icon) => ProviderSettingsExpander.HeaderIcon = icon;
```

- [ ] **Step 3: Add the Icons partial**

```csharp
// src/DesktopGuides.Production/ShellWindow.Icons.cs
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T11.5: the choice on screen, and the last one storage accepted.
    private bool colorfulIcons;
    private bool committedColorfulIcons;
    private bool iconsReady;
    private bool applyingIconToggle;
    private readonly SemaphoreSlim iconSaveGate = new(1, 1);

    private void InitializeIcons()
    {
        ColorfulIconsSettingsCard.Header = ColorfulIconsPresentation.Header;
        AutomationProperties.SetName(ColorfulIconsToggle, ColorfulIconsPresentation.ToggleName);
        // The built-in Settings item exists only once the template has loaded.
        Navigation.Loaded += (_, _) => ApplyIcons();
        ApplyIcons();
    }

    // Sets every host from the catalog; high contrast always gets glyphs.
    private void ApplyIcons()
    {
        bool highContrast = themeSettings?.HighContrast == true;
        int images = 0;
        int glyphs = 0;
        AppIconChoice Pick(AppIcon icon)
        {
            AppIconChoice choice = AppIconCatalog.For(icon, colorfulIcons, highContrast);
            if (choice.IsAsset) images++; else glyphs++;
            return choice;
        }

        LibraryItem.Icon = AppIcons.Element(Pick(AppIcon.NavLibrary));
        if (Navigation.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Icon = AppIcons.Element(Pick(AppIcon.NavSettings));
        }
        AppTitleBar.IconSource = AppIcons.Source(Pick(AppIcon.TitleBar));
        ShowEmptyStateIcon(LibraryEmptyGlyph, LibraryEmptyImage, Pick(AppIcon.EmptyLibrary));
        ShowEmptyStateIcon(LibraryNoResultsGlyph, LibraryNoResultsImage, Pick(AppIcon.EmptySearch));
        AppThemeSettingsCard.HeaderIcon = AppIcons.Element(Pick(AppIcon.ThemeCard));
        WindowMaterialSettingsCard.HeaderIcon = AppIcons.Element(Pick(AppIcon.MaterialCard));
        ExportSettingsCard.HeaderIcon = AppIcons.Element(Pick(AppIcon.BackupCard));
        RestoreSettingsExpander.HeaderIcon = AppIcons.Element(Pick(AppIcon.RestoreCard));
        ProviderSettings.SetHeaderIcon(AppIcons.Element(Pick(AppIcon.ProvidersCard)));

        applyingIconToggle = true;
        ColorfulIconsToggle.IsOn = colorfulIcons;
        applyingIconToggle = false;
        ColorfulIconsToggle.IsEnabled = ColorfulIconsPresentation.ToggleEnabled(iconsReady, highContrast);
        ColorfulIconsSettingsCard.Description = ColorfulIconsPresentation.CardDescription(highContrast);
        AutomationProperties.SetName(
            ColorfulIconsSettingsCard, ColorfulIconsPresentation.CardName(highContrast));
        // Ruling 10: the hosts are Raw, so the smoke reads what was set here.
        AutomationProperties.SetItemStatus(
            ColorfulIconsToggle, ColorfulIconsPresentation.Status(images, glyphs));
    }

    private static void ShowEmptyStateIcon(FontIcon glyph, ImageIcon image, AppIconChoice choice)
    {
        if (choice.IsAsset)
        {
            image.Source = AppIcons.Image(choice.AssetFile!);
        }
        else
        {
            glyph.Glyph = choice.Glyph!;
        }
        glyph.Visibility = choice.IsAsset ? Visibility.Collapsed : Visibility.Visible;
        image.Visibility = choice.IsAsset ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ColorfulIconsToggled(object sender, RoutedEventArgs e)
    {
        if (applyingIconToggle || repository is null)
        {
            return;
        }
        bool requested = ColorfulIconsToggle.IsOn;
        if (requested == colorfulIcons)
        {
            return;
        }
        colorfulIcons = requested;
        ApplyIcons();
        // Saves commit in the order the user chose, as the theme's do.
        await iconSaveGate.WaitAsync();
        try
        {
            await repository.UpdateSettingsAsync(s => s with { ColorfulIcons = requested });
            committedColorfulIcons = requested;
            if (requested == colorfulIcons)
            {
                AnnounceStatus(ColorfulIconsPresentation.Saved(requested));
            }
        }
        catch (Exception error)
        {
            // A newer choice is already on screen; its own save decides.
            if (requested == colorfulIcons)
            {
                colorfulIcons = committedColorfulIcons;
                ApplyIcons();
            }
            ShowErrorStatus(ColorfulIconsPresentation.SaveFailed(error.Message));
        }
        finally
        {
            iconSaveGate.Release();
        }
    }
}
```

- [ ] **Step 4: Wire startup, the settings load and high contrast**

In the `ShellWindow` constructor, directly after
`ApplyTheme(ThemePreference.System);`:

```csharp
        InitializeIcons();
```

In the settings load, directly after `ApplyWindowMaterial(requestedMaterial);`:

```csharp
            colorfulIcons = committedColorfulIcons = settings?.ColorfulIcons ?? false;
            iconsReady = true;
            ApplyIcons();
```

In `ShellWindow.Theme.cs`, `InitializeThemeChoice`, replace the
`themeSettings.Changed` handler with:

```csharp
        themeSettings.Changed += (_, _) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyTheme(requestedTheme);
                // T11.5: high contrast forces glyphs, and turning it off restores the choice.
                ApplyIcons();
            });
```

- [ ] **Step 5: Build**

Run: Production build (host commands).
Expected: build succeeds with 0 warnings and 0 errors. Search the build
output for `warning` and expect none from the changed files.

- [ ] **Step 6: Self-check against the theme pattern**

Compare `ColorfulIconsToggled` with `AppThemeSelectionChanged` line by
line:
- The save is serialised.
- Only the latest choice reverts.
- A failed save shows `ShowErrorStatus`.

Confirm:
- Every `x:Name` that `ApplyIcons()` uses exists in the XAML.
- `ProviderSettings` is the provider card's `x:Name` in
  `ShellWindow.xaml`.
- No `Icon="Home"` and no `TitleBar.IconSource` block is left.

- [ ] **Step 7: Commit**

```bash
git add src/DesktopGuides.Production
git commit -m "feat(shell): add the T11.5 Colorful icons setting"
```

### Task 5: The installed icons smoke, screenshots and the rendering check

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (mode `icons`, parameters, `design-language` order checks)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-ShellSmoke` parameters, `Run-IconScenarios` in the `design` group)
- Create (only if ruling 5's fallback is needed): `src/DesktopGuides.Production/Assets/Icons/Color/<name>.png`

**Interfaces:**
- Consumes: Task 4's AutomationIds, `ItemStatus` and status messages;
  Task 2's `set-colorful-icons`.
- Produces:
  - smoke mode `icons` with `-ExpectedIcons On|Off`, `-SwitchIcons On|Off`
    and `-EmptyLibrary`;
  - screenshots `icons-<On|Off>-settings.png` and
    `icons-<On|Off>-library.png` per result;
  - the report key `icons`.

- [ ] **Step 1: Add the smoke mode (RED)**

In `windows_shell_ui_smoke.ps1`:
- Add `'icons'` to the `-Mode` `ValidateSet`.
- After `$SwitchToMaterial`, add:

```powershell
    [ValidateSet('', 'On', 'Off')]
    [string] $ExpectedIcons = '',
    [ValidateSet('', 'On', 'Off')]
    [string] $SwitchIcons = '',
    [switch] $EmptyLibrary,
```

- In the `design-language` mode, replace
  `Assert-Below 'SettingsLibraryHeading' 'WindowMaterialSettingsCard'` with:

```powershell
        Assert-Below 'ColorfulIconsSettingsCard' 'WindowMaterialSettingsCard'
        Assert-Below 'SettingsLibraryHeading' 'ColorfulIconsSettingsCard'
```

- After the `material` mode's branch, add:

```powershell
    elseif ($Mode -eq 'icons') {
        # T11.5: the toggle, the icons the app set, and the saved choice.
        # How WinUI draws the SVGs is reviewed on the screenshots.
        function Get-IconsToggle {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $toggle = Show-SettingsCard 'ColorfulIconsToggle'
                if ($toggle.Current.IsEnabled) { return $toggle }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw 'Expected the Colorful icons toggle to be enabled once the settings load.'
        }

        function Assert-Icons([string] $state) {
            $toggle = Get-IconsToggle
            $pattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            $toggleState = $pattern.Current.ToggleState.ToString()
            if ($toggleState -ne $state) {
                throw "Expected the Colorful icons toggle $state, found $toggleState."
            }
            $expected = if ($state -eq 'On') { 'images:10 glyphs:0' } else { 'images:0 glyphs:10' }
            $deadline = (Get-Date).AddSeconds(10)
            do {
                $status = (Find-ById 'ColorfulIconsToggle').Current.ItemStatus
                if ($status -ceq $expected) { return }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            throw "Expected the applied icons '$expected', found '$status'."
        }

        # Raw icons add no element to the control view.
        function Assert-NoControlViewImage([string] $id) {
            $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
            $queue = [System.Collections.Generic.Queue[object]]::new()
            $queue.Enqueue((Wait-VisibleById $id))
            while ($queue.Count -gt 0) {
                $child = $walker.GetFirstChild($queue.Dequeue())
                while ($child) {
                    if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Image) {
                        throw "Expected no Image element under $id; the icon must stay Raw."
                    }
                    $queue.Enqueue($child)
                    $child = $walker.GetNextSibling($child)
                }
            }
        }

        Resize-ShellWindow 1500 720
        Select-Element 'Settings'
        [void](Wait-Name 'SettingsHeading' 'Settings')
        [void](Wait-Name 'ColorfulIconsSettingsCard' (
            'Colorful icons. Show navigation and Settings icons in color.'))
        [void](Wait-Name 'ColorfulIconsToggle' 'Colorful icons')
        Assert-Icons $ExpectedIcons
        $report.phases += "icons-$ExpectedIcons-restored"
        $state = $ExpectedIcons

        if ($SwitchIcons) {
            $toggle = Get-IconsToggle
            $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
            $message = if ($SwitchIcons -eq 'On') { 'Colorful icons turned on.' } else { 'Colorful icons turned off.' }
            [void](Wait-Status $message)
            Assert-Icons $SwitchIcons
            $state = $SwitchIcons
            $report.phases += "icons-switched-$state"
        }

        [void](Wait-Name 'LibraryNavigation' 'Library')
        Assert-NoControlViewImage 'LibraryNavigation'
        [void](Wait-HiddenById 'ShellStatus')
        Assert-ShellForeground
        [void](Show-SettingsCard 'AppThemeSettingsCard')
        $report.settingsScreenshot = Save-WindowScreenshot "icons-$state-settings"

        Select-Element 'Library'
        if ($EmptyLibrary) { [void](Wait-VisibleById 'LibraryEmptyState') }
        else { [void](Wait-VisibleById 'LibraryHeading') }
        Start-Sleep -Milliseconds 400
        Assert-ShellForeground
        $report.libraryScreenshot = Save-WindowScreenshot "icons-$state-library"
        $report.icons = $state
        $report.phases += "icons-$state-captured"
    }
```

Check that every helper used exists with that signature and scope in the
smoke script: `Show-SettingsCard`, `Find-ById`, `Wait-Name`,
`Wait-VisibleById`, `Wait-HiddenById`, `Wait-Status`, `Select-Element`,
`Resize-ShellWindow`, `Assert-ShellForeground` and
`Save-WindowScreenshot`. If `LibraryHeading` isn't the library page's
AutomationId, use the id that the `material` mode waits on. Record any
substitution as a ruling.

- [ ] **Step 2: Add the installed scenarios**

In `windows_shell_install.ps1`:
- Add these parameters to `Run-ShellSmoke` after `$SwitchToMaterial`:
  `[string] $ExpectedIcons = ''`, `[string] $SwitchIcons = ''` and
  `[switch] $EmptyLibrary`.
- Pass them on after the `$SwitchToMaterial` argument:

```powershell
    if ($ExpectedIcons) { $arguments += ' -ExpectedIcons ' + $ExpectedIcons }
    if ($SwitchIcons) { $arguments += ' -SwitchIcons ' + $SwitchIcons }
    if ($EmptyLibrary) { $arguments += ' -EmptyLibrary' }
```

- After `Run-MaterialScenarios`, add:

```powershell
function Set-StoredColorfulIcons([bool] $on) {
    $value = if ($on) { 'true' } else { 'false' }
    dotnet $seedDll set-colorful-icons $dataRoot $value
    if ($LASTEXITCODE -ne 0) { throw "Could not store colorful icons $value." }
}

# T11.5: the toggle, the applied icons and the stored choice are app code.
# How the SVGs render is reviewed on the screenshots.
function Run-IconScenarios {
    $originalTheme = Get-AppThemePreference
    $report.icons = [ordered]@{}
    try {
        Set-AppThemePreference $true
        Set-StoredColorfulIcons $false
        Start-InstalledShell
        $report.icons['light-switch-on'] = Run-ShellSmoke 'icons' `
            -ResultName 'icons-light-switch-on' -ExpectedIcons 'Off' -SwitchIcons 'On'
        Close-InstalledShell
        Start-InstalledShell
        $report.icons['light-persisted'] = Run-ShellSmoke 'icons' `
            -ResultName 'icons-light-persisted' -ExpectedIcons 'On' -SwitchIcons 'Off'
        Close-InstalledShell

        Set-AppThemePreference $false
        Set-StoredColorfulIcons $true
        Start-InstalledShell
        $report.icons['dark-on'] = Run-ShellSmoke 'icons' `
            -ResultName 'icons-dark-on' -ExpectedIcons 'On'
        Close-InstalledShell

        # Last in the design group: the empty library needs an empty data root.
        Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
        Set-AppThemePreference $true
        Set-StoredColorfulIcons $true
        Start-InstalledShell
        $report.icons['light-empty-on'] = Run-ShellSmoke 'icons' `
            -ResultName 'icons-light-empty' -ExpectedIcons 'On' -EmptyLibrary
        Close-InstalledShell
    }
    finally {
        Set-StoredColorfulIcons $false
        Restore-AppThemePreference $originalTheme
        $report.restoredAppThemeAfterIcons = Get-AppThemePreference
    }
}
```

- In the `design` group, call `Run-IconScenarios` after
  `Run-ReaderDesignScenarios`, so it is last.

Run: `powershell -NoProfile -Command "[System.Management.Automation.Language.Parser]::ParseFile('tools\p1\windows_shell_ui_smoke.ps1', [ref]$null, [ref]$e) | Out-Null; $e"`
on the host for both scripts, plus a non-ASCII check
(`grep -nP '[^\x00-\x7F]' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1`).
Expected: no parse errors and no non-ASCII lines.

- [ ] **Step 3: Commit and run the design group in CI**

```bash
git add tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): add the T11.5 installed icons smoke"
git push -u origin feat/p1-t11-5-colorful-icons
```

Run: `gh workflow run windows-ci.yml --repo ilya-slalom/desktop-guides --ref feat/p1-t11-5-colorful-icons -f shell-scope=design`,
then watch it with `gh run watch`.
Expected:
- The `design` shard passes, including `design-language` with the new
  order checks.
- Its report has four `icons` results with the `icons-*-captured` phases.
- The `restoredAppThemeAfterIcons` value matches the original.

If it fails, use superpowers:systematic-debugging on the result JSON from
the run's artifacts before changing anything.

- [ ] **Step 4: The rendering check (ruling 5)**

Download the run's screenshot artifacts. Open every `icons-On-*` and
`icons-Off-*` screenshot. For each of the ten places with colour on (light
and dark), check:
- The icon is drawn in colour, with no blank area, black box or missing
  gradient.
- Its size matches the glyph it replaces.
- It has at least 3:1 contrast against its background (Visual direction).

The empty-search icon only appears after a search with no results, so
check `ic_fluent_search_sparkle_24_color.svg` by searching for a
non-matching term in the pcsx2-win run (Step 5).

For any icon that renders wrongly:
- Rasterise its SVG to `ic_fluent_<name>_24_color.png` at 48×48 px. One
  2× image covers both scales; `ImageIcon` scales it down.
- Swap the catalog entry's `.svg` for `.png` in `AppIconCatalog.cs` and
  in Task 1's test data.
- Delete the SVG, and update `SOURCE.md`'s table and its "PNG fallbacks"
  line.
- Rerun the Core tests and the design group.

Record the result, either "all nine SVGs render" or the list of
fallbacks, in the ledger.

- [ ] **Step 5: The pcsx2-win run and the portable check**

Follow `docs/p1/e2e-testing.md`, including its data-backup and cleanup
rules, to run the installed `design` group on pcsx2-win through an
interactive scheduled task. If the run needs an elevated scheduled task,
stop and ask.

In the same interactive session:
- With colour on, search the library for `zzzz` and take a screenshot of
  the empty-search state.
- Start the portable exe from the T17.1 portable build of this branch, as
  `package_portable_release.ps1` builds it, with colour on. Take a
  screenshot of the navigation and Settings.
- If the portable exe would use the user's real library rather than a
  scratch data root, back it up first as the e2e doc requires. If the doc
  has no procedure for that, stop and ask before starting the exe.

Expected: the portable exe shows colour icons, not blank space. If it
shows blank space, the `AppContext.BaseDirectory` URI (ruling 3) is wrong:
debug with superpowers:systematic-debugging.

Copy into `docs/p1/evidence/t11-5-colorful-icons/`:
- `icons-light-on-library.png`, `icons-light-on-settings.png`,
  `icons-dark-on-library.png` and `icons-dark-on-settings.png`;
- `icons-light-empty-library.png` and `icons-off-settings.png`;
- `icons-empty-search.png` and `icons-portable.png`.

Rename them from the run's names as listed. Commit:

```bash
git add docs/p1/evidence/t11-5-colorful-icons
git commit -m "docs(p1): add the T11.5 icon screenshots"
```

### Task 6: Verification record, status pages and the PR

**Files:**
- Modify: `docs/p1/t11-5-colorful-icons-design.md` (status line, a new `## T11.5 verification record`)
- Modify: `docs/p1/implementation-plan.md` (status paragraph)
- Modify: `docs/progress.md` (a T11.5 row; the P1 row's open list)
- Modify: `docs/p1/release-packaging.md` (the notice is in both builds)

- [ ] **Step 1: Record the verification**

Add `## T11.5 verification record` to the end of the design. List:
- the Core and Infrastructure test counts from the host run;
- the package verifier result;
- the CI run IDs (the `design` dispatch and the full PR run), and any
  reruns with their cause;
- the rendering check result (Task 5 Step 4);
- the pcsx2-win run and the portable check (Task 5 Step 5);
- the total asset size (20,355 bytes, or the new total if PNGs replaced
  any SVG);
- rulings 1 to 11 from this plan, one line each;
- anything not run, including the real high-contrast screenshot, which is
  deferred to T16.2.

Embed the eight evidence screenshots.

- [ ] **Step 2: Update the status pages**

- **Design status line:** "Status: in review in PR #<n>."
- **`implementation-plan.md`:** in the status paragraph, change "M2 still
  needs the optional T11.5" to "T11.5 (colorful icons) is in review, the
  last M2 task". Then add a T11.5 sentence linking
  `t11-5-colorful-icons-design.md#t115-verification-record`.
- **`progress.md`:**
  - Add a `P1 T11.5 colorful icons` row: "In review: PR #<n>", with a
    one-line summary.
  - In the P1 row, change "M2 needs the optional T11.5" to "M2's T11.5 is
    in review".
- **`release-packaging.md`:** where the package contents or the release
  notes are described, add one sentence. Both the MSIX and the portable
  exe carry `THIRD-PARTY-NOTICES.txt` for the Fluent UI System Icons
  (MIT). If the doc has no contents section, add it to the release notes
  checklist instead.

Run: `git diff --check`
Expected: no output.

```bash
git add docs
git commit -m "docs(p1): record the T11.5 verification"
```

- [ ] **Step 3: Open the PR**

Push and open the PR against `main`. The body:
- names the target task: T11.5, colorful icons, the last M2 task;
- gives the prerequisites and their status: T11.4 and T14.4 (#54), both
  merged;
- states the intended outcome: an optional, off-by-default Settings toggle
  that swaps the navigation, title bar, empty-state and Settings card
  icons for the Fluent UI System Icons colour set, kept monochrome in high
  contrast, with the MIT notice packaged;
- embeds the light and dark Settings and Library screenshots;
- lists the rulings;
- ends with the Claude Code line.

Run the full PR CI and expect it green before marking the PR ready.

## Traceability

| Requirement | Where |
| --- | --- |
| T11.5 icon set | Task 1 (catalog, assets), Task 5 (rendering check) |
| T11.5 licence notice | Task 1 (`THIRD-PARTY-NOTICES.txt`), Task 3 (packaged, verified) |
| T11.5 asset size | Task 1 (`SOURCE.md`), Task 6 (record) |
| T11.5 theme screenshots | Task 5 (light and dark, on and off) |
| T11.5 high contrast | Task 1 (catalog and toggle rules), Task 4 (wiring); real screenshot in T16.2 (ruling 4) |
| T11.5 UIA names | Task 5 (`Wait-Name`, `Assert-NoControlViewImage`) |
| TR11.3 shared semantic resources | Task 4 (empty states keep `DesktopGuidesAccentBrush`; card glyphs use the theme's foreground) |

## PR outcome

One PR, `feat/p1-t11-5-colorful-icons`. The toggle is off by default, so
nobody sees a change until they turn it on. Merging it completes M2, and
T16.2 can then audit the final icons.
