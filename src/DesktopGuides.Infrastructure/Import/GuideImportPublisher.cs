using System.Security.Cryptography;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Paths;
using DesktopGuides.Core.Text;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Import;

internal enum ImportCheckpoint { Prepared, Copied, Verified, Renamed, InCommit }

/// <summary>
/// Copies a previewed guide into managed storage and publishes it. The whole
/// import holds the library write gate; any failure before the commit rolls
/// back, and a crash is rolled back by the startup reconciler. A duplicate of
/// a guide in the same game is refused unless the caller allows it.
/// </summary>
public sealed class GuideImportPublisher
{
    private const string MissingMessage = "The file is no longer there. Choose it again.";
    private const string ChangedMessage =
        "The file changed after it was checked. Choose it again to see the new version.";
    private const string DuplicateMessage = "This file is already a guide for this game.";
    private const string NoSpaceMessage = "There isn't enough free space to import this guide.";
    private const string SaveFailedMessage =
        "The guide couldn't be saved to your library. Nothing was changed.";
    private const int BufferBytes = 81920;

    private readonly SqliteLibraryRepository repository;
    private readonly ILibraryPaths paths;
    private readonly GuideImportValidator validator;
    private readonly Func<Guid> newId;
    private readonly Func<string, Stream> createStagedFile;
    private readonly Action<ImportCheckpoint> checkpoint;
    private readonly Action<IImportJournal, Guid> rollBack;

