# T08.1 Managed TXT decoding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Opening an imported TXT guide decodes its managed copy by one rule
(a BOM means UTF-8, else the stored code page, else strict UTF-8), keeps every
character apart from normalized newlines, and reports missing, oversized,
unreadable, undecodable or contradictory cases as typed results, while a
changed copy still opens and is flagged.

**Architecture:** `TextGuideDocument.Decode` learns that a BOM wins over a
passed code page. A pure Core `ManagedTextDecoder` turns bytes plus a `Guide`
row into a `TextGuideLoad` value. An Infrastructure `ManagedTextGuideLoader`
resolves and reads the managed `guide.txt` off the UI thread and hands the
bytes to the decoder. Import rejects a BOM file that isn't valid UTF-8.

**Tech Stack:** .NET 10, C#, xUnit, `System.Text.Encoding.CodePages`.

**Spec:** [t08-1-txt-decoding-design.md](t08-1-txt-decoding-design.md)

## Global Constraints

- A UTF-8 BOM means UTF-8 even when a code page is stored or passed.
- Allowed stored code pages: `null`, 437, 1252. Anything else, or a non-TXT
  guide, is `InvalidMetadata`.
- `ContentChanged` = SHA-256 differs from `Guide.ContentSha256` (ignoring
  case) or length differs from `Guide.ContentBytes`. A changed copy still
  opens.
- Size limit: `GuideImportLimits.MaxTxtBytes` (64 MiB); a longer file is
  `TooLarge`, decided before reading its contents.
- Expected failures are `TextGuideLoadFailed` values. Only cancellation
  (`OperationCanceledException`) and `null` arguments throw.
- No partial text, no repair, nothing persisted: the loader writes no file,
  row or log line.
- The loader opens only the managed copy, never the import source.
- No UI, no installed smoke; the CI Core and Infrastructure test jobs (x64
  and arm64) are the gate.
