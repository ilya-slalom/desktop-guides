using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Paths;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Import;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Storage;

/// <summary>One file to archive, with what the database recorded for it.</summary>
internal sealed record PlannedArchiveFile(
    string ArchivePath, string SourcePath, long Bytes, string Sha256, Guid? GuideId, Guid? GameId);

/// <summary>
/// Every live file a database snapshot references, each resolved without
/// links and checked for its recorded size. Hashes are checked while the
/// exporter streams the files. Unreferenced files are never listed.
/// </summary>
internal sealed record LibraryArchivePlan(int Games, int Guides, IReadOnlyList<PlannedArchiveFile> Files)
{
    private sealed record GuideRow(Guid Id, string Format, string Primary, string Sha256, long Bytes);

    private sealed record AssetRow(string RelativePath, long Bytes, string Sha256);

    public static LibraryArchivePlan Read(
        string snapshotPath, ILibraryPaths paths, int maxEntries = LibraryArchiveManifest.MaxEntries)
    {
        // The snapshot keeps the live database's WAL mode, which can't be opened
        // read-only once its -shm is gone. Nothing here writes, so the file's
        // bytes don't change before the exporter hashes it.
        using SqliteConnection snapshot = new(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        snapshot.Open();
        List<(Guid Id, string? Artwork)> games = Query(snapshot,
            "SELECT Id, ArtworkRelativePath FROM Games ORDER BY Id",
            reader => (Guid.ParseExact(reader.GetString(0), "N"), reader.IsDBNull(1) ? null : reader.GetString(1)));
        List<GuideRow> guides = Query(snapshot,
            "SELECT Id, Format, PrimaryRelativePath, ContentSha256, ContentBytes FROM Guides ORDER BY Id",
            reader => new GuideRow(Guid.ParseExact(reader.GetString(0), "N"), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetInt64(4)));
        ILookup<Guid, AssetRow> assets = Query(snapshot,
            "SELECT GuideId, RelativePath, ByteCount, Sha256 FROM GuideAssets ORDER BY GuideId, RequestPath",
            reader => (Guid.ParseExact(reader.GetString(0), "N"),
                new AssetRow(reader.GetString(1), reader.GetInt64(2), reader.GetString(3))))
            .ToLookup(row => row.Item1, row => row.Item2);

        List<PlannedArchiveFile> files = [];
        SortedSet<Guid> damagedGuides = [];
        SortedSet<Guid> damagedGames = [];
        foreach (GuideRow guide in guides)
        {
            if (!TryPlanGuide(guide, assets[guide.Id].ToArray(), paths, files))
            {
                damagedGuides.Add(guide.Id);
            }
        }
        ManagedArtworkStore store = new(paths);
        foreach ((Guid id, string? artwork) in games)
        {
            if (artwork is not null && !TryPlanArtwork(id, artwork, store, files))
            {
                damagedGames.Add(id);
            }
        }
        if (damagedGuides.Count > 0 || damagedGames.Count > 0)
        {
            throw new LibraryExportException(
                LibraryExportIssue.ManagedFilesDamaged, damagedGuides.ToArray(), damagedGames.ToArray());
        }
        if (files.Count + 1 > maxEntries)
        {
            throw new LibraryExportException(LibraryExportIssue.LibraryTooLarge);
        }
        return new LibraryArchivePlan(games.Count, guides.Count,
            files.OrderBy(file => file.ArchivePath, StringComparer.Ordinal).ToArray());
    }

    private static bool TryPlanGuide(
        GuideRow guide, AssetRow[] assets, ILibraryPaths paths, List<PlannedArchiveFile> files)
    {
        AssetRow[] planned;
        if (guide.Format == "Html")
        {
            // Import recorded the fingerprint and size over every row, so they're checked the same way.
            if (assets.Length == 0 ||
                assets.Sum(asset => asset.Bytes) != guide.Bytes ||
                GuideFingerprint.OfHtml(assets.Select(asset => (asset.RelativePath, asset.Sha256))) != guide.Sha256 ||
                !assets.Any(asset => asset.RelativePath == guide.Primary))
            {
                return false;
            }
            IGrouping<string, AssetRow>[] byPath = assets.GroupBy(asset => asset.RelativePath, StringComparer.Ordinal).ToArray();
            if (byPath.Any(group => group.Select(asset => (asset.Bytes, asset.Sha256)).Distinct().Count() != 1))
            {
                return false;
            }
            planned = byPath.Select(group => group.First()).ToArray();
        }
        else
        {
            planned = [new AssetRow(guide.Primary, guide.Bytes, guide.Sha256)];
        }

        List<PlannedArchiveFile> resolved = [];
        foreach (AssetRow asset in planned)
        {
            string archivePath = $"library/content/{guide.Id:N}/{asset.RelativePath}";
            try
            {
                LibraryArchiveManifest.ValidatePath(archivePath);
                string source = paths.ResolveExistingGuideFile(guide.Id, asset.RelativePath);
                if (new FileInfo(source).Length != asset.Bytes)
                {
                    return false;
                }
                resolved.Add(new PlannedArchiveFile(archivePath, source, asset.Bytes, asset.Sha256, guide.Id, null));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        files.AddRange(resolved);
        return true;
    }

    private static bool TryPlanArtwork(
        Guid gameId, string relativePath, ManagedArtworkStore store, List<PlannedArchiveFile> files)
    {
        string archivePath = "library/" + relativePath;
        try
        {
            LibraryArchiveManifest.ValidatePath(archivePath);
            if (store.ResolveFile(relativePath) is not { } source)
            {
                return false;
            }
            ManagedPathResolver.RejectFilesystemLinks(source);
            // Artwork is content-addressed: its name is its SHA-256.
            files.Add(new PlannedArchiveFile(archivePath, source, new FileInfo(source).Length,
                Path.GetFileNameWithoutExtension(source), null, gameId));
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<T> Query<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> read)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        List<T> rows = [];
        while (reader.Read())
        {
            rows.Add(read(reader));
        }
        return rows;
    }
}
