# T06.1 + T06.2 Import Preview and Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** From a game's view, a person picks a local TXT, HTML or PDF file and
sees a validated preview: suggested title, format, file name, size, a TXT
encoding choice when needed, and HTML asset warnings. Nothing is copied and
nothing is written.

**Architecture:** Core defines the import contracts (`ImportManifest`,
`ImportInspection`, `ImportIssue`, `IGuideImportValidator`), `GuideTitle` and
the pure display rules in `ImportPresentation`. Infrastructure implements
`GuideImportValidator`: common file checks, then TXT through
`TextGuideDocument.Decode`, HTML through the existing
`StaticHtmlImportValidator.PreviewAsync`, and PDF through PdfPig 0.1.16.
Production adds an **Import guide** button, the Windows App SDK
`FileOpenPicker`, and an `ImportGuideDialog` `ContentDialog`. There is no
primary button until T06.3.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1,
`Microsoft.Windows.Storage.Pickers`), CommunityToolkit
`HeaderedControls` 8.2.251219, PdfPig 0.1.16, xUnit, the PowerShell 5.1 UI
Automation harness, and GitHub Actions `windows-ci.yml`.

**Spec:** `docs/p1/t06-1-import-preview-design.md`

**Target:** T06.1 and T06.2. **Prerequisites, all merged:** T04.4 (PR #14),
T05.4 (PR #16), T07.1 (PR #9), T07.2 (PR #10), T10.0 and T11.1. **Next:**
T06.3.

## Global Constraints

- Validation only reads. It creates no `FileOperations` row, no `.staging`
  entry and no managed file, and it never changes the source file.
- Sources open with `FileMode.Open, FileAccess.Read, FileShare.Read`.
- Accepted extensions are `.txt`, `.html`, `.htm` and `.pdf`, compared
  case-insensitively.
- Size limits: TXT 64 MiB; PDF 1 GiB; HTML entry 16 MiB; HTML assets 2,048
  files, 32 MiB each and 256 MiB in total (`StaticHtmlScanLimits` defaults).
- The guide title is trimmed and must be 1–200 characters
  (`GuideTitle.TitleLimit = 200`). If no suggestion remains, the fallback is
  "Untitled guide".
- Detail strings name the file, the limit or the reason. They never contain
  file contents.
- Cancellation throws `OperationCanceledException`. It is not an
  `ImportIssue`.
- Password-protected PDFs are rejected. No password is requested or stored.
- Production code has no test hooks. The installed smoke drives the real
  system picker.
- Pin `CommunityToolkit.WinUI.Controls.HeaderedControls` at `8.2.251219`.
  PdfPig stays at `0.1.16`, gaining only a `PackageReference` in
  Infrastructure.
- PowerShell scripts stay ASCII-only, because Windows PowerShell 5.1 reads
  BOM-less files as ANSI. Build non-ASCII strings from code points.
- UI tests assert only what app code controls, not the picker's own layout
  or WinUI rendering.
- Never print, copy or log provider credential values.
- Ctrl+O, Confirm, copying and duplicate detection are out of scope.

## Rulings against the spec

Task 6 records these rulings in the design doc's verification record.

- **Picker API.** Use `Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id)`
  (Windows App SDK 2.x) with `FileTypeFilter` and `PickSingleFileAsync()`,
  returning `PickFileResult?.Path`. It takes the window ID, so
  `InitializeWithWindow` isn't needed; spec flow step 1 named it. The API is
  present in `Microsoft.Windows.Storage.Pickers.winmd` of the pinned
  Foundation package. `ShellWindow` already has `using Windows.Storage;`,
  which doesn't conflict, and there must be no `using Windows.Storage.Pickers;`.
- **Resolve raises `Changed`.** The spec's TXT step 5 (resolve compares size
  and last-write time) is more specific than "T06.1 doesn't raise
  `Changed`". `ResolveTxtEncodingAsync` raises it; nothing else in this work
  does.
- **Windows-1252 decodes every byte.** On .NET 10, code page 1252 maps 0x81,
  0x8D, 0x8F, 0x90 and 0x9D to C1 controls (verified on the host), so
  resolving with 1252 never throws `DecoderFallbackException`. No mapping is
  added for it. A test pins that a file containing 0x81 resolves under 1252.
- **Picker Cancel in the smoke** invokes the dialog's Cancel button
  (AutomationId `2`) rather than sending Esc, which could go to the wrong
  window.
- **Pointer clicks open the picker.** The smoke uses `Click-Element` for
  `ImportGuideButton` and the dialog's SecondaryButton. `InvokePattern.Invoke`
  would not return while the picker is modal on the UI thread.
- **HTML scenario fixtures.** The spec's smoke step 2 says `html-static`
  shows "the warnings list, and the File details and Warnings headers". But
  `html-static` has no warnings, so its details use a native heading. The
  smoke runs `html-static` for native-heading details and `html-hostile` for
  both `HeaderedContentControl` groups and the warnings list.
- **HTML counts.** `AssetCount` is `Manifest.Assets.Count - 1`: linked files,
  excluding the entry. `TotalBytes` is `Manifest.TotalBytes`, which includes
  the entry.
- **Nothing written: where it is checked.** Infrastructure tests check each
  source file's SHA-256 and last-write time, before and after. There is no
  library root in those tests: the validator takes no library path, so it
  can't write there. The installed smoke checks the library through
  ShellSeed `describe-import` (no `Guides`, no `FileOperations`, no
  `.staging` or `content` entries).
- **Warnings list is capped** at 20 rows plus "N more warnings". A target
  over 80 characters is shortened, keeping the start and end, joined by
  "…". Saved web pages can carry hundreds of remote references.
- **HTML entry size is checked up front** against `Html.MaxEntryBytes`, with
  the same `TooLarge` message the scanner's `EntryBytes` limit maps to, so
  the scanner never reads an oversized entry.
- **Encoding samples** use `NoWrap` and are clipped by the dialog width.
- **Empty managed roots are allowed.** The spec says the library has "no
  `.staging` folder" afterwards. `describe-import` counts entries inside
  the staging and content roots instead, because startup may create the
  empty roots. No entry still means nothing was staged or published.
- **Presence checks for scrolled content.** The dialog content scrolls, so
  the smoke checks details and warnings rows by presence and name. A
  collapsed element leaves the UIA tree; an element scrolled out of view
  reports `IsOffscreen`.

## Review Focus

1. **A saved web page with hundreds of remote, script or `data:`
   references** must not produce a dialog thousands of rows tall. Tests:
   `LimitWarnings` and `ShortenTarget` in Task 1, and
   `ManyRemoteReferencesAllComeThrough` in Task 3 (250 remote images give
   250 warnings, which the dialog then limits).
2. **An HTML entry named like `100% Walkthrough.html`** must preview as
   Ready, with the entry mapped to `guide.html` and the title
   "100% Walkthrough". Test: `PercentNamedEntryKeepsTitleAndMapsEntry`
   (Task 3).
3. **A file edited between Inspect and choosing an encoding** must be
   reported as changed, not silently decoded from new bytes. Test:
   `ResolveReportsChangedWhenTheFileChanged` (Task 2).
4. **Upper-case extensions such as `WALKTHRU.TXT`, `GUIDE.HTM` and
   `MAP.PDF`** must be accepted. Tests: `UpperCaseExtensionsAreAccepted`
   (Task 2, TXT) and the `.HTM` and `.PDF` cases (Task 3).
5. **A large legacy TXT (several MB of CP437)** must not put megabytes into
   the sample text. Test: `SamplesStayWithinEightLinesAndTwoKilobytes`
   (Task 2).

Also for the final reviewer: closing the dialog, or the whole window, while
a check is running must cancel it without an unobserved exception or a late
UI update.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/DesktopGuides.Core/Import/ImportContracts.cs` (new) | Spec contracts, verbatim |
| `src/DesktopGuides.Core/Library/GuideTitle.cs` (new) | Title limit, validation, suggestion from a file name |
| `src/DesktopGuides.Core/Import/ImportPresentation.cs` (new) | Format, encoding and size labels, and warning capping and shortening |
| `tests/DesktopGuides.Core.Tests/GuideTitleTests.cs` (new) | Unit tests |
| `tests/DesktopGuides.Core.Tests/ImportPresentationTests.cs` (new) | Unit tests |
| `src/DesktopGuides.Infrastructure/Import/GuideImportLimits.cs` (new) | Injectable limits |
| `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs` (new) | Common checks, TXT, HTML and PDF validation |
| `src/DesktopGuides.Infrastructure/DesktopGuides.Infrastructure.csproj` | PdfPig reference |
| `tests/DesktopGuides.Infrastructure.Tests/Import/P0Fixtures.cs` (new) | Resolves `tests/fixtures/p0` |
| `tests/DesktopGuides.Infrastructure.Tests/Import/ImportTestFiles.cs` (new) | Temp directory and file fingerprints |
| `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorTests.cs` (new) | Common checks and TXT |
| `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorHtmlPdfTests.cs` (new) | HTML and PDF |
| `Directory.Packages.props` | Pin HeaderedControls |
| `src/DesktopGuides.Production/ImportGuideDialog.xaml` + `.cs` (new) | Preview dialog |
| `src/DesktopGuides.Production/ShellWindow.xaml` + `.cs` | Button, picker, dialog lifetime, empty-state text |
| `src/DesktopGuides.Production/Styles/Typography.xaml` | Monospace sample style |
| `src/DesktopGuides.Production/DesktopGuides.Production.csproj` | HeaderedControls reference |
| `packages.lock.json` (Infrastructure, Infrastructure.Tests, ShellSeed, Production) | Regenerated |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | `seed-import` and `describe-import` |
| `tools/p1/windows_shell_ui_smoke.ps1` | `import-preview` mode |
| `tools/p1/windows_shell_install.ps1` | `-ImportOnly`, `Run-ImportScenarios`, full-run step |
| `docs/p1/e2e-testing.md`, `docs/p1/implementation-plan.md`, `docs/progress.md`, design doc | Status and gates |

## Host commands

The Mac has no `dotnet` or `pwsh`. Build and unit-test on the Windows host
over SSH, staging a fresh copy for each run:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t06-1) { Remove-Item -Recurse -Force E:\work\desktop-guides\t06-1 }; New-Item -ItemType Directory E:\work\desktop-guides\t06-1 | Out-Null"'
tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t06-1'
```

The steps below refer to these commands by name: **Core tests**,
**Infrastructure tests**, **Production build** and **Seed build**.

```bash
s 'cd /d E:\work\desktop-guides\t06-1 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'
s 'cd /d E:\work\desktop-guides\t06-1 && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'
s 'cd /d E:\work\desktop-guides\t06-1 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'
s 'cd /d E:\work\desktop-guides\t06-1 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'
```

Add `--filter "FullyQualifiedName~<ClassName>"` to run one test class. Run
PowerShell scripts with `powershell -NoProfile -ExecutionPolicy Bypass -File`.
When a step changes package references, restore on the host (Production
with `-p:Platform=x64`), then copy each changed `packages.lock.json` back
with `scp pcsx2-win:E:/work/desktop-guides/t06-1/<path> <path>`.

---

### Task 1: Core contracts, guide title and presentation rules

**Files:**
- Create: `src/DesktopGuides.Core/Import/ImportContracts.cs`
- Create: `src/DesktopGuides.Core/Library/GuideTitle.cs`
- Create: `src/DesktopGuides.Core/Import/ImportPresentation.cs`
- Test: `tests/DesktopGuides.Core.Tests/GuideTitleTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/ImportPresentationTests.cs`

**Interfaces:**
- Consumes: `DesktopGuides.Core.Library.GuideFormat { Txt, Html, Pdf }`.
- Produces (namespace `DesktopGuides.Core.Import` unless noted):
  - The spec contracts: `ImportSource`, `ImportManifest`,
    `TxtImportManifest`, `HtmlImportManifest`, `PdfImportManifest`,
    `ImportWarning`, `TxtEncodingSample`, `ImportInspection`, `ImportReady`,
    `ImportNeedsTxtEncoding`, `ImportIssue`, `GuideImportException` and
    `IGuideImportValidator`.
  - `DesktopGuides.Core.Library.GuideTitle`: `const int TitleLimit = 200`,
    `const string Fallback = "Untitled guide"`, `string Create(string?)`
    (throws `ArgumentException`), `bool TryCreate(string?, out string)` and
    `string Suggest(string fileName)`.
  - `ImportPresentation`: `const int MaxShownWarnings = 20`,
    `const int MaxTargetLength = 80`, `string FormatLabel(GuideFormat)`,
    `string EncodingLabel(int? codePage)`,
    `string FormatSize(long bytes, IFormatProvider? culture = null)`,
    `(IReadOnlyList<ImportWarning> Shown, int Hidden) LimitWarnings(IReadOnlyList<ImportWarning>, int max = MaxShownWarnings)`,
    `string MoreWarnings(int hidden)`, `string ShortenTarget(string)` and
    `string WarningLine(ImportWarning)`.

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Core.Tests/GuideTitleTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideTitleTests
{
    [Theory]
    [InlineData("Walkthrough.txt", "Walkthrough")]
    [InlineData("  FAQ v2 .HTML", "FAQ v2")]
    [InlineData("100% Walkthrough.html", "100% Walkthrough")]
    [InlineData("archive.tar.pdf", "archive.tar")]
    [InlineData(".txt", GuideTitle.Fallback)]
    [InlineData("   .pdf", GuideTitle.Fallback)]
    public void SuggestsTheFileNameWithoutItsExtension(string fileName, string expected) =>
        Assert.Equal(expected, GuideTitle.Suggest(fileName));

    [Fact]
    public void SuggestionIsCutTo200Characters()
    {
        string suggested = GuideTitle.Suggest(new string('A', 201) + ".txt");

        Assert.Equal(new string('A', 200), suggested);
    }

    [Fact]
    public void SuggestionNeverSplitsASurrogatePair()
    {
        // 199 letters, then U+1F600 (two UTF-16 units) straddles the cut.
        string name = new string('A', 199) + "\U0001F600" + "tail.txt";

        string suggested = GuideTitle.Suggest(name);

        Assert.Equal(new string('A', 199), suggested);
        Assert.False(char.IsHighSurrogate(suggested[^1]));
    }

    [Fact]
    public void CreateTrimsAndAcceptsOneTo200Characters()
    {
        Assert.Equal("A", GuideTitle.Create("  A  "));
        Assert.Equal(200, GuideTitle.Create(" " + new string('T', 200) + " ").Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void CreateRejectsAnEmptyTitle(string? title) =>
        Assert.Throws<ArgumentException>(() => GuideTitle.Create(title));

    [Fact]
    public void CreateRejects201Characters()
    {
        Assert.Throws<ArgumentException>(() => GuideTitle.Create(new string('T', 201)));
        Assert.False(GuideTitle.TryCreate(new string('T', 201), out _));
        Assert.True(GuideTitle.TryCreate(" Guide ", out string title));
        Assert.Equal("Guide", title);
    }
}
```

`tests/DesktopGuides.Core.Tests/ImportPresentationTests.cs`:

```csharp
using System.Globalization;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ImportPresentationTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(GuideFormat.Txt, "Text (TXT)")]
    [InlineData(GuideFormat.Html, "Web page (HTML)")]
    [InlineData(GuideFormat.Pdf, "PDF")]
    public void FormatLabels(GuideFormat format, string expected) =>
        Assert.Equal(expected, ImportPresentation.FormatLabel(format));

    [Theory]
    [InlineData(null, "UTF-8")]
    [InlineData(437, "DOS (CP437)")]
    [InlineData(1252, "Western (Windows-1252)")]
    public void EncodingLabels(int? codePage, string expected) =>
        Assert.Equal(expected, ImportPresentation.EncodingLabel(codePage));

    [Fact]
    public void EncodingLabelRejectsOtherCodePages() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ImportPresentation.EncodingLabel(65001));

    [Theory]
    [InlineData(1L, "1 byte")]
    [InlineData(20L, "20 bytes")]
    [InlineData(1023L, "1023 bytes")]
    [InlineData(1024L, "1 KB")]
    [InlineData(2123L, "2.1 KB")]
    [InlineData(1048524L, "1023.9 KB")]
    [InlineData(1048575L, "1 MB")]
    [InlineData(10485760L, "10 MB")]
    [InlineData(1073741824L, "1 GB")]
    public void FormatsSizes(long bytes, string expected) =>
        Assert.Equal(expected, ImportPresentation.FormatSize(bytes, Invariant));

    [Fact]
    public void LimitWarningsKeepsTheFirstTwentyAndCountsTheRest()
    {
        ImportWarning[] warnings = Enumerable.Range(0, 250)
            .Select(i => new ImportWarning($"https://ads.example/{i}.js", "A remote asset will be blocked in the offline reader."))
            .ToArray();

        (IReadOnlyList<ImportWarning> shown, int hidden) = ImportPresentation.LimitWarnings(warnings);

        Assert.Equal(20, shown.Count);
        Assert.Equal(warnings[0], shown[0]);
        Assert.Equal(230, hidden);
        Assert.Equal("230 more warnings", ImportPresentation.MoreWarnings(hidden));
        Assert.Equal("1 more warning", ImportPresentation.MoreWarnings(1));
    }

    [Fact]
    public void LimitWarningsHidesNothingAtTheCap()
    {
        ImportWarning[] warnings = Enumerable.Range(0, 20)
            .Select(i => new ImportWarning($"{i}.png", "A local asset is missing and will not appear offline."))
            .ToArray();

        Assert.Equal(0, ImportPresentation.LimitWarnings(warnings).Hidden);
    }

    [Fact]
    public void ShortenTargetKeepsBothEnds()
    {
        string target = "data:image/png;base64," + new string('Q', 5000) + "END";

        string shortened = ImportPresentation.ShortenTarget(target);

        Assert.Equal(ImportPresentation.MaxTargetLength, shortened.Length);
        Assert.StartsWith("data:image/png;base64,", shortened);
        Assert.EndsWith("END", shortened);
        Assert.Contains('…', shortened);
        Assert.Equal("images/map.png", ImportPresentation.ShortenTarget("images/map.png"));
    }

    [Fact]
    public void WarningLineJoinsTargetAndReason() =>
        Assert.Equal("images/x.png: A local asset is missing and will not appear offline.",
            ImportPresentation.WarningLine(new ImportWarning(
                "images/x.png", "A local asset is missing and will not appear offline.")));
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Stage, then run **Core tests**.
Expected: build FAIL, with `GuideTitle`, `ImportPresentation` and `ImportWarning` not found.

- [ ] **Step 3: Write the contracts, `GuideTitle` and `ImportPresentation`**

`src/DesktopGuides.Core/Import/ImportContracts.cs`:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Import;

public sealed record ImportSource(
    string FullPath, string FileName, long ByteCount, DateTimeOffset LastWriteUtc);

public abstract record ImportManifest(
    ImportSource Source, GuideFormat Format, string SuggestedTitle);

public sealed record TxtImportManifest(
    ImportSource Source, string SuggestedTitle, int? CodePage)   // null = UTF-8
    : ImportManifest(Source, GuideFormat.Txt, SuggestedTitle);

public sealed record HtmlImportManifest(
    ImportSource Source, string SuggestedTitle, string EntryRelativePath,
    int AssetCount, long TotalBytes, IReadOnlyList<ImportWarning> Warnings)
    : ImportManifest(Source, GuideFormat.Html, SuggestedTitle);

public sealed record PdfImportManifest(
    ImportSource Source, string SuggestedTitle, int PageCount, bool HasText)
    : ImportManifest(Source, GuideFormat.Pdf, SuggestedTitle);

public sealed record ImportWarning(string RelativePath, string Message);

public sealed record TxtEncodingSample(int CodePage, string Text);

public abstract record ImportInspection;

public sealed record ImportReady(ImportManifest Manifest) : ImportInspection;

public sealed record ImportNeedsTxtEncoding(
    ImportSource Source, string SuggestedTitle,
    IReadOnlyList<TxtEncodingSample> Samples) : ImportInspection;   // 437, 1252

public enum ImportIssue
{
    Missing, Unsupported, Empty, TooLarge, Unreadable, Encrypted,
    UnsupportedEncoding, Changed,
}

public sealed class GuideImportException(ImportIssue issue, string detail)
    : Exception(detail)
{
    public ImportIssue Issue { get; } = issue;
}

public interface IGuideImportValidator
{
    Task<ImportInspection> InspectAsync(string fullPath, CancellationToken token);
    Task<TxtImportManifest> ResolveTxtEncodingAsync(
        ImportNeedsTxtEncoding inspection, int codePage, CancellationToken token);
}
```

`src/DesktopGuides.Core/Library/GuideTitle.cs`:

```csharp
namespace DesktopGuides.Core.Library;

public static class GuideTitle
{
    public const int TitleLimit = 200;
    public const string Fallback = "Untitled guide";

    public static string Create(string? title) =>
        TryCreate(title, out string trimmed)
            ? trimmed
            : throw new ArgumentException(
                $"A title of 1–{TitleLimit} characters is required.", nameof(title));

    public static bool TryCreate(string? title, out string trimmed)
    {
        trimmed = title?.Trim() ?? "";
        return trimmed.Length is >= 1 and <= TitleLimit;
    }

    public static string Suggest(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (name.Length > TitleLimit)
        {
            int cut = char.IsHighSurrogate(name[TitleLimit - 1]) ? TitleLimit - 1 : TitleLimit;
            name = name[..cut].TrimEnd();
        }
        return name.Length == 0 ? Fallback : name;
    }
}
```

`src/DesktopGuides.Core/Import/ImportPresentation.cs`:

```csharp
using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Import;

public static class ImportPresentation
{
    public const int MaxShownWarnings = 20;
    public const int MaxTargetLength = 80;

    public static string FormatLabel(GuideFormat format) => format switch
    {
        GuideFormat.Txt => "Text (TXT)",
        GuideFormat.Html => "Web page (HTML)",
        GuideFormat.Pdf => "PDF",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static string EncodingLabel(int? codePage) => codePage switch
    {
        null => "UTF-8",
        437 => "DOS (CP437)",
        1252 => "Western (Windows-1252)",
        _ => throw new ArgumentOutOfRangeException(nameof(codePage)),
    };

    public static string FormatSize(long bytes, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < 1024)
        {
            return bytes == 1 ? "1 byte" : string.Create(culture, $"{bytes} bytes");
        }
        string[] units = ["KB", "MB", "GB"];
        double value = bytes / 1024.0;
        int unit = 0;
        // Promote when rounding to one decimal would show 1024 of a unit.
        while (unit < units.Length - 1 && Math.Round(value, 1) >= 1024)
        {
            value /= 1024;
            unit++;
        }
        return Math.Round(value, 1).ToString("0.#", culture) + " " + units[unit];
    }

    public static (IReadOnlyList<ImportWarning> Shown, int Hidden) LimitWarnings(
        IReadOnlyList<ImportWarning> warnings, int max = MaxShownWarnings) =>
        warnings.Count <= max
            ? (warnings, 0)
            : (warnings.Take(max).ToArray(), warnings.Count - max);

    public static string MoreWarnings(int hidden) =>
        hidden == 1 ? "1 more warning" : $"{hidden} more warnings";

    public static string ShortenTarget(string target)
    {
        if (target.Length <= MaxTargetLength)
        {
            return target;
        }
        int tail = 24;
        int head = MaxTargetLength - tail - 1;
        if (char.IsHighSurrogate(target[head - 1])) head--;
        if (char.IsLowSurrogate(target[^tail])) tail--;
        string shortened = target[..head] + "…" + target[^tail..];
        return shortened;
    }

    public static string WarningLine(ImportWarning warning) =>
        $"{ShortenTarget(warning.RelativePath)}: {warning.Message}";
}
```

`ShortenTarget` on an all-ASCII target gives exactly 55 + 1 + 24 = 80
characters. A surrogate at a cut makes it up to two characters shorter; the
test uses ASCII.

- [ ] **Step 4: Run the tests and watch them pass**

Stage, then run **Core tests**.
Expected: PASS, with every existing Core test still passing.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Import src/DesktopGuides.Core/Library/GuideTitle.cs \
  tests/DesktopGuides.Core.Tests/GuideTitleTests.cs tests/DesktopGuides.Core.Tests/ImportPresentationTests.cs
git commit -m "feat(p1): add import contracts, guide titles and preview labels" \
  -m "Core types for T06.1/T06.2: the typed import manifest and inspection results, GuideTitle (1-200 characters, suggestion from a file name without splitting a surrogate pair) and the preview's format, encoding, size and warning-cap rules." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 2: Validator common checks and TXT

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Import/GuideImportLimits.cs`
- Create: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/Import/P0Fixtures.cs`
- Create: `tests/DesktopGuides.Infrastructure.Tests/Import/ImportTestFiles.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts and `GuideTitle.Suggest`;
  `DesktopGuides.Core.Text.TextGuideDocument.Decode(byte[], int? codePage = null)`,
  which throws `EncodingSelectionRequiredException` only when `codePage` is
  null; `StaticHtmlScanLimits` (defaults are the spec limits).
- Produces:
  - `public sealed record GuideImportLimits(long MaxTxtBytes = 64 MiB, long MaxPdfBytes = 1 GiB, StaticHtmlScanLimits? Html = null)`
    with `StaticHtmlScanLimits HtmlLimits`.
  - `public sealed class GuideImportValidator : IGuideImportValidator`, with
    the constructor `GuideImportValidator(GuideImportLimits? limits = null)`.
  - Detail strings, used again in Task 3 and in the dialog:
    - Missing: `"{name} can't be found. It may have been moved or deleted."`
    - Unsupported: `"{name} isn't a TXT, HTML or PDF file."`
    - Empty: `"The file is empty."`
    - TooLarge (TXT, PDF): `"{name} is larger than the {size} limit for {text files|PDFs}."`
    - Unreadable: `"{name} can't be opened. Close any app that's using it, make sure it's available offline, then try again."`
    - UnsupportedEncoding: `"This text file isn't UTF-8. Save it as UTF-8 and import it again."`
    - Changed: `"{name} changed after it was checked. Choose it again."`
  - Test helpers: `P0Fixtures.Resolve(string relative)`, `ImportTestDirectory`
    (`Root`, `Write(name, byte[])`, `Write(name, string)`, `Copy(fixture, name)`)
    and `FileFingerprint.Of(string fileOrDirectory)`.
  - Until Task 3, `InspectAsync` throws `NotSupportedException` for HTML and
    PDF after the common checks pass. Task 3 replaces that line.

- [ ] **Step 1: Write the test helpers**

`tests/DesktopGuides.Infrastructure.Tests/Import/P0Fixtures.cs`:

```csharp
namespace DesktopGuides.Infrastructure.Tests.Import;

internal static class P0Fixtures
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    public static string Root => RootPath.Value;

    public static string Resolve(string relative) =>
        Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "p0");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("tests/fixtures/p0 was not found above the test output.");
    }
}
```

`tests/DesktopGuides.Infrastructure.Tests/Import/ImportTestFiles.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace DesktopGuides.Infrastructure.Tests.Import;

internal sealed class ImportTestDirectory : IDisposable
{
    public ImportTestDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(),
            "desktop-guides-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(Root, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string Write(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));

    public string Copy(string fixture, string name) =>
        Write(name, File.ReadAllBytes(P0Fixtures.Resolve(fixture)));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record FileFingerprint(string Path, string Sha256, DateTime LastWriteUtc)
{
    public static IReadOnlyList<FileFingerprint> Of(string fileOrDirectory)
    {
        IEnumerable<string> files = Directory.Exists(fileOrDirectory)
            ? Directory.EnumerateFiles(fileOrDirectory, "*", SearchOption.AllDirectories)
            : [fileOrDirectory];
        return files
            .Order(StringComparer.Ordinal)
            .Select(file => new FileFingerprint(
                file,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
                File.GetLastWriteTimeUtc(file)))
            .ToArray();
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorTests.cs`:

```csharp
using System.Text;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class GuideImportValidatorTests
{
    private const string NotUtf8 = "This text file isn't UTF-8. Save it as UTF-8 and import it again.";

    private static Task<ImportInspection> Inspect(string path, GuideImportLimits? limits = null) =>
        new GuideImportValidator(limits).InspectAsync(path, CancellationToken.None);

    private static async Task<GuideImportException> Rejected(string path, GuideImportLimits? limits = null) =>
        await Assert.ThrowsAsync<GuideImportException>(() => Inspect(path, limits));

    [Theory]
    [InlineData("txt-ascii.txt")]
    [InlineData("txt-utf8.txt")]
    [InlineData("txt-bom.txt")]
    public async Task Utf8TextFixturesAreReady(string fixture)
    {
        string path = P0Fixtures.Resolve(fixture);

        ImportReady ready = Assert.IsType<ImportReady>(await Inspect(path));

        TxtImportManifest manifest = Assert.IsType<TxtImportManifest>(ready.Manifest);
        Assert.Null(manifest.CodePage);
        Assert.Equal(GuideFormat.Txt, manifest.Format);
        Assert.Equal(Path.GetFileNameWithoutExtension(fixture), manifest.SuggestedTitle);
        Assert.Equal(fixture, manifest.Source.FileName);
        Assert.Equal(new FileInfo(path).Length, manifest.Source.ByteCount);
        Assert.Equal(Path.GetFullPath(path), manifest.Source.FullPath);
    }

    [Fact]
    public async Task LegacyTextNeedsAnEncodingWithBothSamples()
    {
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await Inspect(P0Fixtures.Resolve("txt-legacy.txt")));

        Assert.Equal("txt-legacy", needs.SuggestedTitle);
        Assert.Equal([437, 1252], needs.Samples.Select(sample => sample.CodePage));
        Assert.Equal("Guide é\nItem list", needs.Samples[0].Text);
        Assert.Equal("Guide ‚\nItem list", needs.Samples[1].Text);
    }

    [Theory]
    [InlineData(437)]
    [InlineData(1252)]
    public async Task ResolveStoresTheChosenCodePage(int codePage)
    {
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(P0Fixtures.Resolve("txt-legacy.txt"), CancellationToken.None));

        TxtImportManifest manifest = await validator.ResolveTxtEncodingAsync(
            needs, codePage, CancellationToken.None);

        Assert.Equal(codePage, manifest.CodePage);
        Assert.Equal(needs.Source, manifest.Source);
        Assert.Equal("txt-legacy", manifest.SuggestedTitle);
    }

    [Fact]
    public async Task ResolveRejectsOtherCodePages()
    {
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(P0Fixtures.Resolve("txt-legacy.txt"), CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            validator.ResolveTxtEncodingAsync(needs, 65001, CancellationToken.None));
    }

    [Fact]
    public async Task Windows1252ResolvesBytesItLeavesUndefined()
    {
        using ImportTestDirectory files = new();
        string path = files.Write("map.txt", [0x4D, 0x61, 0x70, 0x20, 0x81, 0x8D, 0x8F, 0x90, 0x9D]);
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(path, CancellationToken.None));

        TxtImportManifest manifest = await validator.ResolveTxtEncodingAsync(
            needs, 1252, CancellationToken.None);

        Assert.Equal(1252, manifest.CodePage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveReportsChangedWhenTheFileChanged(bool grow)
    {
        using ImportTestDirectory files = new();
        string path = files.Copy("txt-legacy.txt", "legacy.txt");
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(path, CancellationToken.None));
        if (grow)
        {
            File.AppendAllText(path, "more");
            File.SetLastWriteTimeUtc(path, needs.Source.LastWriteUtc.UtcDateTime);
        }
        else
        {
            File.SetLastWriteTimeUtc(path, needs.Source.LastWriteUtc.UtcDateTime.AddMinutes(1));
        }

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            validator.ResolveTxtEncodingAsync(needs, 437, CancellationToken.None));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        Assert.Equal("legacy.txt changed after it was checked. Choose it again.", error.Message);
    }

    public static TheoryData<byte[]> NotUtf8Bytes => new()
    {
        { new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00 } },
        { new byte[] { 0xFE, 0xFF, 0x00, 0x41, 0x00, 0x42 } },
        { Encoding.ASCII.GetBytes("abc\0def") },
    };

    [Theory]
    [MemberData(nameof(NotUtf8Bytes))]
    public async Task Utf16AndNulAreAnUnsupportedEncoding(byte[] bytes)
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(files.Write("wide.txt", bytes));

        Assert.Equal(ImportIssue.UnsupportedEncoding, error.Issue);
        Assert.Equal(NotUtf8, error.Message);
    }

    [Fact]
    public async Task SamplesStayWithinEightLinesAndTwoKilobytes()
    {
        using ImportTestDirectory files = new();
        byte[] line = [.. Encoding.ASCII.GetBytes("Room "), 0x82, .. Encoding.ASCII.GetBytes(new string('.', 90)), 0x0D, 0x0A];
        byte[] bytes = Enumerable.Repeat(line, 3 * 1024 * 1024 / line.Length).SelectMany(part => part).ToArray();

        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await Inspect(files.Write("long legacy.txt", bytes)));

        Assert.All(needs.Samples, sample =>
        {
            Assert.True(sample.Text.Split('\n').Length <= 8);
            Assert.True(sample.Text.Length <= 2048);
            Assert.DoesNotContain('\r', sample.Text);
        });
    }

    [Theory]
    [InlineData("WALKTHRU.TXT")]
    [InlineData("Walkthru.Txt")]
    public async Task UpperCaseExtensionsAreAccepted(string name)
    {
        using ImportTestDirectory files = new();

        ImportReady ready = Assert.IsType<ImportReady>(
            await Inspect(files.Copy("txt-ascii.txt", name)));

        Assert.Equal("WALKTHRU", ready.Manifest.SuggestedTitle.ToUpperInvariant());
    }

    [Theory]
    [InlineData("guide.docx")]
    [InlineData("guide.txt.exe")]
    [InlineData("README")]
    public async Task OtherExtensionsAreUnsupported(string name)
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(files.Write(name, "text"));

        Assert.Equal(ImportIssue.Unsupported, error.Issue);
        Assert.Equal($"{name} isn't a TXT, HTML or PDF file.", error.Message);
    }

    [Fact]
    public async Task MissingPathIsMissing()
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(Path.Combine(files.Root, "gone.txt"));

        Assert.Equal(ImportIssue.Missing, error.Issue);
        Assert.Equal("gone.txt can't be found. It may have been moved or deleted.", error.Message);
    }

    [Fact]
    public async Task RelativePathIsAnArgumentError() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Inspect("guide.txt"));

    [Theory]
    [InlineData("empty.txt")]
    [InlineData("empty.html")]
    [InlineData("empty.pdf")]
    public async Task ZeroByteFilesAreEmpty(string name)
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(files.Write(name, []));

        Assert.Equal(ImportIssue.Empty, error.Issue);
        Assert.Equal("The file is empty.", error.Message);
    }

    [Fact]
    public async Task ExclusivelyLockedTextIsUnreadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory files = new();
        string path = files.Copy("txt-ascii.txt", "locked.txt");
        using FileStream holder = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        GuideImportException error = await Rejected(path);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.StartsWith("locked.txt can't be opened.", error.Message);
    }

    [Theory]
    [InlineData("txt-ascii.txt")]
    [InlineData("html-static/guide.html")]
    [InlineData("pdf-short.pdf")]
    public async Task ACancelledTokenThrows(string fixture)
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GuideImportValidator().InspectAsync(P0Fixtures.Resolve(fixture), cancel.Token));
    }

    [Fact]
    public async Task TextAtTheLimitPassesAndOneByteMoreIsTooLarge()
    {
        using ImportTestDirectory files = new();
        GuideImportLimits limits = new(MaxTxtBytes: 20);
        string atLimit = files.Write("at.txt", new string('a', 20));
        string over = files.Write("over.txt", new string('a', 21));

        Assert.IsType<ImportReady>(await Inspect(atLimit, limits));
        GuideImportException error = await Rejected(over, limits);

        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.Equal("over.txt is larger than the 20 bytes limit for text files.", error.Message);
    }

    [Fact]
    public async Task InspectingAndResolvingLeaveTextFixturesUnchanged()
    {
        string[] fixtures = ["txt-ascii.txt", "txt-utf8.txt", "txt-bom.txt", "txt-legacy.txt"];
        var before = fixtures.Select(f => FileFingerprint.Of(P0Fixtures.Resolve(f))).ToArray();
        GuideImportValidator validator = new();

        foreach (string fixture in fixtures)
        {
            if (await validator.InspectAsync(P0Fixtures.Resolve(fixture), CancellationToken.None)
                is ImportNeedsTxtEncoding needs)
            {
                await validator.ResolveTxtEncodingAsync(needs, 437, CancellationToken.None);
            }
        }

        var after = fixtures.Select(f => FileFingerprint.Of(P0Fixtures.Resolve(f))).ToArray();
        Assert.Equal(before.SelectMany(x => x), after.SelectMany(x => x));
    }
}
```

"20 bytes" relies on `FormatSize` below 1024, which is culture-neutral.

- [ ] **Step 3: Run the tests and watch them fail**

Stage, then run **Infrastructure tests** with
`--filter "FullyQualifiedName~GuideImportValidatorTests"`.
Expected: build FAIL, with `GuideImportValidator` and `GuideImportLimits` not found.

- [ ] **Step 4: Write the limits and the validator**

`src/DesktopGuides.Infrastructure/Import/GuideImportLimits.cs`:

```csharp
namespace DesktopGuides.Infrastructure.Import;

public sealed record GuideImportLimits(
    long MaxTxtBytes = 64L * 1024 * 1024,
    long MaxPdfBytes = 1024L * 1024 * 1024,
    StaticHtmlScanLimits? Html = null)
{
    public StaticHtmlScanLimits HtmlLimits => Html ?? new StaticHtmlScanLimits();
}
```

`src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs`:

```csharp
using System.Text;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Text;

namespace DesktopGuides.Infrastructure.Import;

/// <summary>
/// Reads a picked file and describes it for the import preview. It never
/// writes: no staging, no managed copy, and the source opens read-only with
/// shared read access.
/// </summary>
public sealed class GuideImportValidator : IGuideImportValidator
{
    private const int SampleBytes = 2048;
    private const int SampleLines = 8;
    private static readonly int[] LegacyCodePages = [437, 1252];
    private readonly GuideImportLimits limits;

    public GuideImportValidator(GuideImportLimits? limits = null)
    {
        this.limits = limits ?? new GuideImportLimits();
    }

    public async Task<ImportInspection> InspectAsync(string fullPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException("A full file path is required.", nameof(fullPath));
        }
        token.ThrowIfCancellationRequested();
        FileInfo file = new(Path.GetFullPath(fullPath));
        string name = file.Name;
        if (!file.Exists)
        {
            throw Missing(name);
        }
        GuideFormat format = FormatOf(file.Extension) ??
            throw new GuideImportException(ImportIssue.Unsupported, $"{name} isn't a TXT, HTML or PDF file.");
        if (file.Length == 0)
        {
            throw new GuideImportException(ImportIssue.Empty, "The file is empty.");
        }
        CheckSize(file.Length, name, format);
        ImportSource source = new(
            file.FullName, name, file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
        string title = GuideTitle.Suggest(name);
        return format switch
        {
            GuideFormat.Txt => await InspectTxtAsync(source, title, token),
            _ => throw new NotSupportedException("HTML and PDF validation are added next."),
        };
    }

    public async Task<TxtImportManifest> ResolveTxtEncodingAsync(
        ImportNeedsTxtEncoding inspection, int codePage, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (!LegacyCodePages.Contains(codePage))
        {
            throw new ArgumentOutOfRangeException(nameof(codePage), codePage, "Choose code page 437 or 1252.");
        }
        token.ThrowIfCancellationRequested();
        ImportSource source = inspection.Source;
        FileInfo file = new(source.FullPath);
        if (!file.Exists)
        {
            throw Missing(source.FileName);
        }
        if (file.Length != source.ByteCount ||
            file.LastWriteTimeUtc != source.LastWriteUtc.UtcDateTime)
        {
            throw new GuideImportException(ImportIssue.Changed,
                $"{source.FileName} changed after it was checked. Choose it again.");
        }
        byte[] bytes = await ReadAllAsync(source, token);
        // Both code pages decode every byte, so this can't ask for another encoding.
        TextGuideDocument.Decode(bytes, codePage);
        return new TxtImportManifest(source, inspection.SuggestedTitle, codePage);
    }

    private async Task<ImportInspection> InspectTxtAsync(
        ImportSource source, string title, CancellationToken token)
    {
        byte[] bytes = await ReadAllAsync(source, token);
        bool utf16Bom = bytes.Length >= 2 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
        if (utf16Bom || bytes.AsSpan().IndexOf((byte)0) >= 0)
        {
            throw new GuideImportException(ImportIssue.UnsupportedEncoding,
                "This text file isn't UTF-8. Save it as UTF-8 and import it again.");
        }
        try
        {
            TextGuideDocument.Decode(bytes);
            return new ImportReady(new TxtImportManifest(source, title, null));
        }
        catch (EncodingSelectionRequiredException)
        {
            return new ImportNeedsTxtEncoding(source, title, Samples(bytes));
        }
    }

    private static TxtEncodingSample[] Samples(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        int count = Math.Min(bytes.Length, SampleBytes);
        return LegacyCodePages
            .Select(codePage => new TxtEncodingSample(
                codePage, FirstLines(Encoding.GetEncoding(codePage).GetString(bytes, 0, count))))
            .ToArray();
    }

    private static string FirstLines(string text) => string.Join('\n',
        text.Split('\n').Take(SampleLines).Select(line => line.TrimEnd('\r')));

    private async Task<byte[]> ReadAllAsync(ImportSource source, CancellationToken token)
    {
        using FileStream stream = OpenSource(source.FullPath, source.FileName);
        CheckSize(stream.Length, source.FileName, GuideFormat.Txt);
        byte[] bytes = new byte[stream.Length];
        try
        {
            await stream.ReadExactlyAsync(bytes, token);
        }
        catch (IOException)
        {
            throw Unreadable(source.FileName);
        }
        return bytes;
    }

    private void CheckSize(long length, string name, GuideFormat format)
    {
        (long limit, string kind) = format switch
        {
            GuideFormat.Txt => (limits.MaxTxtBytes, "text files"),
            GuideFormat.Pdf => (limits.MaxPdfBytes, "PDFs"),
            _ => (limits.HtmlLimits.MaxEntryBytes, ""),
        };
        if (length <= limit)
        {
            return;
        }
        throw new GuideImportException(ImportIssue.TooLarge, format == GuideFormat.Html
            ? HtmlLimitMessage(StaticScanLimit.EntryBytes)
            : $"{name} is larger than the {ImportPresentation.FormatSize(limit)} limit for {kind}.");
    }

    internal string HtmlLimitMessage(StaticScanLimit limit)
    {
        StaticHtmlScanLimits html = limits.HtmlLimits;
        return limit switch
        {
            StaticScanLimit.EntryBytes =>
                $"The web page is larger than the {ImportPresentation.FormatSize(html.MaxEntryBytes)} limit.",
            StaticScanLimit.AssetBytes =>
                $"A linked file is larger than the {ImportPresentation.FormatSize(html.MaxAssetBytes)} limit for one file.",
            StaticScanLimit.TotalBytes =>
                $"The web page and its linked files are larger than the {ImportPresentation.FormatSize(html.MaxTotalBytes)} limit.",
            StaticScanLimit.AssetCount => $"The web page links more than {html.MaxAssets:N0} files.",
            StaticScanLimit.ReferenceCount => $"The web page has more than {html.MaxReferences:N0} links to other files.",
            StaticScanLimit.CssDepth => $"The web page's style sheets nest more than {html.MaxCssDepth:N0} levels deep.",
            StaticScanLimit.CssRuleCount => $"The web page's style sheets have more than {html.MaxCssRules:N0} rules.",
            _ => throw new ArgumentOutOfRangeException(nameof(limit)),
        };
    }

    private static FileStream OpenSource(string path, string name, bool asyncIo = true)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, asyncIo ? FileOptions.Asynchronous | FileOptions.SequentialScan : FileOptions.None);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Missing(name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw Unreadable(name);
        }
    }

    private static GuideFormat? FormatOf(string extension) => extension.ToLowerInvariant() switch
    {
        ".txt" => GuideFormat.Txt,
        ".html" or ".htm" => GuideFormat.Html,
        ".pdf" => GuideFormat.Pdf,
        _ => null,
    };

    private static GuideImportException Missing(string name) =>
        new(ImportIssue.Missing, $"{name} can't be found. It may have been moved or deleted.");

    private static GuideImportException Unreadable(string name) =>
        new(ImportIssue.Unreadable,
            $"{name} can't be opened. Close any app that's using it, make sure it's available offline, then try again.");
}
```

`ACancelledTokenThrows` passes for HTML and PDF already, because the token
is checked before any format work. `ZeroByteFilesAreEmpty` passes for all
three because the empty check comes before dispatch.

- [ ] **Step 5: Run the tests and watch them pass**

Stage, then run **Infrastructure tests** (the whole project).
Expected: PASS. Every existing Infrastructure test still passes, and on the
host the Windows-only lock test runs rather than returning early.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Import/GuideImportLimits.cs \
  src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs \
  tests/DesktopGuides.Infrastructure.Tests/Import
git commit -m "feat(p1): validate picked guide files and TXT encodings" \
  -m "GuideImportValidator checks path, extension (case-insensitive), empty and size limits, and opens the source read-only with shared read. TXT is ready for UTF-8, rejects UTF-16 and NUL bytes, and otherwise offers CP437 and Windows-1252 samples (8 lines, 2 KB). Resolving an encoding reports Changed when the size or write time moved." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: HTML and PDF validation

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs`
- Modify: `src/DesktopGuides.Infrastructure/DesktopGuides.Infrastructure.csproj`
- Modify: `packages.lock.json` in `src/DesktopGuides.Infrastructure`,
  `tests/DesktopGuides.Infrastructure.Tests`, `tools/p1/DesktopGuides.ShellSeed`
  and `src/DesktopGuides.Production`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorHtmlPdfTests.cs`

**Interfaces:**
- Consumes: Task 2's `GuideImportValidator`, `GuideImportLimits`,
  `OpenSource`, `Missing`, `Unreadable` and `HtmlLimitMessage`, and its test
  helpers. Also `StaticHtmlImportValidator(StaticHtmlScanLimits?)` with
  `PreviewAsync(string selectedEntryPath, CancellationToken)`, which returns
  `EntryRelativePath`, `Manifest` (`Assets`, `TotalBytes`) and `Warnings`
  (`StaticHtmlPreviewWarning(Status, SourceRelativePath, RawTarget,
  RelativePath?, Message)`). `StaticHtmlScanException.Limit` and
  `StaticHtmlValidationException.Issue` are both `IOException`s.
- Also consumes PdfPig 0.1.16: `UglyToad.PdfPig.PdfDocument.Open(Stream)`,
  `NumberOfPages`, `GetPage(int).Text` and
  `UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException`.
- Produces:
  - HTML gives `ImportReady(HtmlImportManifest)` with `AssetCount` =
    assets other than the entry and `TotalBytes` including the entry.
  - PDF gives `ImportReady(PdfImportManifest)`.
  - New detail strings:
    - `"The guide refers to a file outside its folder."`
    - `"The guide's folder has files whose names differ only by case."`
    - `"{name} or one of its linked files can't be opened."`
    - `"This file isn't a readable PDF."`
    - `"Password-protected PDFs aren't supported. Remove the password and import again."`

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorHtmlPdfTests.cs`:

```csharp
using DesktopGuides.Core.Import;
using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class GuideImportValidatorHtmlPdfTests
{
    private static readonly string[] KnownWarnings =
    [
        "A local asset is missing and will not appear offline.",
        "A remote asset will be blocked in the offline reader.",
        "An unsafe asset path will be blocked.",
        "An unsupported asset will be blocked.",
    ];

    private static Task<ImportInspection> Inspect(string path, GuideImportLimits? limits = null) =>
        new GuideImportValidator(limits).InspectAsync(path, CancellationToken.None);

    private static async Task<GuideImportException> Rejected(string path, GuideImportLimits? limits = null) =>
        await Assert.ThrowsAsync<GuideImportException>(() => Inspect(path, limits));

    private static async Task<T> Manifest<T>(string path, GuideImportLimits? limits = null)
        where T : ImportManifest =>
        Assert.IsType<T>(Assert.IsType<ImportReady>(await Inspect(path, limits)).Manifest);

    [Fact]
    public async Task StaticHtmlMapsEntryAssetsAndSize()
    {
        string root = P0Fixtures.Resolve("html-static");

        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(Path.Combine(root, "guide.html"));

        Assert.Equal("guide.html", manifest.EntryRelativePath);
        Assert.Equal(3, manifest.AssetCount);
        long expected = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
        Assert.Equal(expected, manifest.TotalBytes);
        Assert.Empty(manifest.Warnings);
        Assert.Equal("guide", manifest.SuggestedTitle);
    }

    [Fact]
    public async Task HostileHtmlIsReadyWithWarnings()
    {
        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(
            P0Fixtures.Resolve("html-hostile/guide.html"));

        Assert.True(manifest.Warnings.Count >= 2);
        Assert.Contains(manifest.Warnings, w => w.Message == KnownWarnings[1]);
        Assert.All(manifest.Warnings, w =>
        {
            Assert.Contains(w.Message, KnownWarnings);
            Assert.False(string.IsNullOrEmpty(w.RelativePath));
        });
    }

    [Fact]
    public async Task PercentNamedEntryKeepsTitleAndMapsEntry()
    {
        using ImportTestDirectory files = new();
        string entry = files.Copy("html-static/guide.html", "100% Walkthrough.html");
        files.Copy("html-static/images/map.png", "images/map.png");
        files.Copy("html-static/styles/main.css", "styles/main.css");
        files.Copy("html-static/styles/palette.css", "styles/palette.css");

        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(entry);

        Assert.Equal("guide.html", manifest.EntryRelativePath);
        Assert.Equal("100% Walkthrough", manifest.SuggestedTitle);
        Assert.Equal("100% Walkthrough.html", manifest.Source.FileName);
        Assert.Equal(3, manifest.AssetCount);
    }

    [Theory]
    [InlineData("GUIDE.HTM")]
    [InlineData("Guide.Html")]
    public async Task UpperCaseHtmlExtensionsAreAccepted(string name)
    {
        using ImportTestDirectory files = new();

        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(
            files.Write(name, "<p>Route</p>"));

        Assert.Equal(0, manifest.AssetCount);
    }

    [Fact]
    public async Task CaseCollisionIsUnreadable()
    {
        using ImportTestDirectory files = new();
        string entry = files.Write("guide.html", "<img src=\"map.png\"><img src=\"MAP.png\">");
        files.Write("map.png", "image");

        GuideImportException error = await Rejected(entry);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("The guide's folder has files whose names differ only by case.", error.Message);
    }

    [Fact]
    public async Task LinkLeavingTheFolderIsUnreadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory outside = new();
        using ImportTestDirectory files = new();
        string target = outside.Write("secret.png", "outside");
        string entry = files.Write("guide.html", "<img src=\"linked.png\">");
        File.CreateSymbolicLink(Path.Combine(files.Root, "linked.png"), target);

        GuideImportException error = await Rejected(entry);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("The guide refers to a file outside its folder.", error.Message);
    }

    public static TheoryData<StaticScanLimit, string> HtmlLimitCases => new()
    {
        { StaticScanLimit.EntryBytes, "The web page is larger than the 3 bytes limit." },
        { StaticScanLimit.AssetBytes, "A linked file is larger than the 3 bytes limit for one file." },
        { StaticScanLimit.TotalBytes, "The web page and its linked files are larger than the 5 bytes limit." },
        { StaticScanLimit.AssetCount, "The web page links more than 1 files." },
        { StaticScanLimit.ReferenceCount, "The web page has more than 1 links to other files." },
        { StaticScanLimit.CssDepth, "The web page's style sheets nest more than 1 levels deep." },
        { StaticScanLimit.CssRuleCount, "The web page's style sheets have more than 1 rules." },
    };

    [Theory]
    [MemberData(nameof(HtmlLimitCases))]
    public async Task HtmlLimitsMapToTooLarge(StaticScanLimit limit, string message)
    {
        using ImportTestDirectory files = new();
        string entry = files.Write("guide.html",
            "<link rel=\"stylesheet\" href=\"a.css\">\n<img src=\"one.png\">\n<img src=\"two.png\">");
        files.Write("a.css",
            "@import \"b.css\";\n.one { background: url(one.png) }\n.two { background: url(two.png) }");
        files.Write("b.css", ".third { color: red }");
        files.Write("one.png", "one");
        files.Write("two.png", "two");
        StaticHtmlScanLimits html = limit switch
        {
            StaticScanLimit.EntryBytes => new(MaxEntryBytes: 3),
            StaticScanLimit.AssetBytes => new(MaxAssetBytes: 3),
            StaticScanLimit.TotalBytes => new(MaxTotalBytes: 5),
            StaticScanLimit.AssetCount => new(MaxAssets: 1),
            StaticScanLimit.ReferenceCount => new(MaxReferences: 1),
            StaticScanLimit.CssDepth => new(MaxCssDepth: 1),
            _ => new(MaxCssRules: 1),
        };

        GuideImportException error = await Rejected(entry, new GuideImportLimits(Html: html));

        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task HtmlEntryAtTheLimitPassesAndOneByteMoreIsTooLarge()
    {
        using ImportTestDirectory files = new();
        string entry = files.Write("guide.html", "<p>Route</p>");
        long size = new FileInfo(entry).Length;

        await Manifest<HtmlImportManifest>(entry,
            new GuideImportLimits(Html: new StaticHtmlScanLimits(MaxEntryBytes: size)));
        GuideImportException error = await Rejected(entry,
            new GuideImportLimits(Html: new StaticHtmlScanLimits(MaxEntryBytes: size - 1)));

        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.Equal($"The web page is larger than the {size - 1} bytes limit.", error.Message);
    }

    [Fact]
    public async Task ManyRemoteReferencesAllComeThrough()
    {
        using ImportTestDirectory files = new();
        string body = string.Concat(Enumerable.Range(0, 250)
            .Select(n => $"<img src=\"https://cdn.example.com/maps/area-{n}.png\">\n"));
        string entry = files.Write("guide.html", body);

        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(entry);

        Assert.Equal(250, manifest.Warnings.Count);
        Assert.All(manifest.Warnings, w => Assert.Equal(KnownWarnings[1], w.Message));
    }

    [Fact]
    public async Task ExclusivelyLockedHtmlIsUnreadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory files = new();
        string entry = files.Write("locked.html", "<p>Route</p>");
        using FileStream holder = new(entry, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        GuideImportException error = await Rejected(entry);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.StartsWith("locked.html can't be opened.", error.Message);
    }

    [Fact]
    public async Task ShortPdfHasPagesAndText()
    {
        PdfImportManifest manifest = await Manifest<PdfImportManifest>(P0Fixtures.Resolve("pdf-short.pdf"));

        Assert.Equal(2, manifest.PageCount);
        Assert.True(manifest.HasText);
        Assert.Equal("pdf-short", manifest.SuggestedTitle);
    }

    [Fact]
    public async Task ScannedPdfHasNoText()
    {
        PdfImportManifest manifest = await Manifest<PdfImportManifest>(P0Fixtures.Resolve("pdf-scan.pdf"));

        Assert.Equal(1, manifest.PageCount);
        Assert.False(manifest.HasText);
    }

    [Fact]
    public async Task CopyRestrictedPdfIsAccepted()
    {
        PdfImportManifest manifest = await Manifest<PdfImportManifest>(P0Fixtures.Resolve("pdf-access.pdf"));

        Assert.Equal(1, manifest.PageCount);
        Assert.True(manifest.HasText);
    }

    [Fact]
    public async Task PasswordProtectedPdfIsEncrypted()
    {
        GuideImportException error = await Rejected(P0Fixtures.Resolve("pdf-locked.pdf"));

        Assert.Equal(ImportIssue.Encrypted, error.Issue);
        Assert.Equal(
            "Password-protected PDFs aren't supported. Remove the password and import again.",
            error.Message);
    }

    [Fact]
    public async Task TextRenamedToPdfIsUnreadable()
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(files.Copy("txt-ascii.txt", "notes.pdf"));

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("This file isn't a readable PDF.", error.Message);
    }

    [Fact]
    public async Task TruncatedPdfIsUnreadable()
    {
        using ImportTestDirectory files = new();
        byte[] start = File.ReadAllBytes(P0Fixtures.Resolve("pdf-short.pdf"))[..400];

        GuideImportException error = await Rejected(files.Write("cut.pdf", start));

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("This file isn't a readable PDF.", error.Message);
    }

    [Fact]
    public async Task UpperCasePdfExtensionIsAccepted()
    {
        using ImportTestDirectory files = new();

        PdfImportManifest manifest = await Manifest<PdfImportManifest>(
            files.Copy("pdf-short.pdf", "MAP.PDF"));

        Assert.Equal("MAP", manifest.SuggestedTitle);
    }

    [Fact]
    public async Task PdfAtTheLimitPassesAndOneByteMoreIsTooLarge()
    {
        using ImportTestDirectory files = new();
        string path = files.Copy("pdf-short.pdf", "map.pdf");
        long size = new FileInfo(path).Length;

        await Manifest<PdfImportManifest>(path, new GuideImportLimits(MaxPdfBytes: size));
        GuideImportException error = await Rejected(path, new GuideImportLimits(MaxPdfBytes: size - 1));

        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.StartsWith("map.pdf is larger than the ", error.Message);
        Assert.EndsWith(" limit for PDFs.", error.Message);
    }

    [Fact]
    public async Task ExclusivelyLockedPdfIsUnreadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory files = new();
        string path = files.Copy("pdf-short.pdf", "locked.pdf");
        using FileStream holder = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        GuideImportException error = await Rejected(path);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.StartsWith("locked.pdf can't be opened.", error.Message);
    }

    [Fact]
    public async Task InspectingLeavesHtmlAndPdfFixturesUnchanged()
    {
        string[] sources = ["html-static", "html-hostile", "pdf-short.pdf", "pdf-scan.pdf", "pdf-access.pdf", "pdf-locked.pdf"];
        string[] picked = ["html-static/guide.html", "html-hostile/guide.html", "pdf-short.pdf", "pdf-scan.pdf", "pdf-access.pdf", "pdf-locked.pdf"];
        var before = sources.SelectMany(s => FileFingerprint.Of(P0Fixtures.Resolve(s))).ToArray();
        GuideImportValidator validator = new();

        foreach (string path in picked)
        {
            try
            {
                await validator.InspectAsync(P0Fixtures.Resolve(path), CancellationToken.None);
            }
            catch (GuideImportException)
            {
            }
        }

        Assert.Equal(before, sources.SelectMany(s => FileFingerprint.Of(P0Fixtures.Resolve(s))).ToArray());
    }
}
```

The limit theory uses the same five files and limits as the scanner's own
budget theory, so each case hits exactly one limit. `EntryBytes` is caught
by the up-front size check, and its message matches the scanner mapping.

- [ ] **Step 2: Run the tests and watch them fail**

Stage, then run **Infrastructure tests** with
`--filter "FullyQualifiedName~GuideImportValidatorHtmlPdfTests"`.
Expected: most tests FAIL with `NotSupportedException: HTML and PDF
validation are added next.` The `EntryBytes` limit case, the too-large
cases and the lock cases already pass, because the common checks run first.

- [ ] **Step 3: Reference PdfPig**

In `src/DesktopGuides.Infrastructure/DesktopGuides.Infrastructure.csproj`, add
this after `<PackageReference Include="Microsoft.Data.Sqlite" />`:

```xml
    <PackageReference Include="PdfPig" />
