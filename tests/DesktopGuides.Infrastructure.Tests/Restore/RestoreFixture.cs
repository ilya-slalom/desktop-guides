using System.IO.Compression;
using System.Security.Cryptography;
using DesktopGuides.Core.Backup;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.FaultInjection;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Restore;

internal sealed record ArchiveFile(string Name, byte[] Bytes);

/// <summary>
/// A backup exported from <see cref="ExportFixture"/> and a separate target
/// library to restore it into. Rewrite builds hostile variants of the backup.
/// </summary>
internal sealed class RestoreFixture : IAsyncDisposable
{
    private RestoreFixture(ExportFixture source, RemovalLibrary target, string backup)
    {
        Source = source;
        Target = target;
        Backup = backup;
    }

    public ExportFixture Source { get; }
    public RemovalLibrary Target { get; }
    public string Backup { get; }

    public string StagingParent => Path.Combine(Target.Paths.DataRoot, LibraryRestorer.StagingFolderName);

    public static async Task<RestoreFixture> CreateAsync()
    {
        ExportFixture source = await ExportFixture.CreateAsync();
        string folder = Path.Combine(source.Library.Root, "backups");
        Directory.CreateDirectory(folder);
        LibraryExportResult result = await new LibraryExporter(
                source.Library.Repository, source.Library.Paths,
                new LibraryExportOptions("1.0.0.0", "msix", []))
            .ExportAsync(Path.Combine(folder, "backup.zip"), false, null, CancellationToken.None);
        return new RestoreFixture(source, await RemovalLibrary.CreateAsync(), result.Path);
    }

    public LibraryRestorer Restorer(
        Action<RestoreCheckpoint>? checkpoint = null, Func<string, long>? freeBytes = null) =>
        new(Target.Paths, checkpoint ?? (_ => { }), freeBytes);

    public Task<LibraryRestoreStage> StageAsync(
        string? backup = null, Action<RestoreCheckpoint>? checkpoint = null,
        Func<string, long>? freeBytes = null, CancellationToken token = default) =>
        Restorer(checkpoint, freeBytes).StageAsync(backup ?? Backup, null, token);

    /// <summary>Every live entry except staging, which a stage is allowed to add.</summary>
    public IReadOnlyDictionary<string, string> LiveEntries() =>
        LibrarySnapshot.Capture(Target.Paths).Entries
            .Where(entry => !entry.Key.StartsWith("fs:" + LibraryRestorer.StagingFolderName, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value);

    public void AssertNoStage() =>
        Assert.True(!Directory.Exists(StagingParent) || !Directory.EnumerateFileSystemEntries(StagingParent).Any(),
            "A stage folder was left behind.");

    /// <summary>
    /// Copies the backup with its entries edited. With <paramref name="rewriteManifest"/>
    /// the manifest lists the edited entries, so the archive stays self-consistent;
    /// otherwise the original manifest (optionally edited) is kept.
    /// </summary>
    public string Rewrite(
        Action<List<ArchiveFile>> edit, bool rewriteManifest = true,
        Func<LibraryArchiveManifest, LibraryArchiveManifest>? editManifest = null)
    {
        List<ArchiveFile> files;
        using (ZipArchive zip = ZipFile.OpenRead(Backup))
        {
            files = zip.Entries.Select(entry => new ArchiveFile(entry.FullName, Read(entry))).ToList();
        }
        LibraryArchiveManifest manifest = LibraryArchiveManifest.Parse(files[0].Bytes);
        files.RemoveAt(0);
        edit(files);
        if (rewriteManifest)
        {
            files = files.OrderBy(file => file.Name, StringComparer.Ordinal).ToList();
            manifest = manifest with
            {
                Entries = files.Select(file => new LibraryArchiveEntry(file.Name, file.Bytes.Length, Sha(file.Bytes))).ToArray()
            };
        }
        manifest = editManifest?.Invoke(manifest) ?? manifest;
        string path = Path.Combine(Path.GetDirectoryName(Backup)!, $"edited-{Guid.NewGuid():N}.zip");
        using ZipArchive output = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(output, LibraryArchiveManifest.EntryName, LibraryArchiveManifest.Write(manifest));
        foreach (ArchiveFile file in files)
        {
            Add(output, file.Name, file.Bytes);
        }
        return path;
    }

    /// <summary>A self-consistent backup whose database was changed by <paramref name="sql"/>.</summary>
    public string RewriteDatabase(string sql) => Rewrite(files =>
    {
        int index = files.FindIndex(file => file.Name == LibraryArchiveManifest.DatabasePath);
        string copy = Path.Combine(Target.Root, $"db-{Guid.NewGuid():N}.sqlite");
        File.WriteAllBytes(copy, files[index].Bytes);
        using (SqliteConnection connection = new($"Data Source={copy};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=DELETE; " + sql;
            command.ExecuteNonQuery();
        }
        files[index] = files[index] with { Bytes = File.ReadAllBytes(copy) };
    });

    public string WriteBytes(byte[] bytes)
    {
        string path = Path.Combine(Path.GetDirectoryName(Backup)!, $"raw-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using Stream stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        await Target.DisposeAsync();
        await Source.DisposeAsync();
    }
}
