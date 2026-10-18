# T21.2 image maps PR a (import, storage, the Maps section and backup) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A reader imports a game's maps, PNG or JPEG files, several at
once. Each becomes an `Image` guide with no progress or completion. Maps
are listed in their own section on the game page, open fitted to the
reader, and survive backup and restore. Zoom (PR b) and the Maps pop-up
(PR c) come later.

**Architecture:**
- **Core** gets `GuideFormat.Image`, the pixel size on `Guide`,
  `MapImageHeader` (PNG and JPEG sizes read from the header, before any
  decode), `MapImageLimits`, `MapImportManifest`, `IMapImageDecoder`, the
  map title rule, and the row and library facts.
- **Storage** moves to schema 8: `Guides` is rebuilt, without cascading,
  to allow `Image` and add `PixelWidth` and `PixelHeight`.
- **Import:** `GuideImportValidator` reads the header, applies the
  limits, and asks an injected `IMapImageDecoder` to prove the image
  decodes. The publisher stores it as `map.png` or `map.jpg`.
- **Production:** WIC implements the decoder. The game page gains the
  Maps section and the Import maps dialog, Ctrl+O accepts PNG and JPEG,
  and a minimal `ImageReaderSession` shows a map fitted.

**Tech Stack:** .NET 10, Microsoft.Data.Sqlite, xUnit, WinUI 3,
`Windows.Graphics.Imaging` (WIC), PowerShell UI Automation.

**Spec:** [t21-2-image-maps-design.md](t21-2-image-maps-design.md)
(approved 19 October 2026).

## Global Constraints

- **Accepted files:** extensions `.png`, `.jpg` and `.jpeg`, in any case.
  The header decides the type: a JPEG named `.png` is a JPEG and is
  stored as `map.jpg`.
- **Limits** (`MapImageLimits`):
  - `MaxBytes = 256 MiB` (268,435,456 bytes);
  - `MaxSide = 32768` pixels on either side;
  - `MaxPixels = 268_435_456` (16384²).

  They are checked from the header before anything is decoded.
- **Messages:**
  - not a PNG or JPEG: "{file} isn't a PNG or JPEG image."
  - bytes: "{file} is larger than the {FormatSize(MaxBytes)} limit for maps."
  - a side: "{file} is {w:N0} × {h:N0} pixels. A map can be at most 32,768 pixels on a side."
  - pixels: "{file} is {w:N0} × {h:N0} pixels. A map can have at most 268 million pixels."
  - doesn't decode: "{file} can't be read as an image. It may be damaged or incomplete."
  - the unknown-extension message becomes "{file} isn't a TXT, HTML, PDF, PNG or JPEG file."
- **The decode check** runs off the UI thread. It decodes to at most 256
  pixels on the longer side and compares the decoder's size with the
  header's.
- **Titles** (`GuideTitle.SuggestMap`): the file name without its
  extension, with `-` and `_` read as spaces, runs of spaces collapsed,
  trimmed, and cut at 200 characters. An empty result is "Untitled map".
  `SourceLabel` keeps the file name.
- **Labels:**
  - `FormatLabel(Image)` is "Map image".
  - The type is "PNG" or "JPEG".
  - The size is "{w} × {h}", read as "{w} by {h} pixels".
- **The library row:** "No guides", "1 guide" or "N guides", then
  ", 1 map" or ", M maps" when the game has maps, as one fact. For
  example "3 guides, 2 maps", "No guides, 1 map".
- **The game page:**
  - Maps are ordered by title (current culture, ignoring case), then by
    ID.
  - The Maps section always shows: "No maps yet." when the game has
    none.
  - Guides list only non-map guides.
- **A map isn't reading activity:**
  - no `ReadingStates` row is written;
  - Resume (`LastActiveGuideId`) isn't set;
  - no completion control, no progress tracking.
- **Schema 8:**

```sql
CREATE TABLE Guides8 (
    Id TEXT PRIMARY KEY CHECK (length(Id) = 32),
    GameId TEXT NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
    Title TEXT NOT NULL CHECK (length(Title) BETWEEN 1 AND 200),
    Format TEXT NOT NULL CHECK (Format IN ('Txt', 'Html', 'Pdf', 'Image')),
    ManagedRelativeRoot TEXT NOT NULL UNIQUE
        CHECK (ManagedRelativeRoot = 'content/' || Id),
    PrimaryRelativePath TEXT NOT NULL CHECK (length(PrimaryRelativePath) > 0),
    ContentSha256 TEXT NOT NULL CHECK (length(ContentSha256) = 64),
    ContentBytes INTEGER NOT NULL CHECK (ContentBytes >= 0),
    SourceLabel TEXT CHECK (SourceLabel IS NULL OR length(SourceLabel) <= 255),
    TextCodePage INTEGER CHECK (
        TextCodePage IS NULL OR (Format = 'Txt' AND TextCodePage IN (437, 1252))
    ),
    ImportedUtcMs INTEGER NOT NULL CHECK (ImportedUtcMs >= 0),
    UpdatedUtcMs INTEGER NOT NULL CHECK (UpdatedUtcMs >= 0),
    PixelWidth INTEGER CHECK (PixelWidth IS NULL OR PixelWidth BETWEEN 1 AND 32768),
    PixelHeight INTEGER CHECK (PixelHeight IS NULL OR PixelHeight BETWEEN 1 AND 32768),
    CHECK ((Format = 'Image') = (PixelWidth IS NOT NULL AND PixelHeight IS NOT NULL))
);
INSERT INTO Guides8 (Id, GameId, Title, Format, ManagedRelativeRoot, PrimaryRelativePath,
    ContentSha256, ContentBytes, SourceLabel, TextCodePage, ImportedUtcMs, UpdatedUtcMs)
    SELECT Id, GameId, Title, Format, ManagedRelativeRoot, PrimaryRelativePath,
    ContentSha256, ContentBytes, SourceLabel, TextCodePage, ImportedUtcMs, UpdatedUtcMs FROM Guides;
DROP TABLE Guides;
ALTER TABLE Guides8 RENAME TO Guides;
CREATE INDEX IX_Guides_GameId ON Guides(GameId);
PRAGMA user_version = 8;
```

  If the `Version1` text of the `Guides` columns differs from the above in
  any way other than the `Format` CHECK, copy `Version1`'s text and add
  only the three new lines.
- **Migrations run with foreign keys off.** `MigrateOrValidate` sets
  `PRAGMA foreign_keys=OFF` before its transaction and back `ON` after it
  ends, whether it commits or fails. `ValidateDatabase`'s
  `foreign_key_check` inside the transaction is the integrity gate.
- **Shell rules:** no `cd` in local shell commands. PowerShell stays
  ASCII-only and runs under 5.1 and 7.
- **Visual direction:** the app's existing Fluent styles and the Guides
  section's own layout. No new styles.

## Review Focus

1. **A JPEG with a large EXIF segment** (a camera or phone screenshot has
   64 KiB of APP1 before its frame header) still reports its size. Pinned
   by Task 1's `AJpegWithALargeExifSegmentReadsItsSize`.
2. **A progressive JPEG** (SOF2) reports its size. Pinned by Task 1's
   `AProgressiveJpegReadsItsSize`.
