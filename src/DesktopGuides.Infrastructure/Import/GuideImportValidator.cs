using System.Globalization;
using System.Text;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using DesktopGuides.Core.Paths;
using DesktopGuides.Core.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace DesktopGuides.Infrastructure.Import;

/// <summary>
/// Reads a picked file and describes it for the import preview. It never
/// writes: no staging, no managed copy, and the source opens read-only with
/// shared read access.
/// </summary>
public sealed class GuideImportValidator : IGuideImportValidator
{
    private const int SampleBytes = 2048;
    private const int SampleLines = 8;
    private const int TextSamplePages = 5;
    private const string NotUtf8Message = "This text file isn't UTF-8. Save it as UTF-8 and import it again.";
    private static readonly int[] LegacyCodePages = [437, 1252];
    private static readonly byte[] PdfMarker = "%PDF-"u8.ToArray();
    private readonly GuideImportLimits limits;
    private readonly StaticHtmlImportValidator html;

    private readonly IMapImageDecoder? maps;

    public GuideImportValidator(GuideImportLimits? limits = null, IMapImageDecoder? maps = null)
    {
        this.limits = limits ?? new GuideImportLimits();
        this.maps = maps;
        html = new StaticHtmlImportValidator(this.limits.HtmlLimits);
    }

    internal StaticHtmlImportValidator Html => html;

    public async Task<ImportInspection> InspectAsync(string fullPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException("A full file path is required.", nameof(fullPath));
        }
        token.ThrowIfCancellationRequested();
        FileInfo file = new(Path.GetFullPath(fullPath));
        string name = file.Name;
        if (!file.Exists)
        {
            throw Missing(name);
        }
        GuideFormat format = FormatOf(file.Extension) ??
            throw new GuideImportException(ImportIssue.Unsupported, $"{name} isn't a TXT, HTML, PDF, PNG or JPEG file.");
        if (file.Length == 0)
        {
            throw new GuideImportException(ImportIssue.Empty, "The file is empty.");
        }
        CheckSize(file.Length, name, format);
        ImportSource source = new(
            file.FullName, name, file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
        string title = format == GuideFormat.Image ? GuideTitle.SuggestMap(name) : GuideTitle.Suggest(name);
        // Callers include the UI thread, and cached reads can complete
        // synchronously, so the work runs on the thread pool.
        return format switch
        {
            GuideFormat.Txt => await Task.Run(() => InspectTxtAsync(source, title, token), token).ConfigureAwait(false),
            GuideFormat.Html => await Task.Run(() => InspectHtmlAsync(source, title, token), token).ConfigureAwait(false),
            GuideFormat.Image => await Task.Run(() => InspectMapAsync(source, title, token), token).ConfigureAwait(false),
            _ => await InspectPdfAsync(source, title, token).ConfigureAwait(false),
        };
    }

    public async Task<TxtImportManifest> ResolveTxtEncodingAsync(
        ImportNeedsTxtEncoding inspection, int codePage, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (!LegacyCodePages.Contains(codePage))
        {
            throw new ArgumentOutOfRangeException(nameof(codePage), codePage, "Choose code page 437 or 1252.");
        }
        token.ThrowIfCancellationRequested();
        ImportSource source = inspection.Source;
        FileInfo file = new(source.FullPath);
        if (!file.Exists)
        {
            throw Missing(source.FileName);
        }
        if (file.Length != source.ByteCount ||
            file.LastWriteTimeUtc != source.LastWriteUtc.UtcDateTime)
        {
            throw Changed(source);
        }
        return await Task.Run(async () =>
        {
            byte[] bytes = await ReadAllAsync(source, token);
            // Both code pages decode every byte, so this can't ask for another encoding.
            TextGuideDocument.Decode(bytes, codePage);
            return new TxtImportManifest(source, inspection.SuggestedTitle, codePage, GuideFingerprint.OfBytes(bytes));
        }, token).ConfigureAwait(false);
    }

    public async Task<PdfImportManifest> ResolvePdfPasswordAsync(
        ImportNeedsPdfPassword inspection, string password, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentException.ThrowIfNullOrEmpty(password);
        token.ThrowIfCancellationRequested();
        ImportSource source = inspection.Source;
        FileInfo file = new(source.FullPath);
        if (!file.Exists)
        {
            throw Missing(source.FileName);
        }
        if (file.Length != source.ByteCount ||
            file.LastWriteTimeUtc != source.LastWriteUtc.UtcDateTime)
        {
            throw Changed(source);
        }
        return await Task.Run(() =>
        {
            using FileStream stream = OpenSource(source.FullPath, source.FileName, asyncIo: false);
            string fingerprint;
            try
            {
                fingerprint = GuideFingerprint.OfStream(stream, token);
            }
            catch (IOException)
            {
                throw Unreadable(source.FileName);
            }
            // The size and time can survive an edit; the bytes can't.
            if (fingerprint != inspection.Fingerprint)
            {
                throw Changed(source);
            }
            stream.Position = 0;
            return ReadPdf(stream, source, inspection.SuggestedTitle, fingerprint, token, password) is
                ImportReady { Manifest: PdfImportManifest manifest } ? manifest : throw NotPdf();
        }, token).ConfigureAwait(false);
    }