```

Stage, restore each affected project on the host, then copy the lock files
back:

```bash
s 'cd /d E:\work\desktop-guides\t06-1 && dotnet restore src\DesktopGuides.Infrastructure\DesktopGuides.Infrastructure.csproj --force-evaluate && dotnet restore tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj --force-evaluate && dotnet restore tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj --force-evaluate && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --force-evaluate -p:Platform=x64'
for p in src/DesktopGuides.Infrastructure tests/DesktopGuides.Infrastructure.Tests tools/p1/DesktopGuides.ShellSeed src/DesktopGuides.Production; do scp pcsx2-win:E:/work/desktop-guides/t06-1/$p/packages.lock.json $p/packages.lock.json; done
git diff --stat -- '*packages.lock.json'
```

Expected: each of the four lock files gains `PdfPig` 0.1.16 and nothing
else changes version. If `git diff` shows any other package changing, stop
and ledger it.

- [ ] **Step 4: Add HTML and PDF validation**

In `GuideImportValidator.cs`, add these usings:

```csharp
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;
```

Add fields next to `limits`, and set `html` in the constructor:

```csharp
    private const int TextSamplePages = 5;
    private static readonly byte[] PdfMarker = "%PDF-"u8.ToArray();
    private readonly StaticHtmlImportValidator html;