3. **Upper-case extensions** (`MAP.PNG`, `Chart.JPG`, common from older
   tools) import. Pinned by Task 3's `UpperCaseExtensionsImportAsMaps`.
4. **Two maps with one title** both show, in a stable order, and aren't
   duplicates unless their bytes match. Pinned by Task 1's
   `MapsSortByTitleThenId` and Task 3's `TheSameTitleIsNotADuplicate`.
5. **A library with bookmarks, reading states and HTML pages** keeps
   every row through the schema 8 rebuild. Pinned by Task 2's
   `UpgradesPopulatedVersionSevenKeepingEveryRow`.

## Host commands

```bash
s(){ ssh -o BatchMode=yes -o ConnectTimeout=15 -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t21-2m) { Remove-Item -Recurse -Force E:\work\desktop-guides\t21-2m }; New-Item -ItemType Directory E:\work\desktop-guides\t21-2m | Out-Null"'
  git -C /Users/ilya.lissoboi/work/desktop-guides ls-files -z -co --exclude-standard | grep -z -v -E '^(\.claude/|az2aws)' | COPYFILE_DISABLE=1 tar -C /Users/ilya.lissoboi/work/desktop-guides --null -cf - -T - | s 'tar -xf - -C E:\work\desktop-guides\t21-2m'
  s 'cd /d E:\work\desktop-guides\t21-2m && python tools\p0\make_fixtures.py > E:\work\t21-2m-fixtures.log 2>&1'
}
t(){ f=""; [ -n "$2" ] && f="--filter \"$2\""; s "cd /d E:\\work\\desktop-guides\\t21-2m && dotnet test tests\\$1 $f -nologo > E:\\work\\t21-2m-test.log 2>&1 & findstr /c:\"Passed!\" /c:\"Failed!\" /c:\"error CS\" /c:\"[FAIL]\" E:\\work\\t21-2m-test.log"; }
build(){ s 'cd /d E:\work\desktop-guides\t21-2m && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64 -nologo -warnaserror > E:\work\t21-2m-build.log 2>&1 & findstr /c:"Warning(s)" /c:"Error(s)" /c:"error " E:\work\t21-2m-build.log'; }
```

Installed runs: `gh workflow run windows-ci.yml --ref feat/p2-t21-2-image-maps-import -f shell-scope=import -f shell-lane=msix` (and `portable`).

---

### Task 1: Core: the format, the header, the limits, titles and facts

**Files:**
- Modify: `src/DesktopGuides.Core/Library/LibraryModels.cs` (`GuideFormat.Image`; `Guide` gains `int? PixelWidth = null, int? PixelHeight = null` as its last parameters)
- Modify: `src/DesktopGuides.Core/Library/CatalogSummaries.cs` (`LibraryGameSummary` gains `int MapCount = 0` as its last parameter; `GuideCount` now counts non-map guides)
- Create: `src/DesktopGuides.Core/Maps/MapImageHeader.cs`, `src/DesktopGuides.Core/Maps/MapImageLimits.cs`, `src/DesktopGuides.Core/Maps/MapPresentation.cs`
- Modify: `src/DesktopGuides.Core/Import/ImportContracts.cs` (`MapImportManifest`, `IMapImageDecoder`)
- Modify: `src/DesktopGuides.Core/Library/GuideTitle.cs` (`SuggestMap`)
- Modify: `src/DesktopGuides.Core/Import/ImportPresentation.cs` (`FormatLabel(Image)`)
- Modify: `src/DesktopGuides.Core/Library/CatalogPresentation.cs` (`GameFacts`, `GuideFacts`)
- Modify: `src/DesktopGuides.Core/Library/AppIconCatalog.cs` (`GuideTable[Image] = ("\uE8B9", "ic_fluent_image_24_color.svg")`, the glyph and asset the Material card already uses)
- Test: `tests/DesktopGuides.Core.Tests/MapImageHeaderTests.cs` (new), `MapPresentationTests.cs` (new), `GuideTitleTests.cs`, `CatalogPresentationTests.cs`, `AppIconCatalogTests.cs`

**Interfaces:**
- Produces:
  - `public enum MapImageKind { Png, Jpeg }`;
  - `public sealed record MapImageSize(MapImageKind Kind, int Width, int Height)`;
  - `MapImageHeader.Read(Stream stream) -> MapImageSize?`. It reads from the stream's current position, doesn't need the whole file, and returns null for anything that isn't a PNG or JPEG with a readable size. For JPEG it walks segments by length, skipping `0xFF` fill bytes, until a SOF marker (`0xC0`–`0xCF` except `0xC4`, `0xC8`, `0xCC`). It stops after 1 MiB of segments.
  - `MapImageLimits.MaxBytes`, `MaxSide`, `MaxPixels` (Global Constraints), and `MapImageLimits.Check(MapImageSize size) -> MapLimit?` with `public enum MapLimit { Side, Pixels }`;
  - `MapImageKind.StoredName()` (`"map.png"` or `"map.jpg"`) and `Label()` (`"PNG"` or `"JPEG"`), as extension methods in `MapPresentation`;
  - `MapPresentation.KindOf(Guide guide) -> MapImageKind`, from `PrimaryRelativePath`'s extension;
  - `MapPresentation.SizeFact(int width, int height) -> CatalogFact`;
  - `MapPresentation.Sort(IEnumerable<GuideSummary> maps) -> IReadOnlyList<GuideSummary>`;
  - `public sealed record MapImportManifest(ImportSource Source, string SuggestedTitle, MapImageKind Kind, int PixelWidth, int PixelHeight, string Fingerprint) : ImportManifest(Source, GuideFormat.Image, SuggestedTitle, Fingerprint)`;
  - `public interface IMapImageDecoder { Task<bool> DecodesAsync(string fullPath, MapImageSize expected, CancellationToken token); }`;
  - `GuideTitle.SuggestMap(string fileName) -> string`.

- [ ] **Step 1: Write the failing tests.** `MapImageHeaderTests` builds its
  bytes in the test. A helper `Png(int w, int h)` writes the signature and
  an IHDR chunk. `Jpeg(byte sof, int w, int h, int app1Bytes = 0)` writes
  SOI, an optional APP1 of that length, and a SOF segment.

```csharp
    [Fact]
    public void APngReadsItsSize() =>
        Assert.Equal(new MapImageSize(MapImageKind.Png, 15456, 10304), Read(Png(15456, 10304)));

    [Fact]
    public void ABaselineJpegReadsItsSize() =>
        Assert.Equal(new MapImageSize(MapImageKind.Jpeg, 640, 480), Read(Jpeg(0xC0, 640, 480)));

    // Review Focus 2.
    [Fact]
    public void AProgressiveJpegReadsItsSize() =>
        Assert.Equal(new MapImageSize(MapImageKind.Jpeg, 1200, 900), Read(Jpeg(0xC2, 1200, 900)));

    // Review Focus 1.
    [Fact]
    public void AJpegWithALargeExifSegmentReadsItsSize() =>
        Assert.Equal(new MapImageSize(MapImageKind.Jpeg, 2048, 1536), Read(Jpeg(0xC0, 2048, 1536, app1Bytes: 65533)));

    [Fact]
    public void AHuffmanTableIsNotAFrame() =>
        Assert.Equal(new MapImageSize(MapImageKind.Jpeg, 64, 48), Read(JpegWithDhtFirst(64, 48)));

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]   // GIF89a
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]                // a cut PNG signature
    public void AnythingElseHasNoSize(byte[] bytes) => Assert.Null(Read(bytes));

    [Fact]
    public void AZeroSizeIsNoSize() => Assert.Null(Read(Png(0, 10)));
```

  `Read(bytes)` is `MapImageHeader.Read(new MemoryStream(bytes))`.
  `JpegWithDhtFirst` puts a `0xFFC4` segment before the SOF.

  Also add `LimitsRefuseASideOrTooManyPixels`:

```csharp
    [Theory]
    [InlineData(32768, 1, null)]
    [InlineData(32769, 1, MapLimit.Side)]
    [InlineData(1, 40000, MapLimit.Side)]
    [InlineData(16384, 16384, null)]
    [InlineData(16385, 16384, MapLimit.Pixels)]
    [InlineData(20000, 20000, MapLimit.Pixels)]
    public void LimitsRefuseASideOrTooManyPixels(int w, int h, MapLimit? expected) =>
        Assert.Equal(expected, MapImageLimits.Check(new MapImageSize(MapImageKind.Png, w, h)));
```

  In `GuideTitleTests`:

```csharp
    [Theory]
    [InlineData("castle-floor-1.png", "castle floor 1")]
    [InlineData("Dragon_Quest_V__World.JPG", "Dragon Quest V World")]
    [InlineData("  -_- .png", "Untitled map")]
    public void MapTitlesReadDashesAndUnderscoresAsSpaces(string file, string title) =>
        Assert.Equal(title, GuideTitle.SuggestMap(file));
```

  In `CatalogPresentationTests`:

```csharp
    [Theory]
    [InlineData(3, 2, "3 guides, 2 maps")]
    [InlineData(0, 1, "No guides, 1 map")]
    [InlineData(1, 0, "1 guide")]
    public void AGameRowCountsGuidesAndMaps(int guides, int maps, string expected) =>
        Assert.Contains(expected, CatalogPresentation.GameFacts(Summary(guides, maps)).Select(fact => fact.Label));

    [Fact]
    public void AMapRowShowsItsTypeAndSizeOnly()
    {
        GuideSummary map = new(MapGuide("map.png", 15456, 10304),
            new ReadingState(Guid.Empty, null, null, DateTimeOffset.UnixEpoch, null));
        IReadOnlyList<CatalogFact> facts = CatalogPresentation.GuideFacts(map, Clock, Culture);
        Assert.Equal(["PNG", "15456 × 10304"], facts.Select(fact => fact.Label));
        Assert.Equal("PNG, 15456 by 10304 pixels", CatalogPresentation.AccessibleText(facts));
    }
```

  A map row never shows a reading state or "Opened", even if a state row
  exists. `Summary` and `MapGuide` are local helpers. If the file already
  has an equivalent of `Clock` and `Culture`, use it.

  In `MapPresentationTests`:

```csharp
    // Review Focus 4.
    [Fact]
    public void MapsSortByTitleThenId()
    {
        Guid a = Guid.Parse("00000000000000000000000000000001");
        Guid b = Guid.Parse("00000000000000000000000000000002");
        GuideSummary[] maps = [Map(b, "castle"), Map(a, "Castle"), Map(a, "Catacombs")];
        Assert.Equal([(a, "Castle"), (b, "castle"), (a, "Catacombs")],
            MapPresentation.Sort(maps).Select(m => (m.Guide.Id, m.Guide.Title)));
    }

    [Theory]
    [InlineData("map.png", MapImageKind.Png)]
    [InlineData("map.jpg", MapImageKind.Jpeg)]
    public void AMapsKindComesFromItsStoredName(string path, MapImageKind kind) =>
        Assert.Equal(kind, MapPresentation.KindOf(MapGuide(path, 10, 10)));
```

  The first two maps share a title ignoring case; ID breaks the tie. In
  `AppIconCatalogTests`, extend the per-format test so `Image` has an
  icon.

- [ ] **Step 2: Run them.** `t DesktopGuides.Core.Tests`.
  Expected: build errors for the missing types, then FAIL.
- [ ] **Step 3: Implement** the Interfaces block in the files listed.
  - `GuideFacts`: when `Format == Image`, give the file status first as
    today, then `MapPresentation.KindOf(guide).Label()` and `SizeFact`,
    and nothing else.
  - `GameFacts`: the guide fact becomes one label: the existing count
    text, then `", 1 map"` or `$", {n} maps"` when `MapCount > 0`.
- [ ] **Step 4: Run them.** `t DesktopGuides.Core.Tests`.
  Expected: PASS, the whole suite.
- [ ] **Step 5: Commit:** "feat(p2): maps as a guide format in Core".

