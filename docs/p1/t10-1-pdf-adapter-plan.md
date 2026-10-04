# T10.1 PDF Reader Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the PDF Reader placeholder with a `Windows.Data.Pdf` page
preview beside the same page's PdfPig text in a read-only `TextBox`. Memory
stays bounded on long documents and under rapid page turns (TR10.2, TR10.3).

**Architecture:** Pure rules go in `DesktopGuides.Core/Pdf/`:
- the render cache;
- the raster budget;
- the latest-wins scheduler;
- the error messages.

Managed-copy checks and PdfPig text extraction go in
`DesktopGuides.Infrastructure/Reading/`. Only `Windows.Data.Pdf`, the XAML
view, the session and the shell routing go in `DesktopGuides.Production`,
which has no unit test host. The installed `production-shell-ui` smoke covers
it instead.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), `Windows.Data.Pdf`, PdfPig
0.1.16 (already pinned), xUnit, PowerShell 5.1 UI Automation smoke.

**Spec:** [t10-1-pdf-adapter-design.md](t10-1-pdf-adapter-design.md)

## Global Constraints

**Tooling:**
- There is no local `dotnet` or `pwsh` on the authoring Mac. Every "Run" step
  is a CI run:
  1. Trigger it with `gh workflow run windows-ci.yml --ref feat/p1-t10-1-pdf-adapter`.
  2. Get its ID with `gh run list --workflow windows-ci.yml --branch feat/p1-t10-1-pdf-adapter --limit 1 --json databaseId -q '.[0].databaseId'`.
  3. Watch it with `gh run watch <id> --exit-status --interval 60`.
  4. If it fails, read `gh run view <id> --log-failed`.
- The `core-tests` job runs Core.Tests and Infrastructure.Tests. The
  `production-shell-ui` job is the gate for Production and the smoke.
- To save CI time, a RED run may be batched with the previous task's GREEN
  run: push the failing test together with the previous task's
  implementation. Every test must still be seen failing once before its code
  lands.

**Target frameworks:** Core and Infrastructure are `net10.0`. Production is
`net10.0-windows10.0.19041.0`.

**Limits:**
- The render cap `MaxBytes` is **100,663,296** (96 MiB).
- Raster widths are multiples of **64**, clamped to **64–4096**.
- Text limits: `MaxPageCharacters` **1,048,576**, `MaxPages` **8**,
  `MaxCharacters` **4,194,304**.
- The resize debounce is **150 ms**. **Three** consecutive pages failing both
  ways raise `Failed`. The narrow layout applies below **720** effective pixels.

**Copy (verbatim):**
- Errors:
  - Missing: "This guide's file is missing from the library."
  - Changed: "This guide's files have changed. Re-import it to read it."
  - Unreadable: "This guide's file can't be opened. Close any app that's using it, then open the guide again."
  - Damaged: "This PDF is damaged, so it can't be opened. Re-import it from the original file."
  - PasswordProtected: "This PDF now needs a password, which isn't supported. Remove the password and re-import it."
  - Failed: "This guide stopped responding."
- Text status:
  - empty for normal text;
  - "Image-only page; OCR is unavailable";
  - "This page's text couldn't be read.";
  - "Page text is shortened; it's too long to show in full."
- Preview status: "This page's preview couldn't be shown."
- Names:
  - page status: "Page N of M";
  - image: "Page N of M preview";
  - text box: "Page text, page N of M".

**AutomationIds:** `PdfPreviewImage`, `PdfPreviewStatus`, `PdfTextStatus`,
`PdfDocumentText`, `PdfPageStatus`.

**Diagnostics:**
- The test gate is `Local\DesktopGuides.Preview.PdfDiagnostics.<pid>`.
- The file is `<cacheRoot>\diagnostics\pdf-<guideId:N>.json`.
- Its keys are `requests`, `loads`, `staleResults`, `peakCacheBytes`,
  `maxCacheBytes`, `cachedPagesAtClose`, `peakTextPages`,
  `peakTextCharacters` and `disposedCleanly`. Counts only: no text and no
  paths.

**Input and storage rules:**
- Read only the managed copy, with `FileShare.Read`. Never open the original
  source. Write nothing into the library.
- PDF bytes are untrusted. Messages, statuses and diagnostics never contain
  guide text, passwords or absolute paths.

**Code rules:**
- New enum members go at the end.
- PowerShell files stay ASCII-only.
- UI tests assert only what app code controls.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A page turn while the previous page is still rendering.** The text and
   the preview must show the same page, and the page status must match them.
   Pinned by
   `LatestWinsSchedulerTests.ResultOfASupersededLoadIsNotApplied` (Task 3)
   and by the smoke's page-200 agreement check (Task 7).
2. **Dispose while a load is running.** Closing the guide mid-render must
   not apply a late result, throw, or leave the file locked. Pinned by
   `LatestWinsSchedulerTests.CancelAsyncWaitsForTheRunningLoadAndStopsLaterOnes`
   (Task 3) and by
   `PdfPageTextSourceTests.DisposeDuringAnExtractionWaitsForIt` (Task 5).
3. **A page with an extreme aspect ratio** (a long strip map) must not
   allocate past the cap or crash. Its preview fails on that page only.
   Pinned by `PdfRasterBudgetTests.ExtremeAspectRatioIsTooLarge` (Task 2).
4. **A managed copy replaced by an encrypted or garbage PDF.** The guide
   must show a typed error, never a crash. Pinned by the
   `ManagedPdfGuideLoaderTests` Damaged and PasswordProtected cases
   (Task 6).
5. **A restore location for another guide version or format.** It must
   return `Unavailable` and leave the page alone. Pinned by
   `PdfLocationRulesTests` (Task 4).

## File Map

| File | Responsibility |
| --- | --- |
| `src/DesktopGuides.Core/Pdf/PdfRenderCache.cs` | LRU of rendered images by measured bytes |
| `src/DesktopGuides.Core/Pdf/PdfRasterBudget.cs` | Raster width from display width, aspect ratio and cap |
| `src/DesktopGuides.Core/Pdf/LatestWinsScheduler.cs` | One load at a time; superseded results dropped |
| `src/DesktopGuides.Core/Pdf/PdfGuideLoadMessages.cs` | Error enum, copy and actions |
| `src/DesktopGuides.Core/Pdf/PdfLocationRules.cs` | Capture and restore rules for `PdfPosition` |
| `src/DesktopGuides.Core/Pdf/PdfSessionDiagnostics.cs` | Test-only counts, written as JSON |
| `src/DesktopGuides.Infrastructure/Import/CancellableReadStream.cs` | Extracted from `GuideImportValidator`, with a settable token |
| `src/DesktopGuides.Infrastructure/Reading/PdfPageTextSource.cs` | PdfPig page text with bounded LRU |
| `src/DesktopGuides.Infrastructure/Reading/ManagedPdfGuideLoader.cs` | Managed-copy checks and PdfPig open |
| `src/DesktopGuides.Production/PdfReaderView.xaml(.cs)` | Side-by-side view |
| `src/DesktopGuides.Production/PdfReaderSession.cs` | `IReaderSession` over both engines |
| `src/DesktopGuides.Production/ShellWindow.PdfReader.cs` | Open, error and failure handling in the shell |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | `seed-pdf-reader` mode |
| `tools/p1/windows_shell_install.ps1` | `Run-PdfReaderScenarios` |
| `tools/p1/windows_shell_ui_smoke.ps1` | `pdf-reader` mode |

Rulings already made against the spec:

- **R1.** `PdfGuideLoadMessages.ActionFor` returns the existing
  `HtmlGuideLoadAction` (`None` or `Reopen`). `ActionLabel` delegates to
  `HtmlGuideLoadMessages.ActionLabel`. Reason: the shell's error surface
  (`ShowReaderSurface(..., HtmlGuideLoadAction action)` and
  `readerErrorAction`) is already typed to it. This keeps the spec's
  "same shape" without a second action enum. If wrong, it costs one enum
  and a shell parameter rename.
- **R2.** The spec says `RestoreLocationAsync` decodes through
  `ReaderLocationCodec`. `IReaderSession` already receives a decoded
  `ReaderLocation`, so the session checks it instead:
  - format `Pdf`;
  - a `PdfPosition` payload;
  - a matching `ContentSha256`;
  - a finite `PageFraction`.
  Any mismatch is `Unavailable`. The rule lives in Core
  (`PdfLocationRules`) so it is unit-tested.
- **R3.** `CancellableReadStream` gets a settable `Token`. Each text
  extraction then honors its own token, because PdfPig reads the stream
  lazily after `Open`. Import keeps its constructor call unchanged.
- **R4.** The loader reuses the import's `%PDF` header check, made
  `internal`, before calling PdfPig. Bytes without a header are therefore
  `Damaged` whatever PdfPig's leniency.
- **R5.** Diagnostics move from an ad hoc JSON in Production to a Core
  `PdfSessionDiagnostics.ToJson`, as HTML does, so the JSON keys are
  unit-tested.
- **R6.** The narrow layout is switched in code-behind (`Grid.SetRow`,
  `Grid.SetColumn` and the column and row sizes) rather than with a
  `VisualStateManager` group. No view in the repo uses visual states, and a
  plain width check is easier to follow. The behavior is what the spec
  asks for. If wrong, it costs one XAML state group.
- **R7.** Production has no unit test host, so its failing test is the
  installed smoke. Task 7 writes the seed and the `pdf-reader` smoke first
  and watches CI fail on the PDF placeholder. Task 8 writes the view, the
  session and the shell routing and turns it green. Task 9 is docs and
  evidence.

---

### Task 1: `PdfRenderCache<TImage>`

**Files:**
- Create: `src/DesktopGuides.Core/Pdf/PdfRenderCache.cs`
- Test: `tests/DesktopGuides.Core.Tests/PdfRenderCacheTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct PdfRenderKey(string Fingerprint, int PageIndex, int PixelWidth)`;
  - `sealed class PdfRenderCache<TImage>(long maxBytes) where TImage : class`;
  - `long MaxBytes`, `long CachedBytes`, `long PeakBytes`, `int Count`;
  - `bool TryGet(PdfRenderKey key, out TImage? image)`;
  - `void Add(PdfRenderKey key, TImage image, int pixelWidth, int pixelHeight)`;
  - `void Clear()`;
  - `static long MeasureBytes(int pixelWidth, int pixelHeight)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfRenderCacheTests
{
    private sealed record FakeImage(int Page);

    private static PdfRenderKey Key(int page, int width = 10, string fingerprint = "abc") =>
        new(fingerprint, page, width);

    // 10 x 10 x 4 = 400 bytes per entry; a 1,000-byte cap holds two.
    private static PdfRenderCache<FakeImage> Cache() => new(1000);

    [Fact]
    public void MeasuresFourBytesPerPixel() =>
        Assert.Equal(4096L * 5301 * 4, PdfRenderCache<FakeImage>.MeasureBytes(4096, 5301));

    [Fact]
    public void EvictsTheLeastRecentlyUsedEntryOnceOverTheCap()
    {
        PdfRenderCache<FakeImage> cache = Cache();
        cache.Add(Key(0), new FakeImage(0), 10, 10);
        cache.Add(Key(1), new FakeImage(1), 10, 10);
        cache.Add(Key(2), new FakeImage(2), 10, 10);

        Assert.False(cache.TryGet(Key(0), out _));
        Assert.True(cache.TryGet(Key(1), out _));
        Assert.True(cache.TryGet(Key(2), out _));
        Assert.Equal(800, cache.CachedBytes);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void AHitBecomesMostRecentAndSurvivesTheNextEviction()
    {
        PdfRenderCache<FakeImage> cache = Cache();
        cache.Add(Key(0), new FakeImage(0), 10, 10);
        cache.Add(Key(1), new FakeImage(1), 10, 10);
        Assert.True(cache.TryGet(Key(0), out FakeImage? hit));
        Assert.Equal(0, hit!.Page);

        cache.Add(Key(2), new FakeImage(2), 10, 10);

        Assert.True(cache.TryGet(Key(0), out _));
        Assert.False(cache.TryGet(Key(1), out _));
    }

    [Fact]
    public void AnEntryBiggerThanWhatRemainsEvictsOthersAndStays()
    {
        PdfRenderCache<FakeImage> cache = Cache();
        cache.Add(Key(0), new FakeImage(0), 10, 10);
        cache.Add(Key(1), new FakeImage(1), 10, 10);

        cache.Add(Key(2), new FakeImage(2), 10, 24); // 960 bytes

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(Key(2), out _));
        Assert.Equal(960, cache.CachedBytes);
    }

    [Fact]
    public void AnEntryBiggerThanTheCapIsRejected()
    {
        PdfRenderCache<FakeImage> cache = Cache();

        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Add(Key(0), new FakeImage(0), 10, 26));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void PeakBytesNeverExceedsTheCap()
    {
        PdfRenderCache<FakeImage> cache = Cache();
        for (int page = 0; page < 20; page++)
        {
            cache.Add(Key(page), new FakeImage(page), 10, 10 + page % 3);
        }

        Assert.True(cache.PeakBytes <= cache.MaxBytes);
        Assert.True(cache.PeakBytes >= cache.CachedBytes);
    }

    [Fact]
    public void KeysThatDifferOnlyByWidthOrFingerprintDoNotCollide()
    {
        PdfRenderCache<FakeImage> cache = new(10_000);
        cache.Add(Key(0, 10, "abc"), new FakeImage(1), 10, 10);
        cache.Add(Key(0, 20, "abc"), new FakeImage(2), 20, 10);
        cache.Add(Key(0, 10, "def"), new FakeImage(3), 10, 10);

        Assert.Equal(3, cache.Count);
        Assert.True(cache.TryGet(Key(0, 20, "abc"), out FakeImage? wide));
        Assert.Equal(2, wide!.Page);
        Assert.True(cache.TryGet(Key(0, 10, "def"), out FakeImage? other));
        Assert.Equal(3, other!.Page);
    }

    [Fact]
    public void AddingAnExistingKeyReplacesItsBytes()
    {
        PdfRenderCache<FakeImage> cache = Cache();
        cache.Add(Key(0), new FakeImage(0), 10, 10);
        cache.Add(Key(0), new FakeImage(9), 10, 20);

        Assert.Equal(1, cache.Count);
        Assert.Equal(800, cache.CachedBytes);
        Assert.True(cache.TryGet(Key(0), out FakeImage? image));
        Assert.Equal(9, image!.Page);
    }

    [Fact]
    public void ClearLeavesNoBytesAndNoEntries()
    {
        PdfRenderCache<FakeImage> cache = Cache();
        cache.Add(Key(0), new FakeImage(0), 10, 10);

        cache.Clear();

        Assert.Equal(0, cache.CachedBytes);
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(Key(0), out _));
    }

    [Fact]
    public void ThreeSweepsOfALongDocumentStayWithinTheCap()
    {
        // A US Letter page rendered at 1,024 pixels is 1,024 x 1,326.
        PdfRenderCache<FakeImage> cache = new(100_663_296);
        int maxCount = 0;
        for (int sweep = 0; sweep < 3; sweep++)
        {
            for (int page = 0; page < 200; page++)
            {
                if (!cache.TryGet(Key(page, 1024), out _))
                {
                    cache.Add(Key(page, 1024), new FakeImage(page), 1024, 1326);
                }
                maxCount = Math.Max(maxCount, cache.Count);
            }
        }

        Assert.True(cache.PeakBytes <= 100_663_296);
        Assert.True(maxCount < 200);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCapIsRejected(long maxBytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfRenderCache<FakeImage>(maxBytes));
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Commit the test file alone, then trigger CI.
Expected: `core-tests` fails to compile with
`The type or namespace name 'Pdf' does not exist in the namespace 'DesktopGuides.Core'`.

- [ ] **Step 3: Write the implementation**

```csharp
namespace DesktopGuides.Core.Pdf;