```

```csharp
    public GuideImportValidator(GuideImportLimits? limits = null)
    {
        this.limits = limits ?? new GuideImportLimits();
        html = new StaticHtmlImportValidator(this.limits.HtmlLimits);
    }
```

Replace the `NotSupportedException` arm of the dispatch:

```csharp
            GuideFormat.Txt => await InspectTxtAsync(source, title, token),
            GuideFormat.Html => await InspectHtmlAsync(source, title, token),
            _ => await InspectPdfAsync(source, title, token),
```

Add the two methods after `InspectTxtAsync`:

```csharp
    private async Task<ImportInspection> InspectHtmlAsync(
        ImportSource source, string title, CancellationToken token)
    {
        // Report a locked entry as the picked file, before the scanner reads it.
        using (OpenSource(source.FullPath, source.FileName))
        {
        }
        StaticHtmlImportPreview preview;
        try
        {
            preview = await html.PreviewAsync(source.FullPath, token);
        }
        catch (StaticHtmlScanException error)
        {
            throw new GuideImportException(ImportIssue.TooLarge, HtmlLimitMessage(error.Limit));
        }
        catch (StaticHtmlValidationException error) when (error.Issue == StaticHtmlValidationIssue.UnsafePath)
        {
            throw new GuideImportException(ImportIssue.Unreadable, "The guide refers to a file outside its folder.");
        }
        catch (StaticHtmlValidationException error) when (error.Issue == StaticHtmlValidationIssue.CaseCollision)
        {
            throw new GuideImportException(ImportIssue.Unreadable,
                "The guide's folder has files whose names differ only by case.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Missing(source.FileName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new GuideImportException(ImportIssue.Unreadable,
                $"{source.FileName} or one of its linked files can't be opened.");
        }
        ImportWarning[] warnings = preview.Warnings
            .Select(warning => new ImportWarning(warning.RelativePath ?? warning.RawTarget, warning.Message))
            .ToArray();
        return new ImportReady(new HtmlImportManifest(
            source, title, preview.EntryRelativePath,
            preview.Manifest.Assets.Count - 1, preview.Manifest.TotalBytes, warnings));
    }

    private static Task<ImportInspection> InspectPdfAsync(
        ImportSource source, string title, CancellationToken token) =>
        Task.Run<ImportInspection>(() =>
        {
            using FileStream stream = OpenSource(source.FullPath, source.FileName, asyncIo: false);
            try
            {
                if (!StartsLikePdf(stream))
                {
                    throw NotPdf();
                }
                stream.Position = 0;
                using PdfDocument document = PdfDocument.Open(stream);
                int pages = document.NumberOfPages;
                if (pages == 0)
                {
                    throw NotPdf();
                }
                bool hasText = false;
                for (int number = 1; number <= Math.Min(pages, TextSamplePages) && !hasText; number++)
                {
                    token.ThrowIfCancellationRequested();
                    hasText = document.GetPage(number).Text.Any(char.IsLetter);
                }
                return new ImportReady(new PdfImportManifest(source, title, pages, hasText));
            }
            catch (PdfDocumentEncryptedException)
            {
                throw new GuideImportException(ImportIssue.Encrypted,
                    "Password-protected PDFs aren't supported. Remove the password and import again.");
            }
            catch (Exception error) when (error is not (GuideImportException or OperationCanceledException))
            {
                // PdfPig reports malformed files with several exception types.
                throw NotPdf();
            }
        }, token);

    private static bool StartsLikePdf(FileStream stream)
    {
        byte[] head = new byte[1024];
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return head.AsSpan(0, read).IndexOf(PdfMarker) >= 0;
    }

    private static GuideImportException NotPdf() =>
        new(ImportIssue.Unreadable, "This file isn't a readable PDF.");