### Task 2: Schema 8, the map rows, and backup and restore

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Storage/LibrarySchema.cs` (`CurrentVersion = 8`, `Version8`, `Migrations`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (`MigrateOrValidate` foreign keys; `ReadGuide`; the guide insert; `ListGameSummariesAsync` counts)
- Modify: `src/DesktopGuides.Infrastructure/Storage/ImportJournal.cs` (`NewImportedGuide` gains `int? PixelWidth = null, int? PixelHeight = null` as its last parameters)
- Test: `tests/DesktopGuides.Infrastructure.Tests/SqliteLibraryRepositoryTests.cs`, `ImportJournalTests.cs`, `Restore/RestoreFixture.cs`, `Restore/LibraryRestorerSwapTests.cs`, `LibraryExportRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1's `GuideFormat.Image`, `Guide.PixelWidth/PixelHeight`, `LibraryGameSummary.MapCount`.
- Produces:
  - schema 8, exactly the Global Constraints SQL;
  - `NewImportedGuide(..., int? PixelWidth = null, int? PixelHeight = null)`, written to the new columns;
  - `ListGameSummariesAsync`: `GuideCount` counts `Format <> 'Image'`, and `MapCount` counts `Format = 'Image'`. `GuideTitles` still holds every title, so search finds maps;
  - `RestoreFixture.GuidesBeforeVersionEight()`, SQL that turns a schema 8 database's `Guides` back into the schema 1–7 table.
  - Any other reader of `LibraryGameSummary.GuideCount` that means "everything removed with the game" (game removal's confirmation) uses `GuideCount + MapCount`.

- [ ] **Step 1: Write the failing tests.** In `SqliteLibraryRepositoryTests`,
  beside the version 7 tests, add `CreatePopulatedVersionSeven` =
  `CreatePopulatedVersionSix` plus `LibrarySchema.Version7`:

```csharp
    // Review Focus 5.
    [Fact]
    public async Task UpgradesPopulatedVersionSevenKeepingEveryRow()
    {
        using TestLibrary directory = new();
        Guid gameId = Guid.NewGuid();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionSeven(directory, gameId, guideId);
        string before = Census(directory.Paths.DatabasePath);

        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();

        Assert.Equal(8L, ReadUserVersion(directory.Paths.DatabasePath));
        Assert.Equal(before, Census(directory.Paths.DatabasePath));
        Assert.Equal("", ScalarText(directory.Paths.DatabasePath,
            "SELECT group_concat(IFNULL(PixelWidth, '') || IFNULL(PixelHeight, ''), '') FROM Guides"));
    }

    [Fact]
    public async Task FailedVersionEightMigrationStaysAtVersionSeven()
    {
        using TestLibrary directory = new();
        CreatePopulatedVersionSeven(directory, Guid.NewGuid(), Guid.NewGuid());
        string before = Census(directory.Paths.DatabasePath);

        await using (SqliteLibraryRepository failing = new(directory.Paths, null, version =>
        {
            if (version == 8) throw new IOException("Injected after Guides was rebuilt.");
        }))
        {
            LibraryOpenException error = await Assert.ThrowsAsync<LibraryOpenException>(() => failing.InitializeAsync());
            Assert.Equal(LibraryOpenIssue.MigrationFailed, error.Issue);
        }

        Assert.Equal(7L, ReadUserVersion(directory.Paths.DatabasePath));
        Assert.Equal(before, Census(directory.Paths.DatabasePath));
    }

    [Fact]
    public async Task AMapNeedsItsPixelSizeAndOnlyAMapHasOne()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Maps", null, null);

        InsertGuide(directory.Paths.DatabasePath, Guid.NewGuid(), game.Id, format: "Image", pixels: (640, 480));
        Assert.Throws<SqliteException>(() =>
            InsertGuide(directory.Paths.DatabasePath, Guid.NewGuid(), game.Id, format: "Image", pixels: null));
        Assert.Throws<SqliteException>(() =>
            InsertGuide(directory.Paths.DatabasePath, Guid.NewGuid(), game.Id, format: "Txt", pixels: (640, 480)));
        Assert.Throws<SqliteException>(() =>
            InsertGuide(directory.Paths.DatabasePath, Guid.NewGuid(), game.Id, format: "Image", pixels: (32769, 1)));
    }

    [Fact]
    public async Task RemovingAGuideStillCascadesAfterTheUpgrade()
    {
        using TestLibrary directory = new();
        Guid guideId = Guid.NewGuid();
        CreatePopulatedVersionSeven(directory, Guid.NewGuid(), guideId);
        await using (SqliteLibraryRepository repository = new(directory.Paths))
        {
            await repository.InitializeAsync();
        }

        using SqliteConnection connection = OpenWithForeignKeys(directory.Paths.DatabasePath);
        using SqliteCommand remove = connection.CreateCommand();
        remove.CommandText = $"DELETE FROM Guides WHERE Id = '{guideId:N}'";
        remove.ExecuteNonQuery();
        Assert.Equal("0", ScalarText(directory.Paths.DatabasePath,
            $"SELECT COUNT(*) FROM ReadingStates WHERE GuideId = '{guideId:N}'"));
    }

    [Fact]
    public async Task AGameCountsItsGuidesAndMapsApart()
    {
        using TestLibrary directory = new();
        await using SqliteLibraryRepository repository = new(directory.Paths);
        await repository.InitializeAsync();
        Game game = await repository.AddGameAsync("Maps", null, null);
        InsertGuide(directory.Paths.DatabasePath, Guid.NewGuid(), game.Id, title: "Walkthrough");
        InsertGuide(directory.Paths.DatabasePath, Guid.NewGuid(), game.Id, title: "castle", format: "Image", pixels: (640, 480));
        InsertGuide(directory.Paths.DatabasePath, Guid.NewGuid(), game.Id, title: "catacombs", format: "Image", pixels: (64, 48));

        LibraryGameSummary summary = Assert.Single(await repository.ListGameSummariesAsync());
        Assert.Equal((1, 2), (summary.GuideCount, summary.MapCount));
        Assert.Equal(["castle", "catacombs", "Walkthrough"], summary.GuideTitles);
    }
```

  - `Census` returns one string of every table's row count, plus a hash of
    each table's rows in primary-key order.
  - `CreatePopulatedVersionSeven` must leave at least one row in
    `ReadingStates`, `ReaderPreferences`, `Bookmarks`, `GuideAssets` and
    `GuidePages`. Add the rows if the version 6 helper doesn't.
  - `InsertGuide` gains optional `title`, `format` and `pixels`
    parameters. `GuideTitles` keeps its existing order (title, then ID);
    match the assertion to it if it compares by culture.

  In `ImportJournalTests`, `AMapKeepsItsPixelSize` publishes a
  `NewImportedGuide(..., GuideFormat.Image, "map.png", ..., PixelWidth:
  640, PixelHeight: 480)` and reads back `640|480`.

  In `RestoreFixture`, add `GuidesBeforeVersionEight()`, modelled on
  `ReaderPreferencesBeforeVersionSix()`: with foreign keys off, rebuild
  `Guides` from `Version1`'s `CREATE TABLE Guides` text, copy the old
  columns, and recreate `IX_Guides_GameId`. Prepend it to every existing
  `RewriteDatabase` call that writes a `SchemaVersion` below 8. Find them
  with `grep -n "SchemaVersion = " tests/DesktopGuides.Infrastructure.Tests -r`.
  In `LibraryRestorerSwapTests`:

```csharp
    // A backup from before schema 8 restores, and migration keeps its guides.
    [Fact]
    public async Task AVersionSevenBackupRestores()
    {
        string backup = fixture.RewriteDatabase(
            RestoreFixture.GuidesBeforeVersionEight() + " PRAGMA user_version = 7;",
            manifest => manifest with { SchemaVersion = 7 });
        LibraryRestoreStage stage = await fixture.StageAsync(backup);

        await fixture.Restorer().ReplaceAsync(stage, CancellationToken.None);
        await fixture.Target.RestartAsync();

        Assert.Equal(LibrarySchema.CurrentVersion.ToString(CultureInfo.InvariantCulture), fixture.Target.Scalar("PRAGMA user_version"));
        Assert.Equal("1", fixture.Target.Scalar(
            $"SELECT COUNT(*) FROM Guides WHERE Id = '{fixture.Source.HtmlGuide:N}'"));
    }
```

  In `LibraryExportRepositoryTests` (or the restore fixture's source
  library, whichever builds the exported guides), add a map guide with a
  real `map.png` file. Then add `AMapIsBackedUpAndRestored`: its file
  bytes, `Format = 'Image'` and `640|480` come back after restore.

- [ ] **Step 2: Run them.** `t DesktopGuides.Infrastructure.Tests`.
  Expected: FAIL, the new tests only. (The older-version restore tests
  pass at schema 7 and keep passing with the new prefix.)
- [ ] **Step 3: Implement.**
  - `Version8` and `CurrentVersion = 8`.
  - In `MigrateOrValidate`, set foreign keys off before `BeginTransaction`
    and back on in a `finally`.
  - Read and write the two columns. Write the new `ListGameSummariesAsync`
    counts.
- [ ] **Step 4: Run them.** `t DesktopGuides.Infrastructure.Tests`.
  Expected: PASS, the whole suite.
- [ ] **Step 5: Commit:** "feat(p2): schema 8, maps in the library and its backups".

### Task 3: Inspecting and publishing a map

**Files:**
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs` (`FormatOf`, `CheckSize`, `InspectAsync`, a new `InspectMapAsync`; constructor gains `IMapImageDecoder? maps = null`)
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportLimits.cs` (`long MaxMapBytes = MapImageLimits.MaxBytes`)
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportPublisher.cs` (`PlanAsync`, `VerifyAsync`, the pixel size in `NewImportedGuide`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/MapImportTests.cs` (new)

**Interfaces:**
- Consumes:
  - Task 1's `MapImageHeader`, `MapImageLimits`, `MapImportManifest`, `IMapImageDecoder`, `GuideTitle.SuggestMap`, `StoredName()`;
  - Task 2's `NewImportedGuide(..., PixelWidth, PixelHeight)`.
- Produces:
  - `new GuideImportValidator(GuideImportLimits? limits = null, IMapImageDecoder? maps = null)`. A map inspected with no decoder throws `InvalidOperationException("Map import needs an image decoder.")`. That's a wiring fault, not a user error.
  - `InspectAsync` on a `.png`, `.jpg` or `.jpeg` returns `ImportReady(MapImportManifest)`, or throws `GuideImportException` with the Global Constraints messages:
    - `Unsupported` for a header that isn't PNG or JPEG;
    - `TooLarge` for bytes, a side or pixels;
    - `Unreadable` when the decoder says no;
    - `Empty` as today.
  - The fingerprint is `GuideFingerprint.OfBytes` over the file's bytes, read as a stream, not loaded whole. Use the existing streaming hash helper if there is one; otherwise hash with `IncrementalHash`.
  - `PublishAsync(MapImportManifest, ...)` stores the file as `kind.StoredName()` and writes `PixelWidth` and `PixelHeight`. `VerifyAsync` re-reads the staged file's header and treats any change of kind or size as `Changed`.

- [ ] **Step 1: Write the failing tests.** `MapImportTests` writes its files
  to a temporary folder. It reuses Task 1's header builders (copy them
  into a shared test helper `MapBytes` in this project) plus padding
  bytes. A `FakeDecoder(bool decodes)` records its calls.

