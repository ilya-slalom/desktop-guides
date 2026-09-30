# T06.4 Duplicate Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a previewed file's content is already a guide in the selected
game, the preview says so and offers **Open existing** or **Import another
copy**. The publisher refuses a duplicate unless the caller allows it, and
the copied bytes must match the previewed fingerprint.

**Architecture:** `Fingerprint` moves to the base `ImportManifest`. The
validator hashes TXT and PDF bytes with new `GuideFingerprint.OfBytes` and
`OfStream` helpers. `SqliteLibraryRepository.FindGuideByFingerprintAsync`
and `IImportJournal.FindGuide` share one query. `GuideImportPublisher`
takes `allowDuplicate`, rejects a duplicate with `ImportIssue.Duplicate`,
and compares the copy's hash with the manifest. `ImportGuideDialog` looks
up the fingerprint once a manifest is ready and shows an `ImportDuplicate`
`InfoBar`. `ShellWindow` opens the existing guide when asked.

**Tech Stack:** .NET 10, WinUI 3, Microsoft.Data.Sqlite, PdfPig 0.1.16,
xUnit, the PowerShell 5.1 UI Automation harness.

**Spec:** `docs/p1/t06-4-duplicate-import-design.md`

**Target:** T06.4. **Prerequisite:** T06.3 (PR #19), merged as `494cb02`.

## Global Constraints

- A match means the same `GameId`, the same `Format` and the same
  `ContentSha256`. The lookup returns the oldest match
  (`ORDER BY ImportedUtcMs, Id`).
- `Fingerprint` is lowercase hex SHA-256. TXT and PDF hash the file's
  bytes. HTML uses `GuideFingerprint.OfHtml`, unchanged.
- For TXT, the encoding choice doesn't affect the match.
- No schema change. The schema stays at version 3.
- Messages, verbatim:
  - Duplicate (publisher): "This file is already a guide for this game."
  - Duplicate `InfoBar`: `This file is already in {game title} as "{guide title}".`
    with straight quotes.
  - Primary button: "Import" without a match, "Import another copy" with one.
  - `InfoBar` action: "Open existing".
- Automation IDs: `ImportDuplicate` and `ImportOpenExisting`.
- `OpenGuideId` and `ImportedGuideId` are never both set.
- Existing `ImportIssue` values and messages are unchanged. `Duplicate` is
  appended last.
- The source is only read, and never written.
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only.
- Never print, copy or log provider credential values.

## Rulings against the spec

Task 6 records these rulings in the design's verification record.

- **The same-length rewrite test uses the `Prepared` checkpoint, not
  `Copied`.** At `Copied` the copy already holds the previewed bytes, so a
  rewrite there correctly publishes what was previewed. Only a rewrite
  before the copy can make the copy differ.
- **One fingerprint comparison after `CopyAsync` covers every format.**
  HTML's `PlanAsync` re-scan check stays as it is.
- **The SHA-256 helpers live in `GuideFingerprint`** (`OfBytes` and
  `OfStream`). Cancellation during the PDF hash is tested on `OfStream`
  directly, because a fixture PDF hashes in one read.
- **An `IOException` while hashing a PDF is `Unreadable`,** like the other
  read failures in the validator.
- **A `SqliteException` from the publisher's duplicate lookup is
  `SaveFailed`,** like `Prepare`.
- **The duplicate `InfoBar` reopens after a failed or cancelled import.**
  The primary button still reads "Import another copy", so the reason must
  stay visible.
- **The smoke's duplicate modes take `-ExpectedGuideTitle`** from the light
  `import-publish` result. The open run then also shows that the oldest
  match wins over the dark run's copy.
- **The light duplicate screenshot comes from the open run,** so the PR has
  the duplicate preview in both themes.
- **`ImportHoldsTheWriteGate` passes `allowDuplicate: true`,** because it
  publishes the same manifest twice.
- **Production has no test project.** Task 4 is gated by the Production
  build and by the Task 5 installed smoke. This is the plan's only TDD skip
  for code.
- **The "issues stay distinct" test pins existing behavior,** so it passes
  before any change. It guards the spec's rule rather than driving code.

## Review Focus

1. **The library gains a match after the preview** (another window imports
   the same file). Import must stop with `Duplicate` and leave nothing
   behind. Test `SecondImportOfTheSameFileIsDuplicate` (Task 3).
2. **A legacy TXT previewed with the other encoding.** It's the same bytes,
   so it's still a duplicate. Test `DuplicateCheckIgnoresTheEncodingChoice`
   (Task 3). The dialog runs the lookup after the encoding choice (Task 4).
3. **The same file imported into another game.** That isn't a duplicate.
   Test `SameFileInAnotherGameIsNotADuplicate` (Task 3).
4. **A file rewritten in place between preview and Import, keeping its size
   and write time** (some editors and sync tools do this). Import must
   report `Changed`. Test `SameLengthRewriteBeforeCopyIsChanged` (Task 3).
5. **The dialog closed, or another file picked, while the lookup runs, or
   the existing guide deleted before Open existing.** The generation and
   `closing` checks drop a late lookup result. `OpenGuideAsync` shows "This
   guide is no longer in your library." for a deleted guide. Checked in the
   Task 4 code review, because the UI harness can't hit those moments.

## Host commands

The Mac has no `dotnet`. Build and unit-test on the Windows host over SSH,
staging a fresh copy for each run:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t06-4) { Remove-Item -Recurse -Force E:\work\desktop-guides\t06-4 }; New-Item -ItemType Directory E:\work\desktop-guides\t06-4 | Out-Null"'
COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t06-4'
```

The steps below refer to these commands by name: **Core tests**,
**Infrastructure tests**, **Production build** and **Seed build**.

```bash
s 'cd /d E:\work\desktop-guides\t06-4 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'
s 'cd /d E:\work\desktop-guides\t06-4 && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'
s 'cd /d E:\work\desktop-guides\t06-4 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'
s 'cd /d E:\work\desktop-guides\t06-4 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'
```

Add `--filter "FullyQualifiedName~<ClassName>"` to run one test class. Stage
again before every run.

---

### Task 1: Fingerprint on every manifest

**Files:**
- Modify: `src/DesktopGuides.Core/Import/ImportContracts.cs`
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideFingerprint.cs`
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs` (`ResolveTxtEncodingAsync` ~line 94, `InspectTxtAsync` ~line 112, `InspectPdfAsync`/`ReadPdf` ~lines 181–212)
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs` (`VerifyPdf` ~line 306, the call only)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideFingerprintTests.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorTests.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportValidatorHtmlPdfTests.cs`

**Interfaces:**
- Produces:
  - `ImportManifest.Fingerprint` (`string`) on the base record.
  - `TxtImportManifest(ImportSource Source, string SuggestedTitle, int? CodePage, string Fingerprint)`
  - `PdfImportManifest(ImportSource Source, string SuggestedTitle, int PageCount, bool HasText, string Fingerprint)`
  - `HtmlImportManifest`: unchanged parameters. It passes `Fingerprint` to the base.
  - `ImportIssue.Duplicate`, appended last.
  - `GuideFingerprint.OfBytes(ReadOnlySpan<byte>)` and
    `GuideFingerprint.OfStream(Stream, CancellationToken)`, both returning
    lowercase hex.
  - `GuideImportValidator.ReadPdf(Stream, ImportSource, string title, string fingerprint, CancellationToken)`

- [ ] **Step 1: Write the failing tests**

Append to `GuideFingerprintTests`:

```csharp
    [Fact]
    public void StreamHashMatchesTheByteHashAcrossBuffers()
    {
        byte[] bytes = new byte[200_000];
        new Random(4).NextBytes(bytes);

        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            GuideFingerprint.OfBytes(bytes));
        Assert.Equal(GuideFingerprint.OfBytes(bytes), GuideFingerprint.OfStream(new MemoryStream(bytes), CancellationToken.None));
    }

    [Fact]
    public void StreamHashStopsWhenCancelled()
    {
        using CancellationTokenSource cancel = new();
        using CancelOnReadStream stream = new(new byte[200_000], cancel);

        Assert.ThrowsAny<OperationCanceledException>(() => GuideFingerprint.OfStream(stream, cancel.Token));
        Assert.Equal(1, stream.Reads);
    }

    private sealed class CancelOnReadStream(byte[] bytes, CancellationTokenSource cancel)
        : MemoryStream(bytes, writable: false)
    {
        public int Reads { get; private set; }

        public override int Read(Span<byte> buffer)
        {
            Reads++;
            cancel.Cancel();
            return base.Read(buffer);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    }
```

Append to `GuideImportValidatorTests` (the hashes are the fixture hashes
in `tests/fixtures/p0/manifest.json`):

```csharp
    [Fact]
    public async Task Utf8TextManifestCarriesTheFileHash()
    {
        ImportReady ready = Assert.IsType<ImportReady>(await Inspect(P0Fixtures.Resolve("txt-utf8.txt")));

        Assert.Equal("e21e7137eca4d996fced2143d7111220ea5051dc40708103cc6b7c490354b779", ready.Manifest.Fingerprint);
    }

    [Fact]
    public async Task ResolvedTextManifestCarriesTheFileHash()
    {
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(P0Fixtures.Resolve("txt-legacy.txt"), CancellationToken.None));

        TxtImportManifest manifest = await validator.ResolveTxtEncodingAsync(needs, 437, CancellationToken.None);

        Assert.Equal("f105c9c5952018b15edc617ecced30a4e2bccd3e8fa252ce7792f1a1ac723d26", manifest.Fingerprint);
    }