```

`PdfDocument.Open` reads the stream on demand, so the file is never loaded
whole. Parsing is lenient by default, matching what the T10 reader will
open. If `TruncatedPdfIsUnreadable` fails because lenient parsing recovers
the 400-byte prefix as a readable PDF, don't switch to strict parsing, which
rejects real-world PDFs. Instead, ledger a ruling and change the test's
input to the first 400 bytes followed by 600 bytes of `0x00`.

- [ ] **Step 5: Run the tests and watch them pass**

Stage, then run **Infrastructure tests** (the whole project), **Seed build**
and **Production build**.
Expected: all Infrastructure tests PASS, including the Task 2 class, and
both builds succeed with the new lock files.

- [ ] **Step 6: Commit**

```bash
git add src/DesktopGuides.Infrastructure \
  tests/DesktopGuides.Infrastructure.Tests \
  tools/p1/DesktopGuides.ShellSeed/packages.lock.json \
  src/DesktopGuides.Production/packages.lock.json
git commit -m "feat(p1): validate HTML and PDF guides for import preview" \
  -m "HTML goes through the static scanner: limits map to TooLarge with the limit named, unsafe paths and case collisions to Unreadable, and warnings pass through. PDF checks the %PDF- marker, opens with PdfPig from a shared-read stream, rejects password-protected files and samples the first 5 pages for text." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Import guide dialog and Game view entry point