```csharp
    [Fact]
    public async Task APngIsAMap()
    {
        string path = Write("castle-floor-1.png", MapBytes.Png(640, 480, padding: 100));
        MapImportManifest map = Assert.IsType<MapImportManifest>(
            Assert.IsType<ImportReady>(await Validator().InspectAsync(path, default)).Manifest);
        Assert.Equal(("castle floor 1", MapImageKind.Png, 640, 480), (map.SuggestedTitle, map.Kind, map.PixelWidth, map.PixelHeight));
        Assert.Equal(GuideFingerprint.OfBytes(File.ReadAllBytes(path)), map.Fingerprint);
        Assert.Equal(new MapImageSize(MapImageKind.Png, 640, 480), decoder.Calls.Single());
    }

    [Fact]
    public async Task AJpegNamedPngIsAJpeg()
    {
        string path = Write("chart.png", MapBytes.Jpeg(0xC0, 320, 200));
        Assert.Equal(MapImageKind.Jpeg, ((MapImportManifest)((ImportReady)await Validator().InspectAsync(path, default)).Manifest).Kind);
    }

    // Review Focus 3.
    [Theory]
    [InlineData("MAP.PNG")]
    [InlineData("Chart.JPG")]
    [InlineData("chart.jpeg")]
    public async Task UpperCaseExtensionsImportAsMaps(string name) =>
        Assert.IsType<MapImportManifest>(((ImportReady)await Validator().InspectAsync(
            Write(name, name.EndsWith("PNG") ? MapBytes.Png(8, 8) : MapBytes.Jpeg(0xC0, 8, 8)), default)).Manifest);

    [Theory]
    [InlineData(40000, 40000, "huge.png is 40,000 × 40,000 pixels. A map can be at most 32,768 pixels on a side.")]
    [InlineData(20000, 20000, "huge.png is 20,000 × 20,000 pixels. A map can have at most 268 million pixels.")]
    public async Task ADecompressionBombIsRefusedFromItsHeader(int w, int h, string message)
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => Validator().InspectAsync(Write("huge.png", MapBytes.Png(w, h)), default));
        Assert.Equal((ImportIssue.TooLarge, message), (error.Issue, error.Message));
        Assert.Empty(decoder.Calls);
    }

    [Fact]
    public async Task AFileOverTheByteLimitIsRefused()
    {
        GuideImportValidator small = new(new GuideImportLimits(MaxMapBytes: 64), decoder);
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => small.InspectAsync(Write("big.png", MapBytes.Png(8, 8, padding: 100)), default));
        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.StartsWith("big.png is larger than the ", error.Message);
        Assert.EndsWith(" limit for maps.", error.Message);
    }

    [Fact]
    public async Task AnImageThatDoesntDecodeIsRefused()
    {
        decoder = new FakeDecoder(decodes: false);
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => Validator().InspectAsync(Write("broken.png", MapBytes.Png(64, 64)), default));
        Assert.Equal((ImportIssue.Unreadable, "broken.png can't be read as an image. It may be damaged or incomplete."),
            (error.Issue, error.Message));
    }

    [Theory]
    [InlineData("chart.png", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0 }, "chart.png isn't a PNG or JPEG image.")]
    [InlineData("chart.gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0 }, "chart.gif isn't a TXT, HTML, PDF, PNG or JPEG file.")]
    public async Task OtherImagesAreUnsupported(string name, byte[] bytes, string message)
    {
        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => Validator().InspectAsync(Write(name, bytes), default));
        Assert.Equal((ImportIssue.Unsupported, message), (error.Issue, error.Message));
    }

    [Fact]
    public async Task AMapPublishesAsItsOwnFile()
    {
        // Inspect castle-floor-1.png, publish it to a game, then assert:
        // the Guides row has Format 'Image', PixelWidth 640, PixelHeight 480,
        // PrimaryRelativePath 'map.png', SourceLabel 'castle-floor-1.png';
        // content/<id>/map.png has the source's bytes; no ReadingStates row.
    }

    // Review Focus 4.
    [Fact]
    public async Task TheSameTitleIsNotADuplicate()
    {
        // Two different PNGs both titled "castle" publish to one game without
        // ImportIssue.Duplicate; publishing the first file's bytes again
        // throws ImportIssue.Duplicate.
    }

    [Fact]
    public async Task AMapChangedAfterInspectionIsRefused()
    {
        // Inspect a PNG, overwrite it with a JPEG of the same length and write
        // time restored, then publish: ImportIssue.Changed, nothing published.
    }
```

  Write the last three in full, using the existing publisher tests'
  fixture (`GuideImportPublisherTests` or its helpers). Each comment
  names the arrangement and every assertion. A "Changed" refusal may
  come from the fingerprint check before `VerifyAsync`. Either is
  correct, and the test asserts only the issue.

  `Validator()` is `new GuideImportValidator(null, decoder)`.

- [ ] **Step 2: Run them.** `t DesktopGuides.Infrastructure.Tests MapImportTests`.
  Expected: build errors, then FAIL.
- [ ] **Step 3: Implement** the Interfaces block.
  - `InspectMapAsync` runs on the thread pool, as TXT and HTML do. It
    opens the file read-only with shared read access and reads the
    header.
  - Then it checks `MapImageLimits.Check`, hashes the file, and calls the
    decoder last. The decoder is the slow step, so it runs only for a file
    that passed every cheap check.
  - Update the unknown-extension message in place.
