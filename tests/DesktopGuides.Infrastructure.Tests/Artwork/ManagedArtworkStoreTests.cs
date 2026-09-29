using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Artwork;

public sealed class ManagedArtworkStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "desktop-guides-art-" + Guid.NewGuid());
    private readonly ManagedPathResolver paths;
    private readonly ManagedArtworkStore store;

    public ManagedArtworkStoreTests()
    {
        paths = new ManagedPathResolver(root);
        paths.EnsureCreated();
        store = new ManagedArtworkStore(paths);
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task StoresUnderGameFolderByContentHashAndLeavesNoStagedFile()
    {
        Guid gameId = Guid.NewGuid();
        StoredArtwork stored = await store.StoreAsync(gameId, TestImages.Png(600, 900), default);

        Assert.Matches($"^artwork/{gameId:N}/[0-9a-f]{{64}}\\.png$", stored.RelativePath);
        Assert.EndsWith(stored.Sha256 + ".png", stored.RelativePath);
        Assert.True(File.Exists(store.ResolveFile(stored.RelativePath)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ArtworkStagingRoot));
    }

    [Fact]
    public async Task StoringIdenticalBytesTwiceReturnsTheSameFile()
    {
        Guid gameId = Guid.NewGuid();
        StoredArtwork first = await store.StoreAsync(gameId, TestImages.Jpeg(10, 10), default);
        DateTime written = File.GetLastWriteTimeUtc(store.ResolveFile(first.RelativePath)!);
        StoredArtwork second = await store.StoreAsync(gameId, TestImages.Jpeg(10, 10), default);

        Assert.Equal(first, second);
        Assert.Equal(written, File.GetLastWriteTimeUtc(store.ResolveFile(second.RelativePath)!));
    }

    [Fact]
    public async Task InvalidImageLeavesNoFile()
    {
        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.StoreAsync(Guid.NewGuid(), "not an image"u8.ToArray(), default));
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ArtworkRoot));
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ArtworkStagingRoot));
    }

    [Theory]
    [InlineData("../library.sqlite")]
    [InlineData("artwork/../../x.png")]
    [InlineData("content/x/y.png")]
    [InlineData("artwork/0123/abc.png")]
    public void ResolveAndDeleteIgnoreNonCanonicalPaths(string relativePath)
    {
        Assert.Null(store.ResolveFile(relativePath));
        store.Delete(relativePath);
    }

    [Fact]
    public async Task DeleteRemovesFileAndEmptyGameFolder()
    {
        Guid gameId = Guid.NewGuid();
        StoredArtwork stored = await store.StoreAsync(gameId, TestImages.Png(1, 1), default);
        store.Delete(stored.RelativePath);
        Assert.False(Directory.Exists(Path.Combine(paths.ArtworkRoot, gameId.ToString("N"))));
    }

    [Fact]
    public async Task SweepKeepsReferencedAndRemovesStagedAndUnreferencedFiles()
    {
        StoredArtwork kept = await store.StoreAsync(Guid.NewGuid(), TestImages.Png(2, 2), default);
        StoredArtwork orphan = await store.StoreAsync(Guid.NewGuid(), TestImages.Png(3, 3), default);
        File.WriteAllBytes(Path.Combine(paths.ArtworkStagingRoot, Guid.NewGuid().ToString("N") + ".tmp"), [1]);
        File.WriteAllBytes(Path.Combine(paths.ArtworkRoot, "readme.txt"), [1]);

        int review = store.Sweep(new HashSet<string> { kept.RelativePath });

        Assert.NotNull(store.ResolveFile(kept.RelativePath));
        Assert.Null(store.ResolveFile(orphan.RelativePath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ArtworkStagingRoot));
        Assert.True(File.Exists(Path.Combine(paths.ArtworkRoot, "readme.txt")));
        Assert.Equal(1, review);
    }
}
