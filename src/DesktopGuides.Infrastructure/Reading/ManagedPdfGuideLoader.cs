using System.Security.Cryptography;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using UglyToad.PdfPig.Exceptions;

namespace DesktopGuides.Infrastructure.Reading;

public abstract record PdfGuideLoad;
public sealed record PdfGuideLoaded(string FilePath, PdfPageTextSource Text, string ContentSha256) : PdfGuideLoad;
public sealed record PdfGuideLoadFailed(PdfGuideLoadError Error) : PdfGuideLoad;

/// <summary>
/// Opens a PDF guide's managed copy read-only for its text. Never opens the
/// original source and never writes to the library.
/// </summary>
public sealed class ManagedPdfGuideLoader(ManagedPathResolver paths)
{
    public Task<PdfGuideLoad> LoadAsync(Guide guide, CancellationToken token) =>
        LoadAsync(guide, null, token);

    // The password opens PdfPig once; it isn't stored, and a wrong one
    // reports PasswordIncorrect without echoing it.
    public async Task<PdfGuideLoad> LoadAsync(Guide guide, string? password, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // The resolver reports links and access errors as missing too, so
        // look at the planned path first: only an absent file is Missing.
        try
        {
            string planned = paths.GetPlannedGuideFile(guide.Id, guide.PrimaryRelativePath);
            if (Directory.Exists(planned)) return Failed(PdfGuideLoadError.Changed);
            if (!File.Exists(planned)) return Failed(PdfGuideLoadError.Missing);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException)
        {
            return Failed(PdfGuideLoadError.Changed);
        }
        string path;
        try
        {
            path = paths.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return Failed(PdfGuideLoadError.Missing);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        {
            return Failed(PdfGuideLoadError.Changed);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failed(PdfGuideLoadError.Unreadable);
        }
        return await Task.Run(() => Open(path, password, token), token).ConfigureAwait(false);
    }

    private static PdfGuideLoad Open(string path, string? password, CancellationToken token)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return Failed(PdfGuideLoadError.Missing);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failed(PdfGuideLoadError.Unreadable);
        }
        try
        {
            // The header check keeps garbage Damaged whatever PdfPig tolerates.
            if (!GuideImportValidator.StartsLikePdf(file))
            {
                file.Dispose();
                return Failed(PdfGuideLoadError.Damaged);
            }
            // The managed copy can change after import, so restores compare against
            // the bytes actually opened.
            file.Position = 0;
            string? contentSha256 = HashOrNull(file, token);
            if (contentSha256 is null)
            {
                file.Dispose();
                return Failed(PdfGuideLoadError.Unreadable);
            }
            file.Position = 0;
            PdfPageTextSource text = PdfPageTextSource.Open(file, token, password);
            if (text.PageCount == 0)
            {
                text.Dispose();
                return Failed(PdfGuideLoadError.Damaged);
            }
            return new PdfGuideLoaded(path, text, contentSha256);
        }
        catch (PdfDocumentEncryptedException)
        {
            file.Dispose();
            return Failed(password is null ? PdfGuideLoadError.PasswordRequired : PdfGuideLoadError.PasswordIncorrect);
        }
        catch (OperationCanceledException)
        {
            file.Dispose();
            throw;
        }
        catch (Exception)
        {
            // PdfPig reports malformed files with several exception types,
            // including EndOfStreamException for truncation.
            file.Dispose();
            return Failed(PdfGuideLoadError.Damaged);
        }
    }

    private static string? HashOrNull(FileStream file, CancellationToken token)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        try
        {
            int read;
            while ((read = file.Read(buffer)) > 0)
            {
                token.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static PdfGuideLoadFailed Failed(PdfGuideLoadError error) => new(error);
}
