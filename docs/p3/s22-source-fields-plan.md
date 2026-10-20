# S22 PR 2 (source fields and saving a guide again) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A guide or map can record where it was saved from, and saving
the same source item again replaces its content in place. The guide keeps
its ID, place, bookmarks, completion, title and opened time. A failure at
any step leaves the library as it was (TR22.1). There's no UI; PR 3's
capture is the first caller.

**Architecture:**
- **Core** gets `SourceSite`, `GuideSource`, `GuideReplaceOutcome`, and
  `ImportIssue.FormatChanged`.
- **Storage** moves to schema 9:
  - two new tables, `GuideSources` and `GameSteamApps`;
  - a `FileOperations` rebuild, so that it accepts the `ReplaceGuide` kind.

  `Guides` and `Games` don't change.
- **Import:**
  - `GuideImportPublisher.PublishAsync` takes an optional `GuideSource`.
  - A new `ReplaceAsync` stages and verifies the new copy, moves the old
    files aside, moves the new ones in, and commits the row changes in one
    transaction.
  - `FileOperationReconciler` finishes or undoes an interrupted replacement
    at startup.
- **Providers:** `ProviderGameImporter` stores IGDB's Steam app ID.

**Tech Stack:** .NET 10, Microsoft.Data.Sqlite, xUnit.

**Spec:** [the P3 technical design](../p3-technical-design.md), sections
5.4 and 5.5 (approved 20 October 2026).

## Global Constraints

- **Schema 9**, exactly:

```sql
CREATE TABLE GuideSources (
    GuideId TEXT PRIMARY KEY REFERENCES Guides(Id) ON DELETE CASCADE,
    Site TEXT NOT NULL CHECK (Site IN ('GameFaqs', 'Neoseeker', 'Steam', 'VgMaps')),
    ItemId TEXT NOT NULL CHECK (length(ItemId) BETWEEN 1 AND 200),
    Url TEXT NOT NULL CHECK (length(Url) BETWEEN 1 AND 2048),
    Author TEXT CHECK (Author IS NULL OR length(Author) BETWEEN 1 AND 200),
    Notice TEXT CHECK (Notice IS NULL OR length(Notice) <= 16384),
    SourceTitle TEXT CHECK (SourceTitle IS NULL OR length(SourceTitle) BETWEEN 1 AND 512),
    SavedUtcMs INTEGER NOT NULL CHECK (SavedUtcMs >= 0),
    SourceUpdatedUtcMs INTEGER CHECK (SourceUpdatedUtcMs IS NULL OR SourceUpdatedUtcMs >= 0),
    CapturedPages INTEGER CHECK (CapturedPages IS NULL OR CapturedPages >= 1),
    TotalPages INTEGER CHECK (TotalPages IS NULL OR TotalPages >= 2),
    CHECK ((CapturedPages IS NULL) = (TotalPages IS NULL)),
    CHECK (CapturedPages IS NULL OR CapturedPages < TotalPages)
);
CREATE INDEX IX_GuideSources_Item ON GuideSources(Site, ItemId);
CREATE TABLE GameSteamApps (
    GameId TEXT PRIMARY KEY REFERENCES Games(Id) ON DELETE CASCADE,
    AppId TEXT NOT NULL CHECK (length(AppId) BETWEEN 1 AND 12 AND AppId NOT GLOB '*[^0-9]*')
);
CREATE TABLE FileOperations9 (
    Id TEXT PRIMARY KEY CHECK (length(Id) = 32),
    Kind TEXT NOT NULL CHECK (Kind IN ('Import', 'DeleteGuide', 'DeleteGame', 'ReplaceGuide')),
    Phase TEXT NOT NULL CHECK (Phase IN ('Prepared', 'Committed')),
    ManifestJson TEXT NOT NULL,
    CreatedUtcMs INTEGER NOT NULL CHECK (CreatedUtcMs >= 0)
);
INSERT INTO FileOperations9 SELECT Id, Kind, Phase, ManifestJson, CreatedUtcMs FROM FileOperations;
DROP TABLE FileOperations;
ALTER TABLE FileOperations9 RENAME TO FileOperations;
PRAGMA user_version = 9;
```

  If `Version1`'s `FileOperations` text differs from the above other than
  in the `Kind` CHECK, copy `Version1`'s text and change only that CHECK.