public readonly record struct PdfRenderKey(string Fingerprint, int PageIndex, int PixelWidth);

/// <summary>
/// Rendered page images, least recently used first out, bounded by the
/// bytes their decoded pixels take. Used from the UI thread only.
/// </summary>
public sealed class PdfRenderCache<TImage> where TImage : class
{
    private readonly LinkedList<Entry> order = new();
    private readonly Dictionary<PdfRenderKey, LinkedListNode<Entry>> entries = [];

    public PdfRenderCache(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; }
    public long CachedBytes { get; private set; }
    // The highest total after eviction, so never above MaxBytes.
    public long PeakBytes { get; private set; }
    public int Count => entries.Count;

    public static long MeasureBytes(int pixelWidth, int pixelHeight) => (long)pixelWidth * pixelHeight * 4;

    public bool TryGet(PdfRenderKey key, out TImage? image)
    {
        if (!entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            image = null;
            return false;
        }
        order.Remove(node);
        order.AddFirst(node);
        image = node.Value.Image;
        return true;
    }

    // The new entry always stays; the raster budget keeps it under the cap.
    public void Add(PdfRenderKey key, TImage image, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        long bytes = MeasureBytes(pixelWidth, pixelHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes, MaxBytes);
        if (entries.Remove(key, out LinkedListNode<Entry>? existing))
        {
            order.Remove(existing);
            CachedBytes -= existing.Value.Bytes;
        }
        entries[key] = order.AddFirst(new Entry(key, image, bytes));
        CachedBytes += bytes;
        while (CachedBytes > MaxBytes)
        {
            LinkedListNode<Entry> oldest = order.Last!;
            order.RemoveLast();
            entries.Remove(oldest.Value.Key);
            CachedBytes -= oldest.Value.Bytes;
        }
        PeakBytes = Math.Max(PeakBytes, CachedBytes);
    }

    public void Clear()
    {
        order.Clear();
        entries.Clear();
        CachedBytes = 0;
    }