```

Append to `GuideImportValidatorHtmlPdfTests`:

```csharp
    [Fact]
    public async Task PdfManifestCarriesTheFileHash()
    {
        PdfImportManifest pdf = await Manifest<PdfImportManifest>(P0Fixtures.Resolve("pdf-short.pdf"));

        Assert.Equal("b67dd6f52454ead4b99571b5a66e8be6a63c3a34a522db5b4670d57cc0f0006f", pdf.Fingerprint);
    }

    [Fact]
    public async Task TypedIssuesStayDistinct()
    {
        using ImportTestDirectory files = new();
        GuideImportException[] errors =
        [
            await Rejected(Path.Combine(files.Root, "gone.txt")),
            await Rejected(files.Write("notes.doc", "text")),
            await Rejected(P0Fixtures.Resolve("pdf-locked.pdf")),
            await Rejected(files.Copy("txt-ascii.txt", "notes.pdf")),
        ];

        Assert.Equal(
            [ImportIssue.Missing, ImportIssue.Unsupported, ImportIssue.Encrypted, ImportIssue.Unreadable],
            errors.Select(error => error.Issue));
        Assert.Equal(errors.Length, errors.Select(error => error.Message).Distinct().Count());
    }
```

In `CancellingStopsPdfReadingBeforeTheDocumentIsParsed`, change the call
to `GuideImportValidator.ReadPdf(stream, source, "pdf-short", "", cancel.Token)`.

- [ ] **Step 2: Run the tests to see them fail**

Stage, then run **Infrastructure tests**.
Expected: the build fails. `TxtImportManifest`, `ImportManifest` and
`PdfImportManifest` have no `Fingerprint`, `GuideFingerprint` has no
`OfBytes` or `OfStream`, and `ReadPdf` takes no fingerprint.

- [ ] **Step 3: Implement**

In `ImportContracts.cs`:

```csharp
public abstract record ImportManifest(
    ImportSource Source, GuideFormat Format, string SuggestedTitle, string Fingerprint);

public sealed record TxtImportManifest(
    ImportSource Source, string SuggestedTitle, int? CodePage, string Fingerprint)
    : ImportManifest(Source, GuideFormat.Txt, SuggestedTitle, Fingerprint);
```

For `HtmlImportManifest`, keep its parameter list and change only the base
call to `: ImportManifest(Source, GuideFormat.Html, SuggestedTitle, Fingerprint)`.

```csharp
public sealed record PdfImportManifest(
    ImportSource Source, string SuggestedTitle, int PageCount, bool HasText, string Fingerprint)
    : ImportManifest(Source, GuideFormat.Pdf, SuggestedTitle, Fingerprint);
```

Append `Duplicate,` after `SaveFailed,` in `ImportIssue`. Keep any doc
comments on the records, and add
`/// <summary>Lowercase hex SHA-256: the file's bytes, or the HTML file list.</summary>`
to the base record if it has no summary.

In `GuideFingerprint`, add:

```csharp
    /// <summary>SHA-256 of a whole file's bytes.</summary>
    public static string OfBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>SHA-256 from the current position, checking the token between 81,920-byte reads.</summary>
    public static string OfStream(Stream stream, CancellationToken token)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.Read(buffer);
            if (read == 0)
            {
                break;
            }
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
```

In `GuideImportValidator`:
- In `ResolveTxtEncodingAsync`, return
  `new TxtImportManifest(source, inspection.SuggestedTitle, codePage, GuideFingerprint.OfBytes(bytes))`.
- In `InspectTxtAsync`, return
  `new ImportReady(new TxtImportManifest(source, title, null, GuideFingerprint.OfBytes(bytes)))`.
- Replace `InspectPdfAsync` and the `ReadPdf` signature:

