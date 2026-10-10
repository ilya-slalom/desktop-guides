// src/DesktopGuides.Infrastructure/Storage/LibraryRestorer.cs
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Paths;

namespace DesktopGuides.Infrastructure.Storage;

internal enum RestoreCheckpoint
{
    Copied, Extracted, Validated, MarkerWritten, PriorMoved, Promoted, Confirmed, RolledAside, PriorReturned
}

/// <summary>
/// Restores a library archive. StageAsync copies the backup next to the
/// library, checks every name, size, hash and database reference, and
/// extracts it into its own folder; nothing under library/ changes until
/// ReplaceAsync. A failed or cancelled stage leaves no folder behind.
/// </summary>
public sealed class LibraryRestorer
{
    public const string StagingFolderName = ".restore-staging";
    internal const string ArchiveFileName = "archive.zip";
    internal const string LibraryFolderName = "library";
    private const int BufferBytes = 81920;
    private const long ProgressStepBytes = 1024 * 1024;

    private readonly ILibraryPaths paths;
    private readonly Action<RestoreCheckpoint> checkpoint;
    private readonly Func<string, long> freeBytes;
    private readonly Func<Guid> newId;

    public LibraryRestorer(ILibraryPaths paths)
        : this(paths, _ => { })
    {
    }

    internal LibraryRestorer(
        ILibraryPaths paths, Action<RestoreCheckpoint> checkpoint,
        Func<string, long>? freeBytes = null, Func<Guid>? newId = null)
    {
        this.paths = paths;
        this.checkpoint = checkpoint;
        this.freeBytes = freeBytes ?? (path => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace);
        this.newId = newId ?? Guid.NewGuid;
    }

    internal static string StageRoot(ILibraryPaths paths, Guid stageId) =>
        Path.Combine(paths.DataRoot, StagingFolderName, stageId.ToString("N"));

    public Task<LibraryRestoreStage> StageAsync(
        string zipPath, IProgress<LibraryRestoreProgress>? progress, CancellationToken token) =>
        Task.Run(() => Stage(zipPath, progress, token), token);

    public void DiscardStage(LibraryRestoreStage stage) => DeleteTree(StageRoot(paths, stage.StageId));