    private sealed record Entry(PdfRenderKey Key, TImage Image, long Bytes);
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Trigger CI. Expected: `core-tests` is green, and all `PdfRenderCacheTests`
pass.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Pdf/PdfRenderCache.cs tests/DesktopGuides.Core.Tests/PdfRenderCacheTests.cs
git commit -m "feat(p1): T10.1 PDF render cache bounded by measured bytes"
```

### Task 2: `PdfRasterBudget`

**Files:**
- Create: `src/DesktopGuides.Core/Pdf/PdfRasterBudget.cs`
- Test: `tests/DesktopGuides.Core.Tests/PdfRasterBudgetTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct PdfRasterWidth(int Width)`, with `bool IsTooLarge`
    and `static PdfRasterWidth PageTooLarge`;
  - `static class PdfRasterBudget`, with
    `const long MaxBytes = 100_663_296` and
    `static PdfRasterWidth WidthFor(double displayPixels, double aspectRatio, long maxBytes)`.
- `aspectRatio` is height / width.

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfRasterBudgetTests
{
    private const double Letter = 792.0 / 612.0;

    [Theory]
    [InlineData(1000, 1024)]
    [InlineData(1024, 1024)]
    [InlineData(1025, 1088)]
    [InlineData(65, 128)]
    [InlineData(1, 64)]
    [InlineData(0.5, 64)]
    [InlineData(10000, 4096)]
    public void RoundsUpToMultiplesOf64WithinTheClamp(double display, int expected) =>
        Assert.Equal(expected, PdfRasterBudget.WidthFor(display, Letter, PdfRasterBudget.MaxBytes).Width);

    [Fact]
    public void TallPagesAreHalvedUntilTheyFit()
    {
        // 4096 x 40960 x 4 and 2048 x 20480 x 4 exceed 96 MiB; 1024 x 10240 x 4 fits.
        Assert.Equal(1024, PdfRasterBudget.WidthFor(4096, 10, PdfRasterBudget.MaxBytes).Width);
    }

    [Fact]
    public void HalvingMayGoBelow64()
    {
        // 64 x 256 x 4 = 65,536 > 16,384; 32 x 128 x 4 = 16,384 fits.
        Assert.Equal(32, PdfRasterBudget.WidthFor(64, 4, 16_384).Width);
    }

    [Fact]
    public void ExtremeAspectRatioIsTooLarge()
    {
        PdfRasterWidth width = PdfRasterBudget.WidthFor(1000, 1e9, PdfRasterBudget.MaxBytes);

        Assert.True(width.IsTooLarge);
        Assert.Equal(PdfRasterWidth.PageTooLarge, width);
    }

    [Theory]
    [InlineData(double.NaN, Letter)]
    [InlineData(double.PositiveInfinity, Letter)]
    [InlineData(0, Letter)]
    [InlineData(-5, Letter)]
    [InlineData(1000, double.NaN)]
    [InlineData(1000, double.PositiveInfinity)]
    [InlineData(1000, 0)]
    [InlineData(1000, -1)]
    public void InvalidInputsAreTooLarge(double display, double ratio) =>
        Assert.True(PdfRasterBudget.WidthFor(display, ratio, PdfRasterBudget.MaxBytes).IsTooLarge);

    [Fact]
    public void NonPositiveCapIsTooLarge() =>
        Assert.True(PdfRasterBudget.WidthFor(1000, Letter, 0).IsTooLarge);

    [Fact]
    public void AFittingWidthIsNeverOverTheCap()
    {
        foreach (double ratio in new[] { 0.01, 0.5, Letter, 3, 25, 400 })
        {
            PdfRasterWidth width = PdfRasterBudget.WidthFor(3000, ratio, PdfRasterBudget.MaxBytes);
            if (width.IsTooLarge) continue;
            Assert.True((double)width.Width * Math.Ceiling(width.Width * ratio) * 4 <= PdfRasterBudget.MaxBytes);
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Trigger CI. Expected: a compile failure, because `PdfRasterBudget` doesn't
exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace DesktopGuides.Core.Pdf;

public readonly record struct PdfRasterWidth(int Width)
{
    public static PdfRasterWidth PageTooLarge => new(0);
    public bool IsTooLarge => Width < 1;
}

public static class PdfRasterBudget
{
    public const long MaxBytes = 100_663_296;
    private const int Step = 64;
    private const int MinWidth = 64;
    private const int MaxWidth = 4096;

    // aspectRatio is page height over width. Widths snap to 64 so small
    // resizes reuse cached images.
    public static PdfRasterWidth WidthFor(double displayPixels, double aspectRatio, long maxBytes)
    {
        if (!double.IsFinite(displayPixels) || displayPixels <= 0 ||
            !double.IsFinite(aspectRatio) || aspectRatio <= 0 || maxBytes <= 0)
        {
            return PdfRasterWidth.PageTooLarge;
        }
        int width = (int)Math.Clamp(Math.Ceiling(displayPixels / Step) * Step, MinWidth, MaxWidth);
        // In doubles, so an extreme ratio can't overflow.
        while ((double)width * Math.Ceiling(width * aspectRatio) * 4 > maxBytes)
        {
            width /= 2;
            if (width < 1) return PdfRasterWidth.PageTooLarge;
        }
        return new PdfRasterWidth(width);
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Trigger CI. Expected: `core-tests` is green.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Pdf/PdfRasterBudget.cs tests/DesktopGuides.Core.Tests/PdfRasterBudgetTests.cs
git commit -m "feat(p1): T10.1 PDF raster width budget"
```

### Task 3: `LatestWinsScheduler<TResult>`

**Files:**
- Create: `src/DesktopGuides.Core/Pdf/LatestWinsScheduler.cs`
- Test: `tests/DesktopGuides.Core.Tests/LatestWinsSchedulerTests.cs`

**Interfaces:**
- Produces: `sealed class LatestWinsScheduler<TResult>`, with:
  - constructor `(Func<int, CancellationToken, Task<TResult>> load, Action<int, TResult> apply, Action<int, Exception> failed)`;
  - `void Request(int pageIndex)` and `Task CancelAsync()`;
  - `int Requests`, `int Loads`, `int StaleResults`.
- Contract: `Request` and `CancelAsync` are called from one thread (the UI
  thread in Production). `apply` and `failed` must not throw. Loads resume
  on the caller's synchronization context, so in Production `apply` runs on
  the UI thread with no interleaving between the "is current" check and
  `apply`.

- [ ] **Step 1: Write the failing tests**

xUnit runs async tests under its own synchronization context, so
continuations may run on another thread. The tests wait for the outcome
with a bounded poll, and each wait fails after 5 seconds.

```csharp
using System.Collections.Concurrent;
using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class LatestWinsSchedulerTests
{
    private sealed class Pages
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> pending = new();

        public Pages(Func<int, CancellationToken, Task<string>>? load = null) =>
            Scheduler = new(load ?? Load, (page, _) => Applied.Enqueue(page), (page, _) => Failed.Enqueue(page));

        public LatestWinsScheduler<string> Scheduler { get; }
        public ConcurrentQueue<int> Started { get; } = new();
        public ConcurrentQueue<int> Applied { get; } = new();
        public ConcurrentQueue<int> Failed { get; } = new();

        public TaskCompletionSource<string> Source(int page) =>
            pending.GetOrAdd(page, _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

        private Task<string> Load(int page, CancellationToken token)
        {
            Started.Enqueue(page);
            TaskCompletionSource<string> source = Source(page);
            token.Register(() => source.TrySetCanceled(token));
            return source.Task;
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The scheduler didn't reach the expected state.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task FiftyRapidRequestsRunTwoLoadsAndApplyTheNewest()
    {
        Pages pages = new();
        for (int page = 0; page < 50; page++)
        {
            pages.Scheduler.Request(page);
        }
        Assert.Equal([0], pages.Started);

        pages.Source(0).SetResult("page 1");
        await Until(() => pages.Started.Count == 2);
        pages.Source(49).SetResult("page 50");
        await Until(() => !pages.Applied.IsEmpty);

        Assert.Equal([0, 49], pages.Started);
        Assert.Equal([49], pages.Applied);
        Assert.Equal(50, pages.Scheduler.Requests);
        Assert.Equal(2, pages.Scheduler.Loads);
        Assert.Equal(1, pages.Scheduler.StaleResults);
    }

    [Fact]
    public async Task ResultOfASupersededLoadIsNotApplied()
    {
        Pages pages = new();
        pages.Scheduler.Request(0);
        pages.Scheduler.Request(1);
        pages.Source(0).SetResult("page 1");
        await Until(() => pages.Started.Count == 2);

        pages.Scheduler.Request(2);
        pages.Source(1).SetResult("page 2");
        await Until(() => pages.Started.Count == 3);
        pages.Source(2).SetResult("page 3");
        await Until(() => !pages.Applied.IsEmpty);

        Assert.Equal([2], pages.Applied);
        Assert.Equal(2, pages.Scheduler.StaleResults);
    }

    [Fact]
    public async Task AResultWithNoNewerRequestIsAppliedAndALaterRequestLoadsAgain()
    {
        Pages pages = new();
        pages.Scheduler.Request(3);
        pages.Source(3).SetResult("page 4");
        await Until(() => pages.Applied.Count == 1);

        pages.Scheduler.Request(4);
        pages.Source(4).SetResult("page 5");
        await Until(() => pages.Applied.Count == 2);

        Assert.Equal([3, 4], pages.Applied);
        Assert.Equal(0, pages.Scheduler.StaleResults);
    }

    [Fact]
    public async Task AThrowingLoadIsReportedAndTheNextRequestStillRuns()
    {
        Pages pages = new();
        pages.Scheduler.Request(0);
        pages.Source(0).SetException(new InvalidDataException("bad page"));
        await Until(() => !pages.Failed.IsEmpty);

        pages.Scheduler.Request(1);
        pages.Source(1).SetResult("page 2");
        await Until(() => !pages.Applied.IsEmpty);

        Assert.Equal([0], pages.Failed);
        Assert.Equal([1], pages.Applied);
    }

    [Fact]
    public async Task ASupersededFailureIsNotReported()
    {
        Pages pages = new();
        pages.Scheduler.Request(0);
        pages.Scheduler.Request(1);
        pages.Source(0).SetException(new InvalidDataException("bad page"));
        await Until(() => pages.Started.Count == 2);
        pages.Source(1).SetResult("page 2");
        await Until(() => !pages.Applied.IsEmpty);

        Assert.Empty(pages.Failed);
        Assert.Equal(1, pages.Scheduler.StaleResults);
    }

    [Fact]
    public async Task CancelAsyncWaitsForTheRunningLoadAndStopsLaterOnes()
    {
        TaskCompletionSource<string> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool returned = false;
        Pages pages = new(async (page, _) =>
        {
            // Ignores the token, like a render that can't be interrupted.
            string result = await release.Task;
            returned = true;
            return result;
        });
        pages.Scheduler.Request(0);
        pages.Scheduler.Request(1);

        Task cancelling = pages.Scheduler.CancelAsync();
        Assert.False(cancelling.IsCompleted);
        release.SetResult("late");
        await cancelling;

        Assert.True(returned);
        Assert.Empty(pages.Applied);
        Assert.Empty(pages.Failed);
        pages.Scheduler.Request(2);
        Assert.Equal(1, pages.Scheduler.Loads);
    }

    [Fact]
    public async Task CancelAsyncCancelsTheRunningLoadsToken()
    {
        Pages pages = new();
        pages.Scheduler.Request(0);

        await pages.Scheduler.CancelAsync();

        Assert.True(pages.Source(0).Task.IsCanceled);
        Assert.Empty(pages.Failed);
        Assert.Empty(pages.Applied);
    }

    [Fact]
    public async Task CancelAsyncIsIdempotentAndWorksWithNothingRunning()
    {
        Pages pages = new();
        await pages.Scheduler.CancelAsync();
        await pages.Scheduler.CancelAsync();

        pages.Scheduler.Request(0);

        Assert.Empty(pages.Started);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Push this test file together with Task 2's implementation, then trigger CI.
Expected: `core-tests` fails to compile with
`The type or namespace name 'LatestWinsScheduler<>' could not be found`.

- [ ] **Step 3: Write the implementation**

```csharp
namespace DesktopGuides.Core.Pdf;

/// <summary>
/// Runs page loads one at a time. While a load runs, newer requests replace
/// one another, and a result is applied only if nothing newer was asked for.
/// Call Request and CancelAsync from one thread; apply and failed must not
/// throw.
/// </summary>
public sealed class LatestWinsScheduler<TResult>(
    Func<int, CancellationToken, Task<TResult>> load,
    Action<int, TResult> apply,
    Action<int, Exception> failed)
{
    private readonly object gate = new();
    private readonly CancellationTokenSource cancel = new();
    private Task running = Task.CompletedTask;
    private int? waiting;
    private bool loading;
    private bool cancelled;
    private int requests;
    private int loads;
    private int staleResults;

    public int Requests { get { lock (gate) return requests; } }
    public int Loads { get { lock (gate) return loads; } }
    public int StaleResults { get { lock (gate) return staleResults; } }

    public void Request(int pageIndex)
    {
        lock (gate)
        {
            if (cancelled) return;
            requests++;
            waiting = pageIndex;
            if (loading) return;
            loading = true;
        }
        running = RunAsync();
    }

    public async Task CancelAsync()
    {
        bool first;
        lock (gate)
        {
            first = !cancelled;
            cancelled = true;
            waiting = null;
        }
        if (first) cancel.Cancel();
        await running;
    }

    private async Task RunAsync()
    {
        while (true)
        {
            int page;
            lock (gate)
            {
                // Clearing loading under the same lock that sees no waiting
                // request means the next Request starts a fresh run.
                if (cancelled || waiting is null)
                {
                    loading = false;
                    return;
                }
                page = waiting.Value;
                waiting = null;
                loads++;
            }
            TResult result = default!;
            Exception? error = null;
            try
            {
                result = await load(page, cancel.Token);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                lock (gate) loading = false;
                return;
            }
            catch (Exception exception)
            {
                error = exception;
            }
            bool current;
            lock (gate)
            {
                current = waiting is null && !cancelled;
                if (!current && !cancelled) staleResults++;
            }
            if (!current) continue;
            if (error is null) apply(page, result);
            else failed(page, error);
        }
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Trigger CI. Expected: `core-tests` is green, and all 8
`LatestWinsSchedulerTests` pass.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Pdf/LatestWinsScheduler.cs tests/DesktopGuides.Core.Tests/LatestWinsSchedulerTests.cs
git commit -m "feat(p1): T10.1 latest-wins page load scheduler"
```

### Task 4: Errors, location rules and diagnostics

**Files:**
- Create: `src/DesktopGuides.Core/Pdf/PdfGuideLoadMessages.cs`
- Create: `src/DesktopGuides.Core/Pdf/PdfLocationRules.cs`
- Create: `src/DesktopGuides.Core/Pdf/PdfSessionDiagnostics.cs`
- Test: `tests/DesktopGuides.Core.Tests/PdfGuideLoadMessagesTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/PdfLocationRulesTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/PdfSessionDiagnosticsTests.cs`

**Interfaces:**
- Consumes:
  - `HtmlGuideLoadAction` and `HtmlGuideLoadMessages.ActionLabel` (`DesktopGuides.Core.Html`);
  - `ReaderLocation`, `PdfPosition`, `RestoreKind`, `RestoreOutcome` and `ReaderLocationCodec.CurrentVersion` (`DesktopGuides.Core.Reading`).
- Produces:
  - `enum PdfGuideLoadError { Missing, Changed, Unreadable, Damaged, PasswordProtected, Failed }`.
  - `static class PdfGuideLoadMessages`, with
    `string For(PdfGuideLoadError)`,
    `HtmlGuideLoadAction ActionFor(PdfGuideLoadError)` and
    `string ActionLabel(HtmlGuideLoadAction)`.
  - `sealed record PdfRestore(int PageIndex, RestoreOutcome Outcome)`.
  - `static class PdfLocationRules`, with:
    - `ReaderLocation Capture(string contentSha256, int pageIndex, int pageCount)`;
    - `PdfRestore Restore(ReaderLocation location, string contentSha256, int pageCount)`;
    - `const string ClampedReason`;
    - `const string UnavailableReason`.
  - `sealed record PdfSessionDiagnostics(int Requests, int Loads, int StaleResults, long PeakCacheBytes, long MaxCacheBytes, int CachedPagesAtClose, int PeakTextPages, long PeakTextCharacters, bool DisposedCleanly)`,
    with `string ToJson()`.

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Core.Tests/PdfGuideLoadMessagesTests.cs`:

```csharp
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfGuideLoadMessagesTests
{
    [Theory]
    [InlineData(PdfGuideLoadError.Missing, "This guide's file is missing from the library.")]
    [InlineData(PdfGuideLoadError.Changed, "This guide's files have changed. Re-import it to read it.")]
    [InlineData(PdfGuideLoadError.Unreadable, "This guide's file can't be opened. Close any app that's using it, then open the guide again.")]
    [InlineData(PdfGuideLoadError.Damaged, "This PDF is damaged, so it can't be opened. Re-import it from the original file.")]
    [InlineData(PdfGuideLoadError.PasswordProtected, "This PDF now needs a password, which isn't supported. Remove the password and re-import it.")]
    [InlineData(PdfGuideLoadError.Failed, "This guide stopped responding.")]
    public void EachErrorHasItsMessage(PdfGuideLoadError error, string message) =>
        Assert.Equal(message, PdfGuideLoadMessages.For(error));

    [Theory]
    [InlineData(PdfGuideLoadError.Missing, HtmlGuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.Changed, HtmlGuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.Unreadable, HtmlGuideLoadAction.Reopen)]
    [InlineData(PdfGuideLoadError.Damaged, HtmlGuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.PasswordProtected, HtmlGuideLoadAction.None)]
    [InlineData(PdfGuideLoadError.Failed, HtmlGuideLoadAction.Reopen)]
    public void EachErrorHasItsAction(PdfGuideLoadError error, HtmlGuideLoadAction action) =>
        Assert.Equal(action, PdfGuideLoadMessages.ActionFor(error));

    [Fact]
    public void EveryErrorIsCovered()
    {
        foreach (PdfGuideLoadError error in Enum.GetValues<PdfGuideLoadError>())
        {
            Assert.NotEmpty(PdfGuideLoadMessages.For(error));
            _ = PdfGuideLoadMessages.ActionFor(error);
        }
    }

    [Fact]
    public void UnknownErrorsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.For((PdfGuideLoadError)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.ActionFor((PdfGuideLoadError)99));
    }

    [Fact]
    public void ReopenHasALabelAndNoneHasNone()
    {
        Assert.Equal("Reopen", PdfGuideLoadMessages.ActionLabel(HtmlGuideLoadAction.Reopen));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfGuideLoadMessages.ActionLabel(HtmlGuideLoadAction.None));
    }

    [Fact]
    public void MissingAndUnreadableUseTheTextWording()
    {
        Assert.Equal(TextGuideLoadMessages.For(TextGuideLoadError.Missing),
            PdfGuideLoadMessages.For(PdfGuideLoadError.Missing));
        Assert.Equal(TextGuideLoadMessages.For(TextGuideLoadError.Unreadable),
            PdfGuideLoadMessages.For(PdfGuideLoadError.Unreadable));
    }
}
```

`tests/DesktopGuides.Core.Tests/PdfLocationRulesTests.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfLocationRulesTests
{
    private const string Sha = "ab12";

    private static ReaderLocation At(int page, string sha = Sha) =>
        new(GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, sha, new PdfPosition(page, 0), null);

    [Fact]
    public void CaptureRecordsThePageAndAnEstimate()
    {
        ReaderLocation location = PdfLocationRules.Capture("AB12", 4, 200);

        Assert.Equal(GuideFormat.Pdf, location.Format);
        Assert.Equal(ReaderLocationCodec.CurrentVersion, location.SchemaVersion);
        Assert.Equal("ab12", location.ContentSha256);
        Assert.Equal(new PdfPosition(4, 0), location.Payload);
        Assert.Equal(5.0 / 200, location.EstimatedFraction);
    }

    [Fact]
    public void CapturedLocationsSurviveTheCodec()
    {
        ReaderLocation location = PdfLocationRules.Capture(Sha, 199, 200);

        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(location), GuideFormat.Pdf, Sha);

        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Equal(new PdfPosition(199, 0), decoded.Location!.Payload);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(57)]
    [InlineData(199)]
    public void AnInRangePageIsExact(int page)
    {
        PdfRestore restore = PdfLocationRules.Restore(At(page), Sha, 200);

        Assert.Equal(page, restore.PageIndex);
        Assert.Equal(RestoreKind.Exact, restore.Outcome.Kind);
    }

    [Fact]
    public void ShaComparisonIgnoresCase() =>
        Assert.Equal(RestoreKind.Exact, PdfLocationRules.Restore(At(3, "AB12"), Sha, 200).Outcome.Kind);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(200, 199)]
    [InlineData(int.MaxValue, 199)]
    public void AnOutOfRangePageIsClampedAndApproximate(int page, int expected)
    {
        PdfRestore restore = PdfLocationRules.Restore(At(page), Sha, 200);

        Assert.Equal(expected, restore.PageIndex);
        Assert.Equal(RestoreKind.Approximate, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.ClampedReason, restore.Outcome.Reason);
    }

    [Fact]
    public void AnotherGuideVersionIsUnavailable() =>
        AssertUnavailable(At(3, "ff00"));

    [Fact]
    public void AnotherFormatIsUnavailable() =>
        AssertUnavailable(new ReaderLocation(GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, Sha,
            new TextPosition(0, "x"), null));

    [Fact]
    public void APdfLocationWithATextPayloadIsUnavailable() =>
        AssertUnavailable(new ReaderLocation(GuideFormat.Pdf, ReaderLocationCodec.CurrentVersion, Sha,
            new TextPosition(0, "x"), null));

    [Fact]
    public void AnotherSchemaVersionIsUnavailable() =>
        AssertUnavailable(At(3) with { SchemaVersion = ReaderLocationCodec.CurrentVersion + 1 });

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonFinitePageFractionIsUnavailable(double fraction) =>
        AssertUnavailable(At(3) with { Payload = new PdfPosition(3, fraction) });

    [Fact]
    public void NoPagesIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfLocationRules.Restore(At(0), Sha, 0));

    private static void AssertUnavailable(ReaderLocation location)
    {
        PdfRestore restore = PdfLocationRules.Restore(location, Sha, 200);

        Assert.Equal(RestoreKind.Unavailable, restore.Outcome.Kind);
        Assert.Equal(PdfLocationRules.UnavailableReason, restore.Outcome.Reason);
    }
}
```

`tests/DesktopGuides.Core.Tests/PdfSessionDiagnosticsTests.cs`:

```csharp
using System.Text.Json;
using DesktopGuides.Core.Pdf;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class PdfSessionDiagnosticsTests
{
    [Fact]
    public void JsonHoldsOnlyTheCounts()
    {
        PdfSessionDiagnostics diagnostics = new(600, 41, 3, 90_000_000, 100_663_296, 17, 8, 280, true);

        using JsonDocument json = JsonDocument.Parse(diagnostics.ToJson());
        JsonElement root = json.RootElement;

        Assert.Equal(
            ["requests", "loads", "staleResults", "peakCacheBytes", "maxCacheBytes",
             "cachedPagesAtClose", "peakTextPages", "peakTextCharacters", "disposedCleanly"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(600, root.GetProperty("requests").GetInt32());
        Assert.Equal(41, root.GetProperty("loads").GetInt32());
        Assert.Equal(3, root.GetProperty("staleResults").GetInt32());
        Assert.Equal(90_000_000, root.GetProperty("peakCacheBytes").GetInt64());
        Assert.Equal(100_663_296, root.GetProperty("maxCacheBytes").GetInt64());
        Assert.Equal(17, root.GetProperty("cachedPagesAtClose").GetInt32());
        Assert.Equal(8, root.GetProperty("peakTextPages").GetInt32());
        Assert.Equal(280, root.GetProperty("peakTextCharacters").GetInt64());
        Assert.True(root.GetProperty("disposedCleanly").GetBoolean());
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Push these tests together with Task 3's implementation, then trigger CI.
Expected: `core-tests` fails to compile, with `PdfGuideLoadError`,
`PdfLocationRules` and `PdfSessionDiagnostics` not found.


- [ ] **Step 3: Write the implementation**

`src/DesktopGuides.Core/Pdf/PdfGuideLoadMessages.cs`:

```csharp
using DesktopGuides.Core.Html;

namespace DesktopGuides.Core.Pdf;

// New members go at the end, so existing values keep their numbers.
public enum PdfGuideLoadError { Missing, Changed, Unreadable, Damaged, PasswordProtected, Failed }

// Actions reuse HtmlGuideLoadAction because the Reader's error surface is
// typed to it; a PDF error only ever offers None or Reopen.
public static class PdfGuideLoadMessages
{
    public static string For(PdfGuideLoadError error) => error switch
    {
        PdfGuideLoadError.Missing => "This guide's file is missing from the library.",
        PdfGuideLoadError.Changed => "This guide's files have changed. Re-import it to read it.",
        PdfGuideLoadError.Unreadable =>
            "This guide's file can't be opened. Close any app that's using it, then open the guide again.",
        PdfGuideLoadError.Damaged => "This PDF is damaged, so it can't be opened. Re-import it from the original file.",
        PdfGuideLoadError.PasswordProtected =>
            "This PDF now needs a password, which isn't supported. Remove the password and re-import it.",
        PdfGuideLoadError.Failed => "This guide stopped responding.",
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static HtmlGuideLoadAction ActionFor(PdfGuideLoadError error) => error switch
    {
        PdfGuideLoadError.Unreadable or PdfGuideLoadError.Failed => HtmlGuideLoadAction.Reopen,
        PdfGuideLoadError.Missing or PdfGuideLoadError.Changed or PdfGuideLoadError.Damaged or
            PdfGuideLoadError.PasswordProtected => HtmlGuideLoadAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    public static string ActionLabel(HtmlGuideLoadAction action) => HtmlGuideLoadMessages.ActionLabel(action);
}
```

`src/DesktopGuides.Core/Pdf/PdfLocationRules.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Pdf;

public sealed record PdfRestore(int PageIndex, RestoreOutcome Outcome);

// T10.1 restores by page only. Fraction restore and changed-byte matching
// are T10.3, so a location for other bytes is Unavailable here.
public static class PdfLocationRules
{
    public const string ClampedReason = "That page isn't in this guide, so the nearest page is shown.";
    public const string UnavailableReason = "This reading position can't be used with this guide.";

    public static ReaderLocation Capture(string contentSha256, int pageIndex, int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, pageCount);
        return new ReaderLocation(
            GuideFormat.Pdf,
            ReaderLocationCodec.CurrentVersion,
            contentSha256.ToLowerInvariant(),
            new PdfPosition(pageIndex, 0),
            (pageIndex + 1.0) / pageCount);
    }

    public static PdfRestore Restore(ReaderLocation location, string contentSha256, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        if (location.Format != GuideFormat.Pdf ||
            location.SchemaVersion != ReaderLocationCodec.CurrentVersion ||
            location.Payload is not PdfPosition position ||
            !double.IsFinite(position.PageFraction) ||
            !string.Equals(location.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new PdfRestore(-1, new RestoreOutcome(RestoreKind.Unavailable, UnavailableReason));
        }
        int page = Math.Clamp(position.PageIndex, 0, pageCount - 1);
        return page == position.PageIndex
            ? new PdfRestore(page, new RestoreOutcome(RestoreKind.Exact))
            : new PdfRestore(page, new RestoreOutcome(RestoreKind.Approximate, ClampedReason));
    }
}
```

`src/DesktopGuides.Core/Pdf/PdfSessionDiagnostics.cs`:

```csharp
using System.Text.Json;

namespace DesktopGuides.Core.Pdf;

/// <summary>
/// Test-only counts from one PDF session. No guide text and no paths.
/// </summary>
public sealed record PdfSessionDiagnostics(
    int Requests,
    int Loads,
    int StaleResults,
    long PeakCacheBytes,
    long MaxCacheBytes,
    int CachedPagesAtClose,
    int PeakTextPages,
    long PeakTextCharacters,
    bool DisposedCleanly)
{
    public string ToJson() => JsonSerializer.Serialize(new
    {
        requests = Requests,
        loads = Loads,
        staleResults = StaleResults,
        peakCacheBytes = PeakCacheBytes,
        maxCacheBytes = MaxCacheBytes,
        cachedPagesAtClose = CachedPagesAtClose,
        peakTextPages = PeakTextPages,
        peakTextCharacters = PeakTextCharacters,
        disposedCleanly = DisposedCleanly
    });
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Trigger CI. Expected: `core-tests` is green.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Pdf/PdfGuideLoadMessages.cs src/DesktopGuides.Core/Pdf/PdfLocationRules.cs \
  src/DesktopGuides.Core/Pdf/PdfSessionDiagnostics.cs tests/DesktopGuides.Core.Tests/PdfGuideLoadMessagesTests.cs \
  tests/DesktopGuides.Core.Tests/PdfLocationRulesTests.cs tests/DesktopGuides.Core.Tests/PdfSessionDiagnosticsTests.cs
git commit -m "feat(p1): T10.1 PDF load errors, page restore rules and diagnostics"
```

### Task 5: `CancellableReadStream` extraction and `PdfPageTextSource`

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Import/CancellableReadStream.cs`
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs`:
  - delete the nested `private sealed class CancellableReadStream` (around
    line 241, about 55 lines). The call at line 215 is unchanged.
- Create: `src/DesktopGuides.Infrastructure/Reading/PdfPageTextSource.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Import/CancellableReadStreamTests.cs`
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/PdfPageTextSourceTests.cs`

**Interfaces:**
- Consumes: PdfPig 0.1.16 (`UglyToad.PdfPig.PdfDocument`,
  `UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor`).
- Produces:
  - `internal sealed class CancellableReadStream(Stream inner, CancellationToken token) : Stream`,
    with `public CancellationToken Token { get; set; }`. It does not own
    `inner`.
  - `sealed record PdfPageText(string Text, bool HasLetters, bool Truncated)`.
  - `sealed class PdfPageTextException : Exception`, with `int PageIndex`.
  - `sealed class PdfPageTextSource : IDisposable`, with:
    - `internal static PdfPageTextSource Open(Stream file, CancellationToken token, int maxPageCharacters = DefaultMaxPageCharacters, int maxPages = DefaultMaxPages, long maxCharacters = DefaultMaxCharacters)`;
    - `int PageCount`, `int PeakPages`, `long PeakCharacters`;
    - `Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token)`;
    - `void Dispose()`;
    - the constants `DefaultMaxPageCharacters = 1_048_576`,
      `DefaultMaxPages = 8` and `DefaultMaxCharacters = 4_194_304`.
- `Open` throws whatever PdfPig throws, including
  `PdfDocumentEncryptedException`. On success the source owns `file`; on
  failure the caller still owns it.

- [ ] **Step 1: Write the failing tests**

`tests/DesktopGuides.Infrastructure.Tests/Import/CancellableReadStreamTests.cs`:

```csharp
using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class CancellableReadStreamTests
{
    [Fact]
    public void ReadsThrowOnceTheTokenIsCancelled()
    {
        using CancellationTokenSource cancel = new();
        using CancellableReadStream stream = new(new MemoryStream([1, 2, 3]), cancel.Token);
        Assert.Equal(1, stream.ReadByte());

        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => stream.ReadByte());
        Assert.Throws<OperationCanceledException>(() => stream.Read(new byte[1], 0, 1));
        Assert.Throws<OperationCanceledException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<OperationCanceledException>(() => stream.Position = 0);
    }

    [Fact]
    public void AReplacedTokenAppliesToLaterReads()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        using CancellableReadStream stream = new(new MemoryStream([1, 2, 3]), cancel.Token);

        stream.Token = CancellationToken.None;

        Assert.Equal(1, stream.ReadByte());
    }

    [Fact]
    public void ItIsReadOnly()
    {
        using CancellableReadStream stream = new(new MemoryStream([1]), CancellationToken.None);

        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
    }
}
```

`tests/DesktopGuides.Infrastructure.Tests/Reading/PdfPageTextSourceTests.cs`:

```csharp
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class PdfPageTextSourceTests
{
    // pdf-long is generated by tools/p0/make_fixtures.py, which CI runs first.
    private const string Long = "generated/pdf-long.pdf";

    private static PdfPageTextSource Open(string fixture, int maxPageCharacters = PdfPageTextSource.DefaultMaxPageCharacters,
        int maxPages = PdfPageTextSource.DefaultMaxPages, long maxCharacters = PdfPageTextSource.DefaultMaxCharacters) =>
        PdfPageTextSource.Open(File.OpenRead(P0Fixtures.Resolve(fixture)), CancellationToken.None,
            maxPageCharacters, maxPages, maxCharacters);

    [Fact]
    public async Task ATaggedPageHasItsParagraph()
    {
        using PdfPageTextSource source = Open("pdf-access.pdf");

        PdfPageText page = await source.GetPageTextAsync(0, CancellationToken.None);

        Assert.Equal(1, source.PageCount);
        Assert.Contains("Tagged guide paragraph for Narrator", page.Text);
        Assert.True(page.HasLetters);
        Assert.False(page.Truncated);
    }

    [Fact]
    public async Task AScannedPageHasNoLetters()
    {
        using PdfPageTextSource source = Open("pdf-scan.pdf");

        PdfPageText page = await source.GetPageTextAsync(0, CancellationToken.None);

        Assert.False(page.HasLetters);
    }

    [Fact]
    public async Task TheLastPageOfALongDocumentHasItsOwnText()
    {
        using PdfPageTextSource source = Open(Long);

        PdfPageText page = await source.GetPageTextAsync(199, CancellationToken.None);

        Assert.Equal(200, source.PageCount);
        Assert.Contains("Desktop Guides P0 - page 200 of 200", page.Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(200)]
    public async Task IndexesOutsideTheDocumentThrow(int index)
    {
        using PdfPageTextSource source = Open(Long);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => source.GetPageTextAsync(index, CancellationToken.None));
    }

    [Fact]
    public async Task ReadingEveryPageKeepsTheDefaultLimits()
    {
        using PdfPageTextSource source = Open(Long);
        for (int page = 0; page < source.PageCount; page++)
        {
            await source.GetPageTextAsync(page, CancellationToken.None);
        }

        Assert.Equal(PdfPageTextSource.DefaultMaxPages, source.PeakPages);
        Assert.True(source.PeakCharacters <= PdfPageTextSource.DefaultMaxCharacters);
    }

    [Fact]
    public async Task ACharacterLimitBoundsRetainedText()
    {
        // Each pdf-long page is about 35 characters, so 100 holds two pages.
        using PdfPageTextSource source = Open(Long, maxPageCharacters: 100, maxPages: 8, maxCharacters: 100);
        for (int page = 0; page < 20; page++)
        {
            await source.GetPageTextAsync(page, CancellationToken.None);
        }

        Assert.True(source.PeakCharacters <= 100);
        Assert.True(source.PeakPages < 8);
    }

    [Fact]
    public async Task ARecentPageIsServedAgainWithTheSameText()
    {
        using PdfPageTextSource source = Open(Long);
        PdfPageText first = await source.GetPageTextAsync(4, CancellationToken.None);
        await source.GetPageTextAsync(5, CancellationToken.None);

        PdfPageText again = await source.GetPageTextAsync(4, CancellationToken.None);

        Assert.Equal(first, again);
    }

    [Fact]
    public async Task LongPageTextIsTruncated()
    {
        using PdfPageTextSource source = Open("pdf-access.pdf", maxPageCharacters: 10, maxPages: 8, maxCharacters: 100);

        PdfPageText page = await source.GetPageTextAsync(0, CancellationToken.None);

        Assert.Equal(10, page.Text.Length);
        Assert.True(page.Truncated);
    }

    [Theory]
    [InlineData(0, 8, 100)]
    [InlineData(10, 0, 100)]
    [InlineData(200, 8, 100)]
    public void InvalidLimitsAreRejected(int maxPageCharacters, int maxPages, long maxCharacters)
    {
        using FileStream file = File.OpenRead(P0Fixtures.Resolve("pdf-access.pdf"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfPageTextSource.Open(file, CancellationToken.None, maxPageCharacters, maxPages, maxCharacters));
    }

    [Fact]
    public async Task ACancelledTokenStopsAnExtraction()
    {
        using PdfPageTextSource source = Open(Long);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.GetPageTextAsync(10, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task CancellingDuringAnExtractionStopsIt()
    {
        GatedStream file = new(File.ReadAllBytes(P0Fixtures.Resolve(Long)));
        using PdfPageTextSource source = PdfPageTextSource.Open(file, CancellationToken.None);
        using CancellationTokenSource cancel = new();
        file.Hold();

        Task<PdfPageText> reading = source.GetPageTextAsync(150, cancel.Token);
        Assert.True(await file.ReadSeen.WaitAsync(TimeSpan.FromSeconds(5)), "Extraction never read the file.");
        cancel.Cancel();
        file.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        PdfPageText later = await source.GetPageTextAsync(151, CancellationToken.None);
        Assert.Contains("page 152 of 200", later.Text);
    }

    [Fact]
    public async Task DisposeDuringAnExtractionWaitsForIt()
    {
        GatedStream file = new(File.ReadAllBytes(P0Fixtures.Resolve(Long)));
        PdfPageTextSource source = PdfPageTextSource.Open(file, CancellationToken.None);
        file.Hold();

        Task<PdfPageText> reading = source.GetPageTextAsync(150, CancellationToken.None);
        Assert.True(await file.ReadSeen.WaitAsync(TimeSpan.FromSeconds(5)), "Extraction never read the file.");
        Task disposing = Task.Run(source.Dispose);
        await Task.Delay(200);
        Assert.False(disposing.IsCompleted);
        Assert.False(file.Disposed);
        file.Release();

        Assert.Contains("page 151 of 200", (await reading).Text);
        await disposing;
        Assert.True(file.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => source.GetPageTextAsync(0, CancellationToken.None));
        source.Dispose();
    }

    // Blocks reads while held, so a test can pause an extraction mid-page.
    private sealed class GatedStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false);
        private readonly ManualResetEventSlim open = new(true);

        public SemaphoreSlim ReadSeen { get; } = new(0);
        public bool Disposed { get; private set; }

        public void Hold() => open.Reset();
        public void Release() => open.Set();

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Wait();
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            Wait();
            return inner.Read(buffer);
        }

        public override int ReadByte()
        {
            Wait();
            return inner.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            inner.Dispose();
            base.Dispose(disposing);
        }

        private void Wait()
        {
            if (open.IsSet) return;
            ReadSeen.Release();
            open.Wait();
        }
    }
}
```

The two gated tests rely on PdfPig reading page content from the stream
when `GetPage` runs, not at `Open`. PdfPig 0.1.x reads lazily, and import's
cancellation already relies on this. If `ReadSeen` times out on CI, use
`systematic-debugging` to confirm where PdfPig reads. If it buffers the
whole file at `Open`, delete the two gated tests, keep the pre-cancelled
one, and ledger the change. Dispose still waits on the semaphore either
way.

- [ ] **Step 2: Run the tests and confirm they fail**

Push these tests together with Task 4's implementation, then trigger CI.
Expected: `core-tests` fails to compile in Infrastructure.Tests. The cause
is `CancellableReadStream` being inaccessible (it is private and nested)
and `PdfPageTextSource` not existing.

- [ ] **Step 3: Write the implementation**

`src/DesktopGuides.Infrastructure/Import/CancellableReadStream.cs` keeps
the body moved from `GuideImportValidator`. Only the token field becomes a
settable property:

```csharp
namespace DesktopGuides.Infrastructure.Import;

// PdfPig ignores cancellation tokens and reads lazily, so the token is
// checked on every read. Token is settable so a long-lived document can
// honor each caller's token in turn. Doesn't own the inner stream.
internal sealed class CancellableReadStream(Stream inner, CancellationToken token) : Stream
{
    public CancellationToken Token { get; set; } = token;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set
        {
            Token.ThrowIfCancellationRequested();
            inner.Position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Token.ThrowIfCancellationRequested();
        return inner.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        Token.ThrowIfCancellationRequested();
        return inner.Read(buffer);
    }

    public override int ReadByte()
    {
        Token.ThrowIfCancellationRequested();
        return inner.ReadByte();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Token.ThrowIfCancellationRequested();
        return inner.Seek(offset, origin);
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
```

Delete the nested class from `GuideImportValidator.cs`. The call
`PdfDocument.Open(new CancellableReadStream(stream, token))` now resolves
to the new class in the same namespace.

`src/DesktopGuides.Infrastructure/Reading/PdfPageTextSource.cs`:

```csharp
using DesktopGuides.Infrastructure.Import;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DesktopGuides.Infrastructure.Reading;

public sealed record PdfPageText(string Text, bool HasLetters, bool Truncated);

public sealed class PdfPageTextException(int pageIndex, Exception inner)
    : Exception($"Page {pageIndex + 1}'s text couldn't be read.", inner)
{
    public int PageIndex { get; } = pageIndex;
}

/// <summary>
/// Page text from the managed copy, one page at a time. PdfPig isn't
/// thread-safe, so one extraction runs at a time; recently read pages are
/// kept within page and character limits.
/// </summary>
public sealed class PdfPageTextSource : IDisposable
{
    public const int DefaultMaxPageCharacters = 1_048_576;
    public const int DefaultMaxPages = 8;
    public const long DefaultMaxCharacters = 4_194_304;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Stream file;
    private readonly CancellableReadStream reader;
    private readonly PdfDocument document;
    private readonly int maxPageCharacters;
    private readonly int maxPages;
    private readonly long maxCharacters;
    private readonly LinkedList<(int Page, PdfPageText Text)> recent = new();
    private long recentCharacters;
    private bool disposed;

    private PdfPageTextSource(Stream file, CancellableReadStream reader, PdfDocument document,
        int maxPageCharacters, int maxPages, long maxCharacters)
    {
        this.file = file;
        this.reader = reader;
        this.document = document;
        this.maxPageCharacters = maxPageCharacters;
        this.maxPages = maxPages;
        this.maxCharacters = maxCharacters;
        PageCount = document.NumberOfPages;
    }

    public int PageCount { get; }
    public int PeakPages { get; private set; }
    public long PeakCharacters { get; private set; }

    // On success the source owns file; on failure the caller still does.
    internal static PdfPageTextSource Open(Stream file, CancellationToken token,
        int maxPageCharacters = DefaultMaxPageCharacters, int maxPages = DefaultMaxPages,
        long maxCharacters = DefaultMaxCharacters)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPageCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, maxPageCharacters);
        CancellableReadStream reader = new(file, token);
        PdfDocument document = PdfDocument.Open(reader);
        reader.Token = CancellationToken.None;
        return new PdfPageTextSource(file, reader, document, maxPageCharacters, maxPages, maxCharacters);
    }

    public async Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        await gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (TakeRecent(pageIndex) is PdfPageText hit) return hit;
            PdfPageText text = await Task.Run(() => Extract(pageIndex, token), token);
            Remember(pageIndex, text);
            return text;
        }
        finally
        {
            gate.Release();
        }
    }

    // Waits for a running extraction, so the document is never closed under it.
    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            recent.Clear();
            recentCharacters = 0;
            document.Dispose();
            file.Dispose();
        }
        finally
        {
            gate.Release();
        }
    }

    private PdfPageText Extract(int pageIndex, CancellationToken token)
    {
        reader.Token = token;
        try
        {
            string text = ContentOrderTextExtractor.GetText(document.GetPage(pageIndex + 1));
            bool truncated = text.Length > maxPageCharacters;
            if (truncated)
            {
                int length = maxPageCharacters;
                if (char.IsHighSurrogate(text[length - 1])) length--;
                text = text[..length];
            }
            return new PdfPageText(text, text.Any(char.IsLetter), truncated);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // PdfPig may wrap the stream's cancellation in its own exception.
            throw new OperationCanceledException(token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new PdfPageTextException(pageIndex, error);
        }
        finally
        {
            reader.Token = CancellationToken.None;
        }
    }

    private PdfPageText? TakeRecent(int pageIndex)
    {
        for (LinkedListNode<(int Page, PdfPageText Text)>? node = recent.First; node is not null; node = node.Next)
        {
            if (node.Value.Page != pageIndex) continue;
            recent.Remove(node);
            recent.AddFirst(node);
            return node.Value.Text;
        }
        return null;
    }

    private void Remember(int pageIndex, PdfPageText text)
    {
        recent.AddFirst((pageIndex, text));
        recentCharacters += text.Text.Length;
        while (recent.Count > 1 && (recent.Count > maxPages || recentCharacters > maxCharacters))
        {
            recentCharacters -= recent.Last!.Value.Text.Text.Length;
            recent.RemoveLast();
        }
        PeakPages = Math.Max(PeakPages, recent.Count);
        PeakCharacters = Math.Max(PeakCharacters, recentCharacters);
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Trigger CI. Expected: `core-tests` is green. That includes the existing
`GuideImportValidatorHtmlPdfTests` (import cancellation is unchanged) and
all the new tests.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Import/CancellableReadStream.cs src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs \
  src/DesktopGuides.Infrastructure/Reading/PdfPageTextSource.cs \
  tests/DesktopGuides.Infrastructure.Tests/Import/CancellableReadStreamTests.cs \
  tests/DesktopGuides.Infrastructure.Tests/Reading/PdfPageTextSourceTests.cs
git commit -m "feat(p1): T10.1 bounded PDF page text source"
```

### Task 6: `ManagedPdfGuideLoader`

**Files:**
- Create: `src/DesktopGuides.Infrastructure/Reading/ManagedPdfGuideLoader.cs`
- Modify: `src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs:304`
  (`private static bool StartsLikePdf` becomes `internal static bool StartsLikePdf`)
- Test: `tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedPdfGuideLoaderTests.cs`

**Interfaces:**
- Consumes: Task 5's `PdfPageTextSource.Open`; Task 4's `PdfGuideLoadError`;
  `ManagedPathResolver.GetPlannedGuideFile` and `ResolveExistingGuideFile`;
  `GuideImportValidator.StartsLikePdf(Stream)`.
- Produces:
  - `abstract record PdfGuideLoad`;
  - `sealed record PdfGuideLoaded(string FilePath, PdfPageTextSource Text) : PdfGuideLoad`;
  - `sealed record PdfGuideLoadFailed(PdfGuideLoadError Error) : PdfGuideLoad`;
  - `sealed class ManagedPdfGuideLoader(ManagedPathResolver paths)`, with
    `Task<PdfGuideLoad> LoadAsync(Guide guide, CancellationToken token)`.
- The caller owns `PdfGuideLoaded.Text` and disposes it.

- [ ] **Step 1: Write the failing tests**

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class ManagedPdfGuideLoaderTests
{
    private static async Task<(Guide Guide, string Source)> PublishAsync(PublisherHarness harness, string fixture = "pdf-access.pdf")
    {
        string source = harness.Sources.Copy(fixture, "guide.pdf");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source));
        return ((await harness.Repository.GetGuideAsync(id))!, source);
    }

    private static string ManagedFile(PublisherHarness harness, Guide guide) =>
        harness.Paths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);

    private static Task<PdfGuideLoad> LoadAsync(PublisherHarness harness, Guide guide) =>
        new ManagedPdfGuideLoader(harness.Paths).LoadAsync(guide, CancellationToken.None);

    private static async Task<PdfGuideLoadError> FailedAsync(PublisherHarness harness, Guide guide) =>
        Assert.IsType<PdfGuideLoadFailed>(await LoadAsync(harness, guide)).Error;

    [Fact]
    public async Task LoadsAPublishedGuide()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);

        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
        using PdfPageTextSource text = loaded.Text;

        Assert.Equal(ManagedFile(harness, guide), loaded.FilePath);
        Assert.Equal(1, text.PageCount);
        Assert.Contains("Tagged guide paragraph for Narrator",
            (await text.GetPageTextAsync(0, CancellationToken.None)).Text);
    }

    [Fact]
    public async Task LoadsAfterTheOriginalIsDeleted()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, string source) = await PublishAsync(harness);
        File.Delete(source);

        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
        loaded.Text.Dispose();
    }

    [Fact]
    public async Task DisposingTheTextReleasesTheManagedCopy()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        PdfGuideLoaded loaded = Assert.IsType<PdfGuideLoaded>(await LoadAsync(harness, guide));
        Assert.Throws<IOException>(() => File.Delete(loaded.FilePath));

        loaded.Text.Dispose();

        File.Delete(loaded.FilePath);
    }

    [Fact]
    public async Task MissingFileIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        File.Delete(ManagedFile(harness, guide));

        Assert.Equal(PdfGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task DeletedGuideFolderIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        Directory.Delete(harness.Paths.GetGuideRoot(guide.Id), recursive: true);

        Assert.Equal(PdfGuideLoadError.Missing, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task FolderInPlaceOfTheFileIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        string file = ManagedFile(harness, guide);
        File.Delete(file);
        Directory.CreateDirectory(file);

        Assert.Equal(PdfGuideLoadError.Changed, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task GuideFolderThatIsALinkIsChanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        string root = harness.Paths.GetGuideRoot(guide.Id);
        string elsewhere = Path.Combine(Path.GetTempPath(), "desktop-guides-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Move(root, elsewhere);
            RemovalLibrary.CreateJunction(root, elsewhere);

            Assert.Equal(PdfGuideLoadError.Changed, await FailedAsync(harness, guide));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root);
            if (Directory.Exists(elsewhere)) Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public async Task APathOutsideTheGuideIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);

        Assert.Equal(PdfGuideLoadError.Changed,
            await FailedAsync(harness, guide with { PrimaryRelativePath = "../escape.pdf" }));
    }

    [Fact]
    public async Task TruncatedBytesAreDamaged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness, "pdf-short.pdf");
        string file = ManagedFile(harness, guide);
        File.WriteAllBytes(file, File.ReadAllBytes(file)[..400]);

        Assert.Equal(PdfGuideLoadError.Damaged, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task GarbageBytesAreDamaged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        File.WriteAllBytes(ManagedFile(harness, guide), Enumerable.Repeat((byte)'x', 4096).ToArray());

        Assert.Equal(PdfGuideLoadError.Damaged, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task AnEmptyFileIsDamaged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        File.WriteAllBytes(ManagedFile(harness, guide), []);

        Assert.Equal(PdfGuideLoadError.Damaged, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task AnEncryptedCopyIsPasswordProtected()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        File.Copy(P0Fixtures.Resolve("pdf-locked.pdf"), ManagedFile(harness, guide), overwrite: true);

        Assert.Equal(PdfGuideLoadError.PasswordProtected, await FailedAsync(harness, guide));
    }

    [Fact]
    public async Task AFailedLoadLeavesTheFileClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);
        string file = ManagedFile(harness, guide);
        File.Copy(P0Fixtures.Resolve("pdf-locked.pdf"), file, overwrite: true);
        await FailedAsync(harness, guide);

        File.Delete(file);
    }

    [Fact]
    public async Task ACancelledLoadThrows()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        (Guide guide, _) = await PublishAsync(harness);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ManagedPdfGuideLoader(harness.Paths).LoadAsync(guide, new CancellationToken(canceled: true)));
    }
}
```

`APathOutsideTheGuideIsChanged` assumes that `GetPlannedGuideFile` throws
`InvalidDataException` or `ArgumentException` for `..`. That is the HTML
loader's contract, and `ManagedRelativePath.Parse` rejects `..`.

- [ ] **Step 2: Run the tests and confirm they fail**

Push these tests together with Task 5's implementation, then trigger CI.
Expected: Infrastructure.Tests fails to compile, with
`ManagedPdfGuideLoader`, `PdfGuideLoad`, `PdfGuideLoaded` and
`PdfGuideLoadFailed` not found.

- [ ] **Step 3: Write the implementation**

In `GuideImportValidator.cs`, change
`private static bool StartsLikePdf(Stream stream)` to
`internal static bool StartsLikePdf(Stream stream)`.

`src/DesktopGuides.Infrastructure/Reading/ManagedPdfGuideLoader.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using UglyToad.PdfPig.Exceptions;