```csharp
    private static Task<ImportInspection> InspectPdfAsync(
        ImportSource source, string title, CancellationToken token) =>
        Task.Run<ImportInspection>(() =>
        {
            using FileStream stream = OpenSource(source.FullPath, source.FileName, asyncIo: false);
            string fingerprint;
            try
            {
                fingerprint = GuideFingerprint.OfStream(stream, token);
            }
            catch (IOException)
            {
                throw Unreadable(source.FileName);
            }
            stream.Position = 0;
            return ReadPdf(stream, source, title, fingerprint, token);
        }, token);

    internal static ImportInspection ReadPdf(
        Stream stream, ImportSource source, string title, string fingerprint, CancellationToken token)
```

- In `ReadPdf`, return
  `new ImportReady(new PdfImportManifest(source, title, pages, hasText, fingerprint))`.

In `GuideImportPublisher.VerifyPdf`, pass the previewed fingerprint:
`GuideImportValidator.ReadPdf(stream, pdf.Source, pdf.SuggestedTitle, pdf.Fingerprint, token)`.

Search for other manifest constructions and fix any the build reports:

```bash
grep -rn "new TxtImportManifest\|new PdfImportManifest\|new HtmlImportManifest" src tests tools
```

- [ ] **Step 4: Run the tests to see them pass**

Stage, then run **Core tests** and **Infrastructure tests**.
Expected: both pass with 0 failures. The new tests pass, and
`HtmlManifestCarriesTheContentFingerprint` and the T06.3 publisher tests
still pass.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Core/Import/ImportContracts.cs src/DesktopGuides.Infrastructure/Import \
  tests/DesktopGuides.Infrastructure.Tests/Import
git commit -m "feat(p1): fingerprint every import manifest" \
  -m "Fingerprint moves to the base ImportManifest. TXT manifests hash the bytes the validator already read, and PDF manifests hash the file on the thread pool before parsing, stopping on cancellation between reads. ImportIssue gains Duplicate, and a test keeps the Missing, Unsupported, Encrypted and Unreadable issues distinct." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Fingerprint lookup in the repository and journal

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (after `GetGuideAsync` ~line 268, and the nested `ImportJournal` ~line 721)
- Modify: `src/DesktopGuides.Infrastructure/Storage/ImportJournal.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/ImportJournalTests.cs`

**Interfaces:**
- Consumes: `GuideFormat`, `Guide`, and `NewImportedGuide` (all existing).
- Produces:
  - `public Task<Guide?> SqliteLibraryRepository.FindGuideByFingerprintAsync(Guid gameId, GuideFormat format, string contentSha256, CancellationToken token = default)`
  - `Guid? IImportJournal.FindGuide(Guid gameId, GuideFormat format, string contentSha256)`

- [ ] **Step 1: Write the failing tests**

Add to `ImportJournalTests`, before the private helpers:

```csharp
    private static readonly string Hash = new('b', 64);

    [Fact]
    public async Task FingerprintLookupFindsTheGuideInTheSameGame()
    {
        Guid id = await PublishGuide(game.Id, GuideFormat.Txt, Hash);

        Guide? found = await repository.FindGuideByFingerprintAsync(game.Id, GuideFormat.Txt, Hash);

        Assert.Equal(id, found?.Id);
    }

    [Fact]
    public async Task FingerprintLookupIgnoresOtherGamesFormatsAndHashes()
    {
        Game other = await repository.AddGameAsync("Other Game", null, null);
        await PublishGuide(other.Id, GuideFormat.Txt, Hash);
        await PublishGuide(game.Id, GuideFormat.Pdf, Hash);
        await PublishGuide(game.Id, GuideFormat.Txt, new string('c', 64));

        Assert.Null(await repository.FindGuideByFingerprintAsync(game.Id, GuideFormat.Txt, Hash));
    }

    [Fact]
    public async Task FingerprintLookupReturnsTheOldestMatch()
    {
        await PublishGuide(game.Id, GuideFormat.Txt, Hash);
        Guid older = await PublishGuide(game.Id, GuideFormat.Txt, Hash);
        Scalar($"UPDATE Guides SET ImportedUtcMs = 1 WHERE Id = '{older:N}'");

        Guide? found = await repository.FindGuideByFingerprintAsync(game.Id, GuideFormat.Txt, Hash);

        Assert.Equal(older, found?.Id);
    }

    [Fact]
    public async Task JournalLookupUsesTheSameMatch()
    {
        Guid id = await PublishGuide(game.Id, GuideFormat.Txt, Hash);
        Guid? found = null;
        Guid? otherFormat = Guid.Empty;

        await Run(journal =>
        {
            found = journal.FindGuide(game.Id, GuideFormat.Txt, Hash);
            otherFormat = journal.FindGuide(game.Id, GuideFormat.Pdf, Hash);
        });

        Assert.Equal(id, found);
        Assert.Null(otherFormat);
    }
```

Add this helper next to `Guide()`:

```csharp
    private async Task<Guid> PublishGuide(Guid gameId, GuideFormat format, string sha)
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        string primary = format == GuideFormat.Pdf ? "guide.pdf" : "guide.txt";
        await Run(journal =>
        {
            journal.Prepare(operation, id);
            journal.Publish(
                new NewImportedGuide(operation, id, gameId, "Imported", format, primary, sha, 20, "notes", null),
                () => { });
        });
        return id;
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Stage, then run **Infrastructure tests** with
`--filter "FullyQualifiedName~ImportJournalTests"`.
Expected: the build fails. `FindGuideByFingerprintAsync` and
`IImportJournal.FindGuide` don't exist.

- [ ] **Step 3: Implement**

In `ImportJournal.cs`, add to `IImportJournal`:

```csharp
    /// <summary>The oldest guide in the game with this format and content hash, or null.</summary>
    Guid? FindGuide(Guid gameId, GuideFormat format, string contentSha256);
```

In `SqliteLibraryRepository`, after `GetGuideAsync`:

```csharp
    private const string FindGuideSql = """
        SELECT Id, GameId, Title, Format, ManagedRelativeRoot,
               PrimaryRelativePath, ContentSha256, ContentBytes,
               SourceLabel, TextCodePage, ImportedUtcMs, UpdatedUtcMs
        FROM Guides
        WHERE GameId = $gameId AND Format = $format AND ContentSha256 = $sha
        ORDER BY ImportedUtcMs, Id
        LIMIT 1
        """;

    /// <summary>The oldest guide in the game with this format and content hash, or null.</summary>
    public Task<Guide?> FindGuideByFingerprintAsync(
        Guid gameId, GuideFormat format, string contentSha256, CancellationToken token = default) =>
        ReadAsync<Guide?>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            return FindGuide(connection, gameId, format, contentSha256);
        }, token);

    private static Guide? FindGuide(
        SqliteConnection connection, Guid gameId, GuideFormat format, string contentSha256)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = FindGuideSql;
        command.Parameters.AddWithValue("$gameId", gameId.ToString("N"));
        command.Parameters.AddWithValue("$format", format.ToString());
        command.Parameters.AddWithValue("$sha", contentSha256);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadGuide(reader) : null;
    }