- **A partial save** has `CapturedPages < TotalPages`. A complete save
  stores both as null.
- **Identity:** a saved item is found by `(game, Site, ItemId)`. For
  VGMaps, `ItemId` is the map image's path on the site, so a map is
  replaced by its source URL, as the design says.
- **Saving again** (`ReplaceAsync`):
  - **The fingerprint matches the stored `ContentSha256`:** the outcome is
    `UpToDate`, and no file or row changes, not even the source row.
  - **Kept:** the guide's `Id`, `GameId`, `Title`, `ImportedUtcMs`, its
    `ReadingStates` row (locator, estimate, opened, completed), its
    `Bookmarks` and its `ReaderPreferences`.
  - **Changed:** `PrimaryRelativePath`, `ContentSha256`, `ContentBytes`,
    `TextCodePage`, `PixelWidth`, `PixelHeight`, `SourceLabel`, and
    `UpdatedUtcMs` (now), plus all of `GuideAssets` and `GuidePages`, and
    the `GuideSources` row (upserted).
  - **A format change between Txt and Html:** refused with
    `ImportIssue.FormatChanged` unless `allowFormatChange` is true. When it
    is allowed, the locator and estimate are cleared, the bookmarks are
    deleted, and `ReaderPreferences` reset to nulls. Completion and the
    opened time stay.
  - **Any change involving `Image` or `Pdf`:** always refused with
    `FormatChanged`.
- **Places after a replacement** rely on the existing fallback: a
  locator's hash no longer matches, so it decodes as `ContentChanged` and
  restores by context, then approximately, with the existing notice. An
  HTML place survives only if its page's document path still exists, so
  PR 3 must give a guide's pages stable paths across saves. Record that
  in the PR 3 plan; this PR only tests it.
- **The message for `FormatChanged`:** "The saved copy is a {old} guide
  and the new one is {new}. Replacing it loses your place and bookmarks."
  {old} and {new} come from `ImportPresentation.FormatLabel`.
- **Shell rules:** no `cd` in local shell commands. PowerShell stays
  ASCII-only and runs under 5.1 and 7.

## Review Focus

1. **A crash between moving the old files aside and the commit** leaves
   both trees on disk. The next start must put the old guide back and
   delete the new one, byte for byte. Pinned by Task 4's
   `AnInterruptedReplacementRollsBackOnTheNextStart`, at every checkpoint.
2. **An HTML guide re-saved with one page removed:** a place on a kept
   page restores by context, and a place on the removed page falls back
   to the start, without an error. Pinned by Task 3's
   `APlaceOnARemovedPageDoesNotFailTheOpen`.
3. **A version 8 backup** restores and migrates, with no sources and no
   Steam IDs. Pinned by Task 2's `AVersionEightBackupRestores`.
4. **Removing a saved guide or its game** removes its source row and the
   game's Steam ID, through cascades. Pinned by Task 2's
   `RemovingAGuideOrGameRemovesItsSourceRows`.