    private static GuideImportException Changed(ImportSource source) =>
        new(ImportIssue.Changed, $"{source.FileName} changed after it was checked. Choose it again.");

    private async Task<ImportInspection> InspectTxtAsync(
        ImportSource source, string title, CancellationToken token)
    {
        byte[] bytes = await ReadAllAsync(source, token);
        bool utf16Bom = bytes.Length >= 2 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
        if (utf16Bom || bytes.AsSpan().IndexOf((byte)0) >= 0)
        {
            throw new GuideImportException(ImportIssue.UnsupportedEncoding, NotUtf8Message);
        }
        try
        {
            TextGuideDocument.Decode(bytes);
            return new ImportReady(new TxtImportManifest(source, title, null, GuideFingerprint.OfBytes(bytes)));
        }
        catch (EncodingSelectionRequiredException) when (TextGuideDocument.HasUtf8Bom(bytes))
        {
            // A BOM means UTF-8 when the guide is opened, so no legacy code page can apply.
            throw new GuideImportException(ImportIssue.UnsupportedEncoding, NotUtf8Message);
        }
        catch (EncodingSelectionRequiredException)
        {
            return new ImportNeedsTxtEncoding(source, title, Samples(bytes));
        }
    }

    private async Task<ImportInspection> InspectHtmlAsync(
        ImportSource source, string title, CancellationToken token)
    {
        StaticHtmlImportPreview preview = await PreviewHtmlAsync(source, token);
        ImportWarning[] warnings = preview.Warnings
            .Select(warning => new ImportWarning(warning.RelativePath ?? warning.RawTarget, warning.Message))
            .ToArray();
        StaticHtmlManifest scanned = preview.Manifest;
        string[] titles = scanned.Pages
            .Select((page, index) => page.Title ?? Path.GetFileNameWithoutExtension(
                index == 0 ? source.FileName : page.RelativePath))
            .ToArray();
        return new ImportReady(new HtmlImportManifest(
            source, title, preview.EntryRelativePath,
            scanned.Assets.Count(asset => asset.Kind is not (StaticAssetKind.EntryHtml or StaticAssetKind.Page)),
            scanned.TotalBytes, warnings,
            GuideFingerprint.OfHtml(scanned.Assets.Select(asset => (asset.RelativePath, asset.Sha256))),
            PageTitles.WithoutSharedSuffix(titles)));
    }

    internal async Task<StaticHtmlImportPreview> PreviewHtmlAsync(ImportSource source, CancellationToken token)
    {
        // Report a locked entry as the picked file, before the scanner reads it.
        using (OpenSource(source.FullPath, source.FileName))
        {
        }
        // The scanner renames an entry with '%' to guide.html; any other entry
        // name must already be a safe managed path.
        if (!source.FileName.Contains('%') && !IsSafeEntryName(source.FileName))
        {
            throw new GuideImportException(ImportIssue.Unreadable,
                $"{source.FileName} can't be used as a guide file name. Rename it and import again.");
        }
        StaticHtmlImportPreview preview;
        try
        {
            preview = await html.PreviewAsync(source.FullPath, token);
        }
        catch (StaticHtmlScanException error)
        {
            throw new GuideImportException(ImportIssue.TooLarge, HtmlLimitMessage(error.Limit));
        }
        catch (StaticHtmlValidationException error) when (error.Issue == StaticHtmlValidationIssue.UnsafePath)
        {
            throw new GuideImportException(ImportIssue.Unreadable,
                "The guide's folder reaches files through a link to another location. " +
                "Copy the guide and its files to a regular folder, then import again.");
        }
        catch (StaticHtmlValidationException error) when (error.Issue == StaticHtmlValidationIssue.CaseCollision)
        {
            throw new GuideImportException(ImportIssue.Unreadable,
                "The guide's folder has files whose names differ only by case.");
        }
        catch (InvalidDataException)
        {
            // With the entry name checked, only a style sheet's encoding is left.
            throw new GuideImportException(ImportIssue.Unreadable, "One of the guide's style sheets can't be read.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Missing(source.FileName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new GuideImportException(ImportIssue.Unreadable,
                $"{source.FileName} or one of its linked files can't be opened.");
        }
        return preview;
    }