namespace DesktopGuides.Infrastructure.Reading;

public abstract record PdfGuideLoad;
public sealed record PdfGuideLoaded(string FilePath, PdfPageTextSource Text) : PdfGuideLoad;
public sealed record PdfGuideLoadFailed(PdfGuideLoadError Error) : PdfGuideLoad;

/// <summary>
/// Opens a PDF guide's managed copy read-only for its text. Never opens the
/// original source and never writes to the library.
/// </summary>
public sealed class ManagedPdfGuideLoader(ManagedPathResolver paths)
{
    public async Task<PdfGuideLoad> LoadAsync(Guide guide, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // The resolver reports links and access errors as missing too, so
        // look at the planned path first: only an absent file is Missing.
        try
        {
            string planned = paths.GetPlannedGuideFile(guide.Id, guide.PrimaryRelativePath);
            if (Directory.Exists(planned)) return Failed(PdfGuideLoadError.Changed);
            if (!File.Exists(planned)) return Failed(PdfGuideLoadError.Missing);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException)
        {
            return Failed(PdfGuideLoadError.Changed);
        }
        string path;
        try
        {
            path = paths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return Failed(PdfGuideLoadError.Missing);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        {
            return Failed(PdfGuideLoadError.Changed);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failed(PdfGuideLoadError.Unreadable);
        }
        return await Task.Run(() => Open(path, token), token);
    }

    private static PdfGuideLoad Open(string path, CancellationToken token)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return Failed(PdfGuideLoadError.Missing);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failed(PdfGuideLoadError.Unreadable);
        }
        try
        {
            // The header check keeps garbage Damaged whatever PdfPig tolerates.
            if (!GuideImportValidator.StartsLikePdf(file))
            {
                file.Dispose();
                return Failed(PdfGuideLoadError.Damaged);
            }
            file.Position = 0;
            PdfPageTextSource text = PdfPageTextSource.Open(file, token);
            if (text.PageCount == 0)
            {
                text.Dispose();
                return Failed(PdfGuideLoadError.Damaged);
            }
            return new PdfGuideLoaded(path, text);
        }
        catch (PdfDocumentEncryptedException)
        {
            file.Dispose();
            return Failed(PdfGuideLoadError.PasswordProtected);
        }
        catch (OperationCanceledException)
        {
            file.Dispose();
            throw;
        }
        catch (Exception)
        {
            // PdfPig reports malformed files with several exception types,
            // including EndOfStreamException for truncation.
            file.Dispose();
            return Failed(PdfGuideLoadError.Damaged);
        }
    }

    private static PdfGuideLoadFailed Failed(PdfGuideLoadError error) => new(error);
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Trigger CI. Expected: `core-tests` is green, and all 14
`ManagedPdfGuideLoaderTests` pass on the Windows runner.

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Infrastructure/Reading/ManagedPdfGuideLoader.cs \
  src/DesktopGuides.Infrastructure/Import/GuideImportValidator.cs \
  tests/DesktopGuides.Infrastructure.Tests/Reading/ManagedPdfGuideLoaderTests.cs