5. **The same source item in two games** is two separate guides. Saving
   in one game never replaces the other's. Pinned by Task 3's
   `TheSameItemInAnotherGameIsNotReplaced`.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\s22b) { Remove-Item -Recurse -Force E:\work\desktop-guides\s22b }; New-Item -ItemType Directory E:\work\desktop-guides\s22b | Out-Null"'
  git -C /Users/ilya.lissoboi/work/desktop-guides ls-files -z -co --exclude-standard | grep -z -v -E '^(\.claude/|\.superpowers/|az2aws)' | COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --null -cf - -T - | s 'tar -xf - -C E:\work\desktop-guides\s22b'
  s 'cd /d E:\work\desktop-guides\s22b && python tools\p0\make_fixtures.py > E:\work\s22b-fixtures.log 2>&1'
}
t(){ f=""; [ -n "$2" ] && f="--filter \"$2\""; s "cd /d E:\\work\\desktop-guides\\s22b && dotnet test tests\\$1 $f -nologo > E:\\work\\s22b-test.log 2>&1 & findstr /c:\"Passed!\" /c:\"Failed!\" /c:\"error CS\" /c:\"[FAIL]\" E:\\work\\s22b-test.log"; }
build(){ s 'cd /d E:\work\desktop-guides\s22b && dotnet build DesktopGuides.sln -c Release -p:Platform=x64 -nologo > E:\work\s22b-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\s22b-build.log'; }
```

No installed run is needed: nothing in Production changes. The PR's own
CI still runs every installed group as a regression check.

---
### Task 1: Core: the source record and the replacement outcome

**Files:**
- Create: `src/DesktopGuides.Core/Library/GuideSource.cs`
- Modify: `src/DesktopGuides.Core/Import/ImportContracts.cs` (`ImportIssue.FormatChanged`, `GuideReplaceOutcome`)
- Test: `tests/DesktopGuides.Core.Tests/GuideSourceTests.cs` (new)

**Interfaces:**
- Produces:
  - `public enum SourceSite { GameFaqs, Neoseeker, Steam, VgMaps }`. Stored by name, so the names are fixed;
  - `public sealed record GuideSource(SourceSite Site, string ItemId, string Url, string? Author, string? Notice, string? SourceTitle, DateTimeOffset SavedUtc, DateTimeOffset? SourceUpdatedUtc, int? CapturedPages = null, int? TotalPages = null)`;
  - `GuideSource.IsPartial` (`CapturedPages is not null`);
  - `GuideSource.Validate()`, which throws `ArgumentException` for anything schema 9's CHECKs would refuse. The constructor doesn't validate; the repository calls `Validate` before writing;
  - `public enum GuideReplaceOutcome { UpToDate, Replaced }`;
  - `ImportIssue.FormatChanged`, appended last.

- [ ] **Step 1: Write the failing tests** in `GuideSourceTests`:

```csharp
    [Fact]
    public void ACompleteSourceIsValidAndNotPartial()
    {
        GuideSource source = Source();
        source.Validate();
        Assert.False(source.IsPartial);
    }

    [Fact]
    public void APartialSourceHasFewerCapturedPagesThanTotal()
    {
        Assert.True(Source() with { CapturedPages = 37, TotalPages = 80 } is { IsPartial: true });
        Assert.Throws<ArgumentException>(() => (Source() with { CapturedPages = 80, TotalPages = 80 }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { CapturedPages = 3 }).Validate());
    }

    [Theory]
    [InlineData("", "https://example.test/g")]
    [InlineData("id", "")]
    public void AnEmptyIdOrUrlIsInvalid(string id, string url) =>
        Assert.Throws<ArgumentException>(() => (Source() with { ItemId = id, Url = url }).Validate());

    [Fact]
    public void LimitsMatchTheSchema()
    {
        Assert.Throws<ArgumentException>(() => (Source() with { ItemId = new string('a', 201) }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { Url = new string('a', 2049) }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { Author = "" }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { Notice = new string('a', 16385) }).Validate());
        Assert.Throws<ArgumentException>(() => (Source() with { SourceTitle = new string('a', 513) }).Validate());
    }

    [Fact]
    public void SiteNamesAreStable() =>
        Assert.Equal(["GameFaqs", "Neoseeker", "Steam", "VgMaps"], Enum.GetNames<SourceSite>());
