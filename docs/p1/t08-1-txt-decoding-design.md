# T08.1 Managed TXT decoding design

Status: design approved in brainstorming on 1 October 2026;
implementation planned in
[t08-1-txt-decoding-plan.md](t08-1-txt-decoding-plan.md) and verified in CI run
[36865349872](https://github.com/ilya-slalom/desktop-guides/actions/runs/36865349872)
(see the [verification record](#t081-verification-record)).
Prerequisites T06.3 (PR #19) and T11.2 (M0, PR #3) are merged.

## Intent

Opening an imported TXT guide decodes its managed copy the same way every
time, without touching the original:

- **One decoding rule.** A UTF-8 BOM means UTF-8. Otherwise the code page
  chosen at import (437 or 1252) is used, or strict UTF-8 when none was
  stored.
- **Faithful text.** CRLF and bare CR become LF after decoding; spaces, tabs
  and every other character are kept (TR08.1). Offsets are UTF-16 code units
  in the normalized text.
- **Typed, recoverable failures.** A missing, oversized, unreadable or
  undecodable managed copy, or contradictory encoding metadata, comes back
  as a result value the Reader can explain. Nothing throws for an expected
  failure.
- **Changed bytes still open.** If the managed copy no longer matches the
  fingerprint recorded at import, the guide opens and the result says so,
  so T08.3 can restore by context and T15.1 can report it.

Traces: the T08.1 row of [implementation-plan.md](implementation-plan.md)
(TR08.1), [work-breakdown.md](../work-breakdown.md) S08 and T08.1, and
[p1-technical-design.md](../p1-technical-design.md) §7 S08.

Decisions made during brainstorming:

- **Headless.** T08.1 adds the decoding rules and a loader with unit tests
  only. The Reader keeps its placeholder until T08.2 renders the text.
- **Approach A.** Pure decoding rules in Core and a file loader in
  Infrastructure, returning a typed result; not exceptions in
  `TextGuideDocument.Decode`, and not a view-less `IReaderSession`.
- **A fingerprint mismatch opens the guide** with `ContentChanged` set,
  rather than refusing it or skipping the check.
- **No encoding command.** The encoding chosen at import stands; changing it
  is not part of T08.1.

## Already working (kept as-is)

- `TextGuideDocument.Decode(bytes, codePage)` in
  `src/DesktopGuides.Core/Text/` decodes BOM/UTF-8 and CP437/1252 with
  exception fallback, normalizes newlines, builds UTF-16 line starts, and
  captures and restores text locations. Import validation, publication and
  the P0 probe use it.
- Import stores the choice in `Guides.TextCodePage`: `null` for UTF-8 with or
  without a BOM, otherwise 437 or 1252. The schema allows a code page only
  on a TXT guide.
- Publication copies the source bytes unchanged to `guide.txt` and records
  their lowercase SHA-256 as `ContentSha256` and their length as
  `ContentBytes`.

## Components

### Decoder change (Core/Text/TextGuideDocument.cs)

`Decode(bytes, codePage)` keeps its signature. A UTF-8 BOM now means UTF-8
even when a code page is passed: the BOM is stripped, the rest is decoded as
strict UTF-8, and `EncodingName` is `utf-8`. Import can't produce a BOM with a
stored code page (see the import fix below), so its callers are unaffected.

### ManagedTextDecoder (Core/Text/ManagedTextDecoder.cs)

```csharp
public static TextGuideLoad Decode(byte[] bytes, Guide guide);
```

Pure; no file I/O. The rules, in order:

1. `guide.Format` isn't `Txt`, or `TextCodePage` is set to anything other
   than 437 or 1252 → `Failed(InvalidMetadata)`.
2. The bytes start with a UTF-8 BOM → strict UTF-8, ignoring any stored code
   page. On failure → `Failed(NotUtf8)`.
3. A code page is stored → that code page with exception fallback. On
   failure → `Failed(Undecodable)`.
4. Otherwise → strict UTF-8. Invalid bytes, including a multibyte sequence
   cut off at the end of the file → `Failed(NotUtf8)`.
5. Success → `Loaded(document, contentChanged)`. `contentChanged` is true
   when the SHA-256 of the bytes differs from `guide.ContentSha256`
   (compared ignoring case) or their length differs from
   `guide.ContentBytes`.

### TextGuideLoad (Core/Text/TextGuideLoad.cs)

A small record union:

```csharp
public abstract record TextGuideLoad;
public sealed record TextGuideLoaded(TextGuideDocument Document, bool ContentChanged) : TextGuideLoad;
public sealed record TextGuideLoadFailed(TextGuideLoadError Error) : TextGuideLoad;

public enum TextGuideLoadError
{
    Missing, TooLarge, Unreadable, InvalidMetadata, NotUtf8, Undecodable,
}
```

### ManagedTextGuideLoader (Infrastructure/Reading/ManagedTextGuideLoader.cs)

```csharp
public Task<TextGuideLoad> LoadAsync(Guide guide, CancellationToken token);
```

Constructed with the `ManagedPathResolver`.

1. Resolve `guide.txt` through
   `ManagedPathResolver.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath)`.
   A missing file or folder, a path that escapes the guide root, or a
   filesystem link → `Failed(Missing)`: the managed copy isn't where the app
   owns it.
2. A file longer than `GuideImportLimits.MaxTxtBytes` (64 MiB) →
   `Failed(TooLarge)`, decided from its length before reading.
3. Read the whole file with `FileShare.Read` and decode it through
   `ManagedTextDecoder.Decode`, both off the UI thread. An `IOException` or
   `UnauthorizedAccessException` (for example, a locked file) →
   `Failed(Unreadable)`.
4. Cancellation throws `OperationCanceledException`, as the other services
   do.

The loader opens only the managed copy, never the source path, and writes
nothing: no database rows, files or log lines with guide content.

### Import fix (Infrastructure/Import/GuideImportValidator.cs)

`InspectTxtAsync` rejects a file that starts with a UTF-8 BOM but fails
strict UTF-8 with `ImportIssue.UnsupportedEncoding` and the existing
"This text file isn't UTF-8…" message, instead of offering the CP437/1252
choice. Without this, such a guide would import and then fail to open under
the BOM rule. Editors that write a BOM write valid UTF-8, so this is expected
to be rare in guides from public sites; that is an estimate, not a
measurement.

## Error handling

- **Expected failures are values.** Every reason in `TextGuideLoadError`
  comes back as `TextGuideLoadFailed`. Only cancellation and programming
  errors (a `null` guide or byte array) throw.
- **No partial text.** A failed decode returns no document; the reader never
  shows a truncated or replacement-character version of a guide.
- **No repair.** The loader never rewrites the managed copy or the Guide row.
- **Nothing persisted.** T08.1 adds no schema, settings or file changes.

## Testing

### Core tests

`ManagedTextDecoderTests`:

- Mixed CRLF, CR and LF become LF; tabs and trailing spaces are kept.
- CP437 box-drawing bytes decode to their box-drawing characters.
- Windows-1252 `0x80` decodes to `€`; the undefined byte `0x81` is pinned to
  whatever the registered code-page provider returns, so a provider change
  shows up as a test failure.
- A BOM with code page 437 stored decodes as UTF-8.
- Invalid UTF-8, and a multibyte sequence cut off at the end, give `NotUtf8`.
- A different fingerprint, and a shortened file, open with
  `ContentChanged = true`; matching bytes open with `false`.
- A non-TXT guide, and code page 65001, give `InvalidMetadata`.

`TextGuideDocumentTests`: a BOM wins over a passed code page.

### Infrastructure tests

`ManagedTextGuideLoaderTests`, on a temporary managed root:

- A published TXT guide loads, and the original source file's bytes and
  write time are unchanged.
- A missing `guide.txt` gives `Missing`.
- A file one byte over 64 MiB, made by setting its length, gives `TooLarge`.
- A file held open exclusively gives `Unreadable`.
- A cancelled token throws `OperationCanceledException`.

`GuideImportValidatorTests`: a BOM file with invalid UTF-8 is rejected with
`UnsupportedEncoding`.

### Installed smoke

None. T08.1 has no UI; the Core and Infrastructure test jobs in CI are the
gate. T08.2 adds the installed reader checks.

### Traceability

TR08.1 stays the governing requirement. The verification record lists each
test against the T08.1 exit: BOM, strict UTF-8, CP437, Windows-1252,
newlines and truncation, with untouched originals.

## Out of scope (follow-ups)

- Rendering the text (T08.2) and offset/context navigation (T08.3).
- Changing a guide's encoding after import.
- UI for load failures (T15.1).

## Documentation

- `docs/p1-technical-design.md`: a T08.1 note on the loader and the BOM
  rule.
- `docs/p1/e2e-testing.md`: unchanged; the BOM rejection is a unit test,
  not an installed phase (planning ruling 5).
- `docs/work-breakdown.md`: keep the S08 trace current.
- `docs/progress.md` and `docs/p1/implementation-plan.md`: the T08.1 row and
  paragraph once verified.

## PR outcome

Opening an imported TXT guide decodes its managed copy by one rule (a BOM
means UTF-8, else the stored code page, else strict UTF-8), keeps every
character apart from normalized newlines, and reports missing, oversized,
unreadable, undecodable or contradictory cases as typed results, while a
changed copy still opens and is flagged.

## T08.1 verification record

- **Unit tests.** On `pcsx2-win`, Core 323/323 and Infrastructure 438/438
  passed. Against the T08.1 exit:
  - BOM: `TextGuideDocumentTests.BomWinsOverAPassedCodePage` and
    `BomWithInvalidUtf8RequiresAnEncodingEvenWithACodePage`;
    `ManagedTextDecoderTests.BomWinsOverAStoredCodePage`; import rejection
    `GuideImportValidatorTests.BomTextWithInvalidUtf8IsAnUnsupportedEncoding`;
  - strict UTF-8 and truncation: `InvalidOrTruncatedUtf8IsNotUtf8` (four
    rows, including a multibyte sequence cut off at the end);
  - CP437: `DecodesCp437BoxDrawing`, `KeepsDosEndOfFileByte`;
  - Windows-1252: `DecodesWindows1252AndPinsItsUndefinedByte` (`0x81` →
    `U+0081`);
  - newlines: `NormalizesMixedNewlinesAndKeepsTabsAndTrailingSpaces`,
    `NormalizesNewlinesInLegacyText`;
  - changed copies: `MatchingBytesAreUnchangedEvenWithAnUppercaseFingerprint`,
    `DifferentBytesOpenAsChanged`, `ShortenedCopyOpensAsChanged`,
    `LengthAloneMarksTheCopyChanged`, `EmptyCopyOpensAsChanged`;
  - metadata: `NonTextGuideIsInvalidMetadata`,
    `UnsupportedStoredCodePageIsInvalidMetadata`;
  - loader and untouched originals: `ManagedTextGuideLoaderTests`
    (`LoadsAPublishedGuideWithoutTouchingAnyFile` checks the source and
    managed files' hashes and write times; Missing for a deleted file, a
    deleted guide folder and an escaping path; TooLarge at 64 MiB + 1;
    Unreadable while locked; InvalidMetadata without reading; cancellation
    throws).
- **CI.** Run
  [36865349872](https://github.com/ilya-slalom/desktop-guides/actions/runs/36865349872)
  on `599b283` passed every job, including `core-tests` and
  `native-arm64-core`, which run the Core and Infrastructure tests on x64
  and arm64.
- **Rulings.** Planning rulings 1–6 in the
  [plan](t08-1-txt-decoding-plan.md#rulings-carried-from-planning).
