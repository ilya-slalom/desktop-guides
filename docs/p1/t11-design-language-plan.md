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
| Add Windows Community Toolkit settings and layout controls now | Provides polished settings cards and helper controls | Adds package, license, version, and installed-runtime surface before Settings behavior exists | Defer Toolkit-only controls to T14.4 |
| Build semantic app resources over native WinUI controls | Matches the locked SDK, preserves native input/accessibility behavior, and lets each feature adopt only what it needs | Requires a small resource and layout foundation now | Choose |

The WinUI Gallery review uses Microsoft revision
`7614c0083cc7fe33f5473603bb745a222abaef27`. Its app-level resource aliases,
heading styles, adaptive item layouts, `NavigationView`, `InfoBar`, and native
control samples are suitable references. Its Toolkit `SettingsCard`,
`SettingsExpander`, animation helpers, and experimental-SDK-only controls are
not T11.4 dependencies.

## Design language

### Color and surfaces

Semantic brushes alias WinUI theme resources so system accent, dark mode, and
high contrast remain native:

| Role | WinUI source |
| --- | --- |
| Canvas | `SolidBackgroundFillColorBaseBrush` |
| Surface | `LayerFillColorDefaultBrush` |
| Elevated surface | `CardBackgroundFillColorDefaultBrush` |
| Border | `CardStrokeColorDefaultBrush` |
| Primary and secondary text | `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush` |
| Accent and text on accent | `AccentFillColorDefaultBrush`, `TextOnAccentFillColorPrimaryBrush` |
| Success, warning, destructive | WinUI system fill brushes |

High contrast overrides the app aliases with Windows system window, text,
highlight, and hotlight colors. No page owns a fixed foreground/background
pair.

### Type

Use the WinUI system type family, which selects Segoe UI Variable where
available and the system UI fallback elsewhere. The scale is based on native
`TitleTextBlockStyle`, `SubtitleTextBlockStyle`, `BodyTextBlockStyle`,
`BodyStrongTextBlockStyle`, and `CaptionTextBlockStyle`. T11.4 adds semantic
page-title, section-title, metadata, empty-state, and status styles with
heading levels. Monospace remains exclusive to the TXT reader.

### Layout and shape

Use 4, 8, 12, 16, 24, 32, and 48-DIP spacing steps. Page content is
left-aligned and capped at 1,120 DIPs on wide windows. Padding reduces from
48/32 to 32/24. Narrow layouts use 16-DIP side and bottom padding plus a
64-DIP top inset that keeps content clear of compact NavigationView controls.
Eight-DIP corners mark meaningful surfaces; guide lists remain flat rather
than turning every row into a card. Motion is limited to native control state
and layout transitions.

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
failed operation and offers one recovery action when one exists.

## Implementation sequence and exits

| Step | Output | Verification |
| --- | --- | --- |
| Resource foundation | App-level dictionaries for semantic colors, spacing, typography, surfaces, buttons, lists, and status presentation | Production XAML compiles on Windows x64 and ARM64; every app resource resolves in light, dark, and high contrast |
| Representative routes | Library, Game, Reader, Settings, game editor, and reader toolbar consume semantic styles; adaptive page padding and narrow layouts are active | Existing route, editor, focus, and toolbar UIA checks remain passing |
| Gallery inventory | Built-in, adapted, deferred, and rejected component list tied to the locked SDK | No Gallery application dependency; no Toolkit package added in T11.4 |
| Installed visual check | Seeded realistic metadata is captured at wide and narrow sizes in system light, dark, and high contrast | Keyboard focus, heading names, long text, and screenshots are recorded on Windows 11 x64 at the host's current 100% display scale; 200% display scaling is explicitly deferred |

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
long metadata, and control overlap. The host reported 96 DPI / 100% scaling.

The same source passed 73 Core tests, 90 Infrastructure tests, seven
PowerShell harness checks, and the complete linked reader-toolbar installed
regression. The sanitized
[Windows result](evidence/t11-design-language/windows-11-x64-result.json) and
selected installed screenshots are retained with the task evidence. Windows
10, installed ARM64 behavior, and 200% display scaling remain unverified.