git commit -m "feat(p1): T10.1 managed PDF guide loader"
```

---

### Task 7: PDF Reader seed and installed smoke (RED)

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs`:
  - add a `seed-pdf-reader` block after the `seed-html-reader` block (which
    ends with `return 0;` at about line 485);
  - update the usage line `"or seed-txt-reader|seed-html-reader <app-data-root> <fixtures-root> "`.
- Modify: `tools/p1/windows_shell_ui_smoke.ps1`:
  - add `'pdf-reader'` to the `$Mode` `ValidateSet` (lines 3–16);
  - add it to the shared reader block's mode list (about line 1190);
  - add an `elseif ($Mode -eq 'pdf-reader')` branch before the TXT `else`
    (about line 1646).
- Modify: `tools/p1/windows_shell_install.ps1`:
  - add `pdf-*` to the 240-second timeout in `Run-ShellSmoke` (line 707);
  - add `Invoke-PdfReaderPass`, `Assert-PdfDiagnostics` and
    `Run-PdfReaderScenarios` after `Run-HtmlReaderScenarios`;
  - add a data-root wipe and `Run-PdfReaderScenarios` after
    `Run-HtmlReaderScenarios` in the main sequence (about line 1722).

**Interfaces:**
- Consumes:
  - `GuideImportValidator.InspectAsync`, `ImportReady`,
    `GuideImportPublisher.PublishAsync(manifest, gameId, title, allowDuplicate, sourceLabel, token)`;
  - the ShellSeed helper `InsertTextGuideAsync`;
  - smoke helpers `Find-ById`, `Find-ByName`, `Wait-Name`, `Wait-Status`,
    `Invoke-Element`, `Assert-Absent`, `Resize-ShellWindow`,
    `Save-WindowScreenshot`, and the shared reader block's `Open-TextGuide`,
    `Back-ToTextGame`, `Invoke-ReaderCommand`, `Assert-RowNames`,
    `$asciiNames` and `$missingMessage`;
  - install helpers `Invoke-ShellSeed`, `Start-InstalledShell`,
    `Close-InstalledShell`, `Run-ShellSmoke`, `Get-HtmlCacheRoot`,
    `Get-AppThemePreference`, `Set-AppThemePreference`,
    `Restore-AppThemePreference`.
