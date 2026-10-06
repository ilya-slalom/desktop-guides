# T09.2 HTML theme style design

Status: implemented; see Verification.
Prerequisites:
- T09.1 (the managed-guide HTML adapter, PR #35) is merged.
- T14.2 (the app theme setting, PR #48) is merged.
- T09.3 (HTML capture and restore, PR #39) is merged too. Its DevTools
  `Runtime.evaluate` channel works while page scripts are disabled, and
  this task reuses it.

## Intent

HTML guides render as authored today, whatever the app theme, so a Dark
app shows a white page. T09.2 adds a fixed style that the app owns. It
follows the applied theme (Light, Dark or Windows high contrast) and the
guide's stored text scale. The style is local and fixed: a theme or scale
change adds no network request, no cross-guide request and no CSS
resource outside the guide root.

Success means:

- **Dark:** an HTML guide opens with a dark page and light text, and
  never shows a white frame first. Its images are unchanged.
- **Light:** a guide keeps its own colors. An unstyled page gets a white
  page with black text.
- **High contrast:** with Windows high contrast on, the page uses the
  Windows high-contrast colors.
- **Changing the theme while reading:** changing the app theme, or the
  Windows theme while the app follows System, restyles the open guide at
  once. The page doesn't reload and the reading place doesn't move.
- **Text scale:** the guide's stored `ReaderPreferences.TextScale`
  (0.75–2.0, default 1.0) scales its text at open, before the saved place
  is restored.
- **Offline and contained:** with the network unavailable, a theme change
  adds no served path, no denied request and no non-loopback connection.
  Fonts stay blocked.

Traces: the T09.2 row of [implementation-plan.md](implementation-plan.md)
(TR09.3, TR14.2), [work-breakdown.md](../work-breakdown.md) S09 T09.2, and
[p1-technical-design.md](../p1-technical-design.md) §8 S09 T09.2.

## Decisions

Decisions made during brainstorming:

- **The scale lives in the style; no controls yet.**
  - The style takes a scale and applies the guide's stored
    `ReaderPreferences.TextScale`. The smoke tests it with a seeded value.
  - T14.1 adds the Smaller and Larger controls, saving, and changing the
    scale while reading. T14.3 adds keeping the reading place across
    those changes.
  - HTML doesn't report the `TextSize` capability until T14.1, so the
    toolbar shows no buttons that do nothing.
- **Light keeps the page's own colors; Dark is forced.**
  - Light only sets a default for pages that set nothing.
  - Dark forces a fixed palette.
  - High contrast is left to Chromium's forced colors.
  - Rejected: forcing both themes, which loses authored colors in Light
    for nothing. Also rejected: a light-touch Dark that only sets
    defaults, which leaves most styled guides white.
- **The style is injected through DevTools.**
  - One fixed `<style>` element goes into the entry document through the
    `Runtime.evaluate` channel T09.3 uses. A change rewrites it in place.
  - There's no new URL, the served bytes are unchanged, and the request
    policy is unchanged. `style-src 'unsafe-inline'` already allows an
    inline style element.
  - Rejected: a virtual stylesheet URL served by the request policy,
    which widens the policy. Also rejected: rewriting the HTML bytes as
    they're served, which changes the bytes the T09.3 locator hashes, and
    needs a reload on every change.
- **Scale through `zoom`, not the root font size.**
  - Guides saved from major sites (GameFAQs-style pages, wikis) mostly
    set `px` font sizes, which ignore the root font size.
  - CSS `zoom` on the root scales them, and the page reflows to the
    window width.
  - WebView2's `ZoomFactor` would do the same through a second channel.
    `zoom` keeps the theme and the scale in one atomic write.

## Core

### `ReaderTheme` and `ReaderAppearance`

`DesktopGuides.Core/Reading/ReaderContract.cs` changes:

```csharp
public enum ReaderTheme { Light, Dark, HighContrast }

public sealed record ReaderAppearance(ReaderTheme Theme, double TextScale);
```

`ReaderAppearance` used to carry the stored `ThemePreference`. Nothing
calls `ApplyAppearanceAsync` yet, so the change touches no caller. The
shell resolves System before calling, so a reader never needs to know the
Windows theme.

TXT and PDF keep their no-op `ApplyAppearanceAsync`. Their native chrome
already follows the root's theme (T14.2).

### `HtmlReaderStyle`

`DesktopGuides.Core/Html/HtmlReaderStyle.cs` is pure and unit-tested:

```csharp
public static class HtmlReaderStyle
{
    public const string ElementId = "desktop-guides-style";
    public const double MinScale = 0.75;
    public const double MaxScale = 2.0;

    public static double ClampScale(double scale);   // NaN or infinity gives 1.0
    public static string Css(ReaderTheme theme, double scale);
    public static string? PageColor(ReaderTheme theme); // "#RRGGBB", or null for HighContrast
}
```

`Css` returns one fixed string, built from constants and the clamped scale
formatted with the invariant culture. No page data enters it.

**Every theme** sets `html { zoom: <scale> !important; }`.

**Light:** every rule is inside `:where(...)`, so its specificity is zero
and any authored rule wins:

```css
:where(html) { color-scheme: light; color: #000000; }
```

It sets no background. A root background would stop a page's `body`
background (`<body bgcolor>` or a `body` rule) from filling the window,
because CSS only carries the body's background to the canvas when the
root has none. An unstyled page shows the view's white page color.

**Dark:**

```css
:root { color-scheme: dark !important; }
html, body { background-color: #1E1E1E !important; color: #E6E6E6 !important; }
body * {
  background-color: transparent !important;
  color: inherit !important;
  border-color: #5A5A5A !important;
  text-shadow: none !important;
}
a:link, a:visited, a:link *, a:visited * { color: #8AB4F8 !important; }
mark { background-color: #5C4B00 !important; }
```

The Dark rules keep these as authored:

- `background-image`, so icon sprites and diagrams stay visible;
- `img`, `svg`, `video` and `canvas`;
- layout, fonts and sizes.

**HighContrast:** no color properties, only the zoom rule. WebView2 applies
Chromium's forced colors from the Windows high-contrast palette.

`PageColor` gives `#FFFFFF` for Light, `#1E1E1E` for Dark and null for
HighContrast. The session sets the WebView's `DefaultBackgroundColor` from
it, so the frame before the first paint already has the right color. With
null, the session leaves WebView2's default, which follows forced colors.

The style uses no `url(`, `@import`, `@font-face` or named font family.
The tests assert this. So no theme or scale can make a request, and
`font-src 'none'` stays as it is.

### `HtmlSessionDiagnostics`

The gated diagnostics (T07.3) gain an appearance record:

- `RecordAppearance(string theme, double scale, HtmlAppliedStyle computed)`
  keeps the last applied theme and scale, the number of applications, and
  what the page computed after the write;
- `RecordAppearanceFailed()` counts failed writes;
- `HtmlAppliedStyle(string BodyBackground, string BodyColor, string RootZoom)`
  holds the computed values;
- `ToJson` adds an `appearance` object with `theme`, `scale`,
  `applications`, `failures`, `bodyBackground`, `bodyColor` and
  `rootZoom`.

## Session: `HtmlReaderSession`

**Opening a guide:**

1. Before `Navigate`, the session sets `DefaultBackgroundColor` from
   `PageColor` and sets the view's `Opacity` to 0.
2. `OpenAsync` takes the first `ReaderAppearance`.
3. After `NavigationCompleted` succeeds and the entry has been served, the
   session writes the style (below).
4. The session sets `Opacity` back to 1, whether or not the write
   succeeded.
5. A failed open still shows the existing error. The view is replaced, so
   its opacity doesn't matter.

**`ApplyAppearanceAsync(appearance, token)`** writes the style through
`RunScriptAsync`. One fixed script creates the element on the first write
and rewrites it after that:

```js
(() => {
  const css = <JSON string>;
  let style = document.querySelector("style#desktop-guides-style");
  if (!style) {
    style = document.createElement("style");
    style.id = "desktop-guides-style";
    (document.head || document.documentElement).appendChild(style);
  }
  style.textContent = css;
  return true;
})()
```

- The CSS goes into the script as a JSON-encoded string literal
  (`JsonSerializer.Serialize`), so no character in it can end the
  literal.
- Static markup could carry the same id. Then `getElementById` can return
  a non-`style` element, and setting its `textContent` would replace page
  content. The script therefore looks the element up with
  `document.querySelector("style#desktop-guides-style")`. A page's own
  `style` element with that id would be rewritten, which costs that page
  only its own rules in that element.
- The session keeps the last appearance and also sets
  `DefaultBackgroundColor` again.
- With diagnostics enabled, a second fixed script reads
  `getComputedStyle` for the body's background and color and for the
  root's zoom, and records them.

**Failures:**

- A failed or rejected write, or a script result other than `true`,
  leaves the page as it is. The session records `RecordAppearanceFailed`
  and doesn't raise `Failed`. The style is cosmetic, and the guide stays
  readable as authored.
- A write before open completes, or after dispose, is ignored, apart from
  storing the appearance for the open.

**Reading place:**

- A theme change only changes colors and doesn't reflow, so it needs no
  capture or restore.
- The scale is set only at open, before the shell's
  `OpenAtSavedPlaceAsync` restores the saved place. The T09.3 locator
  therefore sees the final layout.

The style isn't written to the served bytes, so `contentSha256` and the
locators don't change.

## Shell

`ShellWindow.Theme.cs` gains one resolver:

```csharp
private ReaderTheme ReaderThemeNow() => appliedTheme switch
{
    _ when themeSettings?.HighContrast == true => ReaderTheme.HighContrast,
    AppliedTheme.Light => ReaderTheme.Light,
    AppliedTheme.Dark => ReaderTheme.Dark,
    _ => ShellRoot.ActualTheme == ElementTheme.Dark ? ReaderTheme.Dark : ReaderTheme.Light
};
```

**Opening an HTML guide** (`ShellWindow.HtmlReader.cs`):

- Before `OpenAsync`, the shell reads
  `GetReaderPreferencesAsync(guide.Id)`. A null scale, or a failed read,
  gives 1.0. A failed read doesn't block reading.
- The shell keeps the scale as `htmlTextScale` for the open guide.
- It passes `new ReaderAppearance(ReaderThemeNow(), htmlTextScale)` to
  `OpenAsync`.

**Changes while open.** `ApplyTheme` and the existing
`ShellRoot.ActualThemeChanged` handler both call `RefreshReaderAppearance()`.
That method calls `ApplyAppearanceAsync` on the open session with the
current theme and `htmlTextScale`, and ignores cancellation. Between them,
the two handlers cover:

- a Settings change;
- `ThemeSettings.Changed`, for high contrast;
- a Windows light/dark switch while System is selected.

A refresh that arrives during an open is held by the session (above), and
the open applies the latest appearance.

## Installed checks

**Fixture.** `tests/fixtures/p1/html-theme/` is a small guide with
authored light styling, built like a typical saved walkthrough:

- a local stylesheet sets `body { background: #FFFFFF; color: #222222 }`;
- a table has colored header cells;
- one cell has an inline `style="background-color: #FFFFCC"`;
- there are internal links and a local PNG.

`ShellSeed seed-html-theme` imports it. It takes an optional stored theme
and an optional `ReaderPreferences.TextScale`.

**Modes.** They're added to the `html` group, in the `html` shard, inside
the existing offline canary pass:

| Mode | Checks |
| --- | --- |
| `html-theme-open` | Dark is stored and the scale is 1.5. Opening the guide records `theme` `Dark`, `scale` 1.5, `bodyBackground` `rgb(30, 30, 30)`, `bodyColor` `rgb(230, 230, 230)` and `rootZoom` `1.5`, with `applications` 1 and `failures` 0. The view is visible, and the reading place is the guide's start. |
| `html-theme-light` | Light is stored and no scale. The authored background (`rgb(255, 255, 255)`) and color (`rgb(34, 34, 34)`) stay, and `rootZoom` is `1`. |
| `html-theme-switch` | System is stored, Windows is light, and the guide is open. Switching Windows to dark with `Set-AppThemePreference` records `Dark` with `applications` 2. Served paths, denied requests and the navigation count are unchanged, and so is the reported reading place. The harness restores the user's Windows theme. |
| `html-theme-offline` | Across the modes above, in the canary's offline pass: no non-loopback connection, no denied `font` or `stylesheet` request, and no served path outside the fixture's manifest. |

Screenshots of Light, Dark and Dark at 1.5 go to the evidence folder and
the PR, for review only. The tests assert the values the app's style
produces in the page, not rendered pixels.

High contrast isn't switched on in CI, the same choice T14.2 made: it
changes the user's theme, and that pass belongs to T16.2. The Core tests
cover the HighContrast CSS, and `ThemeSettings.Changed` is already wired.

**Core unit tests** (`HtmlReaderStyleTests`):

- Each theme's CSS:
  - Light uses only `:where` rules and no `!important` color;
  - Dark sets the palette with `!important`;
  - HighContrast has no `color`, `background` or `border-color`
    property.
- `ClampScale` at 0.74, 0.75, 1.0, 2.0 and 2.01, and for NaN and
  infinity.
- The zoom is formatted the same under the `de-DE` culture.
- No theme's CSS has `url(`, `@import`, `@font-face` or `font-family`.
- `PageColor` values.
- The new diagnostics JSON fields.

## Docs

In the implementing branch:

- the implementation notes and verification in this file;
- the new modes and the fixture in [e2e-testing.md](e2e-testing.md);
- the T09.2 lines in [implementation-plan.md](implementation-plan.md) and
  [work-breakdown.md](../work-breakdown.md);
- the T14.2 design's "HTML guide content is out of scope" note, which
  gets a pointer to this design.

## Implementation notes

The plan ([t09-2-html-theme-style-plan.md](t09-2-html-theme-style-plan.md))
made these calls against this design:

1. **A live test file.** With the HtmlDiagnostics gate open, the session
   writes `diagnostics\html-appearance-<pid>.json`
   (`{ "opacity", "session" }`) after each application, atomically, so
   the switch check can read it while the guide is open.
2. **Entry navigations are counted.** `RecordEntryNavigation()` and
   `entryNavigations` make "the page doesn't reload" a number (1).
3. **An unchanged appearance isn't rewritten.** One Windows switch can
   raise both `ActualThemeChanged` and `ThemeSettings.Changed`; the
   switch check expects exactly 2 applications.
4. **Writes are serialized by a loop, not a lock.** A call during a write,
   or before the open's write, stores the appearance; the writer loops
   until the page has the latest one.
5. **A failed readback still counts the application.** Its computed
   fields stay null. A failed write counts as a failure.
6. **The offline check runs inside the theme modes.** Each mode asserts
   no non-loopback connection, and the install side asserts the served
   set is the fixture's three files with nothing denied. There's no
   separate `html-theme-offline` mode.
7. **Seed commands.** `seed-html-theme <dataRoot> <fixtures>` and
   `set-html-appearance <dataRoot> <guideId> <System|Light|Dark> <scale|default>`.
8. **The live Windows switch.** The smoke sets `AppsUseLightTheme` and
   broadcasts `WM_SETTINGCHANGE` `ImmersiveColorSet`
   (`DesktopGuidesForegroundProbe.BroadcastThemeChange()`); the install
   side restores the user's value in its `finally`.
9. **The id collision.** The fixture has
   `<p id="desktop-guides-style">`; the Dark pass checks its text stays.
10. **The open check doesn't assert the guide's start.** A new guide has
    no saved place; the switch pass checks the place is unchanged.
11. **The original page color is kept.** HighContrast puts back the
    view's own `DefaultBackgroundColor`.

Made during execution:

- `OpenAsync(source, token)` is an `IReaderSession` member, so it stays
  and opens in Light at 1.0; the shell calls the new
  `OpenAsync(source, appearance, token)`.
- Host syncs from macOS use `COPYFILE_DISABLE=1`, so `tar` adds no
  AppleDouble `._*` files to the Windows build.
- The theme passes allow one denied request: `NotInManifest` as `Other`,
  which Chromium makes for its favicon on every page (the position
  passes see it too). Any other denial fails, so a font or stylesheet
  request still fails.
- The first red run stopped earlier, in `html-position-dark`: a reopen
  whose WebView2 never requested the entry. That path is unchanged here.
  A rerun of that job at the same commit passed it and failed in
  `html-theme-open` as expected.

### Real-guide check: `!important` colors

Fetched on 2026-10-06, one page each, with `curl`:

| Site | Page | Result |
| --- | --- | --- |
| GameFAQs | `gamefaqs.gamespot.com/snes/563538-chrono-trigger/faqs/1934` | Blocked: Cloudflare challenge (403), also with browser headers. Not checked. |
| Fandom | `finalfantasy.fandom.com/wiki/Final_Fantasy_VII_walkthrough` | Blocked (403). The article bodies of `chrono.fandom.com` *Millennial Fair (Chapter)* and `finalfantasy.fandom.com` *Chapter 8 (Crisis Core)*, through the MediaWiki parse API: 0 `!important` colors, 0 inline `!important` colors, 1 inline color without `!important`. The site CSS was blocked too. |
| StrategyWiki | `strategywiki.org/wiki/Chrono_Trigger/Walkthrough` | Page: 0. Its two same-site stylesheets: 10 `!important` color or background rules, all in `@media print` (tables, links) or site chrome (sidebar, footer, search button). None reaches guide content on screen. |

No guide content seen used an `!important` color, so the risk stays
low priority and isn't fixed in P1. This is uncertain: it's three
pages, one fetch each, and GameFAQs wasn't seen. GameFAQs FAQs are
mostly plain text, which this risk doesn't touch, but its HTML FAQs are
unchecked.

## Verification

- Core unit tests: `HtmlReaderStyleTests` (each theme's CSS, `ClampScale`
  bounds, NaN and infinity, `de-DE` formatting, no `url(`, `@import`,
  `@font-face` or `font-family`, `PageColor`, the write and readback
  scripts and `ParseApplied`) and the new `HtmlSessionDiagnostics` fields.
  Core went from 807 to 839 tests; Infrastructure stays at 540.
- Red: CI run 37480289557 (`html`, `dev-fast`) at the harness commit.
  After the rerun noted above, `html-theme-open` failed with
  `The app applied no style for the open`.
- Green: CI run 37482852132 (`html`, `dev-fast`). Run 37481608053 before
  it failed only on the favicon denial noted above. Reports, the
  `html-appearance` files and screenshots are in
  [evidence/t09-2-html-theme-style](evidence/t09-2-html-theme-style/):
  - `html-theme-open`: Dark at 1.5, one application, `rgb(30, 30, 30)` on
    `rgb(230, 230, 230)`, `rootZoom` `1.5`, opacity 1; the page's
    `#desktop-guides-style` paragraph keeps its text.
  - `html-theme-light`: Light with Windows dark, the authored
    `rgb(255, 255, 255)` and `rgb(34, 34, 34)`, `rootZoom` `1`.
  - `html-theme-switch`: System with Windows light opens Light; the
    Windows dark switch (`themeBroadcast` true) restyles it to Dark with 2
    applications, one entry navigation, the same served and denied sets
    and the same reading place. The user's Windows theme is restored.
  - Every pass serves only `guide.html`, `images/route.png` and
    `style.css`, and makes no non-loopback connection.
  - `html-reader` and `html-position` pass unchanged.
- Full run 37484041874 at f3370f4 is green on every job.

## Risks

- **Inline `!important` on the page wins in Dark.** A rule such as
  `style="color: #000 !important"` on a dark page leaves that element
  hard to read.
  - Prevalence looks low but is uncertain: the
    [real-guide check](#real-guide-check-important-colors) found none in
    guide content on three sites, with GameFAQs unchecked.
  - The impact is one unreadable element, not a broken guide.
  - Not fixed in P1.
- **Text over a light `background-image`.** Dark keeps background
  images, so text drawn over a light image (a banner, or a texture used
  as the page background) can be hard to read.
  - This is uncommon in saved guide content.
  - Keeping images matters more, because sprites and diagrams carry
    information.
- **`zoom` and fixed-width layouts.** A page with a fixed `px` width
  scrolls sideways at a large scale. That's the expected cost of scaling
  `px` text.
- **The first frame.** If WebView2 paints before `Opacity` takes effect,
  one frame could show the default background. That background already
  has the theme color, so the user wouldn't see white.
