# T21.2 image maps PR b (the reader: zoom, pan, levels and tiles, the view place) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A map opens on its own for longer study. It opens fitted,
pans by drag or arrow keys, and zooms from fit up to 4× its own pixels.
A map over 16384 pixels wide still displays. It reopens at the zoom and
spot the reader left, and that place never counts as progress or as
reading activity.

**Architecture:**
- **Core** gets the view place `ImagePosition` (the codec's fourth
  payload). It also gets the view math (`MapViewport`: fit, zoom about a
  point, pan, clamping, the level to draw from, the visible tiles), the
  pyramid layout and a streaming 2×2 builder (`MapPyramid`,
  `MapPyramidBuilder`), the in-memory tile budget (`MapTileMemory`), and
  `Track(..., recordOpen: false)` on the progress coordinator.
- **Infrastructure** gets `MapTileStore`, the on-disk cache under the
  app's cache folder. It holds an overview and tiles per map, keyed by
  the file's hash, capped at 2 GiB with the least recently opened
  dropped first, and pruned when no guide has the hash.
- **Production** gets:
  - `WicMapTileBuilder`: WinRT `BitmapDecoder` with a `BitmapTransform`,
    one scaled decode for the overview, then bands of 512 rows into
    `MapPyramidBuilder`, with PNG tiles written by `BitmapEncoder`;
  - `MapCacheQueue`: one build at a time, after an import or on open;
  - `ImageReaderSession`, rewritten: an overview under the visible tiles
    on a `Canvas`, positioned by `MapViewport`;
  - the shell's map open with its saved place.

**Tech Stack:** .NET 10, xUnit, WinUI 3, `Windows.Graphics.Imaging`
(WinRT WIC), PowerShell UI Automation.

**Spec:** [t21-2-image-maps-design.md](t21-2-image-maps-design.md)
(approved 19 October 2026), sections "Reading a map" and "Tests".

## Global Constraints

- **Zoom** is physical pixels per image pixel (`XamlRoot.RasterizationScale` × DIPs).
  - `Fit = min(viewportW / imageW, viewportH / imageH, MaxZoom)`;
    `MaxZoom = 4`. Zoom stays in `[Fit, MaxZoom]`.
  - A zoom within 1% of `Fit` is fitted.
  - Steps: Ctrl+Plus and the Zoom in button multiply by `1.25`, Ctrl+Minus
    and Zoom out divide by it, about the viewport's centre. Ctrl+wheel
    multiplies by `1.25^(delta / 120)` about the pointer.
  - Ctrl+0 and the **Fit map** button fit.
- **Pan:**
  - drag pans with the pointer captured;
  - the arrow keys pan by 10% of the viewport;
  - the wheel pans vertically, and Shift+wheel horizontally;
  - a side of the map smaller than the viewport is centred on that side;
    otherwise the map's edge can't come inside the viewport.
- **The toolbar** for a map shows Zoom out, **Fit map** (`"Fit map (Ctrl+0)"`)
  and Zoom in, as primary commands. A new capability
  `ReaderCapabilities.FitView = 2048`, with command `ReaderCommand.FitView`
  and action `FitViewAction()`, shows Fit map. A map's capabilities are
  `Zoom | FitView`. Zoom in is disabled at `MaxZoom`, and Zoom out at fit.
- **Small and large maps:**
  - A map whose longer side is at most **4096** opens from its file as
    one surface, with no cache.
  - A larger map draws from its cache:
    - the **overview**, the first halving whose longer side is at most
      2048, stored as one image;
    - levels 0 (full size) to the level above the overview, in **512 ×
      512** tiles. Level `k` is `ceil(w / 2^k) × ceil(h / 2^k)`.
  - No surface is wider or taller than **4096** pixels.
- **The level to draw from** is the smallest level `k` (the most detail)
  whose scale `2^-k` is at least the zoom. The overview is used when that
  level is the overview or above.
- **Memory:**
  - tiles in memory stay under `MapTileMemory.DefaultMaxBytes = 100_663_296`
    (`PdfRasterBudget.MaxBytes`), least recently drawn dropped first;
  - visible tiles are touched on every view change;
  - the overview (at most 16 MiB) is held apart from that budget.
- **The disk cache** (`MapTileStore`):
  - lives in `<cacheRoot>/maps/<contentSha256>/`: `overview.png`,
    `L<k>/<col>_<row>.png` and `map-cache.json` (written last; its
    presence and contents mark a complete cache);
  - is built in `<cacheRoot>/maps/<sha>.building/`, then moved into
    place;
  - is capped at `MaxBytes = 2_147_483_648`, dropping the least recently
    opened first (by `map-cache.json`'s last-write time, which is touched
    on open), but never the map being opened or built;
  - is pruned of every hash no guide has, and every `.building` folder.
    This happens at startup, after a guide or game is removed, and after
    a restore.
  - is never backed up.
- **`map-cache.json`:** `{"version":1,"width":W,"height":H,"tileSize":512,"levels":N,"overviewWidth":w,"overviewHeight":h}`.
  Any other version, a mismatched size, or a missing overview or level
  folder makes the cache incomplete, and it is rebuilt.
- **Building:**
  - off the UI thread, one map at a time;
  - large maps only;
  - queued after each map import (both dialogs), and on open when the
    cache is missing or incomplete; an open moves its map to the front.
  - Decoding uses `BitmapPixelFormat.Bgra8`, `BitmapAlphaMode.Premultiplied`,
    `ExifOrientationMode.IgnoreExifOrientation` and
    `ColorManagementMode.DoNotColorManage`, as PR a's check does, so the
    decoded size is the header's.
  - Bands are at most 512 rows at full width: 32768 × 512 × 4 = 64 MiB at
    worst.
- **Opening:**
  - With a complete cache, the overview shows fitted within **1 s**, and
    full detail fills in.
  - Without one, the reader shows "Preparing the map…" until the overview
    is built, then the map, and then detail once the tiles are built.
  - PR a's load-failure message stays.
- **The view place:**
  - `ImagePosition(double? Zoom, double CenterX, double CenterY)`.
  - `Zoom` null means fitted; otherwise `0 < Zoom <= 4`.
  - The centres are fractions of the image, `[0, 1]`.
  - JSON payload: `"zoom"` (number or null), `"centerX"`, `"centerY"`.
  - An `Image` location's `EstimatedFraction` must be null.
  - The codec's `CurrentVersion` stays 1.
- **Restoring:**
  - a `Valid` place restores exactly, with the zoom clamped to `[Fit, MaxZoom]`;
  - any other result (changed content, invalid, unsupported) fits,
    **with no notice and no warning status**.
