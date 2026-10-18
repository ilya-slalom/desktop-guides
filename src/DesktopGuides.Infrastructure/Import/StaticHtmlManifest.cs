namespace DesktopGuides.Infrastructure.Import;

public enum StaticAssetKind
{
    EntryHtml,
    Page,
    StyleSheet,
    Image,
    Other
}

public enum StaticReferenceStatus
{
    Included,
    Missing,
    Unsupported,
    Unsafe,
    Remote,
    // T21.2: a stylesheet found by role that isn't readable CSS.
    Invalid
}

public enum StaticScanLimit
{
    EntryBytes,
    AssetBytes,
    TotalBytes,
    AssetCount,
    ReferenceCount,
    CssDepth,
    CssRuleCount,
    PageCount,
    GuideBytes,
    GuideAssetCount
}

/// <summary>
/// RelativePath is the safe managed-file path. RequestRelativePath is the
/// decoded URL lookup path for the later reader allowlist; never open it as
/// a filesystem path.
/// </summary>
public sealed record StaticAsset(
    string RelativePath,
    StaticAssetKind Kind,
    long ByteCount,
    string Sha256,
    string RequestRelativePath);

public sealed record StaticAssetReference(
    string SourceRelativePath,
    string RawTarget,
    string? RelativePath,
    StaticAssetKind ExpectedKind,
    StaticReferenceStatus Status);

public sealed record StaticHtmlManifest(
    IReadOnlyList<StaticAsset> Assets,
    IReadOnlyList<StaticAssetReference> References,
    long TotalBytes,
    IReadOnlyList<StaticPage> Pages);

/// <summary>
/// T21.2: one page of the guide, in reading order. Pages[0] is the entry.
/// OriginalUrl is the address the saved page recorded, when it did.
/// </summary>
public sealed record StaticPage(
    string RelativePath, string RequestRelativePath, string? OriginalUrl, string? Title);

/// <summary>
/// T21.2: a source that can list the saved pages beside the entry, by
/// top-level file name. Names are candidates only; every read still goes
/// through OpenReadAsync.
/// </summary>
public interface IStaticHtmlPageDirectory
{
    IReadOnlyList<string> ListPages();
}

public sealed record StaticHtmlScanLimits(
    long MaxEntryBytes = 16L * 1024 * 1024,
    long MaxAssetBytes = 32L * 1024 * 1024,
    long MaxTotalBytes = 256L * 1024 * 1024,
    int MaxAssets = 2048,
    int MaxReferences = 8192,
    int MaxCssDepth = 16,
    int MaxCssRules = 10000,
    int MaxPages = 200,
    long MaxGuideBytes = 1024L * 1024 * 1024,
    int MaxGuideAssets = 16384);

public sealed class StaticHtmlScanException(
    StaticScanLimit limit,
    string message) : IOException(message)
{
    public StaticScanLimit Limit { get; } = limit;
}

/// <summary>
/// Supplies bytes by a validated root-relative path. T07.2 implements the
/// Windows filesystem boundary; callers must never resolve paths through links.
/// </summary>
public interface IStaticHtmlAssetSource
{
    ValueTask<Stream?> OpenReadAsync(
        string safeRelativePath,
        CancellationToken cancellationToken = default);
}