```

  `Source()` is a local helper:
  `new(SourceSite.GameFaqs, "13639", "https://gamefaqs.gamespot.com/ps/197341-final-fantasy-vii/faqs/13639", "Author", "(c) Author", "Walkthrough", DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000), null)`.

- [ ] **Step 2: Run them.** `t DesktopGuides.Core.Tests GuideSource`.
  Expected: build errors for the missing types.
- [ ] **Step 3: Implement** the Interfaces block.
- [ ] **Step 4: Run them.** `t DesktopGuides.Core.Tests`.
  Expected: PASS, the whole suite.
- [ ] **Step 5: Commit:** "feat(p3): the guide source record in Core".

### Task 2: Schema 9, source rows on import, Steam app IDs, backup and restore

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibrarySchema.cs` (`CurrentVersion = 9`, `Version9`, `Migrations`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/ImportJournal.cs` (`NewImportedGuide` gains `GuideSource? Source = null` as its last parameter)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (the journal's `Publish` writes the source row; the read and Steam methods below)
- Modify: `src/DesktopGuides.Infrastructure/Storage/FileOperationManifest.cs` (`FileOperationKind.ReplaceGuide`, so the kind parses; it's used from Task 3)
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs` (`PublishAsync` gains `GuideSource? source = null` as its last parameter, passed to `NewImportedGuide`)
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`, `ImportJournalTests.cs`, `Restore/RestoreFixture.cs`, `Restore/LibraryRestorerSwapTests.cs`

**Interfaces:**
- Consumes: Task 1's `GuideSource`, `SourceSite`.
- Produces:
  - schema 9, exactly the Global Constraints SQL;
  - on `ILibraryRepository`:
    - `Task<GuideSource?> GetGuideSourceAsync(Guid guideId, CancellationToken token = default)`;
    - `Task<IReadOnlyDictionary<Guid, GuideSource>> ListGuideSourcesAsync(Guid gameId, CancellationToken token = default)`;
    - `Task<string?> GetSteamAppIdAsync(Guid gameId, CancellationToken token = default)`;
    - `Task SetSteamAppIdAsync(Guid gameId, string? appId, CancellationToken token = default)`. Null deletes the row; an invalid ID throws `ArgumentException`; a missing game throws `InvalidOperationException`;
  - on `SqliteLibraryRepository` (not the interface), `Task<Guide?> FindSourcedGuideAsync(Guid gameId, SourceSite site, string itemId, CancellationToken token = default)`. If more than one matches (two copies saved deliberately), it returns the oldest by `ImportedUtcMs`, then `Id`;
  - `RestoreFixture.SourcesBeforeVersionNine()`: SQL that drops `GuideSources` and `GameSteamApps`, and rebuilds `FileOperations` with `Version1`'s exact text, as `GuidesBeforeVersionEight()` does for `Guides`.

- [ ] **Step 1: Write the failing tests.**
  - **Migration**, beside the version 8 tests:
    - `UpgradesPopulatedVersionEightKeepingEveryRow`: `CreatePopulatedVersionEight` (version 7 plus `Version8`), then the same census as the version 7 test. It asserts `user_version` 9 and that `GuideSources` and `GameSteamApps` are empty.
    - `FailedVersionNineMigrationStaysAtVersionEight`: the checkpoint throws at version 9, and the database stays at 8 with its rows intact.
    - Update the hard-coded `8L` in the version 7 test to `LibrarySchema.CurrentVersion`.
  - **The source row:** `AnImportWithASourceStoresItAndReadsItBack`.
    - Publish a TXT guide through `ImportJournal` with `Source = Source()` (Task 1's helper, copied).
    - Assert that `GetGuideSourceAsync` returns an equal record, `ListGuideSourcesAsync(game)` has that one entry, and `FindSourcedGuideAsync(game, GameFaqs, "13639")` returns the guide.
    - An import without a source has no row.
  - **Review Focus 4:** `RemovingAGuideOrGameRemovesItsSourceRows`. Remove the sourced guide through `GuideRemover`: no `GuideSources` rows remain. Set a Steam app ID, remove the game: no `GameSteamApps` rows remain.
  - **Steam IDs:** `SteamAppIdsRoundTripAndRejectNonDigits`. `"1091500"` round-trips; null clears it; `"12a"`, `""` and 13 digits throw `ArgumentException`.
  - **Review Focus 3:** `AVersionEightBackupRestores` in `LibraryRestorerSwapTests`. Use `RewriteDatabase(RestoreFixture.SourcesBeforeVersionNine() + " PRAGMA user_version = 8;", m => m with { SchemaVersion = 8 })`. After the restore and a restart, the library opens at version 9 with its games and guides, and with no sources.
  - Prepend `SourcesBeforeVersionNine()` to each existing older-version restore test's SQL chain, so they still build their old versions.
- [ ] **Step 2: Run them.** `t DesktopGuides.Infrastructure.Tests`.
  Expected: build errors, then FAIL on the new tests.
- [ ] **Step 3: Implement.**
  - **The read side:** `GuideSources` is read with its own column list and a `ReadGuideSource` helper. Nothing joins it into the existing guide queries.
  - **On import:** `Publish` validates and inserts the source row inside its existing transaction, so a failed import leaves no row.
  - **Writing Steam IDs:** `SetSteamAppIdAsync` goes through `WriteAsync`.
- [ ] **Step 4: Run them.** `t DesktopGuides.Infrastructure.Tests`, then `t DesktopGuides.Core.Tests`.
  Expected: PASS, both suites.
- [ ] **Step 5: Commit:** "feat(p3): schema 9: guide sources and Steam app IDs".

### Task 3: Saving again: `ReplaceAsync`

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs` (`ReplaceAsync`, `ReplaceCheckpoint`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/ImportJournal.cs` (`IImportJournal` gains the replacement members)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (the journal implementation)
- Modify: `src/DesktopGuides.Infrastructure/Storage/FileOperationManifest.cs` (a `ReplaceGuide` manifest owns three paths)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideReplaceTests.cs` (new)

**Interfaces:**
- Consumes: Task 1's types; Task 2's `FindSourcedGuideAsync`, the source row and `FileOperationKind.ReplaceGuide`.
- Produces:
  - `internal enum ReplaceCheckpoint { Prepared, Copied, Verified, OldMovedAside, NewMovedIn, InCommit, Committed, Finished }`;
  - `public Task<GuideReplaceOutcome> ReplaceAsync(ImportManifest manifest, Guid guideId, GuideSource source, bool allowFormatChange, IProgress<ImportProgress>? progress, CancellationToken token)` on `GuideImportPublisher`. The internal constructor gains `Action<ReplaceCheckpoint> replaceCheckpoint` beside the existing checkpoint;
  - `IImportJournal` gains:
    - `Guide? GetGuide(Guid guideId)`;
    - `void PrepareReplace(Guid operationId, Guid guideId)`;
    - `void PublishReplacement(ReplacedGuide replacement, Action beforeCommit)`;
    - `void FinishReplace(Guid operationId)`;
  - `public sealed record ReplacedGuide(Guid OperationId, Guid Id, GuideFormat Format, string PrimaryRelativePath, string ContentSha256, long ContentBytes, string? SourceLabel, int? TextCodePage, GuideSource Source, bool ClearPlace, IReadOnlyList<GuideAsset>? Assets = null, IReadOnlyList<GuidePage>? Pages = null, int? PixelWidth = null, int? PixelHeight = null)` in `ImportJournal.cs`;
  - the `ReplaceGuide` manifest owns `.staging/<op>/<guide>`, `content/<guide>` and `.trash/<op>/<guide>`.

**The order of `ReplaceAsync`:**
1. Read the guide; a missing one throws `GuideImportException(Missing)`.
2. Check the format:
   - unchanged: carry on;
   - Txt↔Html: needs `allowFormatChange`, else throw `FormatChanged` with the Global Constraints message;
   - anything involving Image or Pdf: always throw `FormatChanged`.
3. Compare fingerprints. If `manifest.Fingerprint` equals `ContentSha256`, return `UpToDate`.
4. Prepare the operation, which records the `ReplaceGuide` / `Prepared` row (`Prepared`).
5. Copy into `GetStagedGuideRoot(op, id)`, with progress and cancellation as `PublishAsync` does, and check the fingerprint after the copy (`Copied`).
6. Verify the staged copy with the validator `PublishAsync` uses (`Verified`).
7. After this point cancellation no longer applies; report `Publishing`.
8. Move `content/<id>` to `GetTrashedGuideRoot(op, id)` (`OldMovedAside`).
9. Move the staged root to `content/<id>` (`NewMovedIn`).
10. `PublishReplacement`, in one transaction (`InCommit` from its `beforeCommit`):
    - update the `Guides` columns in the Global Constraints list;
    - delete and rewrite `GuidePages`, then `GuideAssets`;
    - upsert `GuideSources`;
    - when `ClearPlace`, apply the format-change clearing;
    - set the operation to `Committed`.

    It ends at `Committed`.
11. `FinishReplace`: delete the trash and the empty operation folder, then the operation row (`Finished`).

Any exception before step 10's commit goes through Task 4's rollback. An
exception after the commit leaves the `Committed` row for startup to
finish. `ReplaceAsync` still returns `Replaced` then, because the
replacement happened.

- [ ] **Step 1: Write the failing tests** in `GuideReplaceTests`, on a
  `RemovalLibrary` with real files:
  - `SavingTheSameCopyIsUpToDateAndChangesNothing`. Import a TXT guide with a source, then replace it with the same bytes. The outcome is `UpToDate`, and `LibrarySnapshot.Capture` before and after is equal.
  - `ANewCopyReplacesTheContentAndKeepsTheReadersState`. Seed a guide with:
    - a locator and an estimate;
    - `LastOpenedUtcMs`, and completion;
    - two bookmarks;
    - a `TextScale`;
    - the title "My walkthrough", which differs from the source's.

    Replace it with changed text and a new `GuideSource` (`SourceTitle` "Walkthrough v2", `CapturedPages` 3 of 5). Assert:
    - the outcome is `Replaced`, with the same `Id`, and the title still "My walkthrough";
    - the reading state, bookmarks and preferences are unchanged;
    - `ContentSha256` is the new file's, and `UpdatedUtcMs` has changed;
    - the source row is the new one;
    - `content/<id>/guide.txt` holds the new bytes;
    - no `.staging` or `.trash` entries and no `FileOperations` rows remain.
  - `AnHtmlGuideReplacesItsPagesAndAssets`. A three-page HTML guide is replaced by a two-page one. `GetGuidePagesAsync` and the asset rows match the new copy, and `LibraryArchivePlan`'s HTML check still passes (export succeeds).
  - **Review Focus 2:** `APlaceOnARemovedPageDoesNotFailTheOpen`, after that replacement:
    - A saved place on page 1, which still exists, decodes as `ContentChanged`, and `RestoreCandidates` puts Context first.
    - A place on page 3, which was removed, decodes as `Invalid`.
  - `AFormatChangeIsRefusedUnlessAllowed`:
    - Txt→Html without `allowFormatChange` throws `FormatChanged`, and nothing changes.
    - With it, the format is Html, the locator and estimate are null, there are no bookmarks, and preferences are null. Completion and opened time stay.
    - Txt→Pdf and Image→Txt throw `FormatChanged` even when allowed.
  - `AMapIsReplacedWithItsNewSize`. A PNG map with a VgMaps source is replaced by a larger PNG: `PixelWidth` and `PixelHeight` are the new ones.
  - **Review Focus 5:** `TheSameItemInAnotherGameIsNotReplaced`. The same `(Site, ItemId)` is saved in two games. Replacing the first game's guide leaves the second game's guide byte-identical.
  - `CancellingBeforePublishingChangesNothing`. Cancel at `Copied`: an `OperationCanceledException`, and the snapshot is equal.
- [ ] **Step 2: Run them.** `t DesktopGuides.Infrastructure.Tests GuideReplace`.
  Expected: build errors, then FAIL.
- [ ] **Step 3: Implement** the Interfaces block and the order above.
  Reuse `PublishAsync`'s copy and verify helpers rather than duplicating
  them.
- [ ] **Step 4: Run them.** `t DesktopGuides.Infrastructure.Tests`.
  Expected: PASS, the whole suite.
- [ ] **Step 5: Commit:** "feat(p3): save a guide again in place".

### Task 4: A failed or interrupted replacement leaves the library as it was

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/FileOperationReconciler.cs` (`Preflight` and `Resolve` for `ReplaceGuide`; `RollBackReplace`)
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs` (the rollback call on failure)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/GuideReplaceTests.cs`, `FileOperationReconciliationTests.cs`

**Interfaces:**
- Consumes: Task 3's `ReplaceCheckpoint`, the manifest's three owned paths, and `PrepareReplace` / `PublishReplacement`.
- Produces:
  - `FileOperationReconciler.RollBackReplace(SqliteConnection connection, Guid operationId)`;
  - startup handling of `ReplaceGuide` rows, by phase:
    - **Prepared:** the guide row must exist, or the operation is refused as for other kinds. If the trash holds the old copy, delete any `content/<id>` and move the trash back to `content/<id>`. If there's no trash, `content/<id>` is still the old copy, so leave it. Then delete the staging, the operation folder and the row.
    - **Committed:** delete the trash, the staging, the operation folder and the row. The new copy stays.
  - the publisher calls `RollBackReplace` for any exception before the commit, as `PublishAsync` does for imports. If that rollback itself fails, the `Prepared` row stays for startup. The error becomes `GuideImportException(SaveFailed)`; cancellation and import errors are rethrown as they are.

- [ ] **Step 1: Write the failing tests.**
  - **Review Focus 1:** `AnInterruptedReplacementRollsBackOnTheNextStart`, a theory over `Prepared`, `Copied`, `Verified`, `OldMovedAside`, `NewMovedIn` and `InCommit`.
    - Throw `InjectedFault` from that checkpoint, with the publisher's in-process rollback replaced by a no-op, so that it simulates a crash.
    - Then `RestartAsync`.
    - Assert the snapshot equals the one before the replacement, with no `FileOperations` rows and no stage or trash.
  - `ACrashAfterTheCommitKeepsTheNewCopy`, a theory over `Committed` and `Finished`. Same set-up. After the restart:
    - the guide has the new `ContentSha256` and new bytes;
    - the reading state is unchanged;
    - nothing is left in stage or trash, and no operation rows remain.
  - `AFailedReplacementRollsBackAtOnce`. Throw at `NewMovedIn` with the real rollback. `ReplaceAsync` throws `GuideImportException(SaveFailed)`, and the snapshot is equal before any restart.
  - `AFullDiskDuringTheCopyChangesNothing`. The `createStagedFile` hook throws `IOException` with `HResult` `0x80070070` (disk full) on the second write. The existing mapping for imports applies (`NotEnoughSpace`), and the snapshot is equal.
  - In `FileOperationReconciliationTests`, `APreparedReplacementWithoutItsGuideIsRefused`: a `ReplaceGuide` row whose guide row is gone fails `Preflight` the way an orphaned delete does today.
- [ ] **Step 2: Run them.** `t DesktopGuides.Infrastructure.Tests "GuideReplace|FileOperationReconciliation"`.
  Expected: FAIL.
- [ ] **Step 3: Implement** the Interfaces block.
- [ ] **Step 4: Run them.** `t DesktopGuides.Infrastructure.Tests`.
  Expected: PASS, the whole suite.
- [ ] **Step 5: Commit:** "feat(p3): a failed replacement leaves the library unchanged".

### Task 5: IGDB's Steam app ID, the build, and the records

**Files:**
- Modify: `src/DesktopGuides.Core/Providers/ProviderGameImporter.cs` (store `record.Hints.SteamAppId`)
- Modify: `docs/p3-technical-design.md` (a "PR 2 verification record" section, and the Status line)
- Modify: `docs/progress.md` (the P3 row)
- Test: `tests/DesktopGuides.Core.Tests/Providers/ProviderGameImporterTests.cs`, or the file that tests `ProviderGameImporter` today

**Interfaces:**
- Consumes: Task 2's `SetSteamAppIdAsync`.
- Produces: after `AddAsync` and after `RefreshAsync`, when `record.Hints.SteamAppId` isn't null, the importer calls `SetSteamAppIdAsync(gameId, id)`. A null hint leaves any stored ID alone, so a game's ID isn't lost because IGDB no longer lists it.

- [ ] **Step 1: Write the failing tests:**
  - `AddingAGameStoresItsSteamAppId`: hints carrying `"1091500"` mean the game reads it back.
  - `ARefreshWithoutASteamIdKeepsTheStoredOne`: the stored ID stays after a refresh whose hints have none.
  - `ARefreshReplacesAChangedSteamId`.

  Use the test file's existing fake repository, extending it with the two
  Steam methods if it implements `ILibraryRepository`.
- [ ] **Step 2: Run them.** `t DesktopGuides.Core.Tests ProviderGameImporter`.
  Expected: FAIL.
- [ ] **Step 3: Implement.** Writing the Steam ID happens after the
  existing add or update. A failure to store it is logged without the ID,
  and doesn't fail the add or refresh.
- [ ] **Step 4: Run every suite and the build.** Run `t DesktopGuides.Core.Tests`, then `t DesktopGuides.Infrastructure.Tests`, then `build`.
  Expected: PASS, and the build has no new warnings; the 3 existing `GuidePageCounterTests` warnings stay.
- [ ] **Step 5: Records.**
  - Add "PR 2 verification record" to the design, listing:
    - the commits;
    - the suite counts;
    - each Review Focus test, by name;
    - the PR's CI run.
  - Update the Status line and the progress row: PR 2 merged once it has; next, PR 3.
- [ ] **Step 6: Commit:** "feat(p3): keep IGDB's Steam app ID; PR 2 records".