**TDD skip, stated explicitly:** this task is WinUI XAML and code-behind in
`DesktopGuides.Production`, which has no unit-test project. Following the
UI test rule, the behaviour is covered by the installed `import-preview`
smoke in Task 5, which asserts only what app code controls. Here the gate is
**Production build** succeeding with no new warnings. Task 5's smoke is the
failing-then-passing test for this code.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/DesktopGuides.Production/DesktopGuides.Production.csproj`
- Modify: `src/DesktopGuides.Production/packages.lock.json`
- Modify: `src/DesktopGuides.Production/Styles/Typography.xaml`
- Create: `src/DesktopGuides.Production/ImportGuideDialog.xaml`
- Create: `src/DesktopGuides.Production/ImportGuideDialog.xaml.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`

**Interfaces:**
- Consumes: `IGuideImportValidator`, the `ImportInspection` and manifest
  records, `GuideImportException.Issue`/`Message`, `GuideTitle.TryCreate`
  and `GuideTitle.TitleLimit` (Task 1), `ImportPresentation.FormatLabel`,
  `EncodingLabel`, `FormatSize`, `LimitWarnings`, `MoreWarnings` and
  `WarningLine` (Task 1), and `GuideImportValidator` (Tasks 2–3).
  Also consumes the existing `DialogSurface.Apply(ContentDialog, WindowMaterial)`,
  `AutomationGroup`, `RunNavigationAsync`, `RequireRepository()`,
  `ShowWarningStatus(string)`, and the styles `DesktopGuidesBusyRowStyle`,
  `DesktopGuidesStatusInfoBarStyle`, `DesktopGuidesSecondaryBodyStyle`,
  `DesktopGuidesMetadataStyle`, `DesktopGuidesBodyStyle` and
  `DesktopGuidesSecondaryActionButtonStyle`.
- Produces, for Task 5's smoke and for T06.3:
  - `internal ImportGuideDialog(string gameTitle, string path, IGuideImportValidator validator, Func<Task<string?>> pickFile)`
    and `internal ImportManifest? Manifest`, set only when the preview is
    complete (ready, or once an encoding is resolved).
  - UIA AutomationIds:
    - dialog and busy row: `ImportGuideDialog`, `ImportBusy`,
      `ImportProgress`, `ImportBusyText`, `ImportCancel`;
    - status and preview: `ImportStatus`, `ImportPreview`,
      `GuideTitleInput`, `GuideTitleFeedback`, `ImportNoTextWarning`;
    - encoding choice: `EncodingOptions`, `ImportEncodingCp437`,
      `ImportEncodingWindows1252`, `ImportEncodingCp437Sample`,
      `ImportEncodingWindows1252Sample`;
    - details: `ImportDetailsHeading`, `ImportDetailsGroup`,
      `ImportFormat`, `ImportFileName`, `ImportSize`, `ImportEncodingValue`,
      `ImportAssets`, `ImportPages`;
    - warnings: `ImportWarningsGroup`, `ImportWarning0`…`ImportWarning19`,
      `ImportWarningsMore`;
    - Game view: `ImportGuideButton`.
  - Visible copy:
    - Title: `"Import guide for {game title}"`; busy text: `"Checking {file name}…"`.
    - Buttons: `"Choose another file"` and `"Close"`.
    - `"Checking stopped. Choose another file to try again."` (Informational).
    - `"This file couldn't be checked. Choose another file."` (Error).
    - `"This PDF has no selectable text. Pages will show as images."` (Warning).
    - `"Enter a title of 1–200 characters."`
    - `"The file picker couldn't open. Try again."`
    - Game empty state: `"Import a guide for this game to start reading."`

- [ ] **Step 1: Pin and reference HeaderedControls**

In `Directory.Packages.props`, after the MetadataControl `PackageVersion`:

```xml
    <PackageVersion Include="CommunityToolkit.WinUI.Controls.HeaderedControls"
                    Version="8.2.251219" />
```

In `DesktopGuides.Production.csproj`, after the MetadataControl reference:

```xml
    <PackageReference Include="CommunityToolkit.WinUI.Controls.HeaderedControls" />
```

Stage, restore Production on the host, and copy its lock file back:

```bash
s 'cd /d E:\work\desktop-guides\t06-1 && dotnet restore src\DesktopGuides.Production\DesktopGuides.Production.csproj --force-evaluate -p:Platform=x64'
scp pcsx2-win:E:/work/desktop-guides/t06-1/src/DesktopGuides.Production/packages.lock.json src/DesktopGuides.Production/packages.lock.json
git diff -- src/DesktopGuides.Production/packages.lock.json
```

Expected: the lock file gains `CommunityToolkit.WinUI.Controls.HeaderedControls`
8.2.251219. Any transitive `CommunityToolkit.WinUI.*` entries it adds are at
8.2.251219, and no existing package changes version.

- [ ] **Step 2: Add the dialog styles**

Append to `Styles/Typography.xaml`, before `</ResourceDictionary>`:

```xml
    <Style x:Key="DesktopGuidesDialogHeadingStyle"
           TargetType="TextBlock"
           BasedOn="{StaticResource DesktopGuidesEmptyTitleStyle}" />

    <Style x:Key="DesktopGuidesEncodingSampleStyle"
           TargetType="TextBlock"
           BasedOn="{StaticResource DesktopGuidesSecondaryBodyStyle}">
        <Setter Property="FontFamily" Value="Cascadia Mono, Consolas" />
        <Setter Property="TextWrapping" Value="NoWrap" />
        <Setter Property="TextTrimming" Value="Clip" />
        <Setter Property="IsTextSelectionEnabled" Value="False" />
    </Style>