- Commits go straight in (no approval needed); pushes need the user's OK.
  Trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Host helpers (dotnet isn't on the Mac; run from the repo root):

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){ s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t08-1) { Remove-Item -Recurse -Force E:\work\desktop-guides\t08-1 }; New-Item -ItemType Directory E:\work\desktop-guides\t08-1 | Out-Null"'; COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers --exclude='._*' -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t08-1'; }
t(){ stage && s "cd /d E:\work\desktop-guides\t08-1 && $1"; }
```

Test commands:

- Core: `t "dotnet test tests\DesktopGuides.Core.Tests -c Release"`
- Infrastructure: `t "dotnet test tests\DesktopGuides.Infrastructure.Tests -c Release"`
- Filter one class: append `--filter FullyQualifiedName~<ClassName>`.

Baselines before this plan: Core 301 tests, Infrastructure 429 tests.

## Rulings carried from planning

These refine the spec; Task 4 edits the spec to match.

1. **`Undecodable` has no reachable test.** .NET's 437 and 1252 providers map
   all 256 bytes, so no input fails them today. The value stays as the
   guard for a provider change; the pinned `0x81` test is what notices one.
2. **The BOM check is shared.** `TextGuideDocument.HasUtf8Bom` is public so
   the import fix uses the same test as the decoder.
3. **A BOM with invalid UTF-8 throws `EncodingSelectionRequiredException`**
   from `Decode` whether or not a code page was passed, so callers have one
   "not UTF-8" signal. Its message still says to choose an encoding; import
   catches it for BOM files and shows the existing "isn't UTF-8" message
   instead.
4. **The loader checks metadata before touching the disk,** through
   `ManagedTextDecoder.HasValidMetadata`, so a PDF row never reads
   `guide.pdf` under the TXT size limit.
5. **No `e2e-testing.md` change.** The BOM rejection is a unit test, not an
   installed smoke phase; listing it in the installed-scenario table would
   claim coverage that doesn't exist.
6. **The P0 `TextProbe` follows the BOM rule too.** It calls the same
   `Decode`; a BOM file probed with a code page now decodes as UTF-8. No
   P0 evidence depends on the old behaviour.

## Review Focus

1. **DOS end-of-file byte.** Old guides often end with `0x1A` (Ctrl-Z); in a
   CP437 guide it must be kept as `U+001A`, not dropped or rejected. Test in
   Task 1 (`KeepsDosEndOfFileByte`).
2. **CRLF in a legacy code page.** Most CP437 guides use CRLF; newlines are
   normalized after the code-page decode, with correct line starts. Test in
   Task 1 (`NormalizesNewlinesInLegacyText`).
3. **Empty or BOM-only managed copy.** A copy truncated to nothing opens with
   empty text and `ContentChanged = true`, rather than failing. Test in
   Task 1 (`EmptyCopyOpensAsChanged`).
4. **Corrupted `PrimaryRelativePath`.** A row pointing outside the guide root
   (`../escape.txt`) gives `Missing`, not an exception. Test in Task 3
   (`EscapingPathIsMissing`).
5. **Guide folder deleted.** When the whole content folder is gone the load
   gives `Missing`. Test in Task 3 (`DeletedGuideFolderIsMissing`).

---

### Task 1: BOM rule, load result and ManagedTextDecoder (Core)

**Files:**
- Modify: `src/DesktopGuides.Core/Text/TextGuideDocument.cs:34-66` (`Decode`, new `HasUtf8Bom`)
- Create: `src/DesktopGuides.Core/Text/TextGuideLoad.cs`
- Create: `src/DesktopGuides.Core/Text/ManagedTextDecoder.cs`
- Test: `tests/DesktopGuides.Core.Tests/TextGuideDocumentTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/ManagedTextDecoderTests.cs` (new)

**Interfaces:**
- Consumes: `Guide` (`DesktopGuides.Core.Library`), existing
  `TextGuideDocument`, `EncodingSelectionRequiredException`.
- Produces:
  - `public static bool TextGuideDocument.HasUtf8Bom(ReadOnlySpan<byte> bytes)`
  - `TextGuideDocument.Decode(byte[] bytes, int? codePage = null)`: a BOM
    means strict UTF-8; invalid strict UTF-8 throws
    `EncodingSelectionRequiredException`; a code-page failure throws
    `DecoderFallbackException`.
  - `abstract record TextGuideLoad`; `TextGuideLoaded(TextGuideDocument Document, bool ContentChanged)`;
    `TextGuideLoadFailed(TextGuideLoadError Error)`;
    `enum TextGuideLoadError { Missing, TooLarge, Unreadable, InvalidMetadata, NotUtf8, Undecodable }`
  - `public static bool ManagedTextDecoder.HasValidMetadata(Guide guide)`
  - `public static TextGuideLoad ManagedTextDecoder.Decode(byte[] bytes, Guide guide)`

- [ ] **Step 1: Write the failing BOM tests**

Add to `TextGuideDocumentTests`:

```csharp
    [Fact]
    public void BomWinsOverAPassedCodePage()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é\r\n")];

        TextGuideDocument document = TextGuideDocument.Decode(bytes, 437);

        Assert.Equal("é\n", document.Text);
        Assert.Equal("utf-8", document.EncodingName);
    }

    [Fact]
    public void BomWithInvalidUtf8RequiresAnEncodingEvenWithACodePage()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0x48, 0x82, 0x0A];

        Assert.Throws<EncodingSelectionRequiredException>(() => TextGuideDocument.Decode(bytes, 437));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `t "dotnet test tests\DesktopGuides.Core.Tests -c Release --filter FullyQualifiedName~TextGuideDocumentTests"`
Expected: FAIL. `BomWinsOverAPassedCodePage` gets CP437 mojibake
CP437 text and `ibm437`; the second test gets no exception (CP437 decodes
every byte).

- [ ] **Step 3: Implement the BOM rule**

In `TextGuideDocument.cs`, replace the start of `Decode` through its catch
block with:

```csharp
    public static bool HasUtf8Bom(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    public static TextGuideDocument Decode(byte[] bytes, int? codePage = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        bool hasUtf8Bom = HasUtf8Bom(bytes);
        ReadOnlySpan<byte> contents = hasUtf8Bom ? bytes.AsSpan(3) : bytes;
        // A BOM means UTF-8, whatever code page was stored or passed.
        bool strictUtf8 = hasUtf8Bom || codePage is null;
        Encoding encoding;
        if (strictUtf8)
        {
            encoding = new UTF8Encoding(false, true);
        }
        else
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = Encoding.GetEncoding(
                codePage!.Value, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        string decoded;
        try
        {
            decoded = encoding.GetString(contents);
        }
        catch (DecoderFallbackException exception) when (strictUtf8)
        {
            throw new EncodingSelectionRequiredException(exception);
        }
```

The normalization, hash and constructor call after the catch stay as they
are.

- [ ] **Step 4: Run them to verify they pass**

Run: same as Step 2.
Expected: PASS, all `TextGuideDocumentTests`.

- [ ] **Step 5: Write the failing decoder tests**

Create `tests/DesktopGuides.Core.Tests/ManagedTextDecoderTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ManagedTextDecoderTests
{
    private static Guide TxtGuide(
        byte[] recorded, int? codePage = null, GuideFormat format = GuideFormat.Txt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Guide", format, "content/guide", "guide.txt",
            Convert.ToHexStringLower(SHA256.HashData(recorded)), recorded.LongLength,
            null, codePage, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static TextGuideLoaded Loaded(byte[] bytes, int? codePage = null) =>
        Assert.IsType<TextGuideLoaded>(ManagedTextDecoder.Decode(bytes, TxtGuide(bytes, codePage)));

    private static TextGuideLoadError Failed(byte[] bytes, Guide guide) =>
        Assert.IsType<TextGuideLoadFailed>(ManagedTextDecoder.Decode(bytes, guide)).Error;

    [Fact]
    public void NormalizesMixedNewlinesAndKeepsTabsAndTrailingSpaces()
    {
        TextGuideLoaded loaded = Loaded(Encoding.UTF8.GetBytes("a  \r\n\tb\rc\n"));

        Assert.Equal("a  \n\tb\nc\n", loaded.Document.Text);
        Assert.False(loaded.ContentChanged);
    }

    [Fact]
    public void DecodesCp437BoxDrawing()
    {
        TextGuideLoaded loaded = Loaded([0xC9, 0xCD, 0xBB, 0x0A, 0xBA, 0x20, 0xBA, 0x0A, 0xC8, 0xCD, 0xBC], 437);

        Assert.Equal("╔═╗\n║ ║\n╚═╝", loaded.Document.Text);
        Assert.Equal("ibm437", loaded.Document.EncodingName);
    }

    [Fact]
    public void DecodesWindows1252AndPinsItsUndefinedByte()
    {
        TextGuideLoaded loaded = Loaded([0x80, 0x20, 0x81], 1252);

        // 0x81 is undefined in Windows-1252; this pins what the provider returns.
        Assert.Equal("€ \u0081", loaded.Document.Text);
        Assert.Equal("windows-1252", loaded.Document.EncodingName);
    }

    [Fact]
    public void BomWinsOverAStoredCodePage()
    {
        TextGuideLoaded loaded = Loaded([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é")], 437);

        Assert.Equal("é", loaded.Document.Text);
        Assert.Equal("utf-8", loaded.Document.EncodingName);
    }

    public static TheoryData<byte[], int?> NotUtf8Bytes => new()
    {
        { new byte[] { 0x48, 0x82, 0x0A }, null },
        { new byte[] { 0x41, 0xC3 }, null },
        { new byte[] { 0xEF, 0xBB, 0xBF, 0x48, 0x82 }, null },
        { new byte[] { 0xEF, 0xBB, 0xBF, 0x48, 0x82 }, 437 },
    };

    [Theory]
    [MemberData(nameof(NotUtf8Bytes))]
    public void InvalidOrTruncatedUtf8IsNotUtf8(byte[] bytes, int? codePage)
    {
        Assert.Equal(TextGuideLoadError.NotUtf8, Failed(bytes, TxtGuide(bytes, codePage)));
    }

    [Fact]
    public void MatchingBytesAreUnchangedEvenWithAnUppercaseFingerprint()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");
        Guide guide = TxtGuide(bytes) with { ContentSha256 = Convert.ToHexString(SHA256.HashData(bytes)) };

        Assert.False(Assert.IsType<TextGuideLoaded>(ManagedTextDecoder.Decode(bytes, guide)).ContentChanged);
    }

    [Fact]
    public void DifferentBytesOpenAsChanged()
    {
        Guide guide = TxtGuide(Encoding.UTF8.GetBytes("abc"));

        TextGuideLoaded loaded = Assert.IsType<TextGuideLoaded>(
            ManagedTextDecoder.Decode(Encoding.UTF8.GetBytes("abd"), guide));

        Assert.Equal("abd", loaded.Document.Text);
        Assert.True(loaded.ContentChanged);
    }

    [Fact]
    public void ShortenedCopyOpensAsChanged()
    {
        Guide guide = TxtGuide(Encoding.UTF8.GetBytes("abc\n"));

        Assert.True(Assert.IsType<TextGuideLoaded>(
            ManagedTextDecoder.Decode(Encoding.UTF8.GetBytes("ab"), guide)).ContentChanged);
    }

    [Fact]
    public void LengthAloneMarksTheCopyChanged()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");
        Guide guide = TxtGuide(bytes) with { ContentBytes = 4 };

        Assert.True(Assert.IsType<TextGuideLoaded>(ManagedTextDecoder.Decode(bytes, guide)).ContentChanged);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF })]
    public void EmptyCopyOpensAsChanged(byte[] bytes)
    {
        Guide guide = TxtGuide(Encoding.UTF8.GetBytes("abc"));

        TextGuideLoaded loaded = Assert.IsType<TextGuideLoaded>(ManagedTextDecoder.Decode(bytes, guide));

        Assert.Equal("", loaded.Document.Text);
        Assert.True(loaded.ContentChanged);
    }

    [Fact]
    public void KeepsDosEndOfFileByte()
    {
        TextGuideLoaded loaded = Loaded([0x45, 0x6E, 0x64, 0x0D, 0x0A, 0x1A], 437);

        Assert.Equal("End\n\u001A", loaded.Document.Text);
    }

    [Fact]
    public void NormalizesNewlinesInLegacyText()
    {
        TextGuideLoaded loaded = Loaded([0x41, 0x82, 0x0D, 0x0A, 0x42, 0x0D, 0x43], 437);

        Assert.Equal("Aé\nB\nC", loaded.Document.Text);
        Assert.Equal([0, 3, 5], loaded.Document.LineStarts);
    }

    [Fact]
    public void NonTextGuideIsInvalidMetadata()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");

        Assert.Equal(TextGuideLoadError.InvalidMetadata, Failed(bytes, TxtGuide(bytes, format: GuideFormat.Html)));
    }

    [Theory]
    [InlineData(65001)]
    [InlineData(0)]
    public void UnsupportedStoredCodePageIsInvalidMetadata(int codePage)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");

        Assert.Equal(TextGuideLoadError.InvalidMetadata, Failed(bytes, TxtGuide(bytes, codePage)));
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ManagedTextDecoder.Decode(null!, TxtGuide([])));
        Assert.Throws<ArgumentNullException>(() => ManagedTextDecoder.Decode([], null!));
    }
}
```