- **Saving:**
  - the session raises `LocationChanged` when a drag ends, after a key,
    wheel or zoom step, and after a resize;
  - the coordinator's 1 s quiet period saves it;
  - Back and close flush;
  - an unmoved map isn't written.
- **Not reading activity:**
  - a map is tracked with `Track(guideId, session, baseline, recordOpen: false)`,
    so `LastOpenedUtcMs` stays null;
  - Resume (`LastActiveGuideId`) and the completion control stay as PR a
    left them.
- **Shell rules:** no `cd` in local shell commands. PowerShell stays
  ASCII-only and runs under 5.1 and 7.
- **Visual direction:** the app's existing Fluent styles. The map sits
  on the reader surface's own background. No new styles.

## Review Focus

1. **A resize or a move to another DPI while zoomed** keeps the centre
   and clamps the zoom to the new fit. Pinned by Task 2's
   `ResizeKeepsTheCentreAndClampsToTheNewFit`.
2. **A cache interrupted mid-build** (the app closed, or a crash) is
   rebuilt, not shown with holes. Pinned by Task 4's
   `AnInterruptedBuildIsPrunedAndNotComplete`.
3. **Windows Disk Cleanup emptying the cache folder** while the app is
   closed means the next open rebuilds. Pinned by Task 4's
   `ACacheMissingALevelFolderIsIncomplete`.
4. **The same map imported into two games** shares one cache, and
   removing one copy keeps it. Pinned by Task 4's
   `PruneKeepsAHashAnotherGuideStillUses`.
5. **A thin strip** (20000 × 400) fits by width, has one tile row per
   level, and can't pan vertically. Pinned by Task 2's
   `AStripFitsByWidthAndCentresVertically` and Task 3's
   `AStripHasOneTileRowPerLevel`.

## Host commands