- [ ] **Step 4: Run them.** `t DesktopGuides.Infrastructure.Tests`.
  Expected: PASS, the whole suite.
- [ ] **Step 5: Commit:** "feat(p2): inspect and publish a map".

### Task 4: The WIC decoder, Ctrl+O, the Maps section and a fitted map

**Files:**
- Create: `src/DesktopGuides.Production/WicMapImageDecoder.cs`
- Create: `src/DesktopGuides.Production/ImageReaderSession.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml` and `ShellWindow.xaml.cs` (the Maps section; the validator; the picker; routing; Resume)
- Modify: `src/DesktopGuides.Production/ImportGuideDialog.xaml` and `.xaml.cs` (a map's size row)
- Modify: every `switch` or `if` on `GuideFormat` that has a fall-through branch (`ShellWindow.xaml.cs` routing, `ShellWindow.Contents.cs`, `ShellWindow.Bookmarks.cs`, `ImportGuideDialog.xaml.cs`), so `Image` never takes a TXT, HTML or PDF path
- Test: the Production build; installed checks in Task 6

**Interfaces:**
- Consumes:
  - Task 1's `MapImageSize`, `MapPresentation`, `IMapImageDecoder`, `MapImportManifest`;
  - Task 3's `GuideImportValidator(limits, maps)`;
  - Task 2's `Guide.PixelWidth/PixelHeight` and `LibraryGameSummary.MapCount`.
- Produces:
  - `WicMapImageDecoder : IMapImageDecoder`:
    1. On the thread pool, open the file as a stream and call `BitmapDecoder.CreateAsync`.
    2. Return false if the decoder's `PixelWidth`/`PixelHeight` differ from `expected`.
    3. Call `GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)`, with a `BitmapTransform` that scales the longer side to at most 256 (`BitmapInterpolationMode.Fant`).
    4. Return true. Any exception except cancellation returns false.
  - `ImageReaderSession : IReaderSession`:
    - `Format => GuideFormat.Image` and `Capabilities => ReaderCapabilities.None`. Use the enum's zero value, or `default` if it has no `None`.
    - `OpenAsync` decodes the file into a `BitmapImage`. `DecodePixelWidth` is the width scaled so neither side exceeds 4096; for an image within 4096 on both sides it's 0 (natural size).
    - `View` is a `Grid` holding an `Image` with `Stretch="Uniform"`, `AutomationProperties.AutomationId="MapImageView"` and `AutomationProperties.Name` set to the guide's title.
    - `GetLocationAsync` returns null, `RestoreLocationAsync` returns `RestoreKind.Unavailable`, and `ExecuteAsync` throws `NotSupportedException`.
    - It raises no `LocationChanged`.
  - Game page AutomationIds:
    - `GameMapsHeader`, with a title TextBlock "Maps";
    - `OpenSelectedMap` ("Open selected map"), `RemoveSelectedMap` ("Remove map") and `ImportMapsButton` ("Import maps", which Task 5 wires);
    - `MapsEmptyState`, containing `MapsEmpty` with the text "No maps yet.";
    - `MapList`, a `ListView` with `AutomationProperties.Name="Maps"` and the `DesktopGuidesCatalogRowTemplate`.
  - The import dialog's map row: `ImportPixels`, "{w} × {h} pixels".

- [ ] **Step 1: The decoder and the validator.** Add `WicMapImageDecoder`.
  `ShellWindow`'s `importValidator` becomes
  `new GuideImportValidator(maps: new WicMapImageDecoder())`.
  `PickGuideFileAsync` adds `.png`, `.jpg` and `.jpeg`. The publisher
  needs no decoder: its verify step re-reads the header.
- [ ] **Step 2: The single-file dialog.** `ShowManifest` gains
  `case MapImportManifest map`, which fills and shows the `ImportPixels`
  row. `ShowPreview` shows "Map image" as the format. The title box takes
  the manifest's suggested title, as every format does.
- [ ] **Step 3: The Maps section.**
  - Below the guide list, add a header row (title, the three buttons),
    the empty state and `MapList`.
  - The guide list and the maps section share the list area: the guide
    list takes `2*` and the maps section `*`, each with a minimum height
    of two rows. At 1500 × 720 both are visible without scrolling the
    page.
  - When filling the game page, split `ListGuideSummariesAsync`'s result
    by `Format == Image`. The guides keep their order. The maps go
    through `MapPresentation.Sort`.
  - `GameEmptyState` and `loadedGameGuideCount` count non-map guides;
    `MapsEmptyState` shows when there are no maps.
  - Selecting a row in one list clears the other's selection. The guide
    buttons act on `GuideList`, and the map buttons on `MapList`.
  - Opening and removing a map reuse the guide flows (`OpenGuideAsync`,
    the guide remover) with the map's `Guide`.
  - The keys and clicks that open a guide row open a map row.
  - Focus moves between the two lists with Tab.
  - Rename nothing that existing smoke modes look up (`GuideList`,
    `ImportGuideButton`, `GameEmpty`).
- [ ] **Step 4: Routing.** In the `ReaderRoute` case:
  - **Before `ReaderCompletionChoice.Show`:** for `GuideFormat.Image`,
    call `ReaderCompletionChoice.Hide()`. Set `ReaderFormat.Text` to the
    kind's label ("PNG" or "JPEG"), then open the `ImageReaderSession`
    through a new `OpenMapAsync(guide, generation)`, the way
    `OpenPdfGuideAsync` does.
  - **`OpenMapAsync`:**
    - It loads, marks the guide, calls `ShowReaderSurface(loading: false,
      view: session.View)`, and calls `ReaderActions.SetSession(session)`.
    - It doesn't call `OpenAtSavedPlaceAsync` or `progress.Track`.
    - A file that's missing or doesn't decode shows the reader's existing
      error surface: "This map can't be shown. Its file may be missing or
      damaged."
  - **The TXT branch:** it runs only for `GuideFormat.Txt`. Any other
    format reaching it is a bug, so it throws
    `NotSupportedException`.
  - **Resume:** `OpenGuideAsync` skips the `LastActiveGuideId` update
    when `guide.Format == GuideFormat.Image`.
- [ ] **Step 5: The other format switches.** Grep `GuideFormat\.` in
  `src/DesktopGuides.Production`, and give each branch that would treat
  `Image` as another format an explicit `Image` case: no contents, no
  bookmarks, no text size.
- [ ] **Step 6: Build.** `stage; build`. Expected: `0 Warning(s)`,
  `0 Error(s)`. Then run both test suites, `t DesktopGuides.Core.Tests`
  and `t DesktopGuides.Infrastructure.Tests`. Expected: PASS.
- [ ] **Step 7: Commit:** "feat(p2): the Maps section, and a map opened fitted".

### Task 5: Import maps: several files at once

**Files:**
- Create: `src/DesktopGuides.Core/Maps/MapBatch.cs` (the batch's rules, testable without WinUI)
- Create: `src/DesktopGuides.Production/ImportMapsDialog.xaml` and `.xaml.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (`ImportMapsClicked`, a multi-file picker)
- Test: `tests/DesktopGuides.Core.Tests/MapBatchTests.cs` (new)

**Interfaces:**
- Consumes:
  - Task 3's `InspectAsync` and `PublishAsync` with `MapImportManifest`;
  - Task 4's `ImportMapsButton`.
- Produces:
  - `MapBatch.MaxFiles = 100`.
  - `MapBatch.ImportLabel(int ready) -> string`: "Import 1 map", "Import N maps", and "Import maps" for 0.
  - `MapBatch.Summary(int imported, IReadOnlyList<(string Title, string Reason)> failed) -> string`:
    - "Imported 1 map." or "Imported N maps.";
    - with failures, it adds " 1 couldn't be imported: {title}: {reason}" or " N couldn't be imported." followed by each as "{title}: {reason}", separated by "; ".
    - With nothing imported it starts "No maps were imported."
  - `MapBatch.Facts(MapImportManifest map) -> string`: "PNG · 640 × 480 · {FormatSize(bytes)}".
  - The dialog's AutomationIds:
    - `ImportMapsDialog`;
    - `ImportMapsList`, with one `MapRow` per file. A row's `AutomationProperties.Name` is the file name, and its `HelpText` is its facts or its problem.
    - Inside each row: `MapTitleInput`, named "Title for {file name}", and `MapThumbnail`;
    - `PrimaryButton`, the dialog's primary button, labelled with `ImportLabel`.

- [ ] **Step 1: Write the failing tests.** In `MapBatchTests`:

```csharp
    [Theory]
    [InlineData(0, "Import maps")]
    [InlineData(1, "Import 1 map")]
    [InlineData(3, "Import 3 maps")]
    public void TheImportButtonCountsReadyMaps(int ready, string label) =>
        Assert.Equal(label, MapBatch.ImportLabel(ready));

    [Fact]
    public void ASummaryNamesEachFailure()
    {
        Assert.Equal("Imported 2 maps.", MapBatch.Summary(2, []));
        Assert.Equal("Imported 1 map. 1 couldn't be imported: castle: The file changed after it was checked.",
            MapBatch.Summary(1, [("castle", "The file changed after it was checked.")]));
        Assert.Equal("No maps were imported. 2 couldn't be imported: a: x; b: y",
            MapBatch.Summary(0, [("a", "x"), ("b", "y")]));
    }

    [Fact]
    public void ARowShowsTypeSizeAndBytes() =>
        Assert.Equal($"PNG · 640 × 480 · {ImportPresentation.FormatSize(2048)}",
            MapBatch.Facts(new MapImportManifest(
                new ImportSource("C:\\m\\a.png", "a.png", 2048, DateTimeOffset.UnixEpoch),
                "a", MapImageKind.Png, 640, 480, new string('0', 64))));
```

- [ ] **Step 2: Run them.** `t DesktopGuides.Core.Tests MapBatchTests`. Expected: FAIL.
- [ ] **Step 3: Implement `MapBatch`.** Run the tests again. Expected: PASS.
- [ ] **Step 4: The picker.** `PickMapFilesAsync()` uses
  `FileOpenPicker.PickMultipleFilesAsync` with `.png`, `.jpg` and
  `.jpeg`. More than `MaxFiles` files shows the warning status "Choose at
  most 100 maps at a time." and opens nothing.
- [ ] **Step 5: The dialog.**
  - **Inspecting:** it opens with a progress ring and inspects the files
    one at a time, in the picker's order, off the UI thread.
  - **A ready file:**
    - its row has a title box with the suggested title, the facts line,
      and a 48-pixel thumbnail (`BitmapImage`, `DecodePixelWidth = 48`);
    - a file whose fingerprint is already a map of this game
      (`FindGuideByFingerprintAsync(gameId, GuideFormat.Image, ...)`)
      shows "Already a map for this game." instead, and isn't imported.
  - **A refused file** shows the inspection's message, with no title box.
  - **The primary button:** `ImportLabel` of the ready count, disabled at
    0 and while titles are invalid (`GuideTitle.TryCreate`). Close
    cancels.
  - **Importing:**
    - It publishes the ready maps one at a time, with `allowDuplicate:
      false`, showing progress as "Importing 2 of 5…".
    - A publish failure is recorded, and the rest go on.
    - Cancel stops before the next file, never mid-file.
  - **At the end:** the dialog closes, and the game page re-renders with
    the last imported map selected in `MapList`. The status bar shows
    `Summary`: information when nothing failed, a warning otherwise.
- [ ] **Step 6: Wire it.** `ImportMapsClicked` runs
  `PickMapFilesAsync`, then the dialog, behind the same `importRequested`
  guard as Import guide. The button is enabled when Import guide is.
- [ ] **Step 7: Build and test.** `stage; build`, then both suites.
  Expected: 0 warnings, PASS.
- [ ] **Step 8: Commit:** "feat(p2): import several maps at once".

### Task 6: The fixtures and the installed `import-maps` mode

**Files:**
- Modify: `tools/p0/make_fixtures.py` (the map fixtures under `generated/maps/`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`Choose-PickerFiles`; the `import-maps` mode in both mode lists)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-MapImportScenario` in the `import` group)
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`describe-import` gains map facts)

