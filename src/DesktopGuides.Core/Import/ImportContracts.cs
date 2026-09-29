using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Import;

public sealed record ImportSource(
    string FullPath, string FileName, long ByteCount, DateTimeOffset LastWriteUtc);

public abstract record ImportManifest(
    ImportSource Source, GuideFormat Format, string SuggestedTitle);

public sealed record TxtImportManifest(
    ImportSource Source, string SuggestedTitle, int? CodePage)   // null = UTF-8
    : ImportManifest(Source, GuideFormat.Txt, SuggestedTitle);

public sealed record HtmlImportManifest(
    ImportSource Source, string SuggestedTitle, string EntryRelativePath,
    int AssetCount, long TotalBytes, IReadOnlyList<ImportWarning> Warnings)
    : ImportManifest(Source, GuideFormat.Html, SuggestedTitle);

public sealed record PdfImportManifest(
    ImportSource Source, string SuggestedTitle, int PageCount, bool HasText)
    : ImportManifest(Source, GuideFormat.Pdf, SuggestedTitle);

public sealed record ImportWarning(string RelativePath, string Message);

public sealed record TxtEncodingSample(int CodePage, string Text);

public abstract record ImportInspection;

public sealed record ImportReady(ImportManifest Manifest) : ImportInspection;

public sealed record ImportNeedsTxtEncoding(
    ImportSource Source, string SuggestedTitle,
    IReadOnlyList<TxtEncodingSample> Samples) : ImportInspection;   // 437, 1252

public enum ImportIssue
{
    Missing, Unsupported, Empty, TooLarge, Unreadable, Encrypted,
    UnsupportedEncoding, Changed,
}

public sealed class GuideImportException(ImportIssue issue, string detail)
    : Exception(detail)
{
    public ImportIssue Issue { get; } = issue;
}

public interface IGuideImportValidator
{
    Task<ImportInspection> InspectAsync(string fullPath, CancellationToken token);
    Task<TxtImportManifest> ResolveTxtEncodingAsync(
        ImportNeedsTxtEncoding inspection, int codePage, CancellationToken token);
}
