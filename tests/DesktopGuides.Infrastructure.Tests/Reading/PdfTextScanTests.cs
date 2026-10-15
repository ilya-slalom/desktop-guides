using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using DesktopGuides.Infrastructure.Tests.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class PdfTextScanTests
{
    // A null page can't be read. HoldAt makes one page wait for Release.
    private sealed class FakePages(params PdfPageText?[] pages) : IPdfPageTextReader
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int? HoldAt { get; init; }
        public int PageCount => pages.Length;
        public void Release() => gate.TrySetResult();

        public async Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token)
        {
            if (HoldAt == pageIndex) await gate.Task.WaitAsync(token);
            return pages[pageIndex] ?? throw new PdfPageTextException(pageIndex, new InvalidDataException("broken"));
        }
    }

    private static PdfPageText Page(string text) => new(text, text.Any(char.IsLetter), false);

    private static async Task<bool> StillWaiting(Task task)
    {
        await Task.Delay(200);
        return !task.IsCompleted;
    }

    [Fact]
    public async Task MatchesComeInPageOrderWithStableIndexes()
    {
        using PdfTextScan scan = new(new FakePages(Page("a key"), Page("none"), Page("key key")), new FindQuery("key"));
        await scan.Completion;

        Assert.Equal(0, await scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None));
        Assert.Equal(3, scan.ResultFor(0).Total);
        Assert.False(scan.ResultFor(0).TotalIsPartial);
        Assert.Equal(new PdfMatch(2, 4, 3), scan.MatchAt(2));
    }

    [Fact]
    public async Task PagesWithoutLettersAreCountedAsImageOnly()
    {
        using PdfTextScan scan = new(new FakePages(Page("key"), Page("   "), Page("12 34")), new FindQuery("key"));
        await scan.Completion;

        Assert.Equal(2, scan.ResultFor(0).UnsearchablePages);
    }

    [Fact]
    public async Task AnUnreadablePageIsRecordedAndTheScanGoesOn()
    {
        using PdfTextScan scan = new(new FakePages(Page("key"), null, Page("key")), new FindQuery("key"));
        await scan.Completion;

        FindResult result = scan.ResultFor(1);
        Assert.Equal(2, result.Total);
        Assert.Equal([2], result.UnreadablePages);
    }

    [Fact]
    public async Task NextWaitsForTheScanToReachAMatch()
    {
        FakePages pages = new(Page("x"), Page("x"), Page("key")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));

        Task<int?> step = scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None);

        Assert.True(await StillWaiting(step));
        Assert.True(scan.ResultFor(null).TotalIsPartial);
        pages.Release();
        Assert.Equal(0, await step);
        Assert.Equal(2, scan.MatchAt(0).PageIndex);
    }

    [Fact]
    public async Task PreviousNeedsOnlyTheEarlierPages()
    {
        FakePages pages = new(Page("key"), Page("x"), Page("x")) { HoldAt = 2 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));

        Assert.Equal(0, await scan.StepAsync(null, 2, FindDirection.Previous, CancellationToken.None));
        pages.Release();
    }

    [Fact]
    public async Task WrappingWaitsForTheEnd()
    {
        FakePages pages = new(Page("key"), Page("x")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));
        Assert.Equal(0, await scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None));

        Task<int?> wrap = scan.StepAsync(0, 0, FindDirection.Next, CancellationToken.None);

        Assert.True(await StillWaiting(wrap));
        pages.Release();
        Assert.Equal(0, await wrap);
    }

    [Fact]
    public async Task NoMatchesIsAnsweredOnlyWhenTheScanIsDone()
    {
        FakePages pages = new(Page("x"), Page("x")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));

        Task<int?> step = scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None);

        Assert.True(await StillWaiting(step));
        pages.Release();
        Assert.Null(await step);
    }

    [Fact]
    public async Task DisposingStopsTheScanAndAWaitingStep()
    {
        FakePages pages = new(Page("x"), Page("x")) { HoldAt = 1 };
        PdfTextScan scan = new(pages, new FindQuery("key"));
        Task<int?> step = scan.StepAsync(null, 0, FindDirection.Next, CancellationToken.None);

        scan.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step);
        await scan.Completion;
    }

    [Fact]
    public async Task ACancelledStepLeavesTheScanRunning()
    {
        FakePages pages = new(Page("x"), Page("key")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));
        using CancellationTokenSource cancel = new();
        Task<int?> step = scan.StepAsync(null, 0, FindDirection.Next, cancel.Token);

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step);
        pages.Release();
        await scan.Completion;
        Assert.Equal(1, scan.ResultFor(0).Total);
    }

    [Fact]
    public async Task TheMatchCapEndsTheScan()
    {
        using PdfTextScan scan = new(new FakePages(Page("a a a"), Page("a a")), new FindQuery("a"), maxMatches: 3);
        await scan.Completion;

        Assert.Equal(3, scan.ResultFor(0).Total);
        Assert.False(scan.ResultFor(0).TotalIsPartial);
    }

    [Fact]
    public async Task FinishedIsRaisedOnceWhenTheLastPageIsDone()
    {
        int finished = 0;
        FakePages pages = new(Page("x"), Page("key")) { HoldAt = 1 };
        using PdfTextScan scan = new(pages, new FindQuery("key"));
        scan.Finished += (_, _) => Interlocked.Increment(ref finished);

        pages.Release();
        await scan.Completion;

        Assert.Equal(1, finished);
    }

    [Fact]
    public async Task ARealGuideIsScannedThroughItsOwnSource()
    {
        using PdfPageTextSource source = PdfPageTextSource.Open(
            File.OpenRead(P0Fixtures.Resolve("generated/pdf-long.pdf")), CancellationToken.None, maxPages: 1);
        using PdfTextScan scan = new(source, new FindQuery("page 150 of 200"));
        await scan.Completion;

        Assert.Equal(1, scan.ResultFor(0).Total);
        Assert.Equal(149, scan.MatchAt(0).PageIndex);
        Assert.Equal(1, source.PeakPages);
    }
}
