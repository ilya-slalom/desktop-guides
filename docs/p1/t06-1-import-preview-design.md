# T06.1 import preview and T06.2 validation design

Status: implemented and verified 30 September 2026 on `feat/p1-t06-1-import-preview`;
see the [verification record](#t061--t062-verification-record).
Prerequisites T05.4 (catalog components, PR #16) and T04.4 (provider-linked
games) are merged.

## Intent

A person viewing a game picks a local TXT, HTML or PDF file and sees a
validated preview before anything is copied: a suggested title, the detected
format, the file name and size, a TXT encoding choice when the text is not
UTF-8, and HTML asset warnings. Problems are stated with a specific reason.
Confirm, copying and publication stay with T06.3, so this work creates no
guide, no `FileOperation` and no managed or staged file.

This work covers T06.1 (picker and preview) and T06.2 (format validation and
the typed import manifest) together, because the preview can't show anything
true without validation.

Traces: the T06.1 and T06.2 rows of
[implementation-plan.md](implementation-plan.md) and
[p1-technical-design.md](../p1-technical-design.md) (import preview and
validation, 552–573; size limits, 432; component rules, 357 and 378).

Decisions made during brainstorming:

- **Scope:** T06.1 and T06.2. Confirm and publication wait for T06.3.
- **Source:** local files through the system picker only. URLs and
  downloads (S22), multi-page HTML (S21), drag and drop, and "Open with"
  are out of scope.
- **Presentation:** an Import guide `ContentDialog`, not a full-page route.
- **Password-protected PDFs** are rejected with a clear reason. No password
  is requested or stored.
- **Installed UI test** drives the real system picker through UI Automation.
  Production code gets no test hooks.

## Core contracts

A new `src/DesktopGuides.Core/Import/` folder. Core defines what validation
produces and holds no file or UI code.

```csharp
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

Rules:

- **Cancellation** throws `OperationCanceledException`. It is not an
  `ImportIssue`.
- **Suggested title:** the file name without its extension, trimmed, and cut
  to 200 characters without splitting a surrogate pair. If nothing remains
  (for example a file named `.txt`), it's "Untitled guide".
- **`GuideTitle`** in `Core/Library`, following the `GameDetails` pattern:
  `TitleLimit = 200`, and `GuideTitle.Create(string)` trims the title and
  throws `ArgumentException` outside 1–200 characters. The dialog uses it for
  the edited title; T06.3 uses it again on Confirm.
- **Detail strings** name the file, the limit or the reason. They never
  contain file contents.
- **What the manifest holds:** only what the preview and T06.3 need. The full
  HTML asset list stays in Infrastructure. Reader code never sees a raw
  picker path.

## Validation for each format

`GuideImportValidator` in `src/DesktopGuides.Infrastructure/Import/`
implements `IGuideImportValidator`. It takes an optional `GuideImportLimits`
so tests can set small limits.

### Checks for every file

| Order | Check | Result on failure |
| --- | --- | --- |
| 1 | The path exists | `Missing` |
| 2 | Extension is `.txt`, `.html`, `.htm` or `.pdf` (case-insensitive) | `Unsupported`, without opening the file |
| 3 | Size is not zero | `Empty`: "The file is empty." |
| 4 | Size is within the format's limit | `TooLarge`, naming the limit |
| 5 | The file opens with read and shared-read access | `Unreadable` (access denied, sharing violation, or an offline OneDrive file that can't be downloaded) |

`ImportSource` records the size and last-write time. T06.3 compares both
before copying and reports `Changed` if either differs. T06.1 defines
`Changed` but doesn't raise it.

Size limits, from the technical design:

| Format | Limit |
| --- | --- |
| TXT | 64 MiB |
| PDF | 1 GiB |
| HTML entry | 16 MiB |
| HTML static assets | 2,048 files, 32 MiB each, 256 MiB for the whole tree |

### TXT

1. Read the whole file. It is at most 64 MiB.
2. A UTF-16 byte-order mark, or any NUL byte, is `UnsupportedEncoding`:
   "This text file isn't UTF-8. Save it as UTF-8 and import it again." The
   NUL check also catches binary files renamed to `.txt`.
3. `TextGuideDocument.Decode(bytes)` succeeds for a UTF-8 BOM or valid UTF-8:
   `ImportReady(TxtImportManifest(CodePage: null))`.
4. `EncodingSelectionRequiredException` gives `ImportNeedsTxtEncoding`, with
   the first 8 lines (at most 2 KB of source bytes) decoded as CP437 and as
   Windows-1252.
5. `ResolveTxtEncodingAsync(inspection, 437 | 1252)` checks that the source
   size and last-write time haven't changed, decodes the whole file with that
   code page, and returns a manifest with the code page stored. Any other code
   page throws `ArgumentOutOfRangeException`.

### HTML

This delegates to the existing `StaticHtmlImportValidator.PreviewAsync`, with
`StaticHtmlScanLimits` taken from `GuideImportLimits`.

| Scanner result | Import result |
| --- | --- |
| `StaticHtmlScanException(Limit)` | `TooLarge`, naming which limit was hit (entry size, asset count, per-asset size or total size) |
| `StaticHtmlValidationException(UnsafePath)` | `Unreadable`: "The guide refers to a file outside its folder." |
| `StaticHtmlValidationException(CaseCollision)` | `Unreadable`: "The guide's folder has files whose names differ only by case." |
| `FileNotFoundException` for the entry | `Missing` |
| `StaticHtmlPreviewWarning` | `ImportWarning(RelativePath or RawTarget, Message)`, passed through |

The validator keeps no state between calls. T06.3 scans again on Confirm and
checks the staged copy with `VerifyStagedAsync`, so a folder that changed
after the preview is caught there.

### PDF

1. The first 1 KB must contain `%PDF-`; otherwise the result is `Unreadable`:
   "This file isn't a readable PDF."
2. Open with PdfPig 0.1.16 through a read-only, shared-read `FileStream`.
   The file is never loaded fully into memory.
3. A PDF that needs a password is `Encrypted`: "Password-protected PDFs
   aren't supported. Remove the password and import again." A PDF that
   restricts copying but opens without a password (`pdf-access`) is accepted.
4. A parse failure, or zero pages, is `Unreadable`.
5. `HasText` is true when any of the first 5 pages contains a letter.
   Sampling only the first pages keeps a 1 GiB PDF quick. `HasText = false`
   is accepted, and the preview warns that the pages show as images.

The PdfPig 0.1.16 version is already centrally pinned. Only a
`PackageReference` in Infrastructure is new.

## Dialog flow

### Entry points

- An **Import guide** button in the Game view's Guides section header, next
  to Open. It is enabled whenever a game is shown.
- **Straight from Add game:** when Add game succeeds, the new game's view
  opens with focus on Import guide. The Game empty-state text becomes
  "Import a guide for this game to start reading."
- **Ctrl+O** is not part of this work. It stays with T16.1, and the button is
  its visible command.

### Flow

1. **Picker.** The button is disabled while the flow runs. A `FileOpenPicker`
   opens, initialized with the ShellWindow handle through
   `WinRT.Interop.InitializeWithWindow`, with the file types `.txt`, `.html`,
   `.htm` and `.pdf`. Cancelling the picker re-enables the button and returns
   focus to it; nothing else happens.
2. **Checking.** `ImportGuideDialog` (a `ContentDialog` with
   `DialogSurface.Apply`) opens immediately, titled "Import guide for
   ‹game title›". It shows the T05.4 busy row
   (`DesktopGuidesBusyRowStyle`) with "Checking ‹file name›…" and **Cancel**.
   Cancel, or closing the dialog, cancels the token.
3. **Preview.**
   - **Title:** a `TextBox` prefilled with the suggested title. Inline error
     text appears when `GuideTitle.Create` would reject it.
   - **File details:** format, file name and size (KB, MB or GB).
   - **TXT encoding choice**, only for `ImportNeedsTxtEncoding`: native
     `RadioButtons` for "DOS (CP437)" and "Western (Windows-1252)", each with
     its monospace sample. Nothing is selected at first. Selecting one runs
     `ResolveTxtEncodingAsync`, with the busy row shown while it runs.
   - **Warnings:** the HTML asset warnings as a list of path and reason. A
     PDF with no text gets a Warning `InfoBar` instead: "This PDF has no
     selectable text. Pages will show as images."
   - **Grouping:** `HeaderedContentControl` is used for **File details** and
     **Warnings** only when both groups are shown, which happens for HTML with
     warnings. Otherwise the groups use native headings (technical design
     357). The picker, the validation `InfoBar`, the busy row and the encoding
     choice stay native (technical design 378).
4. **Errors** replace the preview with an Error `InfoBar`
   (`DesktopGuidesStatusInfoBarStyle`) showing the `ImportIssue` message.
5. **Buttons.** **Choose another file** (secondary) reopens the picker and
   validates the new file in the same dialog. **Close** is the close button.
   There is no primary button; T06.3 adds **Import**, enabled only when the
   title is valid and any required encoding has been chosen.
6. **Closing** re-enables Import guide and returns focus to it.

### What T06.1 guarantees

- Validation only reads. No `FileOperations` row, no `.staging` folder and no
  managed file is created.
- The source file is opened with read and shared-read access and is never
  changed.

## Packages

- Pin `CommunityToolkit.WinUI.Controls.HeaderedControls` `8.2.251219`, a
  stable release matching the other Toolkit packages, and reference it from
  `DesktopGuides.Production`.
- Reference `PdfPig` from `DesktopGuides.Infrastructure`.
- Regenerate the affected `packages.lock.json` files.

## Testing

### Core tests (`DesktopGuides.Core.Tests`, TDD)

- Suggested title: extension removed, whitespace trimmed, a 201-character
  name cut to 200, no split surrogate pair at the cut, and "Untitled guide"
  for `.txt`.
- `GuideTitle.Create`: accepts 1 and 200 characters after trimming; rejects
  an empty title, a title of only spaces, and 201 characters.

### Infrastructure tests (`DesktopGuides.Infrastructure.Tests`, TDD)

These use the fixtures in `tests/fixtures/p0` and write only inside a temp
directory.

- **TXT:** `txt-ascii`, `txt-utf8` and `txt-bom` are ready with
  `CodePage = null`. `txt-legacy` needs an encoding, with samples for 437 and
  1252. Resolving it with 437 and with 1252 stores that code page, and
  resolving with 65001 throws. Generated UTF-16 LE and BE files and a file with
  a NUL byte get `UnsupportedEncoding`.
- **HTML:** `html-static` maps the entry path, asset count and warnings;
  `html-hostile` maps to the matching issue or warnings. Each scan limit maps
  to `TooLarge` naming that limit.
- **PDF:** `pdf-short` gives the page count and `HasText = true`;
  `pdf-locked` gets `Encrypted`; `pdf-scan` gives `HasText = false`;
  `pdf-access` is accepted; a `.pdf` containing text gets `Unreadable`.
- **Every format:** an unknown extension gets `Unsupported`; a missing path
  gets `Missing`; a zero-byte file gets `Empty`; a file under an exclusive
  lock gets `Unreadable`; an already-cancelled token throws
  `OperationCanceledException`.
- **Limits:** with injected small limits, the limit passes and the limit
  plus one byte gets `TooLarge`, for each format.
- **Nothing written:** after each test the temp library root has no
  `.staging` or `content` entries, and each source file's SHA-256 and
  last-write time are unchanged.

### Installed smoke mode `import-preview`

Added to `tools/p1/windows_shell_ui_smoke.ps1` and `windows_shell_install.ps1`
with a 120 s timeout, and run on the host and in the CI `production-shell-ui`
job in light and dark themes. It seeds one game, opens it and clicks
**Import guide**. UIA finds the system Open dialog, sets the File name box to
the fixture's full path and presses Enter. It asserts only what the app
controls:

1. `txt-legacy`: the title value, format and file name; both encoding
   options are present; selecting CP437 shows its sample.
2. **Choose another file** with `html-static`: details, the warnings list,
   and the File details and Warnings headers.
3. **Choose another file** with `pdf-locked`: the Error `InfoBar` message.
4. Close, then Import guide again and Esc in the picker: no dialog opens and
   focus is on Import guide.
5. After closing: the database has no `FileOperations` rows and the library
   has no `.staging` folder.

It saves light and dark screenshots of the preview as evidence and for the
PR. It doesn't assert the picker's own layout.

**Risk:** it isn't yet proven that the CI runner's session can show the
system file dialog. The first smoke task checks this. If it can't, the mode
runs on `pcsx2-win` only, and the reason is recorded here and in the plan.

## Out of scope

Confirm, copying, `FileOperation` rows and publication (T06.3); duplicate
detection; Ctrl+O (T16.1); URL imports (S22); multi-page HTML (S21); drag and
drop; multiple files or folders; UTF-16 text; PDF passwords.

## Documentation

- Plan: `docs/p1/t06-1-import-preview-plan.md`.
- Update the T06.1 and T06.2 rows of `implementation-plan.md` and
  `docs/progress.md`, and correct the stale T05.4 line in `docs/progress.md`
  (PR #16 is merged).
- Evidence: `docs/p1/evidence/t06-1-import-preview/`.

## PR outcome

The PR names T06.1 and T06.2 as its targets, T05.4 and T04.4 as merged
prerequisites, and T06.3 as the next task. It includes light and dark
screenshots of the preview.

## T06.1 + T06.2 verification record

- **Unit tests.** `core-tests` in CI run 36651770617: 198 Core and 290
  Infrastructure passes, including `GuideTitleTests`,
  `ImportPresentationTests`, `GuideImportValidatorTests` and
  `GuideImportValidatorHtmlPdfTests`.
- **Installed import scenario.** `production-shell-ui` in the same run, light
  and dark: `txt-legacy` title, format, file name and both encoding options,
  CP437 selected with its sample; `html-static` details under a native
  heading with 3 linked files; `html-hostile` details and warnings in
  headered groups; the password-protected PDF message; Close and picker
  Cancel returning focus to Import guide. `describe-import` afterwards: 0
  guides, 0 file operations, 0 staged and 0 managed entries. The CI runner
  shows the system Open dialog, so the scenario stays on CI and no
  `-SkipImport` switch was needed.
- **Screenshots.** [TXT light](evidence/t06-1-import-preview/import-light.import-txt.png),
  [warnings light](evidence/t06-1-import-preview/import-light.import-warnings.png),
  [TXT dark](evidence/t06-1-import-preview/import-dark.import-txt.png),
  [warnings dark](evidence/t06-1-import-preview/import-dark.import-warnings.png).
- **Rulings.**
  - The picker is `Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id)`,
    so `InitializeWithWindow` isn't needed. Cost if wrong: the picker would
    need a different owner-window call.
  - `ResolveTxtEncodingAsync` raises `Changed` when the file's size or
    last-write time moved; nothing else in this work does. Cost if wrong:
    one error path in the resolve step.
  - Windows-1252 decodes every byte on .NET 10, so no extra mapping was added;
    a test pins 0x81. Cost if wrong: a rare legacy file would fail to resolve.
  - The smoke opens the picker and the dialog's secondary button with pointer
    clicks, because a UIA Invoke would not return while the picker is modal.
    Cost if wrong: none for the app.
  - Managed UIA sees the Open dialog's Win32 controls as panes without
    patterns (CI run 36650136288, confirmed by a host probe). The smoke sets
    the File name box (id `1148`) with `WM_SETTEXT` and posts `WM_COMMAND`
    `IDOK` or `IDCANCEL` to the dialog, instead of UIA Value and Invoke or
    Esc. Cost if wrong: the smoke fails at the picker; no product impact.
  - `html-static` checks native-heading details and `html-hostile` checks the
    headered groups and warnings, because `html-static` has no warnings. Cost
    if wrong: none.
  - `AssetCount` counts linked files without the entry; `TotalBytes` includes
    the entry. Cost if wrong: an off-by-one in the linked files row.
  - Infrastructure tests check each source file's hash and last-write time;
    the installed smoke checks the library with `describe-import`, which
    counts entries inside the staging and content roots because startup may
    create the empty roots. Cost if wrong: none.
  - The warnings list shows at most 20 rows plus "N more warnings", and
    targets over 80 characters keep their start and end. Cost if wrong: a
    long list is cut earlier than a reader expects.
  - The HTML entry size is checked against `Html.MaxEntryBytes` before the
    scan, with the scanner's `TooLarge` message. Cost if wrong: none.
  - Encoding samples don't wrap and are clipped by the dialog width. Cost if
    wrong: a long first line is cut in the preview only.
  - The smoke checks scrolled details and warnings rows by presence and name,
    not `IsOffscreen`. Cost if wrong: none.
  - `FirstLines` trims the newline a final CRLF leaves on the sample. Cost if
    wrong: a one-character sample difference.