    private static Task<ImportInspection> InspectPdfAsync(
        ImportSource source, string title, CancellationToken token) =>
        Task.Run<ImportInspection>(() =>
        {
            using FileStream stream = OpenSource(source.FullPath, source.FileName, asyncIo: false);
            string fingerprint;
            try
            {
                fingerprint = GuideFingerprint.OfStream(stream, token);
            }
            catch (IOException)
            {
                throw Unreadable(source.FileName);
            }
            stream.Position = 0;
            return ReadPdf(stream, source, title, fingerprint, token);
        }, token);

    internal static ImportInspection ReadPdf(
        Stream stream, ImportSource source, string title, string fingerprint, CancellationToken token,
        string? password = null)
    {
        try
        {
            if (!StartsLikePdf(stream))
            {
                throw NotPdf();
            }
            stream.Position = 0;
            // PdfPig ignores the token, so the stream checks it on each read.
            CancellableReadStream reader = new(stream, token);
            using PdfDocument document = password is null
                ? PdfDocument.Open(reader)
                : PdfDocument.Open(reader, new ParsingOptions { Password = password });
            int pages = document.NumberOfPages;
            if (pages == 0)
            {
                throw NotPdf();
            }
            bool hasText = false;
            for (int number = 1; number <= Math.Min(pages, TextSamplePages) && !hasText; number++)
            {
                token.ThrowIfCancellationRequested();
                hasText = document.GetPage(number).Text.Any(char.IsLetter);
            }
            return new ImportReady(new PdfImportManifest(
                source, title, pages, hasText, fingerprint, PasswordRequired: password is not null));
        }
        catch (PdfDocumentEncryptedException)
        {
            // PdfPig raises the same exception with no password and a wrong one.
            // The exception never carries the attempt.
            return password is null
                ? new ImportNeedsPdfPassword(source, title, fingerprint)
                : throw new GuideImportException(ImportIssue.PasswordIncorrect, ImportPresentation.PdfPasswordIncorrect);
        }
        catch (Exception error) when (error is not (GuideImportException or OperationCanceledException))
        {
            // PdfPig reports malformed files with several exception types.
            throw NotPdf();
        }
    }