```

In the nested `ImportJournal`, before `Prepare`:

```csharp
        public Guid? FindGuide(Guid gameId, GuideFormat format, string contentSha256)
        {
            using SqliteConnection connection = owner.OpenConnection();
            return SqliteLibraryRepository.FindGuide(connection, gameId, format, contentSha256)?.Id;
        }
```

- [ ] **Step 4: Run the tests to see them pass**

Stage, then run **Infrastructure tests**.
Expected: all tests pass, including the four new `ImportJournalTests`.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Infrastructure/Storage tests/DesktopGuides.Infrastructure.Tests/ImportJournalTests.cs
git commit -m "feat(p1): look up guides by content fingerprint" \
  -m "FindGuideByFingerprintAsync returns the oldest guide in a game with the same format and content hash, reading without the write gate. IImportJournal.FindGuide runs the same query for callers that already hold the gate." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Publisher duplicate rule and copy fingerprint check

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs` (`PublishAsync` ~line 63, `PublishLockedAsync` ~line 74, constants ~line 20)
- Modify: `tests/DesktopGuides.Infrastructure.Tests/Import/PublisherHarness.cs` (`PublishAsync` ~line 62)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideImportPublisherTests.cs`

**Interfaces:**
- Consumes: `ImportManifest.Fingerprint` and `ImportIssue.Duplicate` (Task 1),
  and `IImportJournal.FindGuide` (Task 2).
- Produces: `public Task<Guid> GuideImportPublisher.PublishAsync(ImportManifest manifest, Guid gameId, string title, bool allowDuplicate, IProgress<ImportProgress>? progress, CancellationToken token)`

- [ ] **Step 1: Update the harness and the write-gate test**

In `PublisherHarness`, replace `PublishAsync` with:

```csharp
    public Task<Guid> PublishAsync(
        GuideImportPublisher publisher, ImportManifest manifest,
        IProgress<ImportProgress>? progress = null, CancellationToken token = default,
        bool allowDuplicate = false) =>
        publisher.PublishAsync(manifest, Game.Id, "Imported Guide", allowDuplicate, progress, token);
```

In `ImportHoldsTheWriteGate`, the second import publishes the same file
again, so change it to
`Task<Guid> import = harness.PublishAsync(publisher, manifest, allowDuplicate: true);`.

- [ ] **Step 2: Write the failing tests**

Add to `GuideImportPublisherTests`, after `SourceRewrittenDuringCopyIsChanged`:

```csharp
    private const string DuplicateMessage = "This file is already a guide for this game.";

    [Fact]
    public async Task SecondImportOfTheSameFileIsDuplicate()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        await harness.PublishAsync(harness.Publisher(), manifest);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal((ImportIssue.Duplicate, DuplicateMessage), (error.Issue, error.Message));
        Assert.Equal(1, harness.Count("Guides"));
        Assert.Equal(0, harness.Count("FileOperations"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.StagingRoot));
        Assert.Single(Directory.EnumerateFileSystemEntries(harness.Paths.ContentRoot));
    }

    [Fact]
    public async Task DuplicateCheckIgnoresTheEncodingChoice()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-legacy.txt", "legacy.txt");
        await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source, 437));
        ImportManifest other = await harness.InspectAsync(source, 1252);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), other));

        Assert.Equal(ImportIssue.Duplicate, error.Issue);
    }

    [Fact]
    public async Task AllowDuplicatePublishesAnIndependentCopy()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        Guid first = await harness.PublishAsync(harness.Publisher(), manifest);
        await harness.Repository.SaveReadingLocationAsync(first, "{\"one\":1}", 0.25);
        string firstFile = harness.Paths.ResolveExistingGuideFile(first, "guide.txt");
        byte[] firstBytes = File.ReadAllBytes(firstFile);

        Guid copy = await harness.PublishAsync(harness.Publisher(), manifest, allowDuplicate: true);

        Assert.NotEqual(first, copy);
        Assert.Equal(2, harness.Count("Guides"));
        Assert.Equal(2, Directory.EnumerateDirectories(harness.Paths.ContentRoot).Count());
        Assert.Equal(manifest.Fingerprint, (await harness.Repository.GetGuideAsync(copy))!.ContentSha256);
        Assert.Null((await harness.Repository.GetReadingStateAsync(copy))!.LocatorJson);
        Assert.Equal("{\"one\":1}", (await harness.Repository.GetReadingStateAsync(first))!.LocatorJson);
        Assert.Equal(firstBytes, File.ReadAllBytes(firstFile));
    }

    [Fact]
    public async Task SameFileInAnotherGameIsNotADuplicate()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        await harness.PublishAsync(harness.Publisher(), manifest);
        Game other = await harness.Repository.AddGameAsync("Other Game", "PC", null);

        Guid id = await harness.Publisher().PublishAsync(
            manifest, other.Id, "Imported Guide", false, null, CancellationToken.None);

        Assert.Equal(other.Id, (await harness.Repository.GetGuideAsync(id))!.GameId);
    }

    [Theory]
    [InlineData("txt-utf8.txt", "notes.txt")]
    [InlineData("pdf-short.pdf", "short.pdf")]
    public async Task SameLengthRewriteBeforeCopyIsChanged(string fixture, string name)
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy(fixture, name);
        ImportManifest manifest = await harness.InspectAsync(source);
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point != ImportCheckpoint.Prepared) return;
            // Same length and same write time: only the bytes tell the change.
            byte[] bytes = File.ReadAllBytes(source);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(source, bytes);
            File.SetLastWriteTimeUtc(source, manifest.Source.LastWriteUtc.UtcDateTime);
        });

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        harness.AssertNothingLeft();
    }
```

- [ ] **Step 3: Run the tests to see them fail**

Stage, then run **Infrastructure tests** with
`--filter "FullyQualifiedName~GuideImportPublisherTests"`.
Expected: the build fails, because `PublishAsync` has no `allowDuplicate`
parameter.

- [ ] **Step 4: Implement**

In `GuideImportPublisher`, add the constant after `ChangedMessage`:

```csharp
    private const string DuplicateMessage = "This file is already a guide for this game.";
```

Replace `PublishAsync`:

```csharp
    /// <summary>
    /// Returns the new guide's ID. Throws <see cref="GuideImportException"/>,
    /// with <see cref="ImportIssue.Duplicate"/> when the game already has this
    /// content and <paramref name="allowDuplicate"/> is false, or
    /// <see cref="OperationCanceledException"/> before publication starts.
    /// </summary>
    public Task<Guid> PublishAsync(
        ImportManifest manifest, Guid gameId, string title, bool allowDuplicate,
        IProgress<ImportProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string validTitle = GuideTitle.Create(title);
        return repository.RunImportAsync(
            (journal, gateToken) => PublishLockedAsync(
                journal, manifest, gameId, validTitle, allowDuplicate, progress, gateToken),
            token);
    }
