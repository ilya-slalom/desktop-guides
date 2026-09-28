# T11.4 design language implementation plan

Status: implementation and available Windows 11 x64 verification complete,
28 September 2026; merge pending. T11.1 and T11.3 are merged. The user
deferred 200% display-scaling verification to the later accessibility audit.
This task precedes T04.4 provider-backed game addition.

## Assumptions and choice

Desktop Guides should feel like a quiet field guide used beside a game. Game
artwork and identity will carry visual personality once T04.4 supplies them;
the shell, guide lists, and reader remain restrained so written content stays
dominant. The production package is locked to Windows App SDK 2.5.1 and must
retain Windows 10-compatible fallback behavior even though Windows 10 testing
is deferred.

Three practical approaches were considered:

| Approach | Benefit | Cost or risk | Decision |
| --- | --- | --- | --- |
| Copy the WinUI Gallery shell and resource set | Fastest route to a visibly polished sample | Gallery main uses an experimental Windows App SDK build, Toolkit packages, sample-specific navigation, and controls Desktop Guides does not need | Reject |
| Add every Windows Community Toolkit package now | Makes the catalog immediately available | Adds unused assemblies, package updates, and test surface without a user workflow | Reject |
| Build semantic app resources and add a stable Toolkit package with its first suitable component | Preserves native behavior while allowing purpose-built controls where they improve a recorded workflow | Each introduced package needs an installed Windows regression check | Choose |

The WinUI Gallery review uses Microsoft revision
`7614c0083cc7fe33f5473603bb745a222abaef27`. Its app-level resource aliases,
heading styles, adaptive item layouts, `NavigationView`, `InfoBar`, and native
control samples are suitable references. T11.4 pins
`CommunityToolkit.WinUI.Controls.SettingsControls` `8.2.251219` and uses
`SettingsCard` for the representative local-storage setting. Later tasks add
`MetadataControl`, `HeaderedContentControl`, `Segmented`, `GridSplitter`, or
`RichSuggestBox` only at the mapped workflow and after package-specific
installed checks. Experimental SDK and Toolkit preview controls remain
outside P1. The selected package is MIT licensed and its dependency floor is
below the app's locked Windows App SDK `2.5.1`; no SDK change is required.

## Design language

### Color and surfaces

Semantic brushes alias WinUI theme resources so system accent, dark mode, and
high contrast remain native:

| Role | WinUI source |
| --- | --- |
| Canvas | `SolidBackgroundFillColorBaseBrush` |
| Surface | `LayerFillColorDefaultBrush` |
| Elevated surface | `CardBackgroundFillColorDefaultBrush` |
| Reading surface | `SolidBackgroundFillColorQuarternaryBrush` |
| Border | `CardStrokeColorDefaultBrush` |
| Primary and secondary text | `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush` |
| Accent and text on accent | `AccentFillColorDefaultBrush`, `TextOnAccentFillColorPrimaryBrush` |
| Success, warning, destructive | WinUI system fill brushes |

High contrast overrides the app aliases with Windows system window, text,
highlight, and hotlight colors. No page owns a fixed foreground/background
pair. The window backdrop runs unbroken behind the title bar, navigation pane,
and route background; the `NavigationView` content layer and its border are
transparent. A Settings choice selects Mica (default), Thin Acrylic, or Solid.
Unsupported materials fall back to Solid with a warning. The backdrop follows
the app theme and turns solid while the window is inactive. Reading surfaces,
cards, and dialogs stay opaque or card-filled so text never sits directly on
the wallpaper. In Acrylic mode, `ContentDialog` uses in-app acrylic; in Mica
and Solid it keeps the default solid dialog.

### Type

Use the WinUI system type family, which selects Segoe UI Variable where
available and the system UI fallback elsewhere. The scale is based on native
`TitleTextBlockStyle`, `SubtitleTextBlockStyle`, `BodyTextBlockStyle`,
`BodyStrongTextBlockStyle`, and `CaptionTextBlockStyle`. T11.4 adds semantic
page-title, section-title, metadata, empty-state, and status styles with
heading levels. Monospace remains exclusive to the TXT reader.

### Layout and shape

Use 4, 8, 12, 16, 24, 32, and 48-DIP spacing steps. Page content is
left-aligned and fills the route width after responsive padding. Do not apply a
shell-level maximum that creates unused vertical gutters on wide windows.
Padding reduces from 48/32 to 32/24 and then 16/24/16/16 at narrow widths. The
native `TitleBar` owns Back and pane-toggle controls in a separate row, so
route content does not need a compensating top inset. Eight-DIP corners mark
meaningful surfaces; guide lists remain flat rather than turning every row
into a card. Motion is limited to native control state and layout transitions.