- Produces, for Task 8 to satisfy:
  - AutomationIds `PdfPageStatus`, `PdfPreviewImage`, `PdfPreviewStatus`,
    `PdfTextStatus`, `PdfDocumentText`;
  - the gate `Local\DesktopGuides.Preview.PdfDiagnostics.<pid>`;
  - the file `<cacheRoot>\diagnostics\pdf-<guideId:N>.json`.
- The seed prints `{"pdfLong":"<id:N>"}` as its last line.

- [ ] **Step 1: Write the seed mode**

In `Program.cs`, after the `seed-html-reader` block's closing brace:

```csharp
if (args.Length == 3 && args[0] == "seed-pdf-reader")
{
    ManagedPathResolver pdfPaths = new(args[1]);
    await using SqliteLibraryRepository pdfRepository = new(pdfPaths);
    await pdfRepository.InitializeAsync();
    if ((await pdfRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The PDF reader seed needs an empty library.");
    }
    string pdfFixtures = Path.Combine(Path.GetFullPath(args[2]), "p0");
    Game pdfGame = await pdfRepository.AddGameAsync("PDF Reader Game", null, null);
    GuideImportValidator pdfValidator = new();
    GuideImportPublisher pdfPublisher = new(pdfRepository, pdfPaths);

    // Imports a P0 fixture as a user would. The Reader opens the managed copy.
    async Task<Guid> PublishPdfAsync(string fixture, string title, bool allowDuplicate)
    {
        ImportInspection inspection = await pdfValidator.InspectAsync(
            Path.Combine(pdfFixtures, fixture), CancellationToken.None);
        if (inspection is not ImportReady ready)
        {
            throw new InvalidOperationException($"The {fixture} fixture failed the import preview: {inspection}.");
        }
        return await pdfPublisher.PublishAsync(
            ready.Manifest, pdfGame.Id, title, allowDuplicate, null, CancellationToken.None);
    }

    async Task<string> ManagedCopyAsync(Guid id) =>
        pdfPaths.ResolveExistingGuideFile(id, (await pdfRepository.GetGuideAsync(id))!.PrimaryRelativePath);

    await PublishPdfAsync("pdf-access.pdf", "Tagged PDF Guide", false);
    await PublishPdfAsync("pdf-scan.pdf", "Scanned PDF Guide", false);
    Guid pdfLong = await PublishPdfAsync(Path.Combine("generated", "pdf-long.pdf"), "Long PDF Guide", false);
    Guid damaged = await PublishPdfAsync("pdf-short.pdf", "Damaged PDF Guide", false);
    Guid missing = await PublishPdfAsync("pdf-short.pdf", "Missing PDF Guide", true);
    // Cuts the managed copy before its cross-reference table, as a failed
    // copy might.
    using (FileStream copy = new(await ManagedCopyAsync(damaged), FileMode.Open, FileAccess.Write))
    {
        copy.SetLength(400);
    }
    File.Delete(await ManagedCopyAsync(missing));
    // TXT guides still open after the PDF errors.
    await InsertTextGuideAsync(pdfPaths, pdfGame.Id, Guid.NewGuid(), "Plain Text Guide",
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        File.ReadAllBytes(Path.Combine(pdfFixtures, "txt-ascii.txt")));
    Console.WriteLine(JsonSerializer.Serialize(new { pdfLong = pdfLong.ToString("N") }));
    return 0;
}
```

Change the usage line to
`"or seed-txt-reader|seed-html-reader|seed-pdf-reader <app-data-root> <fixtures-root> " +`.

- [ ] **Step 2: Write the smoke mode**

In `windows_shell_ui_smoke.ps1`:

1. Change the end of the `ValidateSet` from `'html-reader', 'html-runtime-missing')`
   to `'html-reader', 'html-runtime-missing', 'pdf-reader')`.
2. Change the shared reader block's condition and game name:

```powershell
    elseif ($Mode -in @('txt-reader', 'txt-load-paused', 'txt-back-during-load',
        'txt-load-released', 'html-reader', 'html-runtime-missing', 'pdf-reader')) {
        $textGame = if ($Mode -in @('html-reader', 'html-runtime-missing')) { 'Web Reader Game' }
            elseif ($Mode -eq 'pdf-reader') { 'PDF Reader Game' }
            else { 'Text Reader Game' }
```

3. Insert this branch immediately before the TXT branch's `else {`:

```powershell
        elseif ($Mode -eq 'pdf-reader') {
            [void](Wait-Name 'LibraryHeading' 'Library')
            Resize-ShellWindow 1500 720
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')

            # The page text through TextPattern, as a screen reader reads it;
            # null while the text box is not shown.
            function Get-PdfText {
                $box = Find-ById 'PdfDocumentText'
                if (-not $box -or $box.Current.IsOffscreen) { return $null }
                return $box.GetCurrentPattern(
                    [System.Windows.Automation.TextPattern]::Pattern).DocumentRange.GetText(-1)
            }

            # An empty status line is collapsed.
            function Get-PdfStatus([string] $id) {
                $status = Find-ById $id
                if (-not $status -or $status.Current.IsOffscreen) { return '' }
                return $status.Current.Name
            }

            # Waits until the page status, the preview's name and the text all
            # show one page.
            function Wait-PdfPage([int] $page, [int] $count, [string] $text, [int] $seconds = 15) {
                $label = "Page $page of $count"
                $deadline = (Get-Date).AddSeconds($seconds)
                do {
                    try {
                        $status = Find-ById 'PdfPageStatus'
                        $preview = Find-ById 'PdfPreviewImage'
                        if ($status -and $preview -and $status.Current.Name -eq $label -and
                            $preview.Current.Name -eq "$label preview") {
                            $read = Get-PdfText
                            if ($read -and $read.Contains($text)) { return $preview }
                        }
                    }
                    catch [System.Windows.Automation.ElementNotAvailableException] {
                        # The view replaced the element mid-read.
                    }
                    Start-Sleep -Milliseconds 100
                } while ((Get-Date) -lt $deadline)
                throw "Expected $label with its preview, and text containing '$text'."
            }

            function Invoke-NextPages([int] $count) {
                $next = Find-ByName 'Next page'
                if (-not $next -or $next.Current.IsOffscreen) {
                    throw "The PDF reader has no visible 'Next page' command."
                }
                # No waiting between turns: superseded pages must be dropped.
                for ($i = 0; $i -lt $count; $i++) { Invoke-Element $next }
            }

            # Tagged text is readable through UI Automation beside its preview.
            Open-TextGuide 'Tagged PDF Guide'
            [void](Wait-Status 'Guide ready.')
            $preview = Wait-PdfPage 1 1 'Tagged guide paragraph for Narrator'
            $bounds = $preview.Current.BoundingRectangle
            if ($bounds.Width -lt 1 -or $bounds.Height -lt 1) {
                throw 'The PDF page preview has no visible size.'
            }
            $textStatus = Get-PdfStatus 'PdfTextStatus'
            if ($textStatus) { throw "Expected no text status for tagged text; saw '$textStatus'." }
            $previewStatus = Get-PdfStatus 'PdfPreviewStatus'
            if ($previewStatus) { throw "Expected no preview status; saw '$previewStatus'." }
            Assert-Absent 'ReaderPlaceholder'
            Assert-Absent 'ReaderLoadError'
            $report.pdfReaderScreenshot = Save-WindowScreenshot 'pdf-reader'

            # Narrow windows stack the text under the preview; both stay shown.
            Resize-ShellWindow 600 720
            [void](Wait-PdfPage 1 1 'Tagged guide paragraph for Narrator')
            $report.pdfReaderNarrowScreenshot = Save-WindowScreenshot 'pdf-reader-narrow'
            Resize-ShellWindow 1500 720
            $report.phases += 'pdf-access'

            # An image-only page says so and claims no text.
            Back-ToTextGame
            Open-TextGuide 'Scanned PDF Guide'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-Name 'PdfPageStatus' 'Page 1 of 1')
            [void](Wait-Name 'PdfTextStatus' 'Image-only page; OCR is unavailable')
            $scanText = Get-PdfText
            if ($scanText) { throw "Expected no text for an image-only page; read '$scanText'." }
            $report.phases += 'pdf-scan'

            # 199 rapid turns end on page 200 with its own preview and text,
            # then Start and End, then two more full sweeps.
            Back-ToTextGame
            Open-TextGuide 'Long PDF Guide'
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            Invoke-NextPages 199
            [void](Wait-PdfPage 200 200 'page 200 of 200' 60)
            Invoke-ReaderCommand 'Go to start'
            [void](Wait-PdfPage 1 200 'page 1 of 200')
            Invoke-ReaderCommand 'Go to end'
            [void](Wait-PdfPage 200 200 'page 200 of 200')
            foreach ($sweep in 2..3) {
                Invoke-ReaderCommand 'Go to start'
                [void](Wait-PdfPage 1 200 'page 1 of 200')
                Invoke-NextPages 199
                [void](Wait-PdfPage 200 200 'page 200 of 200' 60)
            }
            $report.phases += 'pdf-long'

            # Going back closes the session, which writes the diagnostics.
            Back-ToTextGame
            $damaged = "This PDF is damaged, so it can't be opened. Re-import it from the original file."
            Open-TextGuide 'Damaged PDF Guide'
            [void](Wait-Status $damaged)
            [void](Wait-Name 'ReaderLoadError' $damaged)
            Assert-Absent 'ReaderLoadErrorAction'
            Assert-Absent 'PdfDocumentText'
            $report.pdfErrorScreenshot = Save-WindowScreenshot 'pdf-error'
            $report.phases += 'pdf-damaged'

            Back-ToTextGame
            Open-TextGuide 'Missing PDF Guide'
            [void](Wait-Status $missingMessage)
            [void](Wait-Name 'ReaderLoadError' $missingMessage)
            Assert-Absent 'ReaderLoadErrorAction'
            Assert-Absent 'PdfDocumentText'
            $report.phases += 'pdf-missing'

            # TXT guides still open after the PDF errors.
            Back-ToTextGame
            Open-TextGuide 'Plain Text Guide'
            [void](Wait-Status 'Guide ready.')
            Assert-RowNames 'Plain Text Guide' $asciiNames
            Assert-Absent 'ReaderLoadError'
            $report.phases += 'pdf-txt'
            Back-ToTextGame
        }
```

- [ ] **Step 3: Wire the scenarios into the installer**

In `Run-ShellSmoke`, change the timeout's first branch to:

```powershell
    $timeoutSeconds = if ($mode -like 'provider-*' -or $mode -like 'pdf-*') { 240 }
```

After `Run-HtmlReaderScenarios`, add:

```powershell
function Invoke-PdfReaderPass([string] $resultName) {
    Start-InstalledShell
    $diagnosticsGate = [System.Threading.EventWaitHandle]::new(
        $false, [System.Threading.EventResetMode]::ManualReset,
        "Local\DesktopGuides.Preview.PdfDiagnostics.$($report.launchedProcessId)")
    try {
        $report.pdfReader[$resultName] = Run-ShellSmoke 'pdf-reader' -ResultName $resultName
        Close-InstalledShell
    }
    finally {
        $diagnosticsGate.Dispose()
    }
}

function Assert-PdfDiagnostics([string] $pass, [string] $diagnostics, [string] $guideId) {
    # Counts only; the file holds no guide text and no paths.
    $path = Join-Path $diagnostics "pdf-$guideId.json"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "The $pass pass wrote no diagnostics for the long PDF guide."
    }
    Copy-Item -LiteralPath $path -Destination (Join-Path $ResultDirectory "$pass.pdf-long.json")
    $counts = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($counts.maxCacheBytes -ne 100663296) {
        throw "The $pass pass used a $($counts.maxCacheBytes)-byte render cap; expected 100663296."
    }
    if ($counts.peakCacheBytes -gt 100663296) {
        throw "The $pass pass cached $($counts.peakCacheBytes) bytes of page images, over the cap."
    }
    if ($counts.cachedPagesAtClose -ge 200) {
        throw "The $pass pass still held $($counts.cachedPagesAtClose) page images at close."
    }
    if ($counts.staleResults -le 0 -and $counts.loads -ge $counts.requests) {
        throw "The $pass pass dropped no superseded page: $($counts.loads) loads for $($counts.requests) requests."
    }
    if ($counts.peakTextPages -gt 8) {
        throw "The $pass pass kept the text of $($counts.peakTextPages) pages; expected at most 8."
    }
    if (-not $counts.disposedCleanly) {
        throw "The $pass pass did not close the long PDF guide cleanly."
    }
    return $counts
}

function Run-PdfReaderScenarios {
    # TR10.2-TR10.3: tagged text in UI Automation, an image-only page, 200
    # rapid page turns with a bounded cache, typed errors, and TXT still
    # opening. Light then dark.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'p0\generated\pdf-long.pdf'))) {
        throw 'pdf-long.pdf is missing; run tools/p0/make_fixtures.py first.'
    }
    $ids = Invoke-ShellSeed @('seed-pdf-reader', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    $report.pdfReader = [ordered]@{ pdfLong = $ids.pdfLong }
    $originalTheme = Get-AppThemePreference
    try {
        foreach ($pass in @(
            @{ name = 'pdf-reader-light'; light = $true },
            @{ name = 'pdf-reader-dark'; light = $false })) {
            Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
            Set-AppThemePreference $pass.light
            Invoke-PdfReaderPass $pass.name
            $report.pdfReader["$($pass.name)-diagnostics"] = Assert-PdfDiagnostics $pass.name $diagnostics $ids.pdfLong
        }
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}
```

In the main sequence, after `Run-HtmlReaderScenarios`, add:

```powershell

    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-PdfReaderScenarios
```

Confirm both PowerShell files are still ASCII-only:
`LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1`
Expected: no output.

- [ ] **Step 4: Run the smoke and confirm it fails**

Push, then trigger CI.
Expected: `production-shell-ui` fails in `pdf-reader-light`, at the
`Tagged PDF Guide` step, with "Expected Page 1 of 1 with its preview, and
text containing 'Tagged guide paragraph for Narrator'." The PDF guide still
shows the placeholder. If it fails earlier (in the seed, or with a
ValidateSet error), fix the seed or the script before going on. The failure
must come from the missing view.

- [ ] **Step 5: Commit**