```

Add `bool allowDuplicate` after `string title` in `PublishLockedAsync`'s
parameters. Its start becomes:

```csharp
        CheckSource(manifest.Source);
        if (!allowDuplicate)
        {
            CheckNotDuplicate(journal, manifest, gameId);
        }
        ImportPlan plan = await PlanAsync(manifest, token);
```

Right after the `CopyAsync` line, add:

```csharp
            // Size and write time can survive an in-place rewrite; the bytes can't.
            if (!string.Equals(fingerprint, manifest.Fingerprint, StringComparison.Ordinal))
            {
                throw Changed();
            }
```

Add the helper after `Pass`:

```csharp
    private static void CheckNotDuplicate(IImportJournal journal, ImportManifest manifest, Guid gameId)
    {
        Guid? existing;
        try
        {
            existing = journal.FindGuide(gameId, manifest.Format, manifest.Fingerprint);
        }
        catch (SqliteException)
        {
            throw new GuideImportException(ImportIssue.SaveFailed, SaveFailedMessage);
        }
        if (existing is not null)
        {
            throw new GuideImportException(ImportIssue.Duplicate, DuplicateMessage);
        }
    }
```

Update the class summary to add: "A duplicate of a guide in the same game
is refused unless the caller allows it."

- [ ] **Step 5: Run the tests to see them pass**

Stage, then run **Infrastructure tests**.
Expected: all tests pass. If another existing test now fails with
`Duplicate`, it publishes the same file twice. Pass
`allowDuplicate: true` there and ledger a ruling naming the test.

- [ ] **Step 6: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs tests/DesktopGuides.Infrastructure.Tests/Import
git commit -m "feat(p1): refuse duplicate imports unless allowed" \
  -m "PublishAsync takes allowDuplicate. Without it, a guide in the same game with the same format and content hash stops the import with Duplicate before any journal row or file exists. The copied bytes must also match the preview fingerprint, so a same-length rewrite with a restored write time is Changed." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Duplicate preview in the dialog, and Open existing in the shell

**Files:**
- Modify: `src/DesktopGuides.Production/ImportGuideDialog.xaml` (first child of `ImportPreview`, ~line 64)
- Modify: `src/DesktopGuides.Production/ImportGuideDialog.xaml.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (`ImportGuideClicked`, ~lines 756–782)

**Interfaces:**
- Consumes: `FindGuideByFingerprintAsync` (Task 2) and the five-argument
  `PublishAsync` (Task 3).
- Produces (used by the Task 5 smoke):
  - automation IDs `ImportDuplicate` and `ImportOpenExisting`;
  - a `PrimaryButton` named "Import" or "Import another copy";
  - `internal Guid? ImportGuideDialog.OpenGuideId`.

TDD skip: Production has no test project. This task is gated by the
**Production build** here and by the Task 5 installed smoke.

- [ ] **Step 1: Add the duplicate `InfoBar`**

In `ImportGuideDialog.xaml`, insert as the first child of the
`ImportPreview` `StackPanel`, before the title `StackPanel`:

```xml
                <InfoBar x:Name="ImportDuplicate"
                         Style="{StaticResource DesktopGuidesStatusInfoBarStyle}"
                         Severity="Informational"
                         IsOpen="False"
                         IsClosable="False"
                         AutomationProperties.AutomationId="ImportDuplicate">
                    <InfoBar.ActionButton>
                        <Button x:Name="ImportOpenExisting"
                                Content="Open existing"
                                Click="OpenExistingClicked"
                                AutomationProperties.AutomationId="ImportOpenExisting" />
                    </InfoBar.ActionButton>
                </InfoBar>
```

- [ ] **Step 2: Dialog fields and constructor**

In `ImportGuideDialog.xaml.cs`, add the constants and fields, and replace
the `import` field type:

```csharp
    private const string ImportLabel = "Import";
    private const string ImportCopyLabel = "Import another copy";
    private readonly string gameTitle;
    private readonly Func<ImportManifest, CancellationToken, Task<Guide?>> findDuplicate;
    private readonly Func<ImportManifest, string, bool, IProgress<ImportProgress>, CancellationToken, Task<Guid>> import;
    private Guide? duplicate;
```

Replace the constructor's signature and add the assignments:

```csharp
    internal ImportGuideDialog(
        string gameTitle, string path, IGuideImportValidator validator, Func<Task<string?>> pickFile,
        Func<ImportManifest, CancellationToken, Task<Guide?>> findDuplicate,
        Func<ImportManifest, string, bool, IProgress<ImportProgress>, CancellationToken, Task<Guid>> import)
    {
        InitializeComponent();
        Title = $"Import guide for {gameTitle}";
        this.gameTitle = gameTitle;
        this.validator = validator;
        this.pickFile = pickFile;
        this.findDuplicate = findDuplicate;
        this.import = import;
```

Keep the rest of the constructor. After `ImportedGuideId`, add:

```csharp
    /// <summary>The existing guide chosen with Open existing. Never set together with ImportedGuideId.</summary>
    internal Guid? OpenGuideId { get; private set; }
```

- [ ] **Step 3: Look up the fingerprint when a manifest is ready**

In `Check`, add `ShowDuplicate(null);` after `needsEncoding = null;`, and
replace the `ImportReady` case with:

```csharp
                case ImportReady ready:
                    Guide? existing = await findDuplicate(ready.Manifest, token);
                    if (current != generation || closing)
                    {
                        return;
                    }
                    ShowPreview(ready.Manifest.Source, ready.Manifest.SuggestedTitle, ready.Manifest.Format);
                    ShowManifest(ready.Manifest);
                    ShowDuplicate(existing);
                    break;
```

In `EncodingChanged`, add `ShowDuplicate(null);` after `Manifest = null;`.
Inside the lambda, replace `ShowManifest(manifest);` with:

```csharp
            Guide? existing = await findDuplicate(manifest, token);
            if (current != generation || closing)
            {
                return;
            }
            ShowManifest(manifest);
            ShowDuplicate(existing);
```

A lookup failure throws inside `RunAsync`, so it takes the existing
"couldn't be checked" path with no manifest.

In `ShowStatus`, add `ShowDuplicate(null);` after `needsEncoding = null;`.

Add after `ShowMessage`:

```csharp
    private void ShowDuplicate(Guide? existing)
    {
        duplicate = existing;
        PrimaryButtonText = existing is null ? ImportLabel : ImportCopyLabel;
        if (existing is not null)
        {
            string message = $"This file is already in {gameTitle} as \"{existing.Title}\".";
            ImportDuplicate.Message = message;
            AutomationProperties.SetName(ImportDuplicate, message);
        }
        ImportDuplicate.IsOpen = existing is not null;
    }
```

- [ ] **Step 4: Import another copy and Open existing**

In `ImportClicked`, pass the choice:
`Track(ImportAsync(ready, title, allowDuplicate: duplicate is not null));`.

Change `ImportAsync` to `private async Task ImportAsync(ImportManifest ready, string title, bool allowDuplicate)`
and its call to
`ImportedGuideId = await import(ready, title, allowDuplicate, new Progress<ImportProgress>(ShowImportProgress), cancel.Token);`.

In `ShowImporting`, add `ImportDuplicate.IsOpen = false;` after
`ImportStatus.IsOpen = false;`. In `HideImporting`, add
`ImportDuplicate.IsOpen = duplicate is not null;` after
`EncodingOptions.IsEnabled = true;`.

Add after `CancelClicked`:

```csharp
    private void OpenExistingClicked(object sender, RoutedEventArgs args)
    {
        if (importing || closing || duplicate is not { } existing)
        {
            return;
        }
        OpenGuideId = existing.Id;
        Hide();
    }
```

- [ ] **Step 5: Shell wiring**

In `ShellWindow.ImportGuideClicked`, replace the dialog construction with:

```csharp
                SqliteLibraryRepository library = RequireRepository();
                ImportGuideDialog dialog = new(
                    game.Title, path, importValidator, PickGuideFileAsync,
                    (manifest, token) => library.FindGuideByFingerprintAsync(
                        game.Id, manifest.Format, manifest.Fingerprint, token),
                    (manifest, title, allowDuplicate, progress, token) =>
                        publisher.PublishAsync(manifest, game.Id, title, allowDuplicate, progress, token))
                {
                    XamlRoot = Navigation.XamlRoot
                };
```

Replace the block after `ShowAsync`'s `finally` with:

```csharp
                if (!closeRequested && navigator.Current is GameRoute shown && shown.GameId == route.GameId)
                {
                    // An import that reached publication is kept even if the dialog was closed.
                    if (dialog.ImportedGuideId is Guid guideId)
                    {
                        imported = true;
                        pendingGuideFocus = guideId;
                        await RenderCurrentAsync();
                    }
                    else if (dialog.OpenGuideId is Guid existingId)
                    {
                        // Already inside the navigation queue, so open directly.
                        await OpenGuideAsync(existingId, route.GameId);
                    }
                }
```

If `SqliteLibraryRepository` needs a `using`, it is already imported for
`GuideImportPublisher`'s construction. Check with the build.

- [ ] **Step 6: Build**

Stage, then run the **Production build**.
Expected: `Build succeeded` with 0 errors and no new warnings.

Review the diff against Review Focus item 5:
- each `findDuplicate` await is followed by the generation and `closing`
  check;
- `OpenExistingClicked` does nothing while importing or closing;
- `ShowDuplicate(null)` runs on every path that clears the manifest.

- [ ] **Step 7: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Production/ImportGuideDialog.xaml src/DesktopGuides.Production/ImportGuideDialog.xaml.cs \
  src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(p1): offer Open existing or Import another copy" \
  -m "When a previewed file is already a guide in the game, the preview shows which guide in an InfoBar with an Open existing action, and the primary button becomes Import another copy. Open existing closes the dialog and opens that guide. Import another copy publishes with allowDuplicate." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Installed smoke for the duplicate choice

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (Mode `ValidateSet` ~line 12, params ~line 52, the `import-*` `else` branch ~lines 1658–1685)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-ShellSmoke` ~line 657, `Run-ImportScenarios` ~line 794)

**Interfaces:**
- Consumes: the Task 4 automation IDs and button names.
- Produces:
  - smoke modes `import-duplicate-copy` and `import-duplicate-open`, and
    the parameter `-ExpectedGuideTitle`;
  - results `import-duplicate-dark` and `import-open-light`, with the
    screenshots `import-duplicate`, `import-copied` and
    `import-open-existing`.

TDD skip: the smoke is the test. Its first real run is the Step 5
installed verification.

- [ ] **Step 1: Smoke parameters**

Add `'import-duplicate-copy', 'import-duplicate-open'` after
`'import-publish'` in the Mode `ValidateSet`. After
`$SteamGridDbCredentialFile`, add:

```powershell
    # The existing guide a duplicate preview must name.
    [string] $ExpectedGuideTitle = '',
```

- [ ] **Step 2: Split the publish branch**

Replace the `else` branch of `if ($Mode -eq 'import-preview')` with:

```powershell
        else {
            if ($Mode -ne 'import-publish' -and -not $ExpectedGuideTitle) {
                throw "$Mode needs -ExpectedGuideTitle."
            }
            Select-Element 'Import Test Game'
            [void](Wait-Name 'GameHeading' 'Import Test Game')
            [void](Wait-Status 'Game ready.')
            Click-Element (Wait-EnabledById 'ImportGuideButton')
            Choose-PickerFile 'txt-legacy.txt'
            [void](Wait-VisibleById 'ImportGuideDialog')
            [void](Wait-PresentById 'ImportEncodingCp437')
            if ((Wait-VisibleById 'PrimaryButton').Current.IsEnabled) {
                throw 'Import was enabled before an encoding was chosen.'
            }
            [void](Select-ById 'ImportEncodingCp437')
            [void](Wait-Text 'ImportEncodingValue' 'DOS (CP437)')

            if ($Mode -eq 'import-publish') {
                $importTitle = 'Imported Guide ' + [guid]::NewGuid().ToString('N').Substring(0, 8)
                [void](Wait-Name 'PrimaryButton' 'Import')
                Assert-Absent 'ImportDuplicate'
                Set-Text 'GuideTitleInput' $importTitle
                [void](Wait-EnabledById 'PrimaryButton')
                $report.importReadyScreenshot = Save-WindowScreenshot 'import-ready'
                $report.phases += 'import-ready'

                Invoke-Element (Wait-EnabledById 'PrimaryButton')
                [void](Wait-HiddenById 'ImportGuideDialog')
                [void](Wait-SelectedGuide $importTitle)
                Wait-FocusedGuide $importTitle
                $report.importedTitle = $importTitle
                $report.importPublishedScreenshot = Save-WindowScreenshot 'import-published'
                $report.phases += 'import-published'
            }
            else {
                $duplicateMessage = 'This file is already in Import Test Game as "' + $ExpectedGuideTitle + '".'
                [void](Wait-Name 'ImportDuplicate' $duplicateMessage)
                [void](Wait-Name 'PrimaryButton' 'Import another copy')
                [void](Wait-EnabledById 'ImportOpenExisting')
                $report.importDuplicateScreenshot = Save-WindowScreenshot 'import-duplicate'
                $report.phases += 'import-duplicate'

                if ($Mode -eq 'import-duplicate-copy') {
                    $copyTitle = 'Copied Guide ' + [guid]::NewGuid().ToString('N').Substring(0, 8)
                    Set-Text 'GuideTitleInput' $copyTitle
                    Invoke-Element (Wait-EnabledById 'PrimaryButton')
                    [void](Wait-HiddenById 'ImportGuideDialog')
                    [void](Wait-SelectedGuide $copyTitle)
                    Wait-FocusedGuide $copyTitle
                    $report.importedTitle = $copyTitle
                    $report.importCopiedScreenshot = Save-WindowScreenshot 'import-copied'
                    $report.phases += 'import-copied'
                }
                else {
                    Invoke-Element (Wait-EnabledById 'ImportOpenExisting')
                    [void](Wait-HiddenById 'ImportGuideDialog')
                    [void](Wait-Name 'ReaderHeading' $ExpectedGuideTitle)
                    [void](Wait-Status 'Guide details ready.')
                    $report.importOpenExistingScreenshot = Save-WindowScreenshot 'import-open-existing'
                    $report.phases += 'import-open-existing'
                }
            }
        }