**Interfaces:**
- Consumes: Task 4's and Task 5's AutomationIds; Global Constraints' messages.
- Produces: the fixtures below, and the report keys `importMaps` and `importMapsState`.

- [ ] **Step 1: The fixtures.** Generated by `make_fixtures.py`, beside
  `generated/pdf-long.pdf`:

  | File | Content |
  | --- | --- |
  | `generated/maps/castle-floor-1.png` | `png(640, 480)` |
  | `generated/maps/catacombs.jpg` | `tiny_jpeg(64, 48)` |
  | `generated/maps/strip-map.png` | `png(20000, 400)` |
  | `generated/maps/broken-map.png` | `castle-floor-1.png` cut to its first half |
  | `generated/maps/huge-header.png` | an IHDR of 40000 × 40000, an IDAT of `zlib.compress(b"\0")`, and IEND |

  `tiny_jpeg(width, height)` writes a valid grey baseline JPEG with no
  library:
  - SOI, then DQT with one table of all 1s;
  - SOF0 with one 8-bit component, sampling 1×1, table 0;
  - DHT with the standard luminance DC and AC tables (ITU T.81 Annex K.3);
  - SOS, then the scan, then EOI.

  Every 8×8 block has a DC difference of 0 and no AC coefficients. That
  is the bits `00` (DC category 0) then `1010` (EOB), so each block is
  the 6 bits `001010`. Concatenate `ceil(w/8) * ceil(h/8)` blocks, pad
  the last byte with 1s, and stuff `0x00` after any `0xFF` byte.

  Register each fixture with `add()`, with a purpose line.
- [ ] **Step 2: `describe-import`** adds:
  - `MapGuides`: `SELECT COUNT(*) FROM Guides WHERE Format = 'Image'`;
  - `MapReadingStates`: reading-state rows for those guides;
  - `MapSizes`: `group_concat(PixelWidth || 'x' || PixelHeight)` ordered by title;
  - `LastActiveIsMap`: whether `Settings.LastActiveGuideId` is a map.
