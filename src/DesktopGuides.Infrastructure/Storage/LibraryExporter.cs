using System.IO.Compression;
using System.Security.Cryptography;
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Paths;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Storage;

/// <param name="ProtectedRoots">Folders an archive may not be saved in, in addition to the data root.</param>
public sealed record LibraryExportOptions(string AppVersion, string Build, IReadOnlyList<string> ProtectedRoots);

internal enum ExportCheckpoint { GateHeld, Snapshotted, Planned, EntryWritten, Written, BeforeRename }

/// <summary>
/// Exports the library to a .zip: a database snapshot and every file it
/// references, each checked against its recorded SHA-256. The write gate is
/// held from recovery until the last entry is written, so the archive
/// describes one state. Nothing appears at the destination until the
/// finished archive has been verified; a cancelled or failed export leaves
/// no file behind.
/// </summary>
public sealed class LibraryExporter
{
    private const int BufferBytes = 81920;
    private const long ProgressStepBytes = 1024 * 1024;
    private static readonly string[] StoredExtensions = [".pdf", ".png", ".jpg", ".jpeg", ".webp", ".gif"];

    private sealed record ArchiveItem(LibraryArchiveEntry Entry, string SourcePath, Guid? GuideId, Guid? GameId);

    private readonly SqliteLibraryRepository repository;
    private readonly ILibraryPaths paths;
    private readonly LibraryExportOptions options;
    private readonly TimeProvider clock;
    private readonly Action<ExportCheckpoint> checkpoint;
    private readonly Func<Guid> newId;

    public LibraryExporter(
        SqliteLibraryRepository repository, ILibraryPaths paths, LibraryExportOptions options,
        TimeProvider? clock = null)
        : this(repository, paths, options, clock, _ => { })
    {
    }

    internal LibraryExporter(
        SqliteLibraryRepository repository, ILibraryPaths paths, LibraryExportOptions options,
        TimeProvider? clock, Action<ExportCheckpoint> checkpoint, Func<Guid>? newId = null)
    {
        this.repository = repository;
        this.paths = paths;
        this.options = options;
        this.clock = clock ?? TimeProvider.System;
        this.checkpoint = checkpoint;
        this.newId = newId ?? Guid.NewGuid;
    }

