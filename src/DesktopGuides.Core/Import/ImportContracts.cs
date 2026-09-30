using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Import;

public sealed record ImportSource(
    string FullPath, string FileName, long ByteCount, DateTimeOffset LastWriteUtc);

/// <summary>Fingerprint is lowercase hex SHA-256: the file's bytes, or the HTML file list.</summary>
public abstract record ImportManifest(
    ImportSource Source, GuideFormat Format, string SuggestedTitle, string Fingerprint);

public sealed record TxtImportManifest(
    ImportSource Source, string SuggestedTitle, int? CodePage, string Fingerprint)   // null = UTF-8
    : ImportManifest(Source, GuideFormat.Txt, SuggestedTitle, Fingerprint);

public sealed record HtmlImportManifest(
    ImportSource Source, string SuggestedTitle, string EntryRelativePath,
    int AssetCount, long TotalBytes, IReadOnlyList<ImportWarning> Warnings,
    string Fingerprint)
    : ImportManifest(Source, GuideFormat.Html, SuggestedTitle, Fingerprint);

public sealed record PdfImportManifest(
    ImportSource Source, string SuggestedTitle, int PageCount, bool HasText, string Fingerprint)
    : ImportManifest(Source, GuideFormat.Pdf, SuggestedTitle, Fingerprint);

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
    UnsupportedEncoding, Changed, NotEnoughSpace, SaveFailed, Duplicate,
}

/// <summary>Copy progress from 0 to 1; Publishing is set once cancellation no longer applies.</summary>
public readonly record struct ImportProgress(double Fraction, bool Publishing);

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