- [ ] **Step 6: Run them to verify they fail**

Run: `t "dotnet test tests\DesktopGuides.Core.Tests -c Release --filter FullyQualifiedName~ManagedTextDecoderTests"`
Expected: build FAIL, `ManagedTextDecoder`, `TextGuideLoaded`,
`TextGuideLoadFailed` and `TextGuideLoadError` not found.

- [ ] **Step 7: Implement the result types and the decoder**

Create `src/DesktopGuides.Core/Text/TextGuideLoad.cs`:

```csharp
namespace DesktopGuides.Core.Text;

/// <summary>The result of opening a TXT guide's managed copy.</summary>
public abstract record TextGuideLoad;

/// <summary>The copy decoded. <paramref name="ContentChanged"/> is true when it no
/// longer matches the fingerprint recorded at import.</summary>
public sealed record TextGuideLoaded(TextGuideDocument Document, bool ContentChanged) : TextGuideLoad;

public sealed record TextGuideLoadFailed(TextGuideLoadError Error) : TextGuideLoad;

public enum TextGuideLoadError
{
    Missing,
    TooLarge,
    Unreadable,
    InvalidMetadata,
    NotUtf8,
    Undecodable,
}
```

Create `src/DesktopGuides.Core/Text/ManagedTextDecoder.cs`:

```csharp
using System.Text;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Text;

/// <summary>Decodes a TXT guide's managed copy by the encoding chosen at import.</summary>
public static class ManagedTextDecoder
{
    public static bool HasValidMetadata(Guide guide)
    {
        ArgumentNullException.ThrowIfNull(guide);
        return guide.Format == GuideFormat.Txt && guide.TextCodePage is null or 437 or 1252;
    }

    public static TextGuideLoad Decode(byte[] bytes, Guide guide)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!HasValidMetadata(guide))
        {
            return new TextGuideLoadFailed(TextGuideLoadError.InvalidMetadata);
        }

        TextGuideDocument document;
        try
        {
            document = TextGuideDocument.Decode(bytes, guide.TextCodePage);
        }
        catch (EncodingSelectionRequiredException)
        {
            return new TextGuideLoadFailed(TextGuideLoadError.NotUtf8);
        }
        catch (DecoderFallbackException)
        {
            return new TextGuideLoadFailed(TextGuideLoadError.Undecodable);
        }

        bool contentChanged = bytes.LongLength != guide.ContentBytes ||
            !string.Equals(document.ContentSha256, guide.ContentSha256, StringComparison.OrdinalIgnoreCase);
        return new TextGuideLoaded(document, contentChanged);
    }
}
```

- [ ] **Step 8: Run the Core suite**

Run: `t "dotnet test tests\DesktopGuides.Core.Tests -c Release"`
Expected: PASS, 301 + 2 + 20 = 323 tests (theory rows count separately). If
the `0x81` pin fails, the provider returns something else: record the actual
character in a ruling and pin that instead.

- [ ] **Step 9: Commit**

