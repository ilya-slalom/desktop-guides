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
        Assert.Equal(1, cache.Evictions);
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
        Assert.Equal(0, cache.Evictions);
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
        Assert.True(cache.Evictions > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCapIsRejected(long maxBytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfRenderCache<FakeImage>(maxBytes));
}