As in the [PR a plan](t21-2-image-maps-import-plan.md#host-commands), with
the staging folder `E:\work\desktop-guides\t21-2r` and logs
`E:\work\t21-2r-*.log`.

Installed runs: `gh workflow run windows-ci.yml --ref feat/p2-t21-2-image-maps-reader -f shell-scope=navigation -f shell-lane=msix` (and `portable`).

---

### Task 1: Core: the view place, the Fit capability, and tracking without an open

**Files:**
- Modify: `src/DesktopGuides.Core/Reading/ReaderLocation.cs` (`ImagePosition`)
- Modify: `src/DesktopGuides.Core/Reading/ReaderLocationCodec.cs` (serialize, deserialize, validate; `RestoreCandidates` gives `[Exact]` for a valid `Image` place and `[]` otherwise)
- Modify: `src/DesktopGuides.Core/Reading/ReaderContract.cs` (`FitView = 2048`, `ReaderCommand.FitView`, `FitViewAction`, the policy row)
- Modify: `src/DesktopGuides.Core/Reading/ProgressCoordinator.cs` (`Track(..., bool recordOpen = true)`)
- Test: `tests/DesktopGuides.Core.Tests/ReaderLocationCodecTests.cs`, `ReaderCommandPolicyTests.cs`, `ProgressCoordinatorTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ImagePosition(double? Zoom, double CenterX, double CenterY) : ReaderPosition`, and `ImagePosition.MaxZoom = 4`;
  - `ReaderCapabilities.FitView`, `ReaderCommand.FitView`, `public sealed record FitViewAction() : ReaderAction(ReaderCommand.FitView)`;
  - `ProgressCoordinator.Track(Guid guideId, IReaderSession session, string? restoredJson, bool recordOpen = true)`.

- [ ] **Step 1: Write the failing tests.**

```csharp
    [Theory]
    [InlineData(null, 0.5, 0.5)]
    [InlineData(2.5, 0.0, 1.0)]
    [InlineData(4.0, 0.25, 0.75)]
    public void AnImagePlaceRoundTrips(double? zoom, double x, double y)
    {
        ReaderLocation location = new(GuideFormat.Image, 1, Hash, new ImagePosition(zoom, x, y), null);
        LocationDecodeResult result = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(location), GuideFormat.Image, Hash);
        Assert.Equal(LocationDecodeStatus.Valid, result.Status);
        Assert.Equal(location, result.Location);
    }

    [Theory]
    [InlineData(0.0, 0.5, 0.5)]
    [InlineData(-1.0, 0.5, 0.5)]
    [InlineData(4.01, 0.5, 0.5)]
    [InlineData(1.0, -0.1, 0.5)]
    [InlineData(1.0, 0.5, 1.1)]
    [InlineData(double.NaN, 0.5, 0.5)]
    public void AnImagePlaceOutOfBoundsIsRefused(double zoom, double x, double y) =>
        Assert.Throws<InvalidDataException>(() => ReaderLocationCodec.Serialize(
            new(GuideFormat.Image, 1, Hash, new ImagePosition(zoom, x, y), null)));

    [Fact]
    public void AnImagePlaceNeverCarriesAnEstimate() =>
        Assert.Throws<InvalidDataException>(() => ReaderLocationCodec.Serialize(
            new(GuideFormat.Image, 1, Hash, new ImagePosition(null, 0.5, 0.5), 0.3)));

    [Fact]
    public void AStoredImagePlaceWithAnEstimateIsInvalid() =>
        Assert.Equal(LocationDecodeStatus.Invalid, ReaderLocationCodec.Deserialize(
            $$"""{"format":"Image","schemaVersion":1,"contentSha256":"{{Hash}}","payload":{"zoom":null,"centerX":0.5,"centerY":0.5},"estimatedFraction":0.4}""",
            GuideFormat.Image, Hash).Status);

    [Fact]
    public void AChangedMapHasNoRestoreCandidates()
    {
        string json = ReaderLocationCodec.Serialize(
            new(GuideFormat.Image, 1, Hash, new ImagePosition(2, 0.3, 0.3), null));
        Assert.Equal([RestoreKind.Exact], ReaderLocationCodec.RestoreCandidates(
            ReaderLocationCodec.Deserialize(json, GuideFormat.Image, Hash)));
        Assert.Empty(ReaderLocationCodec.RestoreCandidates(
            ReaderLocationCodec.Deserialize(json, GuideFormat.Image, OtherHash)));
    }

    // ReaderCommandPolicyTests
    [Fact]
    public void AMapShowsZoomAndFitOnly() =>
        Assert.Equal([ReaderCommand.Zoom, ReaderCommand.FitView], ReaderCommandPolicy.VisibleCommands(
            ReaderCapabilities.Zoom | ReaderCapabilities.FitView));

    // ProgressCoordinatorTests, with the file's FakeStore and fake clock
    [Fact]
    public async Task TrackingWithoutAnOpenRecordsNoOpenButStillSaves()
    {
        // Track(..., recordOpen: false), move the session, advance the clock by Quiet.
        // Assert: store.Opens == 0, store.Saves == 1, coordinator.Counts.Opens == 0.
    }
```

  Write the coordinator test's body in the style of its neighbours, with
  the assertions in the comment.
- [ ] **Step 2: Run them.** `t DesktopGuides.Core.Tests "FullyQualifiedName~ReaderLocationCodec|FullyQualifiedName~ReaderCommandPolicy|FullyQualifiedName~ProgressCoordinator"`.
  Expected: compile errors for `ImagePosition`, `FitView` and `recordOpen`.
- [ ] **Step 3: Implement.**
  - Validation: `(GuideFormat.Image, ImagePosition p)` is valid when the
    zoom is null or finite in `(0, MaxZoom]`, both centres are fractions,
    and `EstimatedFraction` is null.
  - The policy row is `(ReaderCommand.FitView, ReaderCapabilities.FitView)`,
    after `Zoom`.
  - `ShowsGroupSeparator` is unchanged.
  - `Track` skips `RecordOpenAsync` when `recordOpen` is false.
- [ ] **Step 4: Run them, then the whole Core suite.** Expected: all pass.
- [ ] **Step 5: Commit.** "feat(p2): a map's view place, and tracking without an open".

### Task 2: Core: the view math

**Files:**
- Create: `src/DesktopGuides.Core/Maps/MapViewport.cs`
- Test: `tests/DesktopGuides.Core.Tests/MapViewportTests.cs`

**Interfaces:**
- Produces an immutable `public sealed record MapViewport`, all sizes in
  physical pixels:
  - `MapViewport.Fitted(int imageWidth, int imageHeight, double viewWidth, double viewHeight)`;
  - properties: `ImageWidth`, `ImageHeight`, `ViewWidth`, `ViewHeight`,
    `Zoom`, `CenterX`, `CenterY` (image pixels), `Fit`, `IsFitted`,
    `CanZoomIn`, `CanZoomOut`;
  - methods:
    - `ZoomBy(double factor, double viewX, double viewY)` keeps the image
      point under `(viewX, viewY)` there;
    - `ZoomIn()` and `ZoomOut()` step by `1.25` about the centre;
    - `FitToView()`;
    - `PanBy(double dx, double dy)` moves the map by that many view
      pixels, as a drag does, so a negative `dx` moves the centre right;
    - `PanStep(int columns, int rows)` (10% of the view per step);
    - `Resize(double viewWidth, double viewHeight)`;
  - `ToPosition() -> ImagePosition` (null zoom when fitted);
    `static FromPosition(ImagePosition p, int w, int h, double vw, double vh)`;
  - `Level(int levelCount) -> int`: the smallest `k` with `2^-k >= Zoom`,
    capped at `levelCount`, where `levelCount` means "the overview";
  - `VisibleTiles(int level, int tileSize) -> (int FirstColumn, int FirstRow, int LastColumn, int LastRow)`,
    clipped to the level's grid, with one extra tile on each side;
  - `ViewRect() -> (double X, double Y, double Width, double Height)`,
    the visible image area in image pixels.
- Every result is clamped: zoom in `[Fit, MaxZoom]`, and the centre so
  that no edge comes inside the view, or centred on a side smaller than
  the view.

- [ ] **Step 1: Write the failing tests.**

```csharp
    [Fact]
    public void AMapOpensFittedToTheView()
    {
        MapViewport view = MapViewport.Fitted(15456, 10304, 1600, 900);
        Assert.Equal(900.0 / 10304, view.Fit, 9);
        Assert.True(view.IsFitted);
        Assert.False(view.CanZoomOut);
        Assert.Equal((7728.0, 5152.0), (view.CenterX, view.CenterY));
    }

    [Fact]
    public void ASmallMapFitsAtNoMoreThanFourTimes() =>
        Assert.Equal(4, MapViewport.Fitted(64, 48, 1600, 900).Fit);

    [Fact]
    public void ZoomAboutAPointKeepsThatPointStill()
    {
        MapViewport view = MapViewport.Fitted(4000, 4000, 1000, 1000).ZoomBy(2, 250, 250);
        // The image point under (250, 250) was (1000, 1000); it still is.
        Assert.Equal(1000, view.CenterX - (500 - 250) / view.Zoom, 6);
        Assert.Equal(0.5, view.Zoom, 9);
    }

    [Fact]
    public void ZoomStopsAtFourTimes()
    {
        MapViewport view = MapViewport.Fitted(1000, 1000, 1000, 1000);
        for (int i = 0; i < 20; i++) view = view.ZoomIn();
        Assert.Equal(ImagePosition.MaxZoom, view.Zoom);
        Assert.False(view.CanZoomIn);
    }

    [Fact]
    public void ZoomingOutPastFitFits() =>
        Assert.True(MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomIn().ZoomOut().ZoomOut().IsFitted);

    [Fact]
    public void PanningStopsAtTheEdge()
    {
        MapViewport view = MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomBy(8, 500, 500).PanBy(-100000, 0);
        Assert.Equal(8000 - 500 / view.Zoom, view.CenterX, 6);
    }

    // Review Focus 5.
    [Fact]
    public void AStripFitsByWidthAndCentresVertically()
    {
        MapViewport view = MapViewport.Fitted(20000, 400, 2000, 1000).PanBy(0, 5000);
        Assert.Equal(0.1, view.Fit, 9);
        Assert.Equal(200, view.CenterY, 6);
    }

    // Review Focus 1.
    [Fact]
    public void ResizeKeepsTheCentreAndClampsToTheNewFit()
    {
        MapViewport zoomed = MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomBy(8, 500, 500).PanBy(100, 100);
        MapViewport resized = zoomed.Resize(2000, 2000);
        Assert.Equal((zoomed.CenterX, zoomed.CenterY), (resized.CenterX, resized.CenterY));
        Assert.True(MapViewport.Fitted(8000, 8000, 1000, 1000).Resize(4000, 4000).IsFitted);
        Assert.Equal(0.5, MapViewport.Fitted(8000, 8000, 1000, 1000).ZoomBy(2, 500, 500).Resize(4000, 4000).Zoom, 9);
    }

    [Fact]
    public void AFittedPlaceRestoresFitted() =>
        Assert.True(MapViewport.FromPosition(new ImagePosition(null, 0.2, 0.9), 8000, 8000, 1000, 1000).IsFitted);

    [Fact]
    public void APlaceRoundTrips()
    {
        MapViewport view = MapViewport.Fitted(8000, 6000, 1000, 800).ZoomBy(4, 300, 200);
        Assert.Equal(view, MapViewport.FromPosition(view.ToPosition(), 8000, 6000, 1000, 800));
    }

    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(0.6, 0)]
    [InlineData(0.5, 1)]
    [InlineData(0.3, 1)]
    [InlineData(0.01, 3)]   // the overview, with 3 tiled levels
    public void TheLevelHasAtLeastTheZoomsDetail(double zoom, int level)
    {
        MapViewport view = MapViewport.Fitted(16000, 16000, 100, 100).ZoomBy(zoom / (100.0 / 16000), 50, 50);
        Assert.Equal(level, view.Level(levelCount: 3));
    }

    [Fact]
    public void VisibleTilesAreClippedToTheGrid()
    {
        MapViewport view = MapViewport.Fitted(4096, 4096, 1024, 1024).ZoomBy(4, 0, 0);
        Assert.Equal((0, 0, 2, 2), view.VisibleTiles(level: 0, tileSize: 512));
    }
```

  Where an expected value above depends on rounding the implementer
  can't avoid, fix the arithmetic in the test and keep the claim.
- [ ] **Step 2: Run them.** Expected: compile errors for `MapViewport`.
- [ ] **Step 3: Implement `MapViewport`.** Use value equality (a
  `record`), with doubles compared exactly in the round trip.
- [ ] **Step 4: Run them, then the whole Core suite.** Expected: all pass.
- [ ] **Step 5: Commit.** "feat(p2): the map view's zoom and pan".

### Task 3: Core: the pyramid, its streaming builder, and the tile memory

**Files:**
- Create: `src/DesktopGuides.Core/Maps/MapPyramid.cs`, `MapPyramidBuilder.cs`, `MapTileMemory.cs`
- Test: `tests/DesktopGuides.Core.Tests/MapPyramidTests.cs`, `MapTileMemoryTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record MapPyramid(int Width, int Height, int TileSize = 512)`:
    - `Small` (the longer side is at most 4096);
    - `Levels` (the tiled levels above the overview);
    - `LevelSize(int level) -> (int Width, int Height)` (`ceil(w / 2^k)`);
    - `Columns(int level)` and `Rows(int level)`;
    - `OverviewSize` (the size at level `Levels`, its longer side at most
      2048);
    - `Small` and `OverviewMaxSide = 2048`, `SmallMaxSide = 4096`.
  - `public readonly record struct MapTileKey(int Level, int Column, int Row)`.
  - `public sealed class MapPyramidBuilder(MapPyramid pyramid, Action<MapTileKey, int width, int height, byte[] bgra> tile)`:
    - `AddBand(ReadOnlySpan<byte> bgra, int rows)` takes level-0 rows
      top to bottom, full width, premultiplied BGRA;
    - `Complete()` flushes the partial last rows of every level;
    - each level's next level is made by averaging 2×2 blocks per channel,
      with an odd last column or row averaged over what exists;
    - it emits each tile of levels `0 … Levels - 1` once, left to right,
      as soon as its rows are complete. The overview isn't its output: the
      shell decodes the overview directly, with Fant scaling.
    - it holds at most one tile-row band per level.
  - `public sealed class MapTileMemory<TImage>(long maxBytes = DefaultMaxBytes) where TImage : class`:
    `DefaultMaxBytes = 100_663_296`, `MaxBytes`, `TryGet(MapTileKey, out TImage?)`,
    `Add(MapTileKey, TImage, int width, int height)`, `Touch(MapTileKey)`,
    `Clear()`, `CachedBytes`, `PeakBytes`, `Evictions`. It is the same
    LRU as `PdfRenderCache`, keyed by `MapTileKey`.

- [ ] **Step 1: Write the failing tests.**

```csharp
    [Theory]
    [InlineData(4096, 300, true, 0)]
    [InlineData(4097, 300, false, 2)]      // 4097 → 2049 → 1025 (overview)
    [InlineData(15456, 10304, false, 3)]   // 15456 → 7728 → 3864 → 1932 (overview)
    [InlineData(20000, 400, false, 4)]     // 20000 → … → 1250 (overview)
    public void LevelsStopAtTheOverview(int w, int h, bool small, int levels)
    {
        MapPyramid pyramid = new(w, h);
        Assert.Equal(small, pyramid.Small);
        if (!small) Assert.Equal(levels, pyramid.Levels);
        Assert.True(Math.Max(pyramid.OverviewSize.Width, pyramid.OverviewSize.Height) <= 2048);
    }

    // Review Focus 5.
    [Fact]
    public void AStripHasOneTileRowPerLevel()
    {
        MapPyramid strip = new(20000, 400);
        Assert.All(Enumerable.Range(0, strip.Levels), level => Assert.Equal(1, strip.Rows(level)));
        Assert.Equal(40, strip.Columns(0));
    }

    [Fact]
    public void EveryTileIsEmittedOnceWithItsSize()
    {
        MapPyramid pyramid = new(10, 6, TileSize: 4);
        List<(MapTileKey Key, int W, int H)> tiles = [];
        MapPyramidBuilder builder = new(pyramid, (key, w, h, _) => tiles.Add((key, w, h)));
        byte[] rows = new byte[10 * 4 * 3];
        builder.AddBand(rows, 3);
        builder.AddBand(rows, 3);
        builder.Complete();
        Assert.Equal(tiles.Count, tiles.Select(t => t.Key).Distinct().Count());
        Assert.Contains((new MapTileKey(0, 2, 1), 2, 2), tiles);   // the corner tile of 10 × 6
        Assert.Contains((new MapTileKey(1, 1, 0), 1, 3), tiles);   // level 1 is 5 × 3
    }

    [Fact]
    public void ALevelAveragesTwoByTwoBlocks()
    {
        // A 2 × 2 image of 0, 40, 80 and 120 in every channel averages to 60.
        MapPyramid pyramid = new(2, 2, TileSize: 1);
        byte[]? top = null;
        MapPyramidBuilder builder = new(pyramid, (key, _, _, bgra) => { if (key.Level == 1) top = bgra; });
        builder.AddBand([0, 0, 0, 0, 40, 40, 40, 40, 80, 80, 80, 80, 120, 120, 120, 120], 2);
        builder.Complete();
        Assert.Equal([60, 60, 60, 60], top);
    }

    [Fact]
    public void AnOddEdgeAveragesWhatExists()
    {
        // A 3 × 1 row: the last level-1 pixel is the lone third pixel.
        MapPyramid pyramid = new(3, 1, TileSize: 2);
        Dictionary<MapTileKey, byte[]> tiles = [];
        MapPyramidBuilder builder = new(pyramid, (key, _, _, bgra) => tiles[key] = bgra);
        builder.AddBand([10, 10, 10, 10, 30, 30, 30, 30, 200, 200, 200, 200], 1);
        builder.Complete();
        Assert.Equal([20, 20, 20, 20, 200, 200, 200, 200], tiles[new MapTileKey(1, 0, 0)]);
    }

    // MapTileMemoryTests, mirroring PdfRenderCacheTests
    [Fact]
    public void TheLeastRecentlyDrawnTileGoesFirst()
    {
        MapTileMemory<object> memory = new(maxBytes: 3 * 512 * 512 * 4);
        MapTileKey a = new(0, 0, 0), b = new(0, 1, 0), c = new(0, 2, 0), d = new(0, 3, 0);
        foreach (MapTileKey key in new[] { a, b, c }) memory.Add(key, new object(), 512, 512);
        memory.Touch(a);
        memory.Add(d, new object(), 512, 512);
        Assert.False(memory.TryGet(b, out _));
        Assert.True(memory.TryGet(a, out _));
        Assert.Equal(1, memory.Evictions);
        Assert.True(memory.PeakBytes <= memory.MaxBytes);
    }
```

  The tests use `TileSize` small enough to check by hand. The
  constructor accepts any positive tile size, and `Levels` with a small
  tile size still stops at the overview rule. With the overview at 2048,
  `new MapPyramid(10, 6, 4)` is `Small` and has `Levels = 0`. So the
  builder takes `levels` from a test-only overload,
  `MapPyramid.ForTest(int width, int height, int tileSize, int levels)`.
  It lives in Core with `internal` visibility and `InternalsVisibleTo`
  for the Core tests, as other Core types already do. Use it in the
  three builder tests: `(10, 6, 4, levels: 2)`, `(2, 2, 1, levels: 2)`
  and `(3, 1, 2, levels: 2)`, so level 1 is tiled. In those tests,
  replace `new MapPyramid(...)` with that call.
- [ ] **Step 2: Run them.** Expected: compile errors.
- [ ] **Step 3: Implement.** The builder keeps one accumulation buffer
  per level, `tileSize` rows tall, at that level's width. When a level's
  buffer fills, or at `Complete`, it cuts the buffer into tiles, emits
  them, and averages the buffer into the next level's buffer.
- [ ] **Step 4: Run them, then the whole Core suite.** Expected: all pass.
- [ ] **Step 5: Commit.** "feat(p2): a map's levels and tiles, built in one pass".

### Task 4: Infrastructure: the disk cache

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Maps/MapTileStore.cs`
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (`ListMapHashesAsync`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Maps/MapTileStoreTests.cs` (new), `SqliteLibraryRepositoryTests.cs`

**Interfaces:**
- Consumes: `MapPyramid`, `MapTileKey` (Task 3).
- Produces:
  - `public sealed class MapTileStore(string cacheRoot, long maxBytes = MapTileStore.MaxBytes)`, with `MaxBytes = 2_147_483_648`:
    - `string Folder(string sha256)`;
    - `bool IsComplete(string sha256, MapPyramid pyramid)` checks the
      manifest (version, sizes, tile size, levels) and that
      `overview.png` and every `L<k>` folder exist;
    - `string OverviewPath(string sha256)` and
      `string TilePath(string sha256, MapTileKey key)`;
    - `MapTileBuild BeginBuild(string sha256)`. The build has
      `OverviewPath`, `TilePath(MapTileKey)`, and
      `Commit(MapPyramid pyramid)`, which writes the manifest, then
      deletes any old folder and moves the `.building` folder into place.
      Its `Dispose` deletes an uncommitted folder.
    - `void Touch(string sha256)` sets the manifest's last-write time to
      now;
    - `long Trim(IReadOnlySet<string> keep)` deletes the least recently
      touched complete caches until the total is at most `maxBytes`, and
      returns the bytes left;
    - `void Prune(IReadOnlySet<string> liveHashes)` deletes every cache
      folder whose name isn't a live hash, and every `.building` folder;
  - `ISqliteLibraryRepository.ListMapHashesAsync(CancellationToken token = default) -> Task<IReadOnlySet<string>>`,
    the distinct `ContentSha256` of `Image` guides. Add it to whichever
    interface `SqliteLibraryRepository` exposes to the shell for
    library-wide reads.
- Hashes are checked as 64 lower-case hex characters before any path is
  built, and anything else throws `ArgumentException`.

- [ ] **Step 1: Write the failing tests.** They use a temp cache root and
  a helper `Build(store, sha, pyramid)` that writes empty files at every
  tile path and the overview, then commits.

```csharp
    [Fact]
    public void ACommittedBuildIsComplete()
    {
        Build(store, ShaA, Big);
        Assert.True(store.IsComplete(ShaA, Big));
        Assert.False(Directory.Exists(store.Folder(ShaA) + ".building"));
    }

    // Review Focus 2.
    [Fact]
    public void AnInterruptedBuildIsPrunedAndNotComplete()
    {
        using (MapTileBuild build = store.BeginBuild(ShaA))
        {
            File.WriteAllBytes(build.OverviewPath, [1]);
        }   // no Commit
        Assert.False(store.IsComplete(ShaA, Big));
        Directory.CreateDirectory(store.Folder(ShaB) + ".building");
        store.Prune(new HashSet<string> { ShaA, ShaB });
        Assert.False(Directory.Exists(store.Folder(ShaB) + ".building"));
    }

    // Review Focus 3.
    [Fact]
    public void ACacheMissingALevelFolderIsIncomplete()
    {
        Build(store, ShaA, Big);
        Directory.Delete(Path.Combine(store.Folder(ShaA), "L1"), recursive: true);
        Assert.False(store.IsComplete(ShaA, Big));
    }

    [Fact]
    public void AManifestForAnotherSizeIsIncomplete()
    {
        Build(store, ShaA, Big);
        Assert.False(store.IsComplete(ShaA, new MapPyramid(Big.Width + 1, Big.Height)));
    }

    // Review Focus 4.
    [Fact]
    public void PruneKeepsAHashAnotherGuideStillUses()
    {
        Build(store, ShaA, Big);
        Build(store, ShaB, Big);
        store.Prune(new HashSet<string> { ShaA });
        Assert.True(store.IsComplete(ShaA, Big));
        Assert.False(Directory.Exists(store.Folder(ShaB)));
    }

    [Fact]
    public void TrimDropsTheLeastRecentlyOpenedButNeverAKeptOne()
    {
        MapTileStore small = new(root, maxBytes: 2 * OneCacheBytes);
        Build(small, ShaA, Big, fileBytes: 1024);
        Build(small, ShaB, Big, fileBytes: 1024);
        Build(small, ShaC, Big, fileBytes: 1024);
        Backdate(small, ShaA, hours: 3); Backdate(small, ShaB, hours: 2); Backdate(small, ShaC, hours: 1);
        small.Trim(new HashSet<string> { ShaA });
        Assert.True(small.IsComplete(ShaA, Big));
        Assert.False(Directory.Exists(small.Folder(ShaB)));
        Assert.True(small.IsComplete(ShaC, Big));
    }

    [Fact]
    public void ABadHashBuildsNoPath() =>
        Assert.Throws<ArgumentException>(() => store.Folder("..\\..\\x"));

    // SqliteLibraryRepositoryTests
    [Fact]
    public async Task MapHashesAreThoseOfImageGuidesOnly()
    {
        // A TXT guide and two maps, one of them in a second game with the same bytes.
        // Assert: the set is exactly the maps' one hash.
    }
```

  `Big` is `new MapPyramid(5000, 3000)`. `OneCacheBytes` is what `Build`
  writes for one cache with `fileBytes` per file. Write the repository
  test's body with the existing helpers that add a game and a map.
- [ ] **Step 2: Run them.** `t DesktopGuides.Infrastructure.Tests "FullyQualifiedName~MapTileStore|FullyQualifiedName~MapHashes"`.
  Expected: compile errors.
- [ ] **Step 3: Implement.** A folder's size is the sum of its files'
  lengths. The `Directory.Move` into place happens only after the
  manifest is written, so a crash leaves only a `.building` folder.
- [ ] **Step 4: Run them, then the whole Infrastructure suite.** Expected: all pass.
- [ ] **Step 5: Commit.** "feat(p2): the map cache on disk".

### Task 5: Production: building the cache, and when

**Files:**
- Create: `src/DesktopGuides.Production/WicMapTileBuilder.cs`, `src/DesktopGuides.Production/MapCacheQueue.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (create the store and queue beside `cacheRoot`; prune at startup, after a guide removal, a game removal and a restore; queue after the Ctrl+O publish of a map)
- Modify: `src/DesktopGuides.Production/ShellWindow.ImportMaps.cs` (queue every map the dialog imported)

**Interfaces:**
- Consumes: `MapPyramid`, `MapPyramidBuilder`, `MapTileKey` (Task 3); `MapTileStore`, `MapTileBuild`, `ListMapHashesAsync` (Task 4).
- Produces:
  - `internal static class WicMapTileBuilder`:
    - `Task WriteOverviewAsync(string sourcePath, MapPyramid pyramid, string overviewPath, CancellationToken token)`
      makes one decode scaled to `OverviewSize` with Fant interpolation
      and writes it as PNG;
    - `Task WriteTilesAsync(string sourcePath, MapPyramid pyramid, Func<MapTileKey, string> tilePath, CancellationToken token)`
      decodes level 0 in bands of `TileSize` rows through
      `BitmapTransform.Bounds`, feeds `MapPyramidBuilder`, and writes
      each tile as PNG with `BitmapEncoder`.
  - `internal sealed class MapCacheQueue(MapTileStore store)`:
    - `MapCacheBuild Ensure(Guide map, string sourcePath, bool front)`.
      It returns the in-flight or queued build for that hash, or starts
      one, moving it to the front when `front`.
    - `MapCacheBuild` has `Task Overview` and `Task Complete`. Both are
      already complete when the cache is complete.
    - `void CancelAll()` runs at window close.
    - After each build: `store.Trim(keep: {that hash, the open map's hash})`.
    - Only large maps (`!new MapPyramid(w, h).Small`) are queued. `Ensure`
      for a small map throws `ArgumentException`.
- A failed build is logged to the diagnostics status like a PDF render
  failure, its folder is disposed, and its tasks fault. Opening the map
  then shows PR a's load-failure message.

- [ ] **Step 1: Wire it, then build.** This task's logic is under the Core
  and Infrastructure tests. Its own check is the Release build here and
  the installed `maps-reader` run (Task 8), which builds a 15456 × 10304
  cache and asserts it. `build` → expected 0 warnings, 0 errors.
- [ ] **Step 2: Measure on the host.** Add a test-gated build timing
  (`MapCacheBuildMs`) to the diagnostics status file the queue writes
  (`<cacheRoot>/diagnostics/map-cache.json`:
  `{"builds":[{"sha":…,"overviewMs":…,"tilesMs":…,"tiles":…,"bytes":…}]}`),
  behind `TestGate` `"MapDiagnostics"`. The numbers go into the design's
  record (Task 9) as the decoder and tile-size choice the design asked
  the builder to record.
- [ ] **Step 3: Commit.** "feat(p2): build a map's levels and tiles in the background".

### Task 6: Production: the image reader

**Files:**
- Rewrite: `src/DesktopGuides.Production/ImageReaderSession.cs`
- Create: `src/DesktopGuides.Core/Maps/MapSessionDiagnostics.cs` (record, `CoreJsonContext` entry)
- Modify: `src/DesktopGuides.Production/ReaderToolbar.xaml` and `.xaml.cs` (a primary **Fit map** button, `FitMap`, shown by `FitView`; Ctrl+0 picks it when visible; `ExecuteAsync(new FitViewAction(), "fit map")`)
- Modify: `src/DesktopGuides.Core/CoreJsonContext.cs` (or wherever `PdfSessionDiagnostics` is registered)
- Test: `tests/DesktopGuides.Core.Tests/MapSessionDiagnosticsTests.cs` (JSON shape)

**Interfaces:**
- Consumes: `MapViewport` (Task 2); `MapPyramid`, `MapTileKey`, `MapTileMemory` (Task 3); `MapTileStore` (Task 4); `MapCacheQueue` (Task 5); `ImagePosition`, `FitViewAction` (Task 1).
- Produces:
  - `ImageReaderSession(Guide guide, MapTileStore store, MapCacheQueue queue, bool diagnostics)`:
    - `Capabilities = Zoom | FitView`;
    - `event EventHandler? ZoomChanged`, with `CanZoomIn` and `CanZoomOut`
      for the shell to pass to `SetZoomAvailability`;
    - `bool Preparing`, true while waiting for the overview, and
      `event EventHandler? Prepared`.
  - `public sealed record MapSessionDiagnostics(long OpenMs, bool FromCache, bool Small, bool TilesReady, int Level, int TilesShown, int MaxSurfaceSide, long PeakTileBytes, long MaxTileBytes, double Zoom, double Fit, double CenterX, double CenterY)`.
    It holds no paths and no titles. `ToJson()` follows `PdfSessionDiagnostics`.
- **The view:**
  - A `Grid` holds a `Canvas`. The overview `Image` (or, for a small map,
    the whole image) is scaled by a `CompositeTransform` from the view,
    and tile `Image`s are placed at `(col, row) × tileSize × 2^k`, scaled
    likewise.
  - The view keeps `AutomationId` `MapImageView` and the map's title as
    its name. It is focusable (`IsTabStop`, `TabFocusNavigation`) so the
    arrow keys reach it.
  - `SizeChanged` and `XamlRoot.Changed` (for the rasterization scale)
    `Resize` the viewport.
- **Tiles:**
  - On each view change, the visible tiles at `Level(pyramid.Levels)` are
    requested.
  - Those in `MapTileMemory` are drawn; the rest load off the UI thread
    (a `BitmapImage` from the tile file), latest request wins (as PDF's
    `LatestWinsScheduler`), and are drawn when they arrive.
  - Tiles not visible are removed from the canvas; memory keeps them up
    to `MaxBytes`.
  - Before the cache is complete, only the overview is drawn.
  - A tile file that fails to load leaves the overview showing.
- **Input:**
  - the pointer drag, the arrow keys, the wheel and Ctrl+wheel, as in
    Global Constraints;
  - `ExecuteAsync`: `ZoomAction` with a factor above 1 is `ZoomIn()`,
    below 1 is `ZoomOut()`, and `FitViewAction` is `FitToView()`;
  - each change raises `ZoomChanged` when `CanZoomIn` or `CanZoomOut`
    changed, and `LocationChanged` as Global Constraints say.
- **The place:**
  - `GetLocationAsync` returns
    `ReaderLocation(GuideFormat.Image, 1, guide.ContentSha256, viewport.ToPosition(), null)`;
  - `RestoreLocationAsync` takes an `ImagePosition`, applies
    `MapViewport.FromPosition` and returns `Exact`. Any other payload
    returns `Unavailable` and fits.
- **Diagnostics:** when on, the session writes
  `<cacheRoot>/diagnostics/map-session-<pid>.json` after it opens and
  after each settled change. `MaxSurfaceSide` is the largest pixel side
  of any bitmap it has decoded, and `OpenMs` runs from `OpenAsync` to the
  first frame with the overview (or the small image) drawn.

- [ ] **Step 1: Write the failing test.**

```csharp
    [Fact]
    public void MapDiagnosticsKeepTheirFieldNames()
    {
        string json = new MapSessionDiagnostics(420, true, false, true, 1, 12, 2048, 12_582_912, 100_663_296, 0.25, 0.09, 7728, 5152).ToJson();
        Assert.Contains("\"OpenMs\":420", json);
        Assert.Contains("\"MaxSurfaceSide\":2048", json);
        Assert.Contains("\"CenterY\":5152", json);
    }
```

- [ ] **Step 2: Run it.** Expected: compile error for `MapSessionDiagnostics`.
- [ ] **Step 3: Implement the record, then the session and the toolbar.**
  Remove PR a's `DecodeWidth` helper. A small map now decodes at its own
  size, which is at most 4096.
- [ ] **Step 4: Run Core, then `build`.** Expected: all pass; 0 warnings, 0 errors.
- [ ] **Step 5: Commit.** "feat(p2): the map reader: zoom, pan, levels and tiles".

### Task 7: The shell: opening at the saved place, saving, and no reading activity

**Files:**
- Modify: `src/DesktopGuides.Production/ShellWindow.MapReader.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.Progress.cs` (`OpenAtSavedPlaceAsync` gains `bool quiet = false` and `bool recordOpen = true`)

**Interfaces:**
- Consumes: `ImageReaderSession` (Task 6); `Track(..., recordOpen)` (Task 1).
- The flow:
  1. `OpenMapAsync` builds the session with the store and queue.
  2. While `Preparing`, the reader surface shows its loading state with
     "Preparing the map…" (the same busy row TXT and PDF use).
  3. On `Prepared` the view shows.
  4. `ReaderActions.SetSession(session)` follows, then
     `SetZoomAvailability`, kept current from `ZoomChanged`.
  5. `OpenAtSavedPlaceAsync(guide, session, generation, guide.ContentSha256, null, token, quiet: true, recordOpen: false)`.
- `quiet` skips `ShowRestoreStatus`, so a map shows no notice. A baseline
  is captured after any outcome but `Exact`, as today.
- `StartContents` already does nothing for a session without `Outline`.
  Confirm that, and add a guard if it doesn't.
- Back, a guide switch and close flush and dispose `progressTracking`
  through the existing reader-close path. Confirm the map path reaches
  it.
- PR a's checks stay: no completion control, Resume untouched, and
  routing that names TXT.

- [ ] **Step 1: Implement and build.** `build` → 0 warnings, 0 errors.
- [ ] **Step 2: Run Core and Infrastructure.** Expected: all pass.
- [ ] **Step 3: Commit.** "feat(p2): a map reopens where it was left".

### Task 8: The fixtures and the installed `maps-reader` mode

**Files:**
- Modify: `tools/p0/make_fixtures.py` (`generated/maps/big-map.png`, 15456 × 10304) and `tests/fixtures/p0/manifest.json`
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`seed-maps-reader <dataRoot> <fixtureRoot>`, `describe-maps <dataRoot> <cacheRoot>`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (the `maps-reader` mode)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-MapReaderScenario` in the `navigation` group, with the `MapDiagnostics` and `ProgressDiagnostics` gates open)

**Interfaces:**
- `big-map.png`:
  - RGB, 15456 × 10304, written row by row with `zlib.compressobj(1)`;
  - drawn from a few precomputed rows: 512-pixel cells in 6 colours by
    `(column + row) % 6`, with a dark line every 64 pixels;
  - it must take under 30 s to generate;
  - the manifest records its size and SHA-256, as for PR a's maps.
- `seed-maps-reader`:
  - needs an empty library;
  - adds "Map Reader Game" with a TXT guide "Walkthrough" (from
    `txt-ascii.txt`) and the maps `castle-floor-1.png` ("castle floor 1",
    small), `strip-map.png` ("strip map", 20000 × 400) and `big-map.png`
    ("big map");
  - publishes them through the validator and publisher, as
    `seed-pdf-reader` does;
  - prints `{walkthrough, castle, strip, big, bigSha, stripSha}`.
- `describe-maps` prints, for each map by title:
  - `HasPlace`, `Zoom` (null when fitted or no place) and `Estimate`;
  - `LastOpened` (`LastOpenedUtcMs` non-null);
  - `CacheComplete` (`MapTileStore.IsComplete`);

  and, for the library, `LastActiveTitle` and `CacheFolders` (the count
  under `<cacheRoot>/maps`).
- The `maps-reader` smoke steps, each with the mode's own `throw` on a
  mismatch:
  1. Open "Walkthrough", then Back. Its Resume is now set.
  2. Open "big map". Wait up to 120 s for the diagnostics file with
     `FromCache` either way. Assert:
     - the toolbar shows `ZoomOut`, `FitMap` and `ZoomIn`, and no
       `CompletionChoice`, `LargerText` or `ShowContents`;
     - `ZoomOut` is disabled.
  3. Wait up to 180 s for the diagnostics' `TilesReady`. Then press Ctrl+Plus three times, Right twice, and drag
     the view 200 px left. Wait 1.5 s. Assert from diagnostics:
     - `Zoom` = `Fit × 1.25³` (to 1e-6, or `MaxZoom`);
     - `MaxSurfaceSide <= 4096`;
     - `PeakTileBytes <= MaxTileBytes`;
     - `TilesShown > 0`.

     Save the screenshot `maps-reader-zoomed`.
  4. Back, then open "big map" again. Assert:
     - `FromCache` is true;
     - `OpenMs <= 1000`;
     - `Zoom`, `CenterX` and `CenterY` equal step 3's (to 1e-6).
  5. Ctrl+0. Assert `Zoom == Fit`. Wait 1.5 s, then Back.
  6. Open "strip map", wait for its overview, and zoom in once. Assert
     `MaxSurfaceSide <= 4096` and that the view is shown. Save
     `maps-reader-strip`. Back.
  7. Remove "strip map" through the Maps section.
- `Run-MapReaderScenario`, after the smoke, runs `describe-maps` and
  asserts:
  - "big map": `HasPlace` true, `Zoom` null (fitted at step 5),
    `Estimate` null, `LastOpened` false, `CacheComplete` true;
  - "castle floor 1": `HasPlace` false;
  - every map's `LastOpened` is false;
  - `LastActiveTitle` is "Walkthrough";
  - `CacheFolders` is 1 (the strip's cache was pruned with its map; the
    small castle never had one).

- [ ] **Step 1: Write the fixture, the seed and describe commands, the mode
  and the scenario.** Parse both scripts on the host, and build the
  shell seed.
- [ ] **Step 2: Dispatch the `navigation` scope** on both lanes. Expected:
  `maps-reader` passes, and every existing navigation mode still passes.
  Debug any failure with systematic-debugging, and pin a code fault with
  a test that fails first.
- [ ] **Step 3: Copy the screenshots** `maps-reader-zoomed.png` and
  `maps-reader-strip.png` from the MSIX artifact into
  `docs/p2/evidence/ci/t21-2/`, and look at each.
- [ ] **Step 4: Commit.** "test(p2): the installed maps-reader mode".

### Task 9: Records, the final review, the PR run and the PR

**Files:**
- Modify: `docs/p2/t21-2-image-maps-design.md`:
  - Status;
  - under "Large images", the decoder and tile-size choice with Task 5's
    timings;
  - a "PR b verification record".
- Modify: `docs/p2/results.md` (#136's row)
- Modify: `docs/p2/implementation-plan.md` and `docs/progress.md`

- [ ] **Step 1: The full suites and the build** on the host: Core,
  Infrastructure, the Production build with 0 warnings, and the script
  parse check.
- [ ] **Step 2: The final review.** Run the whole-branch review first.
  Its fixes land before the records.
- [ ] **Step 3: The records.**
  - **The design's Status:** add "PR a merged (#136). PR b (the reader)
    in review; see the PR b verification record."
  - **The decoder note:**
    - "WinRT `BitmapDecoder`: one Fant-scaled decode for the overview,
      then 512-row bands at full size through `BitmapTransform.Bounds`
      into a 2×2 box pyramid of 512-pixel PNG tiles";
    - the overview and tile build times for `big-map.png` on the CI
      host.
  - **The "PR b verification record":** the tests, the `navigation`
    dispatches on both lanes, the screenshots, the review, and every
    ruling, including this plan's deviations.
  - **`results.md`:** #136's row: 2026-10-19, `7faddc7`, T21.2 maps
    (PR a), its PR run 39184327759, Core 1444, Infra 901, "Import maps
    and the Maps section. Three faults found by the installed runs, three
    by the final review, all fixed before merge." Check the counts
    against the run before writing them.
  - **The status pages:** "Image maps PR a merged (#136); PR b (the
    reader) in review." The `progress.md` "Next" becomes "T21.2 image
    maps PR c (the Maps pop-up)".
- [ ] **Step 4: Commit, push, open the PR, and wait for its run.**
  - **Commit:** "docs(p2): record image maps PR b".
  - **The PR:** "T21.2 image maps PR b: zoom, pan and the view place",
    based on `main`. It names:
    - the task;
    - the prerequisites: multi-page HTML (#133–#135) and image maps PR a
      (#136), all merged;
    - the outcome.

    It includes the screenshots, the dispatches, every ruling and the
    deferred minors.
  - **Expected:** every job passes. Record the run in the PR, then stop
    for the user's merge decision.

## Deviations from the design

1. **The first open of a map with no cache** shows "Preparing the map…"
   until its overview is built. With a cache, which an import starts
   building, the map shows within 1 s. The design's 1 s rests on the
   preview copy, which PR c names. Here the cache's overview is that
   copy, and PR c's pop-up will reuse it.
2. **A map's capabilities are `Zoom | FitView`,** not `Zoom | Scroll`.
   The map view handles its own arrow keys and wheel, and
   `ReaderCommand.Scroll` has no toolbar command. **Fit map** is a new
   capability, so PDF's "Fit to width" keeps its meaning.
3. **Maps up to 4096 on the longer side** open from their file as one
   surface, with no levels, tiles or cache. They are already within the
   surface limit.
4. **A fitted place stores a null zoom,** so a map left fitted reopens
   fitted at any window size.
5. **Restoring a map shows no notice,** whether the place restored or the
   map changed. The design says only that it fits.
6. **The installed mode runs in the `navigation` group,** like the
   multi-page reader modes. It removes a map to check the cache goes with
   it.

## Traceability

| Design item | Task |
| --- | --- |
| Fitted open; drag and the arrow keys pan | 2, 6 |
| Ctrl+wheel, Ctrl+Plus and Ctrl+Minus zoom about the pointer or centre, fit to 4×; Ctrl+0 fits | 2, 6 |
| Toolbar: Zoom out, Fit and Zoom in | 1, 6 |
| Downsampled levels; full-detail tiles; no surface over 4096 | 3, 5, 6 |
| A memory budget like PDF's; off-screen tiles dropped first | 3, 6 |
| A disk cache: 2 GiB, least recently used first, removed with its map, not backed up | 4, 5 |
| Opening within 1 s; detail fills in | 5, 6, 8 |
| `ImagePosition(Zoom, CenterX, CenterY)`, checked against the hash | 1 |
| Saved on close and after the view settles; never progress | 1, 7 |
| Not reading activity: Resume and recent order unchanged | 1, 7, 8 |
| `maps-reader`: open, zoom, pan, reopen; over 16384 wide | 8 |