```

`DesktopGuidesDialogHeadingStyle` is BodyStrong with `HeadingLevel`
Level2, inherited from the empty-title style, and names its use in the
dialog.

- [ ] **Step 3: Write the dialog XAML**

`src/DesktopGuides.Production/ImportGuideDialog.xaml`:

```xml
<ContentDialog
    x:Class="DesktopGuides.Production.ImportGuideDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:DesktopGuides.Production"
    xmlns:toolkit="using:CommunityToolkit.WinUI.Controls"
    SecondaryButtonText="Choose another file"
    CloseButtonText="Close"
    DefaultButton="None"
    AutomationProperties.AutomationId="ImportGuideDialog">
    <ScrollViewer MaxHeight="520"
                  VerticalScrollBarVisibility="Auto">
        <StackPanel Width="480"
                    Spacing="{StaticResource DesktopGuidesSpacing12}">
            <StackPanel x:Name="ImportBusy"
                        Style="{StaticResource DesktopGuidesBusyRowStyle}"
                        Visibility="Collapsed"
                        AutomationProperties.AutomationId="ImportBusy">
                <ProgressRing x:Name="ImportProgress"
                              Width="20"
                              Height="20"
                              IsActive="False"
                              AutomationProperties.AutomationId="ImportProgress" />
                <TextBlock x:Name="ImportBusyText"
                           VerticalAlignment="Center"
                           Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                           AutomationProperties.LiveSetting="Polite"
                           AutomationProperties.AutomationId="ImportBusyText" />
                <Button x:Name="ImportCancel"
                        Content="Cancel"
                        Click="CancelClicked"
                        Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
                        AutomationProperties.AutomationId="ImportCancel" />
            </StackPanel>

            <InfoBar x:Name="ImportStatus"
                     Style="{StaticResource DesktopGuidesStatusInfoBarStyle}"
                     IsOpen="False"
                     IsClosable="False"
                     AutomationProperties.AutomationId="ImportStatus" />

            <StackPanel x:Name="ImportPreview"
                        Visibility="Collapsed"
                        Spacing="{StaticResource DesktopGuidesSpacing12}"
                        AutomationProperties.AutomationId="ImportPreview">
                <StackPanel Spacing="{StaticResource DesktopGuidesSpacing4}">
                    <TextBox x:Name="GuideTitleInput"
                             Header="Title"
                             TextChanged="TitleChanged"
                             AutomationProperties.AutomationId="GuideTitleInput" />
                    <TextBlock x:Name="GuideTitleFeedback"
                               Visibility="Collapsed"
                               Style="{StaticResource DesktopGuidesMetadataStyle}"
                               AutomationProperties.LiveSetting="Polite"
                               AutomationProperties.AutomationId="GuideTitleFeedback" />
                </StackPanel>

                <InfoBar x:Name="ImportNoTextWarning"
                         Severity="Warning"
                         IsOpen="False"
                         IsClosable="False"
                         Message="This PDF has no selectable text. Pages will show as images."
                         AutomationProperties.Name="This PDF has no selectable text. Pages will show as images."
                         AutomationProperties.AutomationId="ImportNoTextWarning" />

                <RadioButtons x:Name="EncodingOptions"
                              Header="Text encoding"
                              Visibility="Collapsed"
                              SelectionChanged="EncodingChanged"
                              AutomationProperties.AutomationId="EncodingOptions">
                    <RadioButton x:Name="ImportEncodingCp437"
                                 AutomationProperties.Name="DOS (CP437)"
                                 AutomationProperties.AutomationId="ImportEncodingCp437">
                        <StackPanel Spacing="{StaticResource DesktopGuidesSpacing4}">
                            <TextBlock Text="DOS (CP437)"
                                       Style="{StaticResource DesktopGuidesBodyStyle}" />
                            <TextBlock x:Name="ImportEncodingCp437Sample"
                                       Style="{StaticResource DesktopGuidesEncodingSampleStyle}"
                                       AutomationProperties.AutomationId="ImportEncodingCp437Sample" />
                        </StackPanel>
                    </RadioButton>
                    <RadioButton x:Name="ImportEncodingWindows1252"
                                 AutomationProperties.Name="Western (Windows-1252)"
                                 AutomationProperties.AutomationId="ImportEncodingWindows1252">
                        <StackPanel Spacing="{StaticResource DesktopGuidesSpacing4}">
                            <TextBlock Text="Western (Windows-1252)"
                                       Style="{StaticResource DesktopGuidesBodyStyle}" />
                            <TextBlock x:Name="ImportEncodingWindows1252Sample"
                                       Style="{StaticResource DesktopGuidesEncodingSampleStyle}"
                                       AutomationProperties.AutomationId="ImportEncodingWindows1252Sample" />
                        </StackPanel>
                    </RadioButton>
                </RadioButtons>

                <StackPanel x:Name="NativeDetails"
                            Spacing="{StaticResource DesktopGuidesSpacing8}">
                    <TextBlock x:Name="ImportDetailsHeading"
                               Text="File details"
                               Style="{StaticResource DesktopGuidesDialogHeadingStyle}"
                               AutomationProperties.AutomationId="ImportDetailsHeading" />
                    <StackPanel x:Name="ImportDetailsRows"
                                Spacing="{StaticResource DesktopGuidesSpacing4}">
                        <Grid ColumnSpacing="{StaticResource DesktopGuidesSpacing12}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="120" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Format" Style="{StaticResource DesktopGuidesMetadataStyle}" />
                            <TextBlock x:Name="ImportFormat" Grid.Column="1"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       AutomationProperties.AutomationId="ImportFormat" />
                        </Grid>
                        <Grid ColumnSpacing="{StaticResource DesktopGuidesSpacing12}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="120" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="File" Style="{StaticResource DesktopGuidesMetadataStyle}" />
                            <TextBlock x:Name="ImportFileName" Grid.Column="1"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       AutomationProperties.AutomationId="ImportFileName" />
                        </Grid>
                        <Grid ColumnSpacing="{StaticResource DesktopGuidesSpacing12}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="120" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Size" Style="{StaticResource DesktopGuidesMetadataStyle}" />
                            <TextBlock x:Name="ImportSize" Grid.Column="1"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       AutomationProperties.AutomationId="ImportSize" />
                        </Grid>
                        <Grid x:Name="ImportEncodingRow"
                              Visibility="Collapsed"
                              ColumnSpacing="{StaticResource DesktopGuidesSpacing12}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="120" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Encoding" Style="{StaticResource DesktopGuidesMetadataStyle}" />
                            <TextBlock x:Name="ImportEncodingValue" Grid.Column="1"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       AutomationProperties.AutomationId="ImportEncodingValue" />
                        </Grid>
                        <Grid x:Name="ImportAssetsRow"
                              Visibility="Collapsed"
                              ColumnSpacing="{StaticResource DesktopGuidesSpacing12}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="120" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Linked files" Style="{StaticResource DesktopGuidesMetadataStyle}" />
                            <TextBlock x:Name="ImportAssets" Grid.Column="1"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       AutomationProperties.AutomationId="ImportAssets" />
                        </Grid>
                        <Grid x:Name="ImportPagesRow"
                              Visibility="Collapsed"
                              ColumnSpacing="{StaticResource DesktopGuidesSpacing12}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="120" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Pages" Style="{StaticResource DesktopGuidesMetadataStyle}" />
                            <TextBlock x:Name="ImportPages" Grid.Column="1"
                                       Style="{StaticResource DesktopGuidesBodyStyle}"
                                       AutomationProperties.AutomationId="ImportPages" />
                        </Grid>
                    </StackPanel>
                </StackPanel>

                <local:AutomationGroup x:Name="ImportDetailsGroup"
                                       Visibility="Collapsed"
                                       AutomationProperties.Name="File details"
                                       AutomationProperties.AutomationId="ImportDetailsGroup">
                    <toolkit:HeaderedContentControl x:Name="ImportDetailsHeadered"
                                                    Header="File details" />
                </local:AutomationGroup>

                <local:AutomationGroup x:Name="ImportWarningsGroup"
                                       Visibility="Collapsed"
                                       AutomationProperties.Name="Warnings"
                                       AutomationProperties.AutomationId="ImportWarningsGroup">
                    <toolkit:HeaderedContentControl Header="Warnings">
                        <StackPanel x:Name="ImportWarningList"
                                    Spacing="{StaticResource DesktopGuidesSpacing4}" />
                    </toolkit:HeaderedContentControl>
                </local:AutomationGroup>
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</ContentDialog>
```

The detail rows live in `ImportDetailsRows`. The code-behind moves that one
panel between `NativeDetails` (native heading) and `ImportDetailsHeadered`
(the group), so the values and AutomationIds exist once.

- [ ] **Step 4: Write the dialog code-behind**

`src/DesktopGuides.Production/ImportGuideDialog.xaml.cs`:

```csharp
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ImportGuideDialog : ContentDialog
{
    private const string StoppedMessage = "Checking stopped. Choose another file to try again.";
    private const string FailedMessage = "This file couldn't be checked. Choose another file.";
    internal const string PickerFailedMessage = "The file picker couldn't open. Try again.";
    private readonly IGuideImportValidator validator;
    private readonly Func<Task<string?>> pickFile;
    private string? firstPath;
    private CancellationTokenSource? check;
    private Task running = Task.CompletedTask;
    private ImportNeedsTxtEncoding? needsEncoding;
    private int generation;
    private bool closing;
    private bool picking;
    private bool settingEncoding;

    internal ImportGuideDialog(
        string gameTitle, string path, IGuideImportValidator validator, Func<Task<string?>> pickFile)
    {
        InitializeComponent();
        Title = $"Import guide for {gameTitle}";
        this.validator = validator;
        this.pickFile = pickFile;
        firstPath = path;
        Opened += DialogOpened;
        Closing += DialogClosing;
        SecondaryButtonClick += ChooseAnotherClicked;
    }

    /// <summary>The checked file, once the preview is complete. T06.3 imports it.</summary>
    internal ImportManifest? Manifest { get; private set; }

    private void DialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        if (firstPath is { } path)
        {
            firstPath = null;
            Check(path);
        }
    }

    private async void DialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        closing = true;
        if (running.IsCompleted)
        {
            return;
        }
        var deferral = args.GetDeferral();
        check?.Cancel();
        try
        {
            // RunAsync handles every exception, so this only waits.
            await running;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void CancelClicked(object sender, RoutedEventArgs args) => check?.Cancel();

    private void ChooseAnotherClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        _ = ChooseAnotherAsync();
    }

    private async Task ChooseAnotherAsync()
    {
        if (picking || closing)
        {
            return;
        }
        picking = true;
        IsSecondaryButtonEnabled = false;
        try
        {
            string? path;
            try
            {
                path = await pickFile();
            }
            catch (Exception)
            {
                if (!closing)
                {
                    ShowStatus(InfoBarSeverity.Error, PickerFailedMessage);
                }
                return;
            }
            if (path is not null && !closing)
            {
                Check(path);
            }
        }
        finally
        {
            picking = false;
            if (!closing)
            {
                IsSecondaryButtonEnabled = true;
            }
        }
    }

    private void Check(string path)
    {
        string name = Path.GetFileName(path);
        Manifest = null;
        needsEncoding = null;
        ImportPreview.Visibility = Visibility.Collapsed;
        Track(RunAsync($"Checking {name}…", async (current, token) =>
        {
            ImportInspection inspection = await validator.InspectAsync(path, token);
            if (current != generation || closing)
            {
                return;
            }
            switch (inspection)
            {
                case ImportReady ready:
                    ShowPreview(ready.Manifest.Source, ready.Manifest.SuggestedTitle, ready.Manifest.Format);
                    ShowManifest(ready.Manifest);
                    break;
                case ImportNeedsTxtEncoding needs:
                    ShowPreview(needs.Source, needs.SuggestedTitle, GuideFormat.Txt);
                    ShowEncodingChoice(needs);
                    break;
            }
            GuideTitleInput.Focus(FocusState.Programmatic);
        }));
    }

    private void EncodingChanged(object sender, SelectionChangedEventArgs args)
    {
        if (settingEncoding || needsEncoding is not { } needs || EncodingOptions.SelectedIndex < 0)
        {
            return;
        }
        int codePage = EncodingOptions.SelectedIndex == 0 ? 437 : 1252;
        Manifest = null;
        ImportEncodingRow.Visibility = Visibility.Collapsed;
        Track(RunAsync($"Checking {needs.Source.FileName}…", async (current, token) =>
        {
            TxtImportManifest manifest = await validator.ResolveTxtEncodingAsync(needs, codePage, token);
            if (current != generation || closing)
            {
                return;
            }
            ShowManifest(manifest);
        }, keepPreview: true));
    }

    private void Track(Task task) => running = Task.WhenAll(running, task);

    private async Task RunAsync(
        string busyText, Func<int, CancellationToken, Task> work, bool keepPreview = false)
    {
        check?.Cancel();
        int current = ++generation;
        using CancellationTokenSource cancel = new();
        check = cancel;
        ShowBusy(busyText, keepPreview);
        try
        {
            await work(current, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (current == generation && !closing)
            {
                ShowStatus(InfoBarSeverity.Informational, StoppedMessage);
            }
        }
        catch (GuideImportException error)
        {
            if (current == generation && !closing)
            {
                ShowStatus(InfoBarSeverity.Error, error.Message);
            }
        }
        catch (Exception)
        {
            // The picked file is untrusted: any unexpected failure is a
            // per-file error, not an app crash.
            if (current == generation && !closing)
            {
                ShowStatus(InfoBarSeverity.Error, FailedMessage);
            }
        }
        finally
        {
            if (ReferenceEquals(check, cancel))
            {
                check = null;
            }
            if (current == generation && !closing)
            {
                HideBusy();
            }
        }
    }

    private void ShowBusy(string text, bool keepPreview)
    {
        ImportStatus.IsOpen = false;
        if (!keepPreview)
        {
            ImportPreview.Visibility = Visibility.Collapsed;
        }
        EncodingOptions.IsEnabled = false;
        ImportBusyText.Text = text;
        ImportProgress.IsActive = true;
        ImportBusy.Visibility = Visibility.Visible;
    }

    private void HideBusy()
    {
        ImportBusy.Visibility = Visibility.Collapsed;
        ImportProgress.IsActive = false;
        EncodingOptions.IsEnabled = true;
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        Manifest = null;
        needsEncoding = null;
        ImportPreview.Visibility = Visibility.Collapsed;
        ImportStatus.Severity = severity;
        ImportStatus.Message = message;
        AutomationProperties.SetName(ImportStatus, message);
        ImportStatus.IsOpen = true;
    }

    private void ShowPreview(ImportSource source, string title, GuideFormat format)
    {
        GuideTitleInput.Text = title;
        ValidateTitle();
        ImportFormat.Text = ImportPresentation.FormatLabel(format);
        ImportFileName.Text = source.FileName;
        ImportSize.Text = ImportPresentation.FormatSize(source.ByteCount);
        ImportEncodingRow.Visibility = Visibility.Collapsed;
        ImportAssetsRow.Visibility = Visibility.Collapsed;
        ImportPagesRow.Visibility = Visibility.Collapsed;
        ImportNoTextWarning.IsOpen = false;
        EncodingOptions.Visibility = Visibility.Collapsed;
        settingEncoding = true;
        try
        {
            EncodingOptions.SelectedIndex = -1;
        }
        finally
        {
            settingEncoding = false;
        }
        ShowWarnings([]);
        ImportPreview.Visibility = Visibility.Visible;
    }

    private void ShowEncodingChoice(ImportNeedsTxtEncoding needs)
    {
        needsEncoding = needs;
        ImportEncodingCp437Sample.Text = needs.Samples.First(s => s.CodePage == 437).Text;
        ImportEncodingWindows1252Sample.Text = needs.Samples.First(s => s.CodePage == 1252).Text;
        EncodingOptions.Visibility = Visibility.Visible;
    }

    private void ShowManifest(ImportManifest manifest)
    {
        Manifest = manifest;
        switch (manifest)
        {
            case TxtImportManifest txt:
                ImportEncodingValue.Text = ImportPresentation.EncodingLabel(txt.CodePage);
                ImportEncodingRow.Visibility = Visibility.Visible;
                break;
            case HtmlImportManifest html:
                string files = html.AssetCount == 1 ? "1 linked file" : $"{html.AssetCount} linked files";
                ImportAssets.Text = $"{files}, {ImportPresentation.FormatSize(html.TotalBytes)} in total";
                ImportAssetsRow.Visibility = Visibility.Visible;
                ShowWarnings(html.Warnings);
                break;
            case PdfImportManifest pdf:
                ImportPages.Text = pdf.PageCount == 1 ? "1 page" : $"{pdf.PageCount} pages";
                ImportPagesRow.Visibility = Visibility.Visible;
                ImportNoTextWarning.IsOpen = !pdf.HasText;
                break;
        }
    }

    // Warnings and details are headered groups only when both are shown.
    private void ShowWarnings(IReadOnlyList<ImportWarning> warnings)
    {
        ImportWarningList.Children.Clear();
        (IReadOnlyList<ImportWarning> shown, int hidden) = ImportPresentation.LimitWarnings(warnings);
        for (int index = 0; index < shown.Count; index++)
        {
            TextBlock row = new()
            {
                Text = ImportPresentation.WarningLine(shown[index]),
                Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            };
            AutomationProperties.SetAutomationId(row, $"ImportWarning{index}");
            ImportWarningList.Children.Add(row);
        }
        if (hidden > 0)
        {
            TextBlock more = new()
            {
                Text = ImportPresentation.MoreWarnings(hidden),
                Style = (Style)Application.Current.Resources["DesktopGuidesSecondaryBodyStyle"],
            };
            AutomationProperties.SetAutomationId(more, "ImportWarningsMore");
            ImportWarningList.Children.Add(more);
        }
        PlaceDetails(grouped: shown.Count > 0);
    }

    private void PlaceDetails(bool grouped)
    {
        if (ReferenceEquals(ImportDetailsHeadered.Content, ImportDetailsRows))
        {
            ImportDetailsHeadered.Content = null;
        }
        NativeDetails.Children.Remove(ImportDetailsRows);
        if (grouped)
        {
            ImportDetailsHeadered.Content = ImportDetailsRows;
        }
        else
        {
            NativeDetails.Children.Add(ImportDetailsRows);
        }
        NativeDetails.Visibility = grouped ? Visibility.Collapsed : Visibility.Visible;
        ImportDetailsGroup.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
        ImportWarningsGroup.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleChanged(object sender, TextChangedEventArgs args) => ValidateTitle();

    private void ValidateTitle()
    {
        bool valid = GuideTitle.TryCreate(GuideTitleInput.Text, out _);
        GuideTitleFeedback.Text = valid ? string.Empty : $"Enter a title of 1–{GuideTitle.TitleLimit} characters.";
        GuideTitleFeedback.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

`App.xaml` merges `Styles/Typography.xaml`, so the
`Application.Current.Resources` lookups for the warning-row styles resolve
through its merged dictionaries.

- [ ] **Step 5: Add the Game view button and empty-state text**

In `ShellWindow.xaml`, replace the Guides header grid's column definitions
and add the button after `OpenSelectedGuideButton`:

```xml
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
```

```xml
                    <Button x:Name="ImportGuideButton"
                            Grid.Column="2"
                            Content="Import guide"
                            IsEnabled="False"
                            Style="{StaticResource DesktopGuidesSecondaryActionButtonStyle}"
                            Click="ImportGuideClicked"
                            AutomationProperties.AutomationId="ImportGuideButton" />
```

In `GameEmptyState`, change the secondary line's `Text` from `"Guides you
import for this game will appear here."` to `"Import a guide for this game to
start reading."`.

- [ ] **Step 6: Wire the picker and dialog into ShellWindow**

In `ShellWindow.xaml.cs`:

1. Add usings, in the existing order, and no `using Windows.Storage.Pickers;`:

```csharp
using DesktopGuides.Core.Import;
using DesktopGuides.Infrastructure.Import;
using Microsoft.Windows.Storage.Pickers;
```

2. Add fields after `activeAddGameDialog`:

```csharp
    private readonly GuideImportValidator importValidator = new();
    private bool importRequested;
    private ImportGuideDialog? activeImportDialog;
```

3. In `WindowClosing`, after `activeAddGameDialog?.Hide();`:

```csharp
        activeImportDialog?.Hide();
```

4. In `AddGameClicked`, after `navigator.OpenGame(added.Game.Id); await RenderCurrentAsync();`,
   and in `ShowManualAddAsync`, after `navigator.OpenGame(created.Id); await RenderCurrentAsync();`, add:

```csharp
                        ImportGuideButton.Focus(FocusState.Programmatic);
```

   (Indent to match each site.)

5. In `RenderCurrentAsync`'s game route, after `EditGameButton.IsEnabled = false;`
   add `ImportGuideButton.IsEnabled = false;`, and after
   `EditGameButton.IsEnabled = true;` add
   `ImportGuideButton.IsEnabled = !importRequested;`.

6. Add after `EditGameClicked`:

```csharp
    private async void ImportGuideClicked(object sender, RoutedEventArgs args)
    {
        if (importRequested || closeRequested || navigator.Current is not GameRoute route)
        {
            return;
        }
        importRequested = true;
        ImportGuideButton.IsEnabled = false;
        try
        {
            await RunNavigationAsync(async () =>
            {
                if (closeRequested ||
                    navigator.Current is not GameRoute current ||
                    current.GameId != route.GameId)
                {
                    return;
                }
                Game? game = await RequireRepository().GetGameAsync(route.GameId);
                if (closeRequested)
                {
                    return;
                }
                if (game is null)
                {
                    ShowWarningStatus("This game is no longer in your library.");
                    return;
                }
                string? path;
                try
                {
                    path = await PickGuideFileAsync();
                }
                catch (COMException)
                {
                    ShowWarningStatus(ImportGuideDialog.PickerFailedMessage);
                    return;
                }
                if (path is null || closeRequested)
                {
                    return;
                }
                ImportGuideDialog dialog = new(game.Title, path, importValidator, PickGuideFileAsync)
                {
                    XamlRoot = Navigation.XamlRoot
                };
                DialogSurface.Apply(dialog, EffectiveMaterial);
                activeImportDialog = dialog;
                try
                {
                    await dialog.ShowAsync();
                }
                finally
                {
                    activeImportDialog = null;
                }
            });
        }
        finally
        {
            importRequested = false;
            if (!closeRequested && navigator.Current is GameRoute)
            {
                ImportGuideButton.IsEnabled = true;
                ImportGuideButton.Focus(FocusState.Programmatic);
            }
        }
    }

    private async Task<string?> PickGuideFileAsync()
    {
        FileOpenPicker picker = new(AppWindow.Id);
        foreach (string type in (string[])[".txt", ".html", ".htm", ".pdf"])
        {
            picker.FileTypeFilter.Add(type);
        }
        PickFileResult? result = await picker.PickSingleFileAsync();
        return result?.Path;
    }
```

`System.Runtime.InteropServices` is already imported, for `COMException`.
The `finally` covers picker Cancel, dialog Close and the "game is gone"
case: each re-enables the button and returns focus to it.

- [ ] **Step 7: Build**

Stage, then run **Production build**.
Expected: build succeeds. `git diff` must show no `using Windows.Storage.Pickers;`.
Also run **Core tests** and **Infrastructure tests** to confirm that nothing
shared changed. Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Directory.Packages.props src/DesktopGuides.Production
git commit -m "feat(p1): add the Import guide dialog to the Game view" \
  -m "An Import guide button opens the system file picker (TXT, HTML, HTM, PDF) and a preview dialog: an editable title with inline validation, file details, a CP437 or Windows-1252 choice with samples, HTML warnings (20 shown, then a count) and a no-text warning for PDFs. Checks show a cancelable busy row, errors replace the preview, and Choose another file rechecks in the same dialog. Adding a game focuses Import guide. Pins Toolkit HeaderedControls 8.2.251219." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Installed `import-preview` smoke through the real picker

This task holds the test for Task 4. **TDD skip, stated explicitly:** as in
T05.4, the installed smoke runs only in CI `production-shell-ui` (or on the
host with a CI-built MSIX), so there is no cheap red run before Task 4's
code exists. The smoke is the gate: every assertion names an AutomationId or
copy string from Task 4's Produces block, and a failure uploads its JSON and
`failure` screenshot.

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`
- Modify: `tools/p1/windows_shell_install.ps1`

**Interfaces:**
- Consumes: the AutomationIds and copy from Task 4's Produces block, the
  ContentDialog button ids `SecondaryButton` and `CloseButton`, the smoke
  helpers `Find-ById`, `Wait-Name`, `Wait-Status`, `Wait-VisibleById`,
  `Wait-HiddenById`, `Wait-EnabledById`, `Click-Element`, `Invoke-Element`,
  `Select-Element`, `Assert-Absent` and `Save-WindowScreenshot`, and the
  install helpers `Invoke-ShellSeed`, `Run-ShellSmoke`,
  `Start-InstalledShell`, `Close-InstalledShell`, `Get-AppThemePreference`,
  `Set-AppThemePreference` and `Restore-AppThemePreference`.
- Produces:
  - ShellSeed `seed-import <app-data-root>`, which adds "Import Test Game"
    (platform "PC") to an empty library.
  - ShellSeed `describe-import <app-data-root>`, which prints one JSON line:
    `{"Guides":n,"FileOperations":n,"StagingEntries":n,"ContentEntries":n}`.
  - Smoke mode `import-preview`, install switch `-ImportOnly`, and report
    keys `importLight`, `importDark` and `importState`.

- [ ] **Step 1: Add the seed commands**

In `Program.cs`, insert before the
`if (args.Length == 3 && args[0] == "check-igdb-fields")` block:

```csharp
if (args.Length == 2 && args[0] == "describe-import")
{
    ManagedPathResolver importPaths = new(args[1]);
    using SqliteConnection importConnection = new(new SqliteConnectionStringBuilder
    {
        DataSource = importPaths.DatabasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString());
    importConnection.Open();
    long CountRows(string table)
    {
        using SqliteCommand command = importConnection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }
    int CountEntries(string root) => Directory.Exists(root)
        ? Directory.EnumerateFileSystemEntries(root).Count()
        : 0;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Guides = CountRows("Guides"),
        FileOperations = CountRows("FileOperations"),
        StagingEntries = CountEntries(importPaths.StagingRoot),
        ContentEntries = CountEntries(importPaths.ContentRoot),
    }));
    return 0;
}
```

Only the constant table names `Guides` and `FileOperations` are
interpolated. The check counts entries **inside** the staging and content
roots, because startup may create the empty roots themselves. An empty root
still means "no staged or managed file", which is what the spec's "no
`.staging` folder" is guarding.

Add `"seed-import"` to the mode check:

```csharp
    args[0] is not ("seed" or "stale" or "seed-long" or "seed-second" or
        "seed-design" or "seed-catalog" or "seed-import"))