```bash
git add src/DesktopGuides.Core/Text tests/DesktopGuides.Core.Tests/TextGuideDocumentTests.cs tests/DesktopGuides.Core.Tests/ManagedTextDecoderTests.cs
git commit -m "feat(p1): decode managed TXT copies by one encoding rule (T08.1)" -m "A UTF-8 BOM now wins over a passed code page. ManagedTextDecoder returns a typed TextGuideLoad and flags a copy that no longer matches its import fingerprint." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Reject a BOM file that isn't UTF-8 at import

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs:98-117` (`InspectTxtAsync`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorTests.cs`

**Interfaces:**
- Consumes: `TextGuideDocument.HasUtf8Bom(ReadOnlySpan<byte>)` and the
  `EncodingSelectionRequiredException` behaviour from Task 1.
- Produces: `InspectAsync` throws
  `GuideImportException(ImportIssue.UnsupportedEncoding, "This text file isn't UTF-8. Save it as UTF-8 and import it again.")`
  for a BOM file with invalid UTF-8.

- [ ] **Step 1: Write the failing test**

Add to `GuideImportValidatorTests`, after `Utf16AndNulAreAnUnsupportedEncoding`:

```csharp
    [Fact]
    public async Task BomTextWithInvalidUtf8IsAnUnsupportedEncoding()
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(
            files.Write("bom.txt", [0xEF, 0xBB, 0xBF, 0x48, 0x82, 0x0A]));

        Assert.Equal(ImportIssue.UnsupportedEncoding, error.Issue);
        Assert.Equal(NotUtf8, error.Message);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `t "dotnet test tests\DesktopGuides.Infrastructure.Tests -c Release --filter FullyQualifiedName~GuideImportValidatorTests"`
Expected: FAIL, no exception thrown (the file is offered the CP437/1252
choice as `ImportNeedsTxtEncoding`).

- [ ] **Step 3: Implement the rejection**

In `GuideImportValidator.cs`, add a constant next to `LegacyCodePages`:

```csharp
    private const string NotUtf8Message = "This text file isn't UTF-8. Save it as UTF-8 and import it again.";
```

Use it in the existing UTF-16/NUL throw, and replace the `try`/`catch` at the
end of `InspectTxtAsync` with:

```csharp
        try
        {
            TextGuideDocument.Decode(bytes);
            return new ImportReady(new TxtImportManifest(source, title, null, GuideFingerprint.OfBytes(bytes)));
        }
        catch (EncodingSelectionRequiredException) when (TextGuideDocument.HasUtf8Bom(bytes))
        {
            // A BOM means UTF-8 when the guide is opened, so no legacy code page can apply.
            throw new GuideImportException(ImportIssue.UnsupportedEncoding, NotUtf8Message);
        }
        catch (EncodingSelectionRequiredException)
        {
            return new ImportNeedsTxtEncoding(source, title, Samples(bytes));
        }
```

- [ ] **Step 4: Run the Infrastructure suite**

Run: `t "dotnet test tests\DesktopGuides.Infrastructure.Tests -c Release"`
Expected: PASS, 430 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorTests.cs
git commit -m "fix(p1): reject BOM text that isn't valid UTF-8 at import (T08.1)" -m "Such a file would otherwise import with a legacy code page and then fail to open under the BOM rule." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: ManagedTextGuideLoader (Infrastructure)

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Reading/ManagedTextGuideLoader.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedTextGuideLoaderTests.cs` (new)

**Interfaces:**
- Consumes: `ManagedTextDecoder.HasValidMetadata`, `ManagedTextDecoder.Decode`,
  `TextGuideLoad*` (Task 1); `ManagedPathResolver.ResolveExistingGuideFile(Guid, string)`;
  `GuideImportLimits.MaxTxtBytes`; test helpers `PublisherHarness`,
  `FileFingerprint` (namespace `DesktopGuides.Infrastructure.Tests.Import`).
- Produces: `public sealed class ManagedTextGuideLoader(ManagedPathResolver paths)` with
  `public Task<TextGuideLoad> LoadAsync(Guide guide, CancellationToken token)`.
  T08.2 calls this to open a TXT guide.

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedTextGuideLoaderTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Text;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class ManagedTextGuideLoaderTests
{
    private static async Task<Guide> PublishLegacyAsync(PublisherHarness harness)
    {
        string source = harness.Sources.Copy("txt-legacy.txt", "legacy.txt");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source, 437));
        return (await harness.Repository.GetGuideAsync(id))!;
    }

    private static string ManagedFile(PublisherHarness harness, Guide guide) =>
        harness.Paths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);

    private static Task<TextGuideLoad> LoadAsync(
        PublisherHarness harness, Guide guide, CancellationToken token = default) =>
        new ManagedTextGuideLoader(harness.Paths).LoadAsync(guide, token);

    private static async Task<TextGuideLoadError> FailedAsync(PublisherHarness harness, Guide guide) =>
        Assert.IsType<TextGuideLoadFailed>(await LoadAsync(harness, guide)).Error;

    [Fact]
    public async Task LoadsAPublishedGuideWithoutTouchingAnyFile()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        string source = Path.Combine(harness.Sources.Root, "legacy.txt");
        IReadOnlyList<FileFingerprint> sourceBefore = FileFingerprint.Of(source);
        IReadOnlyList<FileFingerprint> managedBefore = FileFingerprint.Of(ManagedFile(harness, guide));

        TextGuideLoaded loaded = Assert.IsType<TextGuideLoaded>(await LoadAsync(harness, guide));

        Assert.Equal("Guide é\nItem list\n", loaded.Document.Text);
        Assert.Equal("ibm437", loaded.Document.EncodingName);
        Assert.False(loaded.ContentChanged);
        Assert.Equal(sourceBefore, FileFingerprint.Of(source));
        Assert.Equal(managedBefore, FileFingerprint.Of(ManagedFile(harness, guide)));
    }

    [Fact]
    public async Task MissingCopyIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        File.Delete(ManagedFile(harness, guide));

        Assert.Equal(TextGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task DeletedGuideFolderIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        Directory.Delete(harness.Paths.GetGuideRoot(guide.Id), recursive: true);

        Assert.Equal(TextGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task EscapingPathIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);

        Assert.Equal(TextGuideLoadError.Missing,
            await FailedAsync(harness, guide with { PrimaryRelativePath = "../escape.txt" }));
    }

    [Fact]
    public async Task CopyOverTheSizeLimitIsTooLarge()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        using (FileStream stream = new(ManagedFile(harness, guide), FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(64L * 1024 * 1024 + 1);
        }

        Assert.Equal(TextGuideLoadError.TooLarge, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task LockedCopyIsUnreadable()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        using FileStream locked = new(ManagedFile(harness, guide), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(TextGuideLoadError.Unreadable, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task NonTextGuideIsInvalidMetadataWithoutReading()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);
        File.Delete(ManagedFile(harness, guide));

        Assert.Equal(TextGuideLoadError.InvalidMetadata,
            await FailedAsync(harness, guide with { Format = GuideFormat.Pdf, TextCodePage = null }));
    }

    [Fact]
    public async Task CancelledLoadThrows()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        Guide guide = await PublishLegacyAsync(harness);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LoadAsync(harness, guide, new CancellationToken(canceled: true)));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `t "dotnet test tests\DesktopGuides.Infrastructure.Tests -c Release --filter FullyQualifiedName~ManagedTextGuideLoaderTests"`
Expected: build FAIL, namespace `DesktopGuides.Infrastructure.Reading` /
`ManagedTextGuideLoader` not found.

- [ ] **Step 3: Implement the loader**

Create `src/DesktopGuides.Infrastructure/Reading/ManagedTextGuideLoader.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Text;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure.Reading;

/// <summary>Reads a TXT guide's managed copy and decodes it by its stored encoding.
/// Opens only the managed copy and writes nothing.</summary>
public sealed class ManagedTextGuideLoader
{
    private readonly ManagedPathResolver paths;
    private readonly long maxBytes = new GuideImportLimits().MaxTxtBytes;

    public ManagedTextGuideLoader(ManagedPathResolver paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.paths = paths;
    }

    public Task<TextGuideLoad> LoadAsync(Guide guide, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(guide);
        return Task.Run(() => Load(guide, token), token);
    }

    private TextGuideLoad Load(Guide guide, CancellationToken token)
    {
        if (!ManagedTextDecoder.HasValidMetadata(guide))
        {
            return Failed(TextGuideLoadError.InvalidMetadata);
        }

        byte[] bytes;
        try
        {
            string path = paths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maxBytes)
            {
                return Failed(TextGuideLoadError.TooLarge);
            }
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
        {
            // Not where the app owns it: gone, outside the guide root, or behind a link.
            return Failed(TextGuideLoadError.Missing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(TextGuideLoadError.Unreadable);
        }

        token.ThrowIfCancellationRequested();
        return ManagedTextDecoder.Decode(bytes, guide);
    }

    private static TextGuideLoadFailed Failed(TextGuideLoadError error) => new(error);
}
```

`FileNotFoundException` and `DirectoryNotFoundException` are `IOException`s,
so the `Missing` filter must come first.

- [ ] **Step 4: Run the Infrastructure suite**

Run: `t "dotnet test tests\DesktopGuides.Infrastructure.Tests -c Release"`
Expected: PASS, 438 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Reading tests/DesktopGuides.Infrastructure.Tests/Reading
git commit -m "feat(p1): load managed TXT guides as typed results (T08.1)" -m "ManagedTextGuideLoader resolves guide.txt inside the guide root, applies the 64 MiB limit before reading, and reports a missing, oversized or locked copy as a TextGuideLoadFailed value." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: Documentation and verification record

No TDD: documentation only.

**Files:**
- Modify: `docs/p1-technical-design.md` (S08, T08.1 bullet, around line 619)
- Modify: `docs/work-breakdown.md` (S08, T08.1 bullet, around line 267)
- Modify: `docs/p1/t08-1-txt-decoding-design.md` (status, rulings, Documentation list, verification record)
- Modify after CI passes: `docs/progress.md`, `docs/p1/implementation-plan.md`

- [ ] **Step 1: Technical design note**

Append to the T08.1 bullet in `docs/p1-technical-design.md`:

```markdown
  `ManagedTextDecoder` (Core) applies the rule to a managed copy's bytes and
  `ManagedTextGuideLoader` (Infrastructure) reads `guide.txt` inside the
  guide root; both return a `TextGuideLoad` value. A copy that no longer
  matches its import fingerprint still opens with `ContentChanged` set.
  Import rejects a BOM file that isn't valid UTF-8.
```

- [ ] **Step 2: Work-breakdown trace**

Append to the T08.1 bullet in `docs/work-breakdown.md`:

```markdown
  A UTF-8 BOM wins over a stored code page; missing, oversized, unreadable
  or undecodable copies are typed load results (P1 T08.1).
```

- [ ] **Step 3: Spec updates**

In `docs/p1/t08-1-txt-decoding-design.md`:
- Status line: add "implementation planned in
  [t08-1-txt-decoding-plan.md](t08-1-txt-decoding-plan.md)".
- Documentation section: replace the `e2e-testing.md` bullet with
  "`docs/p1/e2e-testing.md`: unchanged; the BOM rejection is a unit test,
  not an installed phase (planning ruling 5)."
- Add `## T08.1 verification record` with: the Core and Infrastructure test
  counts from the host run; each new test listed against the T08.1 exit
  (BOM, strict UTF-8, CP437, Windows-1252, newlines, truncation, untouched
  originals); the CI run ID once it passes; and a pointer to the planning
  rulings and the ledger rulings.

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs(p1): record T08.1 decoding notes and verification" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: After CI passes on the PR**

Add the CI run ID to the verification record. Add a T08.1 row to
`docs/progress.md` and a T08.1 paragraph to `docs/p1/implementation-plan.md`
once merged, as the first commit on the next branch (the "mark merged"
convention).

## Self-review

- **Spec coverage.** BOM rule → Task 1 Steps 1–4. `TextGuideLoad`,
  `ManagedTextDecoder` rules 1–5 → Task 1 Steps 5–8. Loader steps 1–4 and
  "writes nothing" → Task 3. Import fix → Task 2. Every listed Core and
  Infrastructure test is present; `Undecodable` has none (ruling 1). Docs →
  Task 4; `e2e-testing.md` dropped (ruling 5).
- **Placeholders.** None; every code step has full code.
- **Type consistency.** `HasUtf8Bom`, `HasValidMetadata`, `TextGuideLoaded`,
  `TextGuideLoadFailed`, `TextGuideLoadError` and `LoadAsync(Guide, CancellationToken)`
  are named the same in every task.
- **Review Focus.** Each of the five items has its test in Task 1 or Task 3.