```text
Wide Library
┌ navigation ┬────────────────────────────────────────────┐
│ Library    │ Library                         [Add game] │
│            │ Context sentence                           │
│ Settings   │                                            │
│            │ game list / directed empty state           │
└────────────┴────────────────────────────────────────────┘

Narrow Reader
┌──────────────────────────────────────┐
│ [Back to game]                       │
│ Guide title                          │
│ Game title · format                  │
│ reader commands / overflow           │
├──────────────────────────────────────┤
│ guide content                        │
└──────────────────────────────────────┘
```

The memorable element is the future game artwork in catalog views. Until that
data exists, the shell uses hierarchy and spacing rather than decorative
gradients or repeated cards.

### Copy

Use sentence case and concrete actions: `Add game`, `Edit game`, `Back to
game`, and `Open selected guide`. Empty states explain the next action.
Status text reports state without fixture terminology. Error text names the
failed operation and offers one recovery action when one exists. Loading uses
a top `InfoBar`; routine ready messages close after three seconds, while
warnings and errors remain dismissible.

## Implementation sequence and exits

| Step | Output | Verification |
| --- | --- | --- |
| Resource foundation | App-level dictionaries for semantic colors, spacing, typography, surfaces, buttons, lists, backdrop, title bar, and status presentation | Production XAML compiles on Windows x64 and ARM64; every app resource resolves in light, dark, and high contrast |
| Representative routes | Library, Game, Reader, Settings, game editor, and reader toolbar consume semantic styles; the Settings route uses Toolkit `SettingsCard`; native `TitleBar`, Mica, full-width layout, transient `InfoBar`, adaptive page padding, and narrow layouts are active | Existing route, editor, title-bar navigation, focus, Settings-card UIA, status-timeout, and toolbar checks remain passing |
| Gallery inventory | Built-in, adapted, deferred, and rejected component list tied to the locked SDK | No Gallery application dependency; stable Toolkit packages are centrally pinned when first used |
| Installed visual check | Seeded realistic metadata is captured at wide and narrow sizes in system, light, and dark, and in each window material; high contrast moves to T16.2 | Keyboard focus, heading names, long text, and screenshots are recorded on Windows 11 x64 at the host's current 100% display scale; 200% display scaling is explicitly deferred |

The implementation PR targets **T11.4**. Prerequisites T11.1 and T11.3 are
merged. Its outcome is a reviewed visual and resource foundation that unblocks
T04.4 without pre-building provider, catalog, import, or final Settings
components.

## Verification record

The Windows 11 x64 host built both x64 and ARM64 production packages. The x64
package passed the complete interactive signed-install shell regression plus
the design-language scenarios in system, light, dark, and high-contrast
themes. Each appearance run covered Library, Game, Reader, and Settings at
wide and narrow window sizes, heading semantics, keyboard focus, UIA names,
the native title-bar controls, full-width shell bounds, automatic dismissal of
routine status, the Toolkit `SettingsCard` name and bounds, long metadata, and
control overlap. The host reported 96 DPI / 100% scaling.

The same source passed 73 Core tests, 90 Infrastructure tests, seven
PowerShell harness checks, x64 and ARM64 package builds, and the complete shell
installed regression. The unchanged linked reader toolbar retains its prior
installed pass. The final exact-source installed run passed the complete
workflow and all four appearance modes. The sanitized
[Windows result](evidence/t11-design-language/windows-11-x64-result.json) and
selected installed screenshots are retained with the task evidence. Windows
10, installed ARM64 behavior, and 200% display scaling remain unverified.

The window-material follow-up replaced the high-contrast pass, which rewrote
the host's active Windows theme, with material passes; high contrast moves to
T16.2. The design-only installed run covered system, light, and dark, then
Solid, Acrylic, and Mica in light and dark, plus a Mica-to-Acrylic switch that
persisted across relaunch. An 80×8 strip across the pane/content boundary had
a channel range of 0 in every material, so no seam shows. Acrylic differed from
Solid by 32 (light) and 52 (dark) channel levels against a threshold of 4,
which also confirms that dark-theme acrylic now renders. The Acrylic dialog's
command area differed from the Solid dialog by 6 (light) and 12 (dark) against
a threshold of 2; its content area keeps WinUI's native overlay, so no template
resource is overridden. Mica matched Solid on this host's neutral wallpaper,
which the harness does not assert. The same source passed 73 Core tests, 98
Infrastructure tests, eight PowerShell harness checks, x64 and ARM64 package
builds, and the linked reader toolbar installed smoke.