    public GuideImportPublisher(
        SqliteLibraryRepository repository, ILibraryPaths paths, GuideImportLimits? limits = null)
        : this(repository, paths, limits, Guid.NewGuid,
            path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true),
            _ => { },
            (journal, operationId) => journal.RollBack(operationId))
    {
    }

    internal GuideImportPublisher(
        SqliteLibraryRepository repository, ILibraryPaths paths, GuideImportLimits? limits,
        Func<Guid> newId, Func<string, Stream> createStagedFile,
        Action<ImportCheckpoint> checkpoint, Action<IImportJournal, Guid> rollBack)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        validator = new GuideImportValidator(limits);
        this.newId = newId;
        this.createStagedFile = createStagedFile;
        this.checkpoint = checkpoint;
        this.rollBack = rollBack;
    }

    /// <summary>
    /// Returns the new guide's ID. Throws <see cref="GuideImportException"/>,
    /// with <see cref="ImportIssue.Duplicate"/> when the game already has this
    /// content and <paramref name="allowDuplicate"/> is false, or
    /// <see cref="OperationCanceledException"/> before publication starts.
    /// </summary>
    public Task<Guid> PublishAsync(
        ImportManifest manifest, Guid gameId, string title, bool allowDuplicate,
        IProgress<ImportProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string validTitle = GuideTitle.Create(title);
        return repository.RunImportAsync(
            (journal, gateToken) => PublishLockedAsync(
                journal, manifest, gameId, validTitle, allowDuplicate, progress, gateToken),
            token);
    }

    private async Task<Guid> PublishLockedAsync(
        IImportJournal journal, ImportManifest manifest, Guid gameId, string title, bool allowDuplicate,
        IProgress<ImportProgress>? progress, CancellationToken token)
    {
        CheckSource(manifest.Source);
        if (!allowDuplicate)
        {
            CheckNotDuplicate(journal, manifest, gameId);
        }
        ImportPlan plan = await PlanAsync(manifest, token);
        token.ThrowIfCancellationRequested();
        Guid operationId = newId();
        Guid guideId = newId();
        try
        {
            journal.Prepare(operationId, guideId);
        }
        catch (SqliteException)
        {
            throw new GuideImportException(ImportIssue.SaveFailed, SaveFailedMessage);
        }
        try
        {
            Pass(ImportCheckpoint.Prepared, token);
            string staged = paths.GetStagedGuideRoot(operationId, guideId);
            string fingerprint = await CopyAsync(plan, manifest.Source, guideId, staged, progress, token);
            // Size and write time can survive an in-place rewrite; the bytes can't.
            if (!string.Equals(fingerprint, manifest.Fingerprint, StringComparison.Ordinal))
            {
                throw Changed();
            }
            Pass(ImportCheckpoint.Copied, token);
            CheckSource(manifest.Source);
            await VerifyAsync(manifest, plan, staged, token);
            Pass(ImportCheckpoint.Verified, token);
            Directory.Move(staged, paths.GetGuideRoot(guideId));
            // The operation folder held only this guide; rollback and startup accept it missing.
            Directory.Delete(Path.GetDirectoryName(staged)!);
            Pass(ImportCheckpoint.Renamed, token);
            // Past this point cancellation no longer applies.
            progress?.Report(new ImportProgress(1, true));
            journal.Publish(
                new NewImportedGuide(
                    operationId, guideId, gameId, title, manifest.Format, plan.PrimaryRelativePath,
                    fingerprint, plan.TotalBytes, manifest.Source.FileName,
                    (manifest as TxtImportManifest)?.CodePage),
                () => checkpoint(ImportCheckpoint.InCommit));
            return guideId;
        }
        catch (Exception error)
        {
            try
            {
                rollBack(journal, operationId);
            }
            catch (Exception)
            {
                // The Prepared row stays, and the startup reconciler finishes the rollback.
            }
            if (error is OperationCanceledException or GuideImportException)
            {
                throw;
            }
            throw new GuideImportException(ImportIssue.SaveFailed, SaveFailedMessage);
        }
    }

    private void Pass(ImportCheckpoint point, CancellationToken token)
    {
        checkpoint(point);
        token.ThrowIfCancellationRequested();
    }

    private static void CheckNotDuplicate(IImportJournal journal, ImportManifest manifest, Guid gameId)
    {
        Guid? existing;
        try
        {
            existing = journal.FindGuide(gameId, manifest.Format, manifest.Fingerprint);
        }
        catch (SqliteException)
        {
            throw new GuideImportException(ImportIssue.SaveFailed, SaveFailedMessage);
        }
        if (existing is not null)
        {
            throw new GuideImportException(ImportIssue.Duplicate, DuplicateMessage);
        }
    }

    private sealed record PlannedFile(string RelativePath, long ByteCount, string? Sha256);

    private sealed record ImportPlan(
        IReadOnlyList<PlannedFile> Files, string PrimaryRelativePath, StaticHtmlImportPreview? Html = null)
    {
        public long TotalBytes => Files.Sum(file => file.ByteCount);
    }

    private async Task<ImportPlan> PlanAsync(ImportManifest manifest, CancellationToken token)
    {
        switch (manifest)
        {
            case TxtImportManifest:
                return Single("guide.txt", manifest.Source);
            case PdfImportManifest:
                return Single("guide.pdf", manifest.Source);
            case HtmlImportManifest html:
                // The manifest carries only counts, so scan again for the file list.
                StaticHtmlImportPreview preview = await validator.PreviewHtmlAsync(html.Source, token);
                IReadOnlyList<StaticAsset> assets = preview.Manifest.Assets;
                if (GuideFingerprint.OfHtml(assets.Select(asset => (asset.RelativePath, asset.Sha256))) != html.Fingerprint)
                {
                    throw Changed();
                }
                return new ImportPlan(
                    assets.Select(asset => new PlannedFile(asset.RelativePath, asset.ByteCount, asset.Sha256)).ToArray(),
                    preview.EntryRelativePath,
                    preview);
            default:
                throw new ArgumentException("Unsupported import manifest.", nameof(manifest));
        }
    }

    private static ImportPlan Single(string name, ImportSource source) =>
        new([new PlannedFile(name, source.ByteCount, null)], name);

    /// <summary>Copies and hashes every planned file; returns the fingerprint.</summary>
    private async Task<string> CopyAsync(
        ImportPlan plan, ImportSource source, Guid guideId, string staged,
        IProgress<ImportProgress>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(staged);
        string guideRoot = paths.GetGuideRoot(guideId);
        long total = plan.TotalBytes;
        long copied = 0;
        byte[] buffer = new byte[BufferBytes];
        List<(string RelativePath, string Sha256)> hashes = [];
        foreach (PlannedFile file in plan.Files)
        {
            string target = Path.Combine(
                staged, Path.GetRelativePath(guideRoot, paths.GetPlannedGuideFile(guideId, file.RelativePath)));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            await using (Stream input = await OpenSourceAsync(plan, file, source, token))
            await using (Stream output = Create(target))
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int read = await ReadAsync(input, buffer, source.FileName, token);
                    if (read == 0)
                    {
                        break;
                    }
                    written += read;
                    if (written > file.ByteCount)
                    {
                        throw Changed();
                    }
                    hash.AppendData(buffer, 0, read);
                    await WriteAsync(output, buffer.AsMemory(0, read), token);
                    copied += read;
                    progress?.Report(new ImportProgress(
                        total == 0 ? 0 : Math.Min((double)copied / total, 0.99), false));
                }
                Flush(output);
            }
            string sha = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (written != file.ByteCount || (file.Sha256 is not null && sha != file.Sha256))
            {
                throw Changed();
            }
            hashes.Add((file.RelativePath, sha));
        }
        return plan.Html is null ? hashes.Single().Sha256 : GuideFingerprint.OfHtml(hashes);
    }

    private static async Task<Stream> OpenSourceAsync(
        ImportPlan plan, PlannedFile file, ImportSource source, CancellationToken token)
    {
        if (plan.Html is null)
        {
            return OpenSource(source);
        }
        try
        {
            // Maps the managed name back to the source file, including a renamed entry.
            return await plan.Html.CreateSource().OpenReadAsync(file.RelativePath, token) ?? throw Changed();
        }
        catch (Exception error) when (error is StaticHtmlValidationException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw Changed();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw GuideImportValidator.Unreadable(source.FileName);
        }
    }

    private static Stream OpenSource(ImportSource source)
    {
        try
        {
            return new FileStream(
                source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, useAsync: true);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Missing();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw GuideImportValidator.Unreadable(source.FileName);
        }
    }

    private async Task VerifyAsync(ImportManifest manifest, ImportPlan plan, string staged, CancellationToken token)
    {
        string primary = Path.Combine(staged, plan.PrimaryRelativePath);
        switch (manifest)
        {
            case TxtImportManifest txt:
                VerifyText(primary, txt.CodePage);
                break;
            case PdfImportManifest pdf:
                VerifyPdf(primary, pdf, token);
                break;
            case HtmlImportManifest:
                try
                {
                    // Re-hashes every source asset and every staged file.
                    await validator.Html.VerifyStagedAsync(plan.Html!, staged, token);
                }
                catch (StaticHtmlValidationException)
                {
                    throw Changed();
                }
                break;
        }
    }

    private static void VerifyText(string path, int? codePage)
    {
        try
        {
            TextGuideDocument.Decode(File.ReadAllBytes(path), codePage);
        }
        catch (EncodingSelectionRequiredException)
        {
            throw new GuideImportException(ImportIssue.Unreadable,
                "This text file isn't UTF-8. Save it as UTF-8 and import it again.");
        }
    }

    private static void VerifyPdf(string path, PdfImportManifest pdf, CancellationToken token)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ImportInspection staged = GuideImportValidator.ReadPdf(stream, pdf.Source, pdf.SuggestedTitle, pdf.Fingerprint, token);
        if (staged is not ImportReady { Manifest: PdfImportManifest copy } || copy.PageCount != pdf.PageCount)
        {
            throw GuideImportValidator.NotPdf();
        }
    }

    /// <summary>The source must still have the size and write time the preview saw.</summary>
    private static void CheckSource(ImportSource source)
    {
        FileInfo file = new(source.FullPath);
        if (!file.Exists)
        {
            throw Missing();
        }
        if (file.Length != source.ByteCount || file.LastWriteTimeUtc != source.LastWriteUtc.UtcDateTime)
        {
            throw Changed();
        }
    }

    private static async Task<int> ReadAsync(Stream input, byte[] buffer, string name, CancellationToken token)
    {
        try
        {
            return await input.ReadAsync(buffer, token);
        }
        catch (IOException)
        {
            throw GuideImportValidator.Unreadable(name);
        }
    }

    private Stream Create(string path)
    {
        try
        {
            return createStagedFile(path);
        }
        catch (IOException error)
        {
            throw WriteFailed(error);
        }
    }

    private static async Task WriteAsync(Stream output, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        try
        {
            await output.WriteAsync(bytes, token);
        }
        catch (IOException error)
        {
            throw WriteFailed(error);
        }
    }

    private static void Flush(Stream output)
    {
        try
        {
            if (output is FileStream file)
            {
                file.Flush(flushToDisk: true);
            }
            else
            {
                output.Flush();
            }
        }
        catch (IOException error)
        {
            throw WriteFailed(error);
        }
    }

    // ERROR_HANDLE_DISK_FULL (39) and ERROR_DISK_FULL (112).
    private static GuideImportException WriteFailed(IOException error) =>
        (error.HResult & 0xFFFF) is 39 or 112
            ? new(ImportIssue.NotEnoughSpace, NoSpaceMessage)
            : new(ImportIssue.SaveFailed, SaveFailedMessage);

    private static GuideImportException Missing() => new(ImportIssue.Missing, MissingMessage);

    private static GuideImportException Changed() => new(ImportIssue.Changed, ChangedMessage);
}