Commit before the push in Step 4:

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs \
  tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): T10.1 installed PDF reader smoke"
```

---

### Task 8: PDF view, session and shell routing (GREEN)

**Files:**
- Create: `src/DesktopGuides.Production/PdfReaderView.xaml`
- Create: `src/DesktopGuides.Production/PdfReaderView.xaml.cs`
- Create: `src/DesktopGuides.Production/PdfReaderSession.cs`
- Create: `src/DesktopGuides.Production/ShellWindow.PdfReader.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs`:
  - field after `htmlLoader` (line 77): `private ManagedPdfGuideLoader? pdfLoader;`;
  - after `htmlLoader = new ManagedHtmlGuideLoader(repository, paths);`
    (line 275): `pdfLoader = new ManagedPdfGuideLoader(paths);`;
  - in `RenderCurrentAsync` (about line 1665), replace the
    `// PDF keeps the placeholder until T10.` block with the PDF branch.
- Test: Task 7's installed smoke (`production-shell-ui`).

**Interfaces:**
- Consumes:
  - Task 1: `PdfRenderCache<TImage>`, `PdfRenderKey`;
  - Task 2: `PdfRasterBudget.WidthFor`, `PdfRasterBudget.MaxBytes`, `PdfRasterWidth`;
  - Task 3: `LatestWinsScheduler<TResult>`;
  - Task 4: `PdfGuideLoadError`, `PdfGuideLoadMessages`, `PdfLocationRules`,
    `PdfRestore`, `PdfSessionDiagnostics`;
  - Task 5: `PdfPageText`, `PdfPageTextException`, `PdfPageTextSource`
    (`PageCount`, `PeakPages`, `PeakCharacters`, `GetPageTextAsync`, `Dispose`);
  - Task 6: `ManagedPdfGuideLoader`, `PdfGuideLoad`, `PdfGuideLoaded`, `PdfGuideLoadFailed`;
  - Task 7: the AutomationIds, the gate name and the diagnostics path;
  - shell: `ShowReaderSurface`, `ShowWarningStatus`, `ShowTransientStatus`,
    `RunNavigationAsync`, `ReaderActions.SetSession`, `readerLoad`,
    `readerSession`, `renderGeneration`, `cacheRoot`, `TestGate.IsOpen`.
- Produces:
  - `internal sealed class PdfGuideLoadException(PdfGuideLoadError error) : Exception`, with `Error`;
  - `internal sealed class PdfReaderSession : IReaderSession`, with
    constructor `(Guide guide, PdfGuideLoaded loaded, string cacheRoot, bool writeDiagnostics)`,
    `PdfReaderView View`, `event EventHandler<PdfGuideLoadError>? Failed`
    and `static bool DiagnosticsEnabledForTest()`;
  - `public sealed partial class PdfReaderView : UserControl`, with
    `double PreviewWidth`, `event EventHandler? PreviewSizeChanged`,
    `void ShowPage(int index, int count, ImageSource? image, PdfPageText? text)`
    and `void Clear()`.
- The session owns `loaded.Text` from construction and disposes it in
  `DisposeAsync`.

Production has no unit test host (R7). Task 7's smoke is this task's
failing test; Step 1 is already done.

- [ ] **Step 1: Confirm the failing test**

Task 7's CI run failed at the `Tagged PDF Guide` step on the placeholder.
Nothing to write here.

- [ ] **Step 2: Write the view**

`src/DesktopGuides.Production/PdfReaderView.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl x:Class="DesktopGuides.Production.PdfReaderView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition x:Name="TextColumn" Width="*" />
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition x:Name="TextRow" Height="0" />
        </Grid.RowDefinitions>
        <TextBlock x:Name="PageStatus"
                   Grid.ColumnSpan="2"
                   Margin="0,0,0,12"
                   AutomationProperties.AutomationId="PdfPageStatus"
                   Style="{StaticResource DesktopGuidesBodyStyle}"
                   Foreground="{ThemeResource DesktopGuidesPrimaryTextBrush}" />
        <Grid x:Name="PreviewPane" Grid.Row="1">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <TextBlock x:Name="PreviewStatus"
                       Visibility="Collapsed"
                       Margin="0,0,0,12"
                       TextWrapping="Wrap"
                       AutomationProperties.AutomationId="PdfPreviewStatus"
                       Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                       Foreground="{ThemeResource DesktopGuidesSecondaryTextBrush}" />
            <ScrollViewer x:Name="PreviewScroller"
                          Grid.Row="1"
                          HorizontalScrollBarVisibility="Disabled"
                          VerticalScrollBarVisibility="Auto">
                <Image x:Name="Preview"
                       AutomationProperties.AutomationId="PdfPreviewImage"
                       Stretch="Uniform"
                       VerticalAlignment="Top" />
            </ScrollViewer>
        </Grid>
        <Grid x:Name="TextPane" Grid.Row="1" Grid.Column="1" Margin="16,0,0,0">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <TextBlock x:Name="TextStatus"
                       Visibility="Collapsed"
                       Margin="0,0,0,12"
                       TextWrapping="Wrap"
                       AutomationProperties.AutomationId="PdfTextStatus"
                       Style="{StaticResource DesktopGuidesSecondaryBodyStyle}"
                       Foreground="{ThemeResource DesktopGuidesSecondaryTextBrush}" />
            <TextBox x:Name="DocumentText"
                     Grid.Row="1"
                     IsReadOnly="True"
                     TextWrapping="Wrap"
                     AcceptsReturn="True"
                     IsSpellCheckEnabled="False"
                     ScrollViewer.VerticalScrollBarVisibility="Auto"
                     AutomationProperties.AutomationId="PdfDocumentText" />
        </Grid>
    </Grid>
</UserControl>
```

`src/DesktopGuides.Production/PdfReaderView.xaml.cs`:

```csharp
using DesktopGuides.Infrastructure.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production;

// One PDF page: its preview beside its text, or the text under the preview
// below 720 effective pixels. The session decides what to show; a new page
// never moves keyboard focus.
public sealed partial class PdfReaderView : UserControl
{
    private const double NarrowWidth = 720;
    private bool? narrow;

    public PdfReaderView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        PreviewScroller.SizeChanged += (_, _) => PreviewSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? PreviewSizeChanged;

    // The width a preview fills, in effective pixels; 0 before layout.
    public double PreviewWidth => PreviewScroller.ActualWidth;

    public void ShowPage(int index, int count, ImageSource? image, PdfPageText? text)
    {
        string page = $"Page {index + 1} of {count}";
        PageStatus.Text = page;
        Preview.Source = image;
        AutomationProperties.SetName(Preview, page + " preview");
        SetStatus(PreviewStatus, image is null ? "This page's preview couldn't be shown." : null);
        AutomationProperties.SetName(DocumentText, $"Page text, page {index + 1} of {count}");
        DocumentText.Text = text is { HasLetters: true } ? text.Text : string.Empty;
        SetStatus(TextStatus, text switch
        {
            null => "This page's text couldn't be read.",
            { HasLetters: false } => "Image-only page; OCR is unavailable",
            { Truncated: true } => "Page text is shortened; it's too long to show in full.",
            _ => null,
        });
        PreviewScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    public void Clear()
    {
        Preview.Source = null;
        DocumentText.Text = string.Empty;
    }

    private static void SetStatus(TextBlock status, string? message)
    {
        status.Text = message ?? string.Empty;
        status.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool isNarrow = args.NewSize.Width < NarrowWidth;
        if (narrow == isNarrow) return;
        narrow = isNarrow;
        Grid.SetRow(TextPane, isNarrow ? 2 : 1);
        Grid.SetColumn(TextPane, isNarrow ? 0 : 1);
        TextPane.Margin = isNarrow ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
        TextColumn.Width = isNarrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        TextRow.Height = isNarrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }
}
```

- [ ] **Step 3: Write the session**

`src/DesktopGuides.Production/PdfReaderSession.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace DesktopGuides.Production;

internal sealed class PdfGuideLoadException(PdfGuideLoadError error)
    : Exception(PdfGuideLoadMessages.For(error))
{
    public PdfGuideLoadError Error { get; } = error;
}

// The Reader's session for one PDF guide: a Windows.Data.Pdf preview of the
// current page beside PdfPig's text of the same page. Loads run one at a
// time and the latest wins, so fast page turns never show one page's text
// beside another page's preview.
internal sealed class PdfReaderSession : IReaderSession
{
    private const int FailuresBeforeStop = 3;
    // ERROR_WRONG_PASSWORD. Import rejects encrypted PDFs, so this is a
    // managed copy replaced after import.
    private const int WrongPassword = unchecked((int)0x8007052B);
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(150);
    private static readonly PageResult BothFailed = new(null, null, 0, double.NaN);
    private readonly Guide guide;
    private readonly string filePath;
    private readonly PdfPageTextSource text;
    private readonly string cacheRoot;
    private readonly bool writeDiagnostics;
    private readonly PdfRenderCache<BitmapImage> cache = new(PdfRasterBudget.MaxBytes);
    private readonly LatestWinsScheduler<PageResult> scheduler;
    private FileStream? file;
    private IRandomAccessStream? stream;
    private WinPdf.PdfDocument? document;
    private int pageCount;
    private CancellationTokenSource? resizeDelay;
    private int target;
    private int displayed;
    private int appliedWidth;
    private double appliedAspect = double.NaN;
    private int failedPages;
    private bool failed;
    private bool disposed;

    public PdfReaderSession(Guide guide, PdfGuideLoaded loaded, string cacheRoot, bool writeDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        this.guide = guide;
        filePath = loaded.FilePath;
        text = loaded.Text;
        this.cacheRoot = cacheRoot;
        this.writeDiagnostics = writeDiagnostics;
        scheduler = new LatestWinsScheduler<PageResult>(
            LoadPageAsync, Apply, (index, _) => Apply(index, BothFailed));
        View = new PdfReaderView();
        View.PreviewSizeChanged += OnPreviewSizeChanged;
    }

    public PdfReaderView View { get; }
    public GuideFormat Format => GuideFormat.Pdf;
    public ReaderCapabilities Capabilities => ReaderCapabilities.PageNavigation;

    // Capabilities don't change during a PDF session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;
    // Raised once, with Failed, after three pages in a row fail both ways.
    public event EventHandler<PdfGuideLoadError>? Failed;

    public static bool DiagnosticsEnabledForTest() =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.PdfDiagnostics.{Environment.ProcessId}");

    public async Task OpenAsync(ManagedGuideSource source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Guide.Id != guide.Id || source.PrimaryFilePath != filePath)
        {
            throw new ArgumentException("The source is a different guide.", nameof(source));
        }
        ObjectDisposedException.ThrowIf(disposed, this);
        if (document is not null)
        {
            throw new InvalidOperationException("The guide is already open.");
        }
        // The loader already resolved and checked this path; it isn't
        // resolved again, and no StorageFile is used.
        try
        {
            file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream = file.AsRandomAccessStream();
            document = await WinPdf.PdfDocument.LoadFromStreamAsync(stream).AsTask(token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new PdfGuideLoadException(OpenError(error));
        }
        if (document.PageCount != text.PageCount)
        {
            throw new PdfGuideLoadException(PdfGuideLoadError.Damaged);
        }
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        pageCount = (int)document.PageCount;
        target = 0;
        scheduler.Request(0);
    }

    private static PdfGuideLoadError OpenError(Exception error) => error switch
    {
        FileNotFoundException or DirectoryNotFoundException => PdfGuideLoadError.Missing,
        IOException or UnauthorizedAccessException => PdfGuideLoadError.Unreadable,
        _ when error.HResult == WrongPassword => PdfGuideLoadError.PasswordProtected,
        _ => PdfGuideLoadError.Damaged,
    };

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (document is null) throw new InvalidOperationException("The guide isn't open.");
        return Task.FromResult(PdfLocationRules.Capture(guide.ContentSha256, displayed, pageCount));
    }

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        token.ThrowIfCancellationRequested();
        if (document is null) throw new InvalidOperationException("The guide isn't open.");
        PdfRestore restore = PdfLocationRules.Restore(location, guide.ContentSha256, pageCount);
        if (!disposed && restore.Outcome.Kind != RestoreKind.Unavailable)
        {
            GoTo(restore.PageIndex);
        }
        return Task.FromResult(restore.Outcome);
    }

    // T10.2 adds fit-width and zoom; the preview has no theme of its own.
    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        token.ThrowIfCancellationRequested();
        if (disposed) return Task.CompletedTask;
        if (document is null) throw new InvalidOperationException("The guide isn't open.");
        switch (action)
        {
            // Turns build on the wanted page, not the shown one, so ten fast
            // Next clicks move ten pages.
            case PageTurnAction turn:
                GoTo((long)target + turn.Delta);
                break;
            case PageEdgeAction edge:
                GoTo(edge.Edge == ReaderEdge.Start ? 0 : pageCount - 1);
                break;
            default:
                throw new NotSupportedException($"PDF guides don't support {action.Command}.");
        }
        return Task.CompletedTask;
    }

    private void GoTo(long index)
    {
        int clamped = (int)Math.Clamp(index, 0, pageCount - 1);
        if (clamped == target) return;
        target = clamped;
        scheduler.Request(clamped);
    }

    // Text extraction runs on a worker while the raster renders on the UI
    // thread; both finish before the result is offered, so a cancellation
    // surfaces only once neither is still using the document.
    private async Task<PageResult> LoadPageAsync(int index, CancellationToken token)
    {
        Task<PdfPageText?> textTask = ReadTextAsync(index, token);
        Task<Raster> rasterTask = RenderAsync(index, token);
        await Task.WhenAll(textTask, rasterTask);
        Raster raster = await rasterTask;
        return new PageResult(await textTask, raster.Image, raster.Width, raster.Aspect);
    }

    private async Task<PdfPageText?> ReadTextAsync(int index, CancellationToken token)
    {
        try
        {
            return await text.GetPageTextAsync(index, token);
        }
        catch (PdfPageTextException)
        {
            return null;
        }
    }

    private async Task<Raster> RenderAsync(int index, CancellationToken token)
    {
        int width = 0;
        double aspect = double.NaN;
        try
        {
            using WinPdf.PdfPage page = document!.GetPage((uint)index);
            aspect = page.Size.Height / page.Size.Width;
            PdfRasterWidth raster = WidthFor(page.Size.Width, aspect);
            if (raster.IsTooLarge) return new Raster(null, 0, aspect);
            width = raster.Width;
            PdfRenderKey key = new(guide.ContentSha256, index, width);
            if (cache.TryGet(key, out BitmapImage? hit)) return new Raster(hit, width, aspect);
            using InMemoryRandomAccessStream png = new();
            await page.RenderToStreamAsync(
                png, new WinPdf.PdfPageRenderOptions { DestinationWidth = (uint)width }).AsTask(token);
            png.Seek(0);
            BitmapImage image = new();
            await image.SetSourceAsync(png).AsTask(token);
            // Measured from the decoded image, not from the requested width.
            cache.Add(key, image, image.PixelWidth, image.PixelHeight);
            return new Raster(image, width, aspect);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A page the engine can't draw fails on its own; the next may work.
            return new Raster(null, width, aspect);
        }
    }

    // Before layout the preview has no width, so the page's own width stands in.
    private PdfRasterWidth WidthFor(double pageWidth, double aspect)
    {
        double display = View.PreviewWidth >= 1 ? View.PreviewWidth : pageWidth;
        double scale = View.XamlRoot?.RasterizationScale ?? 1;
        return PdfRasterBudget.WidthFor(display * scale, aspect, PdfRasterBudget.MaxBytes);
    }

    // Runs on the UI thread, only for the newest request.
    private void Apply(int index, PageResult result)
    {
        if (disposed || failed) return;
        displayed = index;
        appliedWidth = result.RasterWidth;
        appliedAspect = result.Aspect;
        View.ShowPage(index, pageCount, result.Image, result.Text);
        failedPages = result.Image is null && result.Text is null ? failedPages + 1 : 0;
        LocationChanged?.Invoke(this, new LocationChangedEventArgs());
        if (failedPages < FailuresBeforeStop) return;
        failed = true;
        // Queued, so the shell disposes the session after this load returns.
        View.DispatcherQueue.TryEnqueue(() =>
        {
            if (!disposed) Failed?.Invoke(this, PdfGuideLoadError.Failed);
        });
    }

    // Re-renders the current page only when the raster width changes.
    private async void OnPreviewSizeChanged(object? sender, EventArgs args)
    {
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        CancellationTokenSource delay = new();
        resizeDelay = delay;
        try
        {
            await Task.Delay(ResizeDelay, delay.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (disposed || document is null || View.PreviewWidth < 1 || !double.IsFinite(appliedAspect))
        {
            return;
        }
        if (WidthFor(View.PreviewWidth, appliedAspect).Width != appliedWidth)
        {
            scheduler.Request(target);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        LocationChanged = null;
        Failed = null;
        View.PreviewSizeChanged -= OnPreviewSizeChanged;
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        resizeDelay = null;
        bool clean = true;
        try
        {
            await scheduler.CancelAsync();
        }
        catch (Exception)
        {
            // Recorded for the test diagnostics; closing goes on regardless.
            clean = false;
        }
        PdfSessionDiagnostics counts = new(
            scheduler.Requests, scheduler.Loads, scheduler.StaleResults,
            cache.PeakBytes, cache.MaxBytes, cache.Count,
            text.PeakPages, text.PeakCharacters, clean);
        View.Clear();
        cache.Clear();
        text.Dispose();
        stream?.Dispose();
        file?.Dispose();
        WriteDiagnostics(counts);
    }

    private void WriteDiagnostics(PdfSessionDiagnostics counts)
    {
        if (!writeDiagnostics) return;
        try
        {
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"pdf-{guide.Id:N}.json"), counts.ToJson());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must never affect closing a guide.
        }
    }

    private sealed record Raster(BitmapImage? Image, int Width, double Aspect);

    private sealed record PageResult(PdfPageText? Text, BitmapImage? Image, int RasterWidth, double Aspect);
}
```

