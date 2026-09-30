using System.Text;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
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
    private static readonly int[] LegacyCodePages = [437, 1252];
    private static readonly byte[] PdfMarker = "%PDF-"u8.ToArray();
    private readonly GuideImportLimits limits;
    private readonly StaticHtmlImportValidator html;

    public GuideImportValidator(GuideImportLimits? limits = null)
    {
        this.limits = limits ?? new GuideImportLimits();
        html = new StaticHtmlImportValidator(this.limits.HtmlLimits);
    }

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
            throw new GuideImportException(ImportIssue.Unsupported, $"{name} isn't a TXT, HTML or PDF file.");
        if (file.Length == 0)
        {
            throw new GuideImportException(ImportIssue.Empty, "The file is empty.");
        }
        CheckSize(file.Length, name, format);
        ImportSource source = new(
            file.FullName, name, file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
        string title = GuideTitle.Suggest(name);
        // Callers include the UI thread, and cached reads can complete
        // synchronously, so the work runs on the thread pool.
        return format switch
        {
            GuideFormat.Txt => await Task.Run(() => InspectTxtAsync(source, title, token), token).ConfigureAwait(false),
            GuideFormat.Html => await Task.Run(() => InspectHtmlAsync(source, title, token), token).ConfigureAwait(false),
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
            throw new GuideImportException(ImportIssue.Changed,
                $"{source.FileName} changed after it was checked. Choose it again.");
        }
        return await Task.Run(async () =>
        {
            byte[] bytes = await ReadAllAsync(source, token);
            // Both code pages decode every byte, so this can't ask for another encoding.
            TextGuideDocument.Decode(bytes, codePage);
            return new TxtImportManifest(source, inspection.SuggestedTitle, codePage);
        }, token).ConfigureAwait(false);
    }

    private async Task<ImportInspection> InspectTxtAsync(
        ImportSource source, string title, CancellationToken token)
    {
        byte[] bytes = await ReadAllAsync(source, token);
        bool utf16Bom = bytes.Length >= 2 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
        if (utf16Bom || bytes.AsSpan().IndexOf((byte)0) >= 0)
        {
            throw new GuideImportException(ImportIssue.UnsupportedEncoding,
                "This text file isn't UTF-8. Save it as UTF-8 and import it again.");
        }
        try
        {
            TextGuideDocument.Decode(bytes);
            return new ImportReady(new TxtImportManifest(source, title, null));
        }
        catch (EncodingSelectionRequiredException)
        {
            return new ImportNeedsTxtEncoding(source, title, Samples(bytes));
        }
    }

    private async Task<ImportInspection> InspectHtmlAsync(
        ImportSource source, string title, CancellationToken token)
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
            throw new GuideImportException(ImportIssue.Unreadable, "The guide refers to a file outside its folder.");
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
        ImportWarning[] warnings = preview.Warnings
            .Select(warning => new ImportWarning(warning.RelativePath ?? warning.RawTarget, warning.Message))
            .ToArray();
        return new ImportReady(new HtmlImportManifest(
            source, title, preview.EntryRelativePath,
            preview.Manifest.Assets.Count - 1, preview.Manifest.TotalBytes, warnings));
    }

    private static Task<ImportInspection> InspectPdfAsync(
        ImportSource source, string title, CancellationToken token) =>
        Task.Run<ImportInspection>(() =>
        {
            using FileStream stream = OpenSource(source.FullPath, source.FileName, asyncIo: false);
            return ReadPdf(stream, source, title, token);
        }, token);

    internal static ImportInspection ReadPdf(
        Stream stream, ImportSource source, string title, CancellationToken token)
    {
        try
        {
            if (!StartsLikePdf(stream))
            {
                throw NotPdf();
            }
            stream.Position = 0;
            // PdfPig ignores the token, so the stream checks it on each read.
            using PdfDocument document = PdfDocument.Open(new CancellableReadStream(stream, token));
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
            return new ImportReady(new PdfImportManifest(source, title, pages, hasText));
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new GuideImportException(ImportIssue.Encrypted,
                "Password-protected PDFs aren't supported. Remove the password and import again.");
        }
        catch (Exception error) when (error is not (GuideImportException or OperationCanceledException))
        {
            // PdfPig reports malformed files with several exception types.
            throw NotPdf();
        }
    }

    private sealed class CancellableReadStream(Stream inner, CancellationToken token) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set
            {
                token.ThrowIfCancellationRequested();
                inner.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            token.ThrowIfCancellationRequested();
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            return inner.Read(buffer);
        }

        public override int ReadByte()
        {
            token.ThrowIfCancellationRequested();
            return inner.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            token.ThrowIfCancellationRequested();
            return inner.Seek(offset, origin);
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

    private static bool StartsLikePdf(Stream stream)
    {
        byte[] head = new byte[1024];
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return head.AsSpan(0, read).IndexOf(PdfMarker) >= 0;
    }

    private static GuideImportException NotPdf() =>
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
            _ => throw new ArgumentOutOfRangeException(nameof(limit)),
        };
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
        _ => null,
    };

    private static GuideImportException Missing(string name) =>
        new(ImportIssue.Missing, $"{name} can't be found. It may have been moved or deleted.");

    private static GuideImportException Unreadable(string name) =>
        new(ImportIssue.Unreadable,
            $"{name} can't be opened. Close any app that's using it, make sure it's available offline, then try again.");
}