    public async Task<LibraryExportResult> ExportAsync(
        string destination, bool overwrite, IProgress<LibraryExportProgress>? progress, CancellationToken token)
    {
        // Everything below blocks on disk I/O, so none of it runs on the caller's
        // (UI) context: the destination check runs on the pool, and every await
        // below resumes there.
        string target = await Task.Run(() => CheckDestination(destination, overwrite), token)
            .ConfigureAwait(false);
        Guid exportId = newId();
        DateTimeOffset created = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        string temp = $"{target}.{exportId:N}.tmp";
        string snapshot = Path.Combine(Path.GetTempPath(), $"desktop-guides-export-{exportId:N}.sqlite");
        progress?.Report(new LibraryExportProgress(LibraryExportPhase.Preparing, 0, 0));
        try
        {
            LibraryArchiveManifest manifest;
            try
            {
                manifest = await repository.RunExportAsync(
                    (connection, work) => Task.FromResult(
                        WriteUnderGate(connection, exportId, created, snapshot, temp, progress, work)),
                    token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is InvalidDataException or SqliteException or FormatException)
            {
                // Opening or reading the live library failed, for example a linked database file.
                throw new LibraryExportException(LibraryExportIssue.DatabaseInvalid, inner: error);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new LibraryExportException(LibraryExportIssue.WriteFailed, inner: error);
            }
            string sha256 = Verify(temp, manifest, progress, token);
            checkpoint(ExportCheckpoint.BeforeRename);
            token.ThrowIfCancellationRequested();
            Rename(temp, target, overwrite);
            return new LibraryExportResult(
                target, manifest.Games, manifest.Guides, manifest.Entries.Count, new FileInfo(target).Length, sha256);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
        finally
        {
            TryDeleteDatabase(snapshot);
        }
    }

    private LibraryArchiveManifest WriteUnderGate(
        SqliteConnection connection, Guid exportId, DateTimeOffset created, string snapshot, string temp,
        IProgress<LibraryExportProgress>? progress, CancellationToken token)
    {
        checkpoint(ExportCheckpoint.GateHeld);
        token.ThrowIfCancellationRequested();
        Recover(connection);
        Snapshot(connection, snapshot);
        checkpoint(ExportCheckpoint.Snapshotted);
        token.ThrowIfCancellationRequested();

        LibraryArchivePlan plan = LibraryArchivePlan.Read(snapshot, paths);
        (long databaseBytes, string databaseSha) = HashSnapshot(snapshot, token);
        ArchiveItem[] items = plan.Files
            .Select(file => new ArchiveItem(
                new LibraryArchiveEntry(file.ArchivePath, file.Bytes, file.Sha256), file.SourcePath, file.GuideId, file.GameId))
            .Append(new ArchiveItem(
                new LibraryArchiveEntry(LibraryArchiveManifest.DatabasePath, databaseBytes, databaseSha), snapshot, null, null))
            .OrderBy(item => item.Entry.Path, StringComparer.Ordinal)
            .ToArray();
        LibraryArchiveManifest manifest = new(
            exportId, created, options.AppVersion, options.Build, LibrarySchema.CurrentVersion,
            plan.Games, plan.Guides, items.Select(item => item.Entry).ToArray());
        byte[] manifestJson;
        try
        {
            manifestJson = LibraryArchiveManifest.Write(manifest);
        }
        catch (InvalidDataException error)
        {
            // The plan already capped the entry count; only very long paths get here.
            throw new LibraryExportException(LibraryExportIssue.LibraryTooLarge, inner: error);
        }
        checkpoint(ExportCheckpoint.Planned);
        token.ThrowIfCancellationRequested();

        WriteArchive(temp, created, manifestJson, items, manifest.TotalBytes, progress, token);
        checkpoint(ExportCheckpoint.Written);
        token.ThrowIfCancellationRequested();
        return manifest;
    }

    private void Recover(SqliteConnection connection)
    {
        try
        {
            new FileOperationReconciler(paths).Run(connection);
            using SqliteCommand count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM FileOperations";
            if ((long)count.ExecuteScalar()! != 0)
            {
                throw new InvalidDataException("File operations remain after recovery.");
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new LibraryExportException(LibraryExportIssue.RecoveryIncomplete, inner: error);
        }
    }

    private static void Snapshot(SqliteConnection connection, string snapshot)
    {
        try
        {
            SqliteLibraryRepository.BackupTo(connection, snapshot, LibrarySchema.CurrentVersion);
        }
        catch (Exception error) when (error is InvalidDataException or SqliteException)
        {
            throw new LibraryExportException(LibraryExportIssue.DatabaseInvalid, inner: error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LibraryExportException(LibraryExportIssue.WriteFailed, inner: error);
        }
    }

    private void WriteArchive(
        string temp, DateTimeOffset created, byte[] manifestJson, ArchiveItem[] items, long totalBytes,
        IProgress<LibraryExportProgress>? progress, CancellationToken token)
    {
        SortedSet<Guid> damagedGuides = [];
        SortedSet<Guid> damagedGames = [];
        long done = 0;
        long reported = 0;
        byte[] buffer = new byte[BufferBytes];
        try
        {
            using FileStream file = new(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using (ZipArchive zip = new(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry manifestEntry = zip.CreateEntry(LibraryArchiveManifest.EntryName, CompressionLevel.Optimal);
                manifestEntry.LastWriteTime = created;
                using (Stream output = manifestEntry.Open())
                {
                    output.Write(manifestJson);
                }
                foreach (ArchiveItem item in items)
                {
                    token.ThrowIfCancellationRequested();
                    ZipArchiveEntry entry = zip.CreateEntry(item.Entry.Path, LevelFor(item.Entry.Path));
                    entry.LastWriteTime = created;
                    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long copied = 0;
                    bool readable = true;
                    using (Stream output = entry.Open())
                    using (FileStream? input = TryOpenSource(item.SourcePath))
                    {
                        readable = input is not null;
                        while (input is not null)
                        {
                            token.ThrowIfCancellationRequested();
                            // Only a failed read marks the file damaged; a failed write
                            // (a full disk) is the archive's fault and fails the export.
                            int count = TryRead(input, buffer);
                            if (count < 0) { readable = false; break; }
                            if (count == 0) break;
                            hash.AppendData(buffer, 0, count);
                            output.Write(buffer, 0, count);
                            copied += count;
                            done += count;
                            if (done - reported >= ProgressStepBytes)
                            {
                                reported = done;
                                progress?.Report(new LibraryExportProgress(LibraryExportPhase.Writing, done, totalBytes));
                            }
                        }
                    }
                    if (!readable || copied != item.Entry.Bytes ||
                        Convert.ToHexStringLower(hash.GetHashAndReset()) != item.Entry.Sha256)
                    {
                        if (item.GuideId is { } guide) damagedGuides.Add(guide);
                        else if (item.GameId is { } game) damagedGames.Add(game);
                        else throw new InvalidDataException("The database snapshot changed while it was archived.");
                    }
                    progress?.Report(new LibraryExportProgress(LibraryExportPhase.Writing, done, totalBytes));
                    checkpoint(ExportCheckpoint.EntryWritten);
                }
            }
            file.Flush(flushToDisk: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new LibraryExportException(LibraryExportIssue.WriteFailed, inner: error);
        }
        if (damagedGuides.Count > 0 || damagedGames.Count > 0)
        {
            throw new LibraryExportException(
                LibraryExportIssue.ManagedFilesDamaged, damagedGuides.ToArray(), damagedGames.ToArray());
        }
    }

    private static string Verify(
        string temp, LibraryArchiveManifest manifest, IProgress<LibraryExportProgress>? progress, CancellationToken token)
    {
        progress?.Report(new LibraryExportProgress(LibraryExportPhase.Verifying, 0, manifest.TotalBytes));
        try
        {
            // Share reads: an antivirus scan or indexer may already hold the new file open.
            using FileStream stream = new(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (LibraryArchiveVerifier.Verify(stream, token).ExportId != manifest.ExportId)
            {
                throw new InvalidDataException("The archive's manifest isn't the one this export wrote.");
            }
            stream.Position = 0;
            string sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
            progress?.Report(new LibraryExportProgress(LibraryExportPhase.Verifying, manifest.TotalBytes, manifest.TotalBytes));
            return sha256;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new LibraryExportException(LibraryExportIssue.VerificationFailed, inner: error);
        }
    }

    private string CheckDestination(string destination, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination) ||
            !destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationUnavailable);
        }
        string full = Path.GetFullPath(destination);
        string? folder = Path.GetDirectoryName(full);
        if (folder is null || !Directory.Exists(folder) || Directory.Exists(full))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationUnavailable);
        }
        List<string> candidates = [folder];
        if (new DirectoryInfo(folder).ResolveLinkTarget(returnFinalTarget: true) is { } linked)
        {
            candidates.Add(linked.FullName);
        }
        IEnumerable<string> roots = options.ProtectedRoots.Append(paths.DataRoot);
        if (candidates.Any(candidate => roots.Any(root => IsInside(candidate, root))))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationNotAllowed);
        }
        if (!overwrite && File.Exists(full))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationExists);
        }
        return full;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or below it, by whole segment.</summary>
    private static bool IsInside(string path, string root)
    {
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void Rename(string temp, string target, bool overwrite)
    {
        try
        {
            File.Move(temp, target, overwrite);
        }
        catch (IOException error) when (!overwrite && File.Exists(target))
        {
            throw new LibraryExportException(LibraryExportIssue.DestinationExists, inner: error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LibraryExportException(LibraryExportIssue.WriteFailed, inner: error);
        }
    }

    private static FileStream? TryOpenSource(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The bytes read, or -1 if the source can't be read.</summary>
    private static int TryRead(FileStream input, byte[] buffer)
    {
        try
        {
            return input.Read(buffer);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static CompressionLevel LevelFor(string path) =>
        StoredExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            ? CompressionLevel.NoCompression
            : CompressionLevel.Optimal;

    private static (long Bytes, string Sha256) HashSnapshot(string path, CancellationToken token)
    {
        try
        {
            return HashFile(path, token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LibraryExportException(LibraryExportIssue.WriteFailed, inner: error);
        }
    }

    private static (long Bytes, string Sha256) HashFile(string path, CancellationToken token)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[BufferBytes];
        long bytes = 0;
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
            bytes += count;
        }
        return (bytes, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDatabase(string path)
    {
        try { SqliteLibraryRepository.DeleteDatabaseFiles(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