- [ ] **Step 4: Write the shell routing**

`src/DesktopGuides.Production/ShellWindow.PdfReader.cs`:

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // Returns false when a newer render took over, like the TXT path.
    private async Task<bool> OpenPdfGuideAsync(Guide guide, int generation)
    {
        ShowReaderSurface(placeholder: false);
        readerLoad = new CancellationTokenSource();
        CancellationToken token = readerLoad.Token;
        PdfGuideLoad load;
        try
        {
            load = await pdfLoader!.LoadAsync(guide, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        if (generation != renderGeneration)
        {
            // No session owns the text source yet.
            (load as PdfGuideLoaded)?.Text.Dispose();
            return false;
        }
        if (load is PdfGuideLoadFailed failed)
        {
            ShowPdfLoadError(failed.Error);
            return true;
        }
        PdfGuideLoaded loaded = (PdfGuideLoaded)load;
        PdfReaderSession session = new(guide, loaded, cacheRoot!, PdfReaderSession.DiagnosticsEnabledForTest());
        // The next render disposes it if this one is cancelled.
        readerSession = session;
        session.Failed += OnPdfSessionFailed;
        ShowReaderSurface(placeholder: false, view: session.View);
        try
        {
            await session.OpenAsync(new ManagedGuideSource(guide, loaded.FilePath), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (PdfGuideLoadException error)
        {
            if (generation != renderGeneration)
            {
                return false;
            }
            readerSession = null;
            ShowReaderSurface(placeholder: false);
            await session.DisposeAsync();
            ShowPdfLoadError(error.Error);
            return true;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        ReaderActions.SetSession(session);
        ShowTransientStatus("Guide ready.");
        return true;
    }

    private void ShowPdfLoadError(PdfGuideLoadError error)
    {
        string message = PdfGuideLoadMessages.For(error);
        ShowReaderSurface(placeholder: false, error: message, action: PdfGuideLoadMessages.ActionFor(error));
        ShowWarningStatus(message);
    }

    // A late event from a session that a newer render replaced is ignored.
    private async void OnPdfSessionFailed(object? sender, PdfGuideLoadError error)
    {
        await RunNavigationAsync(async () =>
        {
            if (!ReferenceEquals(sender, readerSession))
            {
                return;
            }
            IReaderSession failed = readerSession!;
            readerSession = null;
            ReaderActions.SetSession(null);
            ShowReaderSurface(placeholder: false);
            await failed.DisposeAsync();
            ShowPdfLoadError(error);
        });
    }
}
```

In `ShellWindow.xaml.cs`, add the field and the loader:

```csharp
    private ManagedHtmlGuideLoader? htmlLoader;
    private ManagedPdfGuideLoader? pdfLoader;
```

```csharp
            htmlLoader = new ManagedHtmlGuideLoader(repository, paths);
            pdfLoader = new ManagedPdfGuideLoader(paths);
```

In `RenderCurrentAsync`, replace:

```csharp
                    // PDF keeps the placeholder until T10.
                    if (guide.Format != GuideFormat.Txt)
                    {
                        ShowTransientStatus("Guide ready.");
                        break;
                    }
```

with:

```csharp
                    if (guide.Format == GuideFormat.Pdf)
                    {
                        if (!await OpenPdfGuideAsync(guide, generation))
                        {
                            return false;
                        }
                        break;
                    }
```

The Reopen action in `ReaderLoadErrorActionClicked` re-runs
`RenderCurrentAsync` for any format, so `Unreadable` and `Failed` need no
new handler.

- [ ] **Step 5: Run the smoke and confirm it passes**

Push, then trigger CI.
Expected:
- every job is green;
- `production-shell-ui` reports the phases `pdf-access`, `pdf-scan`,
  `pdf-long`, `pdf-damaged`, `pdf-missing` and `pdf-txt` in both
  `pdf-reader-light` and `pdf-reader-dark`;
- the artifact holds `pdf-reader-light.pdf-long.json` and
  `pdf-reader-dark.pdf-long.json`, with `maxCacheBytes` 100663296,
  `peakCacheBytes` at most that, `disposedCleanly` true, and either
  `staleResults` > 0 or `loads` < `requests`;
- the artifact holds the `pdf-reader`, `pdf-reader-narrow` and `pdf-error`
  screenshots for both passes.

If a check fails, use superpowers:systematic-debugging before changing
anything. Read the smoke's result JSON and screenshots in the artifact
first.

- [ ] **Step 6: Commit**

Commit before the push in Step 5:

```bash
git add src/DesktopGuides.Production/PdfReaderView.xaml \
  src/DesktopGuides.Production/PdfReaderView.xaml.cs \
  src/DesktopGuides.Production/PdfReaderSession.cs \
  src/DesktopGuides.Production/ShellWindow.PdfReader.cs \
  src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(p1): T10.1 PDF reader view and session"
```

---

### Task 9: Docs and evidence

**Files:**
- Modify: `docs/p1/t10-1-pdf-adapter-design.md` (status line; append
  "Implementation notes" and "Verification")
- Modify: `docs/p1/implementation-plan.md` (after the T09.1 paragraph, about
  line 815)
- Modify: `docs/work-breakdown.md` (the T10.1 bullet, about line 320)
- Modify: `docs/p1-technical-design.md` (the T10.1 bullet, about line 703)
- Create: `docs/p1/evidence/t10-1-pdf-adapter/` holding
  `pdf-reader-light.pdf-long.json`, `pdf-reader-dark.pdf-long.json`,
  `pdf-reader-light.png`, `pdf-reader-dark.png`,
  `pdf-reader-narrow-light.png`, `pdf-reader-narrow-dark.png`,
  `pdf-error-light.png` and `pdf-error-dark.png`

**Interfaces:**
- Consumes: Task 8's green CI run ID (`<RUN>` below) and its
  `production-shell-ui` artifact.
- Produces: docs only.

The design's Docs section asks for the T09.1 paragraph to change to
"merged". That change is already on main, at
`docs/p1/implementation-plan.md` line 810, so this task leaves it alone.

This task is mechanical (docs and copied artifacts), so it has no TDD
cycle.

- [ ] **Step 1: Copy the evidence from the CI artifact**

```bash
RUN=<run id from Task 8 Step 5>
rm -rf /tmp/t10-1-artifact && mkdir -p /tmp/t10-1-artifact
gh run download "$RUN" --dir /tmp/t10-1-artifact
find /tmp/t10-1-artifact -name 'pdf-*' | sort
```

Expected: the two `.pdf-long.json` files plus six PNGs, under the
`pdf-reader-light` and `pdf-reader-dark` result folders.

Copy them into `docs/p1/evidence/t10-1-pdf-adapter/`, renaming each PNG to
`<screenshot>-<light|dark>.png`. Then check that the diagnostics hold
counts only:

```bash
cat docs/p1/evidence/t10-1-pdf-adapter/*.json
```

Expected: only the nine keys from `PdfSessionDiagnostics.ToJson()`. No
path, and no guide text.

Open each PNG and confirm that it shows the Reader. The side-by-side
screenshot should show the preview on the left and the text on the right;
the narrow one should show the text below the preview; the error one should
show the Damaged message with no action button.

- [ ] **Step 2: Update the design doc**

Replace the status line in `docs/p1/t10-1-pdf-adapter-design.md`:

```markdown
Status: implemented in PR #36; CI run <RUN> passed the installed
`pdf-reader` scenario in light and dark.
```

`#36` is filled in once the PR exists, as for T09.1.

Append the following, replacing each `<...>` with the value from
`<RUN>`'s light-pass JSON:

```markdown
## Implementation notes

Planning rulings, from the
[implementation plan](t10-1-pdf-adapter-plan.md):

- **R1.** `PdfGuideLoadMessages.ActionFor` returns the existing
  `HtmlGuideLoadAction`, so the Reader error surface and its Reopen button
  are shared rather than duplicated.
- **R2.** `PdfLocationRules` validates the decoded locator itself; the
  session holds no codec logic.
- **R3.** `CancellableReadStream` has a settable `Token`, so each text
  extraction can be cancelled through the import's stream wrapper.
- **R4.** The loader reuses the import's `%PDF` header check.
- **R5.** Diagnostics are a Core record (`PdfSessionDiagnostics`) with a
  unit-tested JSON shape.
- **R6.** The narrow layout is switched in code-behind, not with
  `VisualStateManager`; no XAML in the repo uses visual states yet.
- **R7.** Production has no unit test host, so its RED step is the
  installed smoke (Task 7), made GREEN by the view and the session
  (Task 8).

Execution notes:

- The session is `PdfReaderSession`, not the `PdfReaderAdapter` named in
  the P1 technical design; it implements `IReaderSession` like the TXT and
  HTML sessions.
- RED was observed in CI, as for T09.1. No local .NET toolchain was used.

## Verification

CI run <RUN> passed `core-tests` and the installed `production-shell-ui`
`pdf-reader` scenario in light and dark. The light pass's `pdf-long`
diagnostics after three sweeps: `requests` <R>, `loads` <L>, `staleResults`
<S>, `peakCacheBytes` <P> of 100663296, `cachedPagesAtClose` <C>,
`peakTextPages` <T>, `disposedCleanly` true
([light](evidence/t10-1-pdf-adapter/pdf-reader-light.pdf-long.json),
[dark](evidence/t10-1-pdf-adapter/pdf-reader-dark.pdf-long.json)).

Screenshots:

- [Side by side, light](evidence/t10-1-pdf-adapter/pdf-reader-light.png)
- [Side by side, dark](evidence/t10-1-pdf-adapter/pdf-reader-dark.png)
- [Narrow, light](evidence/t10-1-pdf-adapter/pdf-reader-narrow-light.png)
- [Narrow, dark](evidence/t10-1-pdf-adapter/pdf-reader-narrow-dark.png)
- [Damaged, light](evidence/t10-1-pdf-adapter/pdf-error-light.png)
- [Damaged, dark](evidence/t10-1-pdf-adapter/pdf-error-dark.png)
```

- [ ] **Step 3: Update the plan docs**

In `docs/p1/implementation-plan.md`, add after the T09.1 paragraph:

```markdown
T10.1 is in review in PR #36 (CI run <RUN>); see the
[design and implementation notes](t10-1-pdf-adapter-design.md). PDF guides
open in the Reader as a `Windows.Data.Pdf` preview beside the same page's
PdfPig text in a read-only text box with a UI Automation `TextPattern`.
Rendered pages are cached by measured bytes under 96 MiB, page loads run
one at a time with the newest request winning, and damaged, missing,
changed, unreadable and encrypted managed copies show typed errors.
```

In `docs/work-breakdown.md`, change the T10.1 bullet to:

```markdown
- **T10.1** Render and recycle pages with bounded cache/memory use.
  Implemented in PR #36; see
  [p1/t10-1-pdf-adapter-design.md](p1/t10-1-pdf-adapter-design.md).
```

In `docs/p1-technical-design.md`, append to the T10.1 bullet (after "Test
200-page turns and a long session without retaining every page."):

```markdown
  See the [T10.1 design](p1/t10-1-pdf-adapter-design.md). The adapter ships
  as `PdfReaderSession : IReaderSession`; Core holds the cache, raster budget
  and latest-wins scheduler, and Infrastructure holds the managed-copy loader
  and PdfPig text.
```

- [ ] **Step 4: Check the links**

```bash
for f in docs/p1/t10-1-pdf-adapter-design.md; do
  grep -o '](evidence/[^)]*)' "$f" | sed 's/](\(.*\))/\1/' | while read -r p; do
    test -f "docs/p1/$p" || echo "missing: $p"; done; done
```

Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add docs/p1/t10-1-pdf-adapter-design.md docs/p1/implementation-plan.md \
  docs/work-breakdown.md docs/p1-technical-design.md docs/p1/evidence/t10-1-pdf-adapter
git commit -m "docs(p1): T10.1 implementation notes and evidence"
```

Once the PR exists, replace `#36` with its number in a follow-up commit,
`docs(p1): T10.1 PR and CI run references`.
