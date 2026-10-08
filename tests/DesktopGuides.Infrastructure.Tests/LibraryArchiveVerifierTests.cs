using System.IO.Compression;
using System.Security.Cryptography;
using DesktopGuides.Core.Backup;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryArchiveVerifierTests
{
    private static readonly string GuidePath = $"library/content/{new string('1', 32)}/guide.txt";
    private static readonly (string, byte[]) Database = (LibraryArchiveManifest.DatabasePath, "database"u8.ToArray());
    private static readonly (string, byte[]) Guide = (GuidePath, "guide text"u8.ToArray());

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static (string, byte[]) Manifest(params (string Path, byte[] Bytes)[] files) =>
        (LibraryArchiveManifest.EntryName, LibraryArchiveManifest.Write(new LibraryArchiveManifest(
            Guid.NewGuid(), new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), "1.0.0.0", "msix", 4, 1, 1,
            files.Select(file => new LibraryArchiveEntry(file.Path, file.Bytes.Length, Sha(file.Bytes))).ToArray())));

    private static MemoryStream Zip(params (string Name, byte[] Bytes)[] entries)
    {
        MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] bytes) in entries)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void Rejected(MemoryStream zip) =>
        Assert.Throws<InvalidDataException>(() => LibraryArchiveVerifier.Verify(zip, CancellationToken.None));

    [Fact]
    public void AnArchiveThatMatchesItsManifestPasses()
    {
        using MemoryStream zip = Zip(Manifest(Database, Guide), Guide, Database);

        LibraryArchiveManifest manifest = LibraryArchiveVerifier.Verify(zip, CancellationToken.None);

        Assert.Equal(new[] { GuidePath, LibraryArchiveManifest.DatabasePath }, manifest.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public void ATruncatedArchiveIsRejected()
    {
        byte[] whole = Zip(Manifest(Database, Guide), Guide, Database).ToArray();

        Rejected(new MemoryStream(whole[..(whole.Length / 2)]));
    }

    [Fact]
    public void AnEntryMissingFromTheManifestIsRejected() =>
        Rejected(Zip(Manifest(Database), Guide, Database));

    [Fact]
    public void AnEntryMissingFromTheArchiveIsRejected() =>
        Rejected(Zip(Manifest(Database, Guide), Database));

    [Fact]
    public void AManifestThatIsNotTheFirstEntryIsRejected() =>
        Rejected(Zip(Guide, Manifest(Database, Guide), Database));

    [Fact]
    public void AnEntryWhoseBytesDoNotMatchItsHashIsRejected() =>
        Rejected(Zip(Manifest(Database, Guide), (GuidePath, "guide texT"u8.ToArray()), Database));

    [Fact]
    public void EntriesOutOfManifestOrderAreRejected() =>
        Rejected(Zip(Manifest(Database, Guide), Database, Guide));

    [Fact]
    public void ACancelledVerifyStops()
    {
        using MemoryStream zip = Zip(Manifest(Database, Guide), Guide, Database);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => LibraryArchiveVerifier.Verify(zip, cancelled.Token));
    }
}