    private static bool IsSafeEntryName(string name)
    {
        try
        {
            ManagedRelativePath.Parse(name);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    internal static bool StartsLikePdf(Stream stream)
    {
        byte[] head = new byte[1024];
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return head.AsSpan(0, read).IndexOf(PdfMarker) >= 0;
    }

    internal static GuideImportException NotPdf() =>
        new(ImportIssue.Unreadable, "This file isn't a readable PDF.");

    private static TxtEncodingSample[] Samples(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        int count = Math.Min(bytes.Length, SampleBytes);
        return LegacyCodePages
            .Select(codePage => new TxtEncodingSample(
                codePage, FirstLines(Encoding.GetEncoding(codePage).GetString(bytes, 0, count))))
            .ToArray();
    }

    private static string FirstLines(string text) => string.Join('\n',
        text.Split('\n').Take(SampleLines).Select(line => line.TrimEnd('\r'))).TrimEnd('\n');

    private async Task<byte[]> ReadAllAsync(ImportSource source, CancellationToken token)
    {
        using FileStream stream = OpenSource(source.FullPath, source.FileName);
        CheckSize(stream.Length, source.FileName, GuideFormat.Txt);
        byte[] bytes = new byte[stream.Length];
        try
        {
            await stream.ReadExactlyAsync(bytes, token);
        }
        catch (IOException)
        {
            throw Unreadable(source.FileName);
        }
        return bytes;
    }

    private void CheckSize(long length, string name, GuideFormat format)
    {
        (long limit, string kind) = format switch
        {
            GuideFormat.Txt => (limits.MaxTxtBytes, "text files"),
            GuideFormat.Pdf => (limits.MaxPdfBytes, "PDFs"),
            GuideFormat.Image => (limits.MaxMapBytes, "maps"),
            _ => (limits.HtmlLimits.MaxEntryBytes, ""),
        };
        if (length <= limit)
        {
            return;
        }
        throw new GuideImportException(ImportIssue.TooLarge, format == GuideFormat.Html
            ? HtmlLimitMessage(StaticScanLimit.EntryBytes)
            : $"{name} is larger than the {ImportPresentation.FormatSize(limit)} limit for {kind}.");
    }

    internal string HtmlLimitMessage(StaticScanLimit limit)
    {
        StaticHtmlScanLimits html = limits.HtmlLimits;
        return limit switch
        {
            StaticScanLimit.EntryBytes =>
                $"The web page is larger than the {ImportPresentation.FormatSize(html.MaxEntryBytes)} limit.",
            StaticScanLimit.AssetBytes =>
                $"A linked file is larger than the {ImportPresentation.FormatSize(html.MaxAssetBytes)} limit for one file.",
            StaticScanLimit.TotalBytes =>
                $"The web page and its linked files are larger than the {ImportPresentation.FormatSize(html.MaxTotalBytes)} limit.",
            StaticScanLimit.AssetCount => $"The web page links more than {html.MaxAssets:N0} files.",
            StaticScanLimit.ReferenceCount => $"The web page has more than {html.MaxReferences:N0} links to other files.",
            StaticScanLimit.CssDepth => $"The web page's style sheets nest more than {html.MaxCssDepth:N0} levels deep.",
            StaticScanLimit.CssRuleCount => $"The web page's style sheets have more than {html.MaxCssRules:N0} rules.",
            StaticScanLimit.PageCount => $"The guide links more than {html.MaxPages:N0} saved pages.",
            StaticScanLimit.GuideBytes =>
                $"The guide's pages and their linked files are larger than the {ImportPresentation.FormatSize(html.MaxGuideBytes)} limit.",
            StaticScanLimit.GuideAssetCount => $"The guide's pages link more than {html.MaxGuideAssets:N0} files.",
            _ => throw new ArgumentOutOfRangeException(nameof(limit)),
        };
    }

    // T21.2: the header decides the kind and size; the limits apply before
    // anything decodes it, and the slow decode check runs last.
    private async Task<ImportInspection> InspectMapAsync(ImportSource source, string title, CancellationToken token)
    {
        IMapImageDecoder decoder = maps ?? throw new InvalidOperationException("Map import needs an image decoder.");
        MapImageSize size;
        string fingerprint;
        using (FileStream stream = OpenSource(source.FullPath, source.FileName, asyncIo: false))
        {
            try
            {
                size = MapImageHeader.Read(stream) ??
                    throw new GuideImportException(ImportIssue.Unsupported, $"{source.FileName} isn't a PNG or JPEG image.");
                string pixels = string.Create(CultureInfo.CurrentCulture, $"{source.FileName} is {size.Width:N0} × {size.Height:N0} pixels.");
                switch (MapImageLimits.Check(size))
                {
                    case MapLimit.Side:
                        throw new GuideImportException(ImportIssue.TooLarge,
                            $"{pixels} A map can be at most {MapImageLimits.MaxSide.ToString("N0", CultureInfo.CurrentCulture)} pixels on a side.");
                    case MapLimit.Pixels:
                        throw new GuideImportException(ImportIssue.TooLarge,
                            $"{pixels} A map can have at most 268 million pixels.");
                }
                stream.Position = 0;
                fingerprint = GuideFingerprint.OfStream(stream, token);
            }
            catch (IOException)
            {
                throw Unreadable(source.FileName);
            }
        }
        if (!await decoder.DecodesAsync(source.FullPath, size, token).ConfigureAwait(false))
        {
            throw new GuideImportException(ImportIssue.Unreadable,
                $"{source.FileName} can't be read as an image. It may be damaged or incomplete.");
        }
        return new ImportReady(new MapImportManifest(source, title, size.Kind, size.Width, size.Height, fingerprint));
    }

    // The staged copy's header must match what was inspected.
    internal static void VerifyMap(string path, MapImportManifest map)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (MapImageHeader.Read(stream) != new MapImageSize(map.Kind, map.PixelWidth, map.PixelHeight))
        {
            throw new GuideImportException(ImportIssue.Changed,
                "The file changed after it was checked. Choose it again to see the new version.");
        }
    }

    private static FileStream OpenSource(string path, string name, bool asyncIo = true)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, asyncIo ? FileOptions.Asynchronous | FileOptions.SequentialScan : FileOptions.None);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Missing(name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw Unreadable(name);
        }
    }

    private static GuideFormat? FormatOf(string extension) => extension.ToLowerInvariant() switch
    {
        ".txt" => GuideFormat.Txt,
        ".html" or ".htm" => GuideFormat.Html,
        ".pdf" => GuideFormat.Pdf,
        ".png" or ".jpg" or ".jpeg" => GuideFormat.Image,
        _ => null,
    };

    private static GuideImportException Missing(string name) =>
        new(ImportIssue.Missing, $"{name} can't be found. It may have been moved or deleted.");

    internal static GuideImportException Unreadable(string name) =>
        new(ImportIssue.Unreadable,
            $"{name} can't be opened. Close any app that's using it, make sure it's available offline, then try again.");
}
