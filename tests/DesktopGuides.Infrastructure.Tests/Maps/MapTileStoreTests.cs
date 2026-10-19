using DesktopGuides.Core.Maps;
using DesktopGuides.Infrastructure.Maps;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Maps;

public sealed class MapTileStoreTests : IDisposable
{
    private static readonly string ShaA = new('a', 64);
    private static readonly string ShaB = new('b', 64);
    private static readonly string ShaC = new('c', 64);
    private static readonly MapPyramid Big = new(5000, 3000);

    private readonly string root = Path.Combine(Path.GetTempPath(), $"map-tiles-{Guid.NewGuid():N}");
    private readonly MapTileStore store;

    public MapTileStoreTests() => store = new MapTileStore(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void TheCapIsTwoGibibytes() => Assert.Equal(2_147_483_648, MapTileStore.MaxBytes);

    [Fact]
    public void ACommittedBuildIsComplete()
    {
        Build(store, ShaA, Big);
        Assert.True(store.IsComplete(ShaA, Big));
        Assert.True(File.Exists(store.TilePath(ShaA, new MapTileKey(1, 0, 0))));
        Assert.False(Directory.Exists(store.Folder(ShaA) + ".building"));
    }

    [Fact]
    public void NoCacheIsIncomplete() => Assert.False(store.IsComplete(ShaA, Big));

    // Review Focus 2.
    [Fact]
    public void AnInterruptedBuildIsPrunedAndNotComplete()
    {
        using (MapTileBuild build = store.BeginBuild(ShaA))
        {
            File.WriteAllBytes(build.OverviewPath, [1]);
        }
        Assert.False(store.IsComplete(ShaA, Big));
        Assert.False(Directory.Exists(store.Folder(ShaA) + ".building"));
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
    public void ACacheMissingItsOverviewIsIncomplete()
    {
        Build(store, ShaA, Big);
        File.Delete(store.OverviewPath(ShaA));
        Assert.False(store.IsComplete(ShaA, Big));
    }

    [Fact]
    public void AManifestForAnotherSizeIsIncomplete()
    {
        Build(store, ShaA, Big);
        Assert.False(store.IsComplete(ShaA, new MapPyramid(Big.Width + 1, Big.Height)));
    }

    [Fact]
    public void AnUnreadableManifestIsIncomplete()
    {
        Build(store, ShaA, Big);
        File.WriteAllText(Path.Combine(store.Folder(ShaA), "map-cache.json"), "{ not json");
        Assert.False(store.IsComplete(ShaA, Big));
    }

    [Fact]
    public void ARebuildReplacesTheOldCache()
    {
        Build(store, ShaA, Big);
        File.WriteAllText(Path.Combine(store.Folder(ShaA), "stale.txt"), "x");
        Build(store, ShaA, Big);
        Assert.True(store.IsComplete(ShaA, Big));
        Assert.False(File.Exists(Path.Combine(store.Folder(ShaA), "stale.txt")));
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
        long one = Build(store, ShaA, Big, fileBytes: 1024);
        MapTileStore small = new(root, maxBytes: 2 * one);
        Build(small, ShaB, Big, fileBytes: 1024);
        Build(small, ShaC, Big, fileBytes: 1024);
        Backdate(ShaA, hours: 3);
        Backdate(ShaB, hours: 2);
        Backdate(ShaC, hours: 1);

        long left = small.Trim(new HashSet<string> { ShaA });

        Assert.True(small.IsComplete(ShaA, Big));
        Assert.False(Directory.Exists(small.Folder(ShaB)));
        Assert.True(small.IsComplete(ShaC, Big));
        Assert.True(left <= 2 * one);
    }

    [Fact]
    public void TouchMakesAMapRecent()
    {
        long one = Build(store, ShaA, Big, fileBytes: 1024);
        Build(store, ShaB, Big, fileBytes: 1024);
        Backdate(ShaA, hours: 2);
        Backdate(ShaB, hours: 1);
        store.Touch(ShaA);
        new MapTileStore(root, maxBytes: one).Trim(new HashSet<string>());
        Assert.True(store.IsComplete(ShaA, Big));
        Assert.False(Directory.Exists(store.Folder(ShaB)));
    }

    [Theory]
    [InlineData("..\\..\\x")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("")]
    public void ABadHashBuildsNoPath(string sha) =>
        Assert.Throws<ArgumentException>(() => store.Folder(sha));

    // Writes every tile and the overview, then commits; returns the cache's bytes.
    private static long Build(MapTileStore target, string sha, MapPyramid pyramid, int fileBytes = 1)
    {
        long total = 0;
        using MapTileBuild build = target.BeginBuild(sha);
        File.WriteAllBytes(build.OverviewPath, new byte[fileBytes]);
        total += fileBytes;
        for (int level = 0; level < pyramid.Levels; level++)
        {
            for (int column = 0; column < pyramid.Columns(level); column++)
            {
                for (int row = 0; row < pyramid.Rows(level); row++)
                {
                    string path = build.TilePath(new MapTileKey(level, column, row));
                    File.WriteAllBytes(path, new byte[fileBytes]);
                    total += fileBytes;
                }
            }
        }
        build.Commit(pyramid);
        return total + new FileInfo(Path.Combine(target.Folder(sha), "map-cache.json")).Length;
    }

    private void Backdate(string sha, int hours) =>
        File.SetLastWriteTimeUtc(Path.Combine(store.Folder(sha), "map-cache.json"), DateTime.UtcNow.AddHours(-hours));
}