```

- [ ] **Step 3: Install-script runs**

In `Run-ShellSmoke`, add `[string] $ExpectedGuideTitle = ''` as the last
parameter, after `$ExpectedProviderFailure`. After the
`ExpectedProviderFailure` argument block, add:

```powershell
    if ($ExpectedGuideTitle) {
        $arguments += ' -ExpectedGuideTitle "' + $ExpectedGuideTitle + '"'
    }
```

In `Run-ImportScenarios`, replace the dark `import-publish` run with:

```powershell
        $existingTitle = $report.importPublishLight.importedTitle

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.importDuplicateDark = Run-ShellSmoke 'import-duplicate-copy' `
            -ResultName 'import-duplicate-dark' -ExpectedGuideTitle $existingTitle
        Close-InstalledShell

        Set-AppThemePreference $true
        Start-InstalledShell
        $report.importOpenLight = Run-ShellSmoke 'import-duplicate-open' `
            -ResultName 'import-open-light' -ExpectedGuideTitle $existingTitle
        Close-InstalledShell
```

Keep the final expected counts. Change their error message from
`"After two imports, ..."` to
`"After an import, a copy and Open existing, $name was $($state.$name); expected $($expected[$name])."`

- [ ] **Step 4: Static checks**

```bash
perl -ne 'print "$ARGV:$.: non-ASCII\n" if /[^\x00-\x7F]/; close ARGV if eof' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
```

Expected: no output.

Stage, then parse both scripts on the host:

```bash
s 'powershell -NoProfile -Command "foreach ($f in @(''E:\work\desktop-guides\t06-4\tools\p1\windows_shell_ui_smoke.ps1'',''E:\work\desktop-guides\t06-4\tools\p1\windows_shell_install.ps1'')) { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); if ($e) { $e; exit 1 } }; ''parsed''"'
```

Expected: `parsed`.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): smoke-test duplicate imports" \
  -m "After the light import-publish run, a dark import-duplicate-copy run picks the same file, checks the duplicate InfoBar names the existing guide and the primary button reads Import another copy, and imports a copy. A light import-duplicate-open run invokes Open existing and checks the Reader shows the oldest guide. The final library state is unchanged: two CP437 guides and no residue." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Installed verification**

This needs a pushed branch, so ask the user first. After approval, push
and let CI's `production-shell-ui` job run. It is the gate of record.

Alternatively, with a CI-built x64 MSIX already on the host, run
`windows_shell_install.ps1 -PackagePath <msix> -ResultDirectory E:\work\desktop-guides\t06-4-results -ImportOnly`
through an interactive scheduled task. Follow `docs/p1/e2e-testing.md`:
back up and restore the app data and `%LOCALAPPDATA%\DesktopGuides\P0-WebView`.

Expected:
- `success: true`;
- phases `import-duplicate` and `import-copied` in `import-duplicate-dark`;
- phases `import-duplicate` and `import-open-existing` in `import-open-light`;
- `importState` shows 2 guides, 0 file operations, 0 staging entries, 2
  content directories and 2 CP437 guides.

Copy the screenshots to `docs/p1/evidence/t06-4-duplicate-import/`:

| From | To |
| --- | --- |
| `import-open-light.import-duplicate.png` | `duplicate-light.png` |
| `import-duplicate-dark.import-duplicate.png` | `duplicate-dark.png` |
| `import-duplicate-dark.import-copied.png` | `copied-dark.png` |
| `import-open-light.import-open-existing.png` | `open-existing-light.png` |

---

### Task 6: Documentation and verification record

**Files:**
- Modify: `docs/p1/t06-4-duplicate-import-design.md` (status line, Testing, new verification record)
- Modify: `docs/p1/implementation-plan.md` (after the T06.3 paragraph ending "Duplicate handling remains T06.4.", ~line 638)
- Modify: `docs/progress.md` (date line, row 19, new T06.4 row)
- Modify: `docs/p1/e2e-testing.md` (the `Import publication` row ~line 250, a new `Duplicate import` row)
- Modify: `docs/p1/t06-3-import-publication-design.md` (the Duplicates bullet ~line 33)

**Interfaces:**
- Consumes:
  - the CI run ID and conclusion from Task 5 Step 6;
  - the four evidence PNGs;
  - every `Ruling:` line in the executor's ledger.

TDD skip: this task changes documentation only. The gate is
`git diff --check` and a read-through.

Fill every `<…>` below from the observed run before committing. Never
commit a placeholder.

- [ ] **Step 1: Spec**

In `t06-4-duplicate-import-design.md`:

- Replace the status line with:
  `Status: implemented on \`feat/p1-t06-4-duplicate-import\`; verified by CI run <run id>.`
  Keep the prerequisite sentence.
- In Testing → Publisher, change "(at the `Copied` checkpoint)" to "(at
  the `Prepared` checkpoint, before the copy reads it)". This follows
  Ruling 1.
- In Testing → Validator, change "Cancelling during the PDF hash stops the
  inspection." to "Cancelling during a stream hash stops after the current
  read (`GuideFingerprint.OfStream`)." This follows Ruling 3.
- Append:

```markdown
## T06.4 verification record

- **Unit tests.** On `pcsx2-win`, Infrastructure <n>/<n> and Core <n>/<n>
  passed. The new tests are:
  - `GuideFingerprintTests`: the stream hash across buffers and on
    cancellation;
  - `GuideImportValidatorTests`: the UTF-8, resolved TXT and PDF manifest
    fingerprints, and the typed issues staying distinct;
  - `ImportJournalTests`: the fingerprint lookup's same-game match, the
    other game, format and hash cases, the oldest match, and the
    journal's `FindGuide`;
  - `GuideImportPublisherTests`: `Duplicate`, the ignored encoding,
    `allowDuplicate`, another game, and the same-length rewrite for TXT
    and PDF.
- **Installed.** CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>)
  passed `production-shell-ui`. In the import group:
  - the light `import-publish` run imported `txt-legacy`;
  - the dark `import-duplicate-copy` run showed the duplicate InfoBar
    naming that guide and imported a copy through **Import another copy**;
  - the light `import-duplicate-open` run opened the first guide through
    **Open existing**.

  The final state was 2 guides, 0 file operations, 0 staging entries, 2
  content directories and 2 CP437 guides.
- **Rulings.** Rulings 1–11 in the [plan](t06-4-duplicate-import-plan.md#rulings-against-the-spec),
  plus <the ledger rulings made during implementation, each on one line,
  or "none">.
- **Evidence.**
  - [Duplicate preview, light](evidence/t06-4-duplicate-import/duplicate-light.png)
  - [Duplicate preview, dark](evidence/t06-4-duplicate-import/duplicate-dark.png)
  - [Copy imported, dark](evidence/t06-4-duplicate-import/copied-dark.png)
  - [Open existing, light](evidence/t06-4-duplicate-import/open-existing-light.png)
```

- [ ] **Step 2: Implementation plan, T06.3 design and E2E catalogue**

In `docs/p1/implementation-plan.md`, after the T06.3 paragraph, add:

```markdown
T06.4 is implemented on `feat/p1-t06-4-duplicate-import`; see the
[design and verification record](t06-4-duplicate-import-design.md). The
preview fingerprints every format and looks up the same game, format and
hash. A match offers **Open existing** or **Import another copy**. The
publisher refuses a duplicate unless the caller allows it, and it fails
with `Changed` when the copied bytes differ from the preview. CI run
<run id> passed the installed import group: light publish, a dark copy
and a light Open existing.
```

In `docs/p1/t06-3-import-publication-design.md`, replace the Duplicates
bullet with:

```markdown
- **Duplicates** are handled by T06.4; see
  [t06-4-duplicate-import-design.md](t06-4-duplicate-import-design.md).
```

In `docs/p1/e2e-testing.md`:

- In the `Import publication` row, change "In light and dark, the dialog
  closes" to "In light, the dialog closes".
- Change "one CP437 guide per run" to "one CP437 guide".
- After that row, add:

```markdown
| Duplicate import | After Import publication, pick `txt-legacy` again in the same game and choose CP437. The preview's `ImportDuplicate` InfoBar names the existing guide, and the primary button reads Import another copy. In dark, import a copy under a new title; it is selected and focused. In light, choose Open existing; the dialog closes and the Reader shows the first guide. Afterwards the library has two CP437 guides, two content directories, and no file operation or staging entry. | T06.4, TR06.3, TR11.3 |
```

- [ ] **Step 3: Progress**

In `docs/progress.md`:

- Keep the "Updated" date line current.
- Replace row 19's status cell with:
  `Merged through [PR #19](https://github.com/ilya-slalom/desktop-guides/pull/19) on 30 September 2026, merge commit \`494cb02\`.`
- In row 19, replace "Duplicate handling is T06.4." with "T06.4 adds
  duplicate handling."
- After row 19, add:

```markdown
| P1 T06.4 duplicate import | Implemented on `feat/p1-t06-4-duplicate-import`; PR open. | Importing a file already in the game offers Open existing or Import another copy, and the publisher never adds a duplicate unless asked. The copied bytes must match the preview fingerprint. CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>) passed the installed import group; see the [verification record](p1/t06-4-duplicate-import-design.md#t064-verification-record). |
```

After the PR opens, change "PR open" to the linked PR number in a
follow-up commit on the same branch.

- [ ] **Step 4: Check and commit**

```bash
grep -n '<run id>\|<n>/<n>\|<the ledger' docs/p1/t06-4-duplicate-import-design.md docs/p1/implementation-plan.md docs/progress.md
git diff --check
```

Expected: no output from either command.

Show the message in chat first.

```bash
git add docs/p1/t06-4-duplicate-import-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/e2e-testing.md docs/p1/t06-3-import-publication-design.md docs/p1/evidence/t06-4-duplicate-import
git commit -m "docs(p1): record T06.4 duplicate import verification" \
  -m "Mark the T06.4 design implemented and add its verification record: unit tests, CI run <run id>, rulings and four screenshots. Add the T06.4 paragraph to the implementation plan, a Duplicate import row to the E2E catalogue and a T06.4 row to progress, record T06.3 as merged, and point the T06.3 Duplicates bullet at T06.4." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Traceability

| Requirement | Evidence in this plan |
| --- | --- |
| TR06.3: fingerprints and IDs distinguish an unchanged copy | Task 1 manifest fingerprint tests; Task 2 lookup tests; Task 3 `SecondImportOfTheSameFileIsDuplicate`, `DuplicateCheckIgnoresTheEncodingChoice`, `AllowDuplicatePublishesAnIndependentCopy` and `SameFileInAnotherGameIsNotADuplicate`; Task 5 `import-duplicate-copy` and `import-duplicate-open` |
| TR06.1: the original is never written | Task 3 `SameLengthRewriteBeforeCopyIsChanged`, plus the T06.3 source-fingerprint assertions |
| TR06.2: nothing is published after a failed check | Task 3 `SecondImportOfTheSameFileIsDuplicate` checks no row, staging entry or content directory |
| TR11.3: installed UI Automation | Task 5 drives both choices by automation ID in light and dark |

## PR outcome

Open the PR only with the user's approval. Use the GitHub MCP, targeting
`main`. The body covers:

- **Target task:** T06.4, duplicate fingerprint choice.
- **Prerequisites:** T06.3 (PR #19), merged as `494cb02`.
- **Outcome:** importing a file that's already in the game offers **Open
  existing** or **Import another copy**. The publisher refuses a silent
  duplicate, and the copied bytes must match the preview.
- **Verification:**
  - the host unit test counts;
  - CI run <run id> with `production-shell-ui` green.
- **Screenshots:** the four evidence PNGs, embedded with absolute
  `https://github.com/ilya-slalom/desktop-guides/blob/<sha>/docs/p1/evidence/t06-4-duplicate-import/<name>.png?raw=true`
  URLs on the pushed commit.
- **Footer:** `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

Fill `<run id>` and `<sha>` from the pushed branch when the PR is written.