```

and update the usage string:

```csharp
        "Usage: DesktopGuides.ShellSeed seed|stale|seed-long|seed-second|seed-design|seed-catalog|seed-import " +
        "<app-data-root> " +
        "or seed-linked-game|describe-providers|describe-import <app-data-root> " +
```

After the `seed-catalog` block, add:

```csharp
if (args[0] == "seed-import")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The import seed needs an empty library.");
    }
    Game importGame = await repository.AddGameAsync("Import Test Game", "PC", null);
    Console.WriteLine($"Seeded import game {importGame.Id:N}.");
    return 0;
}
```

- [ ] **Step 2: Build the seed tool**

Stage, then run **Seed build**. Expected: build succeeds. Then check both
commands on a scratch root:

```bash
s 'cd /d E:\work\desktop-guides\t06-1 && dotnet run --project tools\p1\DesktopGuides.ShellSeed -c Release --no-restore -- seed-import E:\work\desktop-guides\t06-1-seed && dotnet run --project tools\p1\DesktopGuides.ShellSeed -c Release --no-restore -- describe-import E:\work\desktop-guides\t06-1-seed'
```

Expected: `Seeded import game …`, then
`{"Guides":0,"FileOperations":0,"StagingEntries":0,"ContentEntries":0}`.
Delete `E:\work\desktop-guides\t06-1-seed` afterwards.

- [ ] **Step 3: Add the `import-preview` smoke mode**

In `windows_shell_ui_smoke.ps1`, add `'import-preview'` to the `Mode`
`ValidateSet`, just before `'provider-live'`.

Insert this branch before `elseif ($Mode -eq 'long-list') {`:

```powershell
    elseif ($Mode -eq 'import-preview') {
        $fixtureRoot = (Resolve-Path -LiteralPath (
            Join-Path $PSScriptRoot '..\..\tests\fixtures\p0')).Path
        $uia = [System.Windows.Automation.AutomationElement]

        function Wait-FilePicker {
            # The picker runs in the app's process as a #32770 window. Look
            # among top-level windows first, then under the shell window.
            $condition = [System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new(
                    $uia::ClassNameProperty, '#32770'),
                [System.Windows.Automation.PropertyCondition]::new(
                    $uia::ProcessIdProperty, $process.Id))
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $picker = $uia::RootElement.FindFirst(
                    [System.Windows.Automation.TreeScope]::Children, $condition)
                if (-not $picker) {
                    $picker = $root.FindFirst(
                        [System.Windows.Automation.TreeScope]::Children, $condition)
                }
                if ($picker) { return $picker }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw 'The system Open dialog did not appear.'
        }

        function Find-InPicker($picker, [string] $id, $controlType) {
            $condition = [System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new(
                    $uia::AutomationIdProperty, $id),
                [System.Windows.Automation.PropertyCondition]::new(
                    $uia::ControlTypeProperty, $controlType))
            $element = $picker.FindFirst($scope, $condition)
            if (-not $element) { throw "The Open dialog has no '$id' $($controlType.ProgrammaticName)." }
            return $element
        }

        function Wait-PickerClosed($picker) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                try {
                    if ($picker.Current.ProcessId -ne $process.Id) { return }
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                    return
                }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw 'The system Open dialog did not close.'
        }

        function Choose-PickerFile([string] $relativePath) {
            $picker = Wait-FilePicker
            $fileName = Find-InPicker $picker '1148' ([System.Windows.Automation.ControlType]::Edit)
            $fileName.GetCurrentPattern(
                [System.Windows.Automation.ValuePattern]::Pattern).SetValue(
                (Join-Path $fixtureRoot $relativePath))
            Invoke-Element (Find-InPicker $picker '1' ([System.Windows.Automation.ControlType]::Button))
            Wait-PickerClosed $picker
        }

        function Cancel-Picker {
            $picker = Wait-FilePicker
            Invoke-Element (Find-InPicker $picker '2' ([System.Windows.Automation.ControlType]::Button))
            Wait-PickerClosed $picker
        }

        function Select-ById([string] $id) {
            $element = Wait-VisibleById $id
            $element.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            return $element
        }

        # The dialog content scrolls, so rows below the fold report
        # IsOffscreen. Collapsed elements leave the UIA tree, so presence
        # alone shows the app made them visible.
        function Wait-PresentById([string] $id) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $element = Find-ById $id
                if ($element) { return $element }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw "Expected '$id' to be shown."
        }

        function Wait-Text([string] $id, [string] $expected) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $element = Find-ById $id
                if ($element -and $element.Current.Name -eq $expected) { return $element }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw "Expected '$id' named '$expected'."
        }

        function Wait-FocusedId([string] $id) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $focused = $uia::FocusedElement
                if ($focused -and $focused.Current.AutomationId -eq $id) { return }
                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)
            throw "Expected keyboard focus on '$id'."
        }

        Select-Element 'Import Test Game'
        [void](Wait-Name 'GameHeading' 'Import Test Game')
        [void](Wait-Status 'Game ready.')
        Click-Element (Wait-EnabledById 'ImportGuideButton')
        Choose-PickerFile 'txt-legacy.txt'
        [void](Wait-VisibleById 'ImportGuideDialog')
        [void](Wait-Text 'ImportFileName' 'txt-legacy.txt')
        [void](Wait-Text 'ImportFormat' 'Text (TXT)')
        $title = (Wait-VisibleById 'GuideTitleInput').GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($title -ne 'txt-legacy') { throw "Expected the suggested title 'txt-legacy', got '$title'." }
        [void](Wait-PresentById 'ImportEncodingWindows1252')
        $sample = (Wait-VisibleById 'ImportEncodingCp437Sample').Current.Name
        if ($sample -notlike '*Guide*') { throw "The CP437 sample was '$sample'." }
        $cp437 = Select-ById 'ImportEncodingCp437'
        [void](Wait-Text 'ImportEncodingValue' 'DOS (CP437)')
        if (-not $cp437.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) {
            throw 'CP437 was not selected.'
        }
        $report.importTxtScreenshot = Save-WindowScreenshot 'import-txt'
        $report.phases += 'import-txt'

        Click-Element (Wait-EnabledById 'SecondaryButton')
        Choose-PickerFile 'html-static\guide.html'
        [void](Wait-Text 'ImportFormat' 'Web page (HTML)')
        [void](Wait-VisibleById 'ImportDetailsHeading')
        $assets = (Wait-PresentById 'ImportAssets').Current.Name
        if ($assets -notlike '3 linked files, * in total') { throw "The linked files row was '$assets'." }
        foreach ($id in @('ImportDetailsGroup', 'ImportWarningsGroup', 'ImportEncodingCp437')) {
            if (Find-ById $id) { throw "'$id' was shown for a web page without warnings." }
        }
        $report.phases += 'import-html'

        Click-Element (Wait-EnabledById 'SecondaryButton')
        Choose-PickerFile 'html-hostile\guide.html'
        [void](Wait-VisibleById 'ImportDetailsGroup')
        [void](Wait-PresentById 'ImportWarningsGroup')
        [void](Wait-PresentById 'ImportWarning0')
        if (Find-ById 'ImportDetailsHeading') { throw 'The native details heading was shown with groups.' }
        $report.importWarningsScreenshot = Save-WindowScreenshot 'import-warnings'
        $report.phases += 'import-html-warnings'

        Click-Element (Wait-EnabledById 'SecondaryButton')
        Choose-PickerFile 'pdf-locked.pdf'
        [void](Wait-Name 'ImportStatus' "Password-protected PDFs aren't supported. Remove the password and import again.")
        Assert-Absent 'ImportPreview'
        $report.phases += 'import-pdf-locked'

        Invoke-Element (Wait-EnabledById 'CloseButton')
        [void](Wait-HiddenById 'ImportGuideDialog')
        Wait-FocusedId 'ImportGuideButton'
        Click-Element (Wait-EnabledById 'ImportGuideButton')
        Cancel-Picker
        [void](Wait-EnabledById 'ImportGuideButton')
        Assert-Absent 'ImportGuideDialog'
        Wait-FocusedId 'ImportGuideButton'
        $report.phases += 'import-picker-cancel'
    }