- [ ] **Step 3: `Choose-PickerFiles([string] $relativeFolder, [string[]] $names)`.**
  1. Put the folder's full path in the picker's name box (`1148`) and
     press Open (`1`). That navigates.
  2. Put `"a.png" "b.jpg"` (each name quoted, separated by a space) in the
     same box and press Open again.
  3. Wait for the picker to close.

  Throw before step 1 if any fixture is missing, as `Choose-PickerFile`
  does. If the dialog doesn't accept this, find a way that drives the
  real multi-select picker and record it as a ruling.
- [ ] **Step 4: The `import-maps` mode.** It runs in the `import` group,
  after `import-html-pages`, on Import Test Game:
  1. **The empty section:** `MapsEmpty` reads "No maps yet." and
     `ImportMapsButton` is enabled.
  2. **The dialog:** Import maps, then pick `castle-floor-1.png`,
     `catacombs.jpg`, `broken-map.png` and `huge-header.png` from
     `generated\maps`.
     - `ImportMapsDialog` shows four `MapRow`s, by file name. Their
       `HelpText`:
       - castle: `PNG · 640 × 480 · *`;
       - catacombs: `JPEG · 64 × 48 · *`;
       - broken: "broken-map.png can't be read as an image. It may be
         damaged or incomplete.";
       - huge: "huge-header.png is 40,000 × 40,000 pixels. A map can be at
         most 32,768 pixels on a side."
     - The primary button reads "Import 2 maps".
     - Save the screenshot `import-maps`.
  3. **The import:** press it. The dialog closes, the status reads
     "Imported 2 maps.", and `MapList` holds "castle floor 1" then
     "catacombs". Each row's help text starts with its type and size, and
     has no reading state. `MapsEmptyState` is hidden.
  4. **One map by Ctrl+O:** press Import guide and pick
     `generated\maps\strip-map.png`.
     - `ImportFormat` reads "Map image" and `ImportPixels` reads
       "20000 × 400 pixels".
     - Import it. `MapList` holds three maps by title, and the guide list
       is unchanged.
  5. **Opening a map:** open "castle floor 1".
     - `MapImageView` is visible, named "castle floor 1".
     - `ReaderCompletionChoice` isn't visible.
     - Save the screenshot `import-maps-open`.
     - Go Back to the game page.
  6. **The section's screenshot:** save `import-maps-section`.
- [ ] **Step 5: `Run-MapImportScenario`** runs the mode, closes the shell,
  and checks `describe-import`:
  - `MapGuides` is 3;
  - `MapReadingStates` is 0;
  - `MapSizes` is `640x480,64x48,20000x400`;
  - `LastActiveIsMap` is false.

  Call it in the `import` group, after `Run-MultipageImportScenario`.
- [ ] **Step 6: The parse check, locally.** Run
  `pwsh -NoProfile -Command "[void][System.Management.Automation.Language.Parser]::ParseFile('tools/p1/windows_shell_ui_smoke.ps1',[ref]\$null,[ref]\$e); \$e.Count"`
  and the same for `windows_shell_install.ps1`. Expected: `0` for each.
  Run `python3 tools/p0/make_fixtures.py`, then
  `file tests/fixtures/p0/generated/maps/*`. Expected: the PNGs report
  their sizes, and the JPEG reports "JPEG image data … 64x48".
- [ ] **Step 7: Commit** "test(p2): the installed import-maps mode", **push,** then run the `import` scope on both
  lanes (see Host commands). Expected: success, with `import-maps`
  passing. On failure, use systematic-debugging and record what the run
  showed.
- [ ] **Step 8: The screenshots.** Copy `import-maps.png`,
  `import-maps-section.png` and `import-maps-open.png` from the MSIX
  artifact to `docs/p2/evidence/ci/t21-2/`. Look at each. They're
  committed with Task 7's records.

### Task 7: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t21-2-image-maps-design.md` (Status; a "PR a verification record")
- Modify: `docs/p2/results.md` (#135's row)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md`
- Create: the three screenshots from Task 6

- [ ] **Step 1: The full suites and the build** on the host: Core,
  Infrastructure, the Production build with 0 warnings, and the script
  parse check.
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **The design's Status:** "Approved in conversation, 19 October 2026.
    PR a (import, storage, the Maps section and backup) implemented on
    `feat/p2-t21-2-image-maps-import`."
  - **The "PR a verification record":** the tests, the `import`
    dispatches on both lanes, the screenshots, the review, and every
    ruling, including this plan's deviations.
  - **`results.md`:** #135's row: 2026-10-18, `c0addf1`, T21.2 (PR c),
    run 39133678906, Core 1396, Infra 876, "Contents, search and
    bookmarks across pages; multi-page HTML supported. Passed on the
    first attempt." Check the counts against the run before writing them.
  - **The status pages:** "T21.2 multi-page HTML complete (#133–#135).
    Image maps PR a (import and the Maps section) in review." The
    `progress.md` "Next" becomes "T21.2 image maps PR b (the reader)".
- [ ] **Step 4: Commit, push, open the PR, and wait for its run.**
  - **Commit:** "docs(p2): record image maps PR a".
  - **The PR:** "T21.2 image maps PR a: import maps and the Maps
    section", based on `main`. It names:
    - the task;
    - the prerequisites: T21.1 (#132) and multi-page HTML (#133–#135),
      all merged;
    - the outcome.

    It includes the screenshots (this is a UI change), the dispatches,
    every ruling and the deferred minors.
  - **Expected:** every job passes. Record the run in the PR, then stop
    for the user's merge decision.

## Deviations from the design

1. **The installed mode is `import-maps`,** not `maps-import`. It shares
   the import group's picker helpers and fixture root
   (`$Mode -like 'import-*'`).
2. **The single-file dialog (Ctrl+O) shows no thumbnail.** The Import
   maps dialog does. Ctrl+O shows the type, the pixel size and the file
   size.
3. **A duplicate in Import maps is skipped,** with "Already a map for
   this game." The single-file dialog's "import anyway" choice isn't
   repeated per row.
4. **At most 100 files per Import maps,** a limit the design didn't name.
5. **PR a's map opens through `DecodePixelWidth`** (one downsampled
   decode). The design's levels, tiles and preview copies come in PR b
   and PR c.

## Traceability

| Design item | Task |
| --- | --- |
| The `Image` format; schema 8 with `PixelWidth`/`PixelHeight`; the rebuild doesn't cascade | 1, 2 |
| PNG and JPEG by header; the extension only gates picking | 1, 3 |
| Limits: 256 MiB, 32,768 a side, 268 million pixels, from the header | 1, 3 |
| The decode check off the UI thread | 3, 4 |
| Titles from file names, `-` and `_` as spaces; `SourceLabel` keeps the name | 1, 3 |
| Import maps, several files; Ctrl+O imports one map | 5, 4 |
| The Maps section: always shown, "No maps yet.", by title | 4 |
| A map row: type and size, no reading state | 1, 4 |
| "N guides, M maps"; search finds maps | 1, 2 |
| A map opens fitted; not reading activity; no completion | 4, 6 |
| Backup and restore of a map; a schema 7 backup | 2 |
| `maps-import` (as `import-maps`) | 6 |