    private LibraryRestoreStage Stage(
        string zipPath, IProgress<LibraryRestoreProgress>? progress, CancellationToken token)
    {
        Guid stageId = newId();
        string stageRoot = StageRoot(paths, stageId);
        try
        {
            ManagedPathResolver.RejectFilesystemLinks(Path.GetDirectoryName(stageRoot)!);
            Directory.CreateDirectory(stageRoot);
            ManagedPathResolver.RejectFilesystemLinks(stageRoot);
            string archive = Path.Combine(stageRoot, ArchiveFileName);
            Copy(zipPath, archive, progress, token);
            checkpoint(RestoreCheckpoint.Copied);
            token.ThrowIfCancellationRequested();

            LibraryArchiveManifest manifest = Check(archive, progress, token);
            RequireSpace(manifest.TotalBytes);
            Extract(archive, manifest, Path.Combine(stageRoot, LibraryFolderName), progress, token);
            checkpoint(RestoreCheckpoint.Extracted);
            token.ThrowIfCancellationRequested();

            return new LibraryRestoreStage(stageId, manifest.CreatedUtc, manifest.AppVersion,
                manifest.SchemaVersion, manifest.Games, manifest.Guides, manifest.TotalBytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DeleteTree(stageRoot);
            throw new LibraryRestoreException(LibraryRestoreIssue.SourceUnavailable, inner: error);
        }
        catch
        {
            DeleteTree(stageRoot);
            throw;
        }
    }

    private void Copy(string source, string target, IProgress<LibraryRestoreProgress>? progress, CancellationToken token)
    {
        FileStream input;
        try
        {
            input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.SourceUnavailable, inner: error);
        }
        using (input)
        {
            long total = input.Length;
            RequireSpace(total);
            using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[BufferBytes];
            long done = 0;
            long reported = 0;
            int count;
            while ((count = ReadSource(input, buffer)) > 0)
            {
                token.ThrowIfCancellationRequested();
                output.Write(buffer, 0, count);
                done += count;
                if (done - reported >= ProgressStepBytes)
                {
                    reported = done;
                    progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Copying, done, total));
                }
            }
            output.Flush(flushToDisk: true);
            progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Copying, done, total));
        }
    }

    private static int ReadSource(FileStream input, byte[] buffer)
    {
        try
        {
            return input.Read(buffer);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.SourceUnavailable, inner: error);
        }
    }

    // The volume needs the bytes plus a tenth, so a restore can't fill the disk.
    private void RequireSpace(long bytes)
    {
        long needed = bytes + bytes / 10;
        if (freeBytes(paths.DataRoot) < needed)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.NotEnoughSpace, bytesNeeded: needed);
        }
    }

    private static LibraryArchiveManifest Check(
        string archive, IProgress<LibraryRestoreProgress>? progress, CancellationToken token)
    {
        progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Checking, 0, 0));
        using FileStream stream = new(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            using (ZipArchive zip = new(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                RejectUnsafeNames(zip.Entries.Select(entry => entry.FullName).ToArray());
            }
            stream.Position = 0;
            return LibraryArchiveVerifier.Verify(stream, token);
        }
        catch (InvalidDataException error)
        {
            throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid, inner: error);
        }
    }

    // Every name must be a library path, and no two may land on the same
    // file on a case-insensitive disk once Unicode-normalized.
    private static void RejectUnsafeNames(IReadOnlyList<string> names)
    {
        HashSet<string> folded = new(StringComparer.Ordinal);
        for (int index = 0; index < names.Count; index++)
        {
            string name = names[index];
            if (!(index == 0 && name == LibraryArchiveManifest.EntryName))
            {
                try
                {
                    LibraryArchiveManifest.ValidatePath(name);
                }
                catch (InvalidDataException error)
                {
                    throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveUnsafe, inner: error);
                }
            }
            if (!folded.Add(name.Normalize(NormalizationForm.FormC).ToUpperInvariant()))
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveUnsafe);
            }
        }
    }

    private static void Extract(
        string archive, LibraryArchiveManifest manifest, string libraryRoot,
        IProgress<LibraryRestoreProgress>? progress, CancellationToken token)
    {
        string root = Path.GetFullPath(libraryRoot);
        string stageRoot = Path.GetDirectoryName(root)!;
        long total = manifest.TotalBytes;
        long done = 0;
        long reported = 0;
        byte[] buffer = new byte[BufferBytes];
        using ZipArchive zip = ZipFile.OpenRead(archive);
        for (int index = 0; index < manifest.Entries.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            LibraryArchiveEntry expected = manifest.Entries[index];
            ZipArchiveEntry entry = zip.Entries[index + 1];
            if (entry.FullName != expected.Path)
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid);
            }
            string target = Path.GetFullPath(Path.Combine(stageRoot, expected.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveUnsafe);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            try
            {
                using Stream input = entry.Open();
                using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                int count;
                while ((count = input.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    // Never write past the declared size, whatever the entry inflates to.
                    if (written + count > expected.Bytes)
                    {
                        throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid);
                    }
                    output.Write(buffer, 0, count);
                    hash.AppendData(buffer, 0, count);
                    written += count;
                    done += count;
                    if (done - reported >= ProgressStepBytes)
                    {
                        reported = done;
                        progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Extracting, done, total));
                    }
                }
            }
            catch (InvalidDataException error)
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid, inner: error);
            }
            if (written != expected.Bytes || Convert.ToHexStringLower(hash.GetHashAndReset()) != expected.Sha256)
            {
                throw new LibraryRestoreException(LibraryRestoreIssue.ArchiveInvalid);
            }
        }
        progress?.Report(new LibraryRestoreProgress(LibraryRestorePhase.Extracting, done, total));
    }

    internal static void DeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