```

Notes for the implementer:

- `Click-Element` (pointer input) opens the picker, not `Invoke-Element`.
  A UIA Invoke can block until a modal window started from the handler
  closes, which would hang the smoke.
- `1148` is the File name box, `1` is Open and `2` is Cancel in the common
  Open dialog. Invoking Open matches the spec's "press Enter", and
  invoking Cancel matches its "Esc". Both are the dialog's own controls,
  so nothing about the picker's layout is asserted.
- `Wait-Text` and `Wait-PresentById` check presence, not `IsOffscreen`.
  Two 8-line samples can push the details rows below the dialog's 520 px
  scroll area. For "not shown" checks, `Find-ById` returning nothing is
  the assertion, because `Assert-Absent` accepts an off-screen element.
- `html-hostile` has the same file name as `html-static`, so that step
  waits for `ImportDetailsGroup`, which the `html-static` preview doesn't
  show, rather than for `ImportFileName`.

In the `catch` block, add `$Mode -eq 'import-preview'` to the condition
that captures diagnostic names, and add `'ImportStatus'`,
`'ImportGuideDialog'` and `'ImportBusyText'` to its id list.

- [ ] **Step 4: Add `-ImportOnly` and the import scenarios to the install script**

In `windows_shell_install.ps1`, add the switch after `[switch] $CatalogOnly,`:

```powershell
    [switch] $ImportOnly,
```

In `Run-ShellSmoke`, give the new mode the same timeout as `catalog`:

```powershell
    $timeoutSeconds = if ($mode -like 'provider-*') { 240 }
        elseif ($mode -eq 'catalog' -or $mode -eq 'import-preview') { 120 }
        else { 60 }
```

After `Run-CatalogScenarios`, add:

```powershell
function Run-ImportScenarios {
    Invoke-ShellSeed @('seed-import', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.importLight = Run-ShellSmoke 'import-preview' -ResultName 'import-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.importDark = Run-ShellSmoke 'import-preview' -ResultName 'import-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
    $state = Invoke-ShellSeed @('describe-import', $dataRoot) | ConvertFrom-Json
    $report.importState = $state
    foreach ($name in @('Guides', 'FileOperations', 'StagingEntries', 'ContentEntries')) {
        if ($state.$name -ne 0) {
            throw "The import preview left $($state.$name) $name; expected none."
        }
    }
}
```

After the `if ($CatalogOnly) { ... }` early return, add:

```powershell
    if ($ImportOnly) {
        Run-ImportScenarios
        $report.success = $true
        return
    }
```

In the full run, after `Run-CatalogScenarios`, add:

```powershell
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-ImportScenarios
```

so the full run reads catalog, then import, then provider, each on a fresh
data root.

- [ ] **Step 5: Check the scripts parse and stay ASCII**

```bash
perl -ne 'print "$ARGV:$.\n" if /[^\x00-\x7F]/' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1 || true
s 'powershell -NoProfile -Command "foreach ($f in ''E:\work\desktop-guides\t06-1\tools\p1\windows_shell_ui_smoke.ps1'',''E:\work\desktop-guides\t06-1\tools\p1\windows_shell_install.ps1'') { $e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile($f,[ref]$null,[ref]$e); if ($e) { $e; exit 1 } }; ''parse-ok''"'
s 'cd /d E:\work\desktop-guides\t06-1 && for %f in (tools\p1\test_windows_*.ps1) do powershell -NoProfile -ExecutionPolicy Bypass -File %f || exit /b 1'
```

Expected: `perl` prints nothing, then `parse-ok`, then every guard prints
its pass line. The PDF message uses a straight apostrophe, so it
stays ASCII. No smoke step asserts the busy text, which contains `…`.

- [ ] **Step 6: Commit, push and read the CI result**

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): drive the import preview through the real picker" \
  -m "Adds ShellSeed seed-import and describe-import, the import-preview smoke mode and -ImportOnly. The smoke opens the system Open dialog from Import guide, enters fixture paths through UIA and checks the TXT encoding choice, HTML details with and without warnings, the password-protected PDF error, and picker Cancel with focus back on Import guide, in light and dark. Afterwards the library has no guides, file operations, or staged or managed files." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -u origin feat/p1-t06-1-import-preview
```

Pushing a shared branch needs the user's OK. Reading CI with `gh` also needs
it, because the GitHub MCP has no Actions tools. Once approved:

```bash
gh run list --branch feat/p1-t06-1-import-preview --limit 1
gh run view <run-id> --log-failed
gh run download <run-id> -n production-shell-ui -D /tmp/t06-1-ci
```

Expected: `core-tests`, `packages`, `production-packages` and
`production-shell-ui` pass. `import-light.json` and `import-dark.json` report
`success: true` and the phases `import-txt`, `import-html`,
`import-html-warnings`, `import-pdf-locked` and `import-picker-cancel`; the
install result's `importState` is all zeros. Copy the four screenshots
(`import-light.import-txt.png`, `import-light.import-warnings.png` and the
dark pair) for Task 6.

On failure, use superpowers:systematic-debugging with the uploaded JSON and
`failure` screenshot before changing code.

- [ ] **Step 7: Only if CI can't show the picker**

If the failure is `The system Open dialog did not appear.` and the
`failure` screenshot shows no Open dialog, the runner session can't show
the system picker (the design's named risk). Confirm on the host with the
CI-built MSIX, following `docs/p1/e2e-testing.md` for the scheduled-task
entry point, data backup and cleanup:

```bash
s 'powershell -NoProfile -ExecutionPolicy Bypass -File E:\work\desktop-guides\t06-1\tools\p1\windows_shell_install.ps1 -PackagePath <CI-built x64 msix> -ResultDirectory E:\work\desktop-guides\t06-1-results -ImportOnly'
```

If the host passes, ask the user before changing CI. The proposed change
is an `-SkipImport` switch that the CI job passes, with the host
`-ImportOnly` run recorded as the gate of record for this scenario. Record
the reason in the design's verification record and in Task 6.

---

### Task 6: Status, gate and verification docs

Documentation only; no TDD cycle.

**Files:**
- Modify: `docs/p1/t06-1-import-preview-design.md` (status line 3, new
  verification record at the end)
- Modify: `docs/p1/implementation-plan.md` (status prose after the T05.4
  paragraph, about line 614)
- Modify: `docs/progress.md` (date line 3, T06.1 + T06.2 row line 18)
- Modify: `docs/p1/e2e-testing.md` (Gates of record, Scenario checklist)
- Create: `docs/p1/evidence/t06-1-import-preview/` (the four screenshots and
  both smoke JSON files from Task 5)

**Interfaces:**
- Consumes: the CI run ID, test counts and artifact files from Task 5
  Step 6; the host `-ImportOnly` result if Task 5 Step 7 ran; every
  `Ruling:` line in the ledger.
- Produces: docs that record T06.1 and T06.2 as implemented and name the
  gate of record for the `import-preview` scenario.

- [ ] **Step 1: Copy the evidence**

```bash
mkdir -p docs/p1/evidence/t06-1-import-preview
cp /tmp/t06-1-ci/import-light.json /tmp/t06-1-ci/import-dark.json \
   /tmp/t06-1-ci/import-light.import-txt.png /tmp/t06-1-ci/import-light.import-warnings.png \
   /tmp/t06-1-ci/import-dark.import-txt.png /tmp/t06-1-ci/import-dark.import-warnings.png \
   docs/p1/evidence/t06-1-import-preview/
grep -l 'E:\\\\work\|C:\\\\Users' docs/p1/evidence/t06-1-import-preview/*.json || echo no-host-paths
```

Expected: `no-host-paths`, or runner paths only (`D:\a\...`). If the files
came from the host run, replace host user paths with `<data-root>` before
committing. Open each screenshot and confirm it shows the dialog, not the
picker or another window.

- [ ] **Step 2: Design doc status and verification record**

Replace line 3 of `docs/p1/t06-1-import-preview-design.md` with:

```markdown
Status: implemented and verified <date> on `feat/p1-t06-1-import-preview`;
see the [verification record](#t061--t062-verification-record).
```

Append:

```markdown
## T06.1 + T06.2 verification record

- **Unit tests.** `core-tests` in CI run <run-id>: <n> Core and <n>
  Infrastructure passes, including `GuideTitleTests`,
  `ImportPresentationTests`, `GuideImportValidatorTests` and
  `GuideImportValidatorHtmlPdfTests`.
- **Installed import scenario.** <`production-shell-ui` in the same run | host
  `-ImportOnly` with the CI x64 MSIX, because <reason from Task 5 Step 7>>,
  light and dark: `txt-legacy` title, format, file name and both encoding
  options, CP437 selected with its sample; `html-static` details under a
  native heading with 3 linked files; `html-hostile` details and warnings in
  headered groups; the password-protected PDF message; Close and picker
  Cancel returning focus to Import guide. `describe-import` afterwards: 0
  guides, 0 file operations, 0 staged and 0 managed entries.
- **Screenshots.** [TXT light](evidence/t06-1-import-preview/import-light.import-txt.png),
  [warnings light](evidence/t06-1-import-preview/import-light.import-warnings.png),
  [TXT dark](evidence/t06-1-import-preview/import-dark.import-txt.png),
  [warnings dark](evidence/t06-1-import-preview/import-dark.import-warnings.png).
- **Rulings.** <one line for each ruling in this plan's "Rulings against the
  spec" section and each ledger `Ruling:` line, with what it costs if wrong>.
```

Fill every `<…>` from the Task 5 results and the ledger. The record must not
keep any angle-bracket text, and it states the real outcome: if a check was
skipped or moved to the host, say which one and why.

- [ ] **Step 3: Implementation plan status**

In `docs/p1/implementation-plan.md`, change
`T05.4 was implemented and verified on 29 September 2026 in PR #16:` to
`T05.4 was merged through PR #16, merge commit
`515440a203843afee8eec6d48bce7f4d9bf8f8ca`, after verification on 29 September 2026:`
(the stale T05.4 line the design asks to correct). After that paragraph, add:

```markdown
The [T06.1 + T06.2 import preview design](t06-1-import-preview-design.md)
and [plan](t06-1-import-preview-plan.md) add the Import guide button, the
system file picker, the preview dialog and `GuideImportValidator` for TXT,
HTML and PDF. They were implemented and verified on <date>: CI run
<run-id> passed `core-tests`, both package builds and <the installed
`import-preview` scenario in light and dark | the other installed scenarios,
with `import-preview` passing on the host because <reason>>. Confirm,
copying and publication remain T06.3.
```

- [ ] **Step 4: Progress page**

In `docs/progress.md`, set line 3 to `Updated <date>.` (keep the rest of the
sentence) and replace row 18's state cell `Design in review.` with
`Implemented and verified on `feat/p1-t06-1-import-preview`; PR pending.`
and its evidence cell with:

```markdown
Picker, validated preview and typed import manifest for TXT, HTML and PDF. <CI run <run-id> | the host `-ImportOnly` run> passed the installed `import-preview` scenario in light and dark with no guide, file operation or staged file created; see the [verification record](p1/t06-1-import-preview-design.md#t061--t062-verification-record). T06.3 adds Confirm and publication.
```

The T05.4 row already records the PR #16 merge, so it needs no change.

- [ ] **Step 5: E2E gates and checklist**

In `docs/p1/e2e-testing.md`, **Gates of record**, change
`` `-CatalogOnly` and `-DesignOnly` run one scenario group `` to
`` `-CatalogOnly`, `-ImportOnly` and `-DesignOnly` run one scenario group ``.
If Task 5 Step 7 moved the scenario to the host, add after that paragraph:

```markdown
The `import-preview` scenario runs on the Windows host with `-ImportOnly`,
because <reason>. CI passes `-SkipImport`.
```

After the **Library catalog** checklist row, add:

```markdown
| Import preview | From a seeded game, Import guide opens the system Open dialog; UIA enters each fixture path. In light and dark: `txt-legacy` shows its title, format, file name and both encodings, and CP437 shows its sample; `html-static` shows details under a native heading; `html-hostile` shows details and warnings in headered groups; `pdf-locked` shows the password-protected message; Close and picker Cancel return focus to Import guide. Afterwards no guide, file operation, or staged or managed file exists. | T06.1, T06.2, TR06.1, TR11.3 |
```

- [ ] **Step 6: Check links and commit**

```bash
grep -n '<' docs/p1/t06-1-import-preview-design.md | grep -v '^[0-9]*:.*`' || echo no-placeholders
git add docs/p1/t06-1-import-preview-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/e2e-testing.md docs/p1/evidence/t06-1-import-preview
git commit -m "docs(p1): record T06.1 + T06.2 import preview verification" \
  -m "Marks the design implemented with its verification record and rulings, adds the installed import scenario to the E2E checklist and gates, updates the plan status and progress page, corrects the T05.4 merge line, and adds light and dark preview screenshots." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Expected: `no-placeholders`. Then check each new relative link resolves:

```bash
for f in docs/p1/evidence/t06-1-import-preview/*.png; do test -s "$f" && echo "ok $f"; done
```

Expected: four `ok` lines.
