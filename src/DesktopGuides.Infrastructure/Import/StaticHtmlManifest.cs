namespace DesktopGuides.Infrastructure.Import;

public enum StaticAssetKind
{
    EntryHtml,
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
    Remote
}

public enum StaticScanLimit
{
    EntryBytes,
    AssetBytes,
    TotalBytes,
    AssetCount,
    ReferenceCount,
    CssDepth,
    CssRuleCount
}

public sealed record StaticAsset(
    string RelativePath,
    StaticAssetKind Kind,
    long ByteCount,
    string Sha256);

public sealed record StaticAssetReference(
    string SourceRelativePath,
    string RawTarget,
    string? RelativePath,
    StaticAssetKind ExpectedKind,
    StaticReferenceStatus Status);

public sealed record StaticHtmlManifest(
    IReadOnlyList<StaticAsset> Assets,
    IReadOnlyList<StaticAssetReference> References,
    long TotalBytes);

public sealed record StaticHtmlScanLimits(
    long MaxEntryBytes = 16L * 1024 * 1024,
    long MaxAssetBytes = 32L * 1024 * 1024,
    long MaxTotalBytes = 256L * 1024 * 1024,
    int MaxAssets = 2048,
    int MaxReferences = 8192,
    int MaxCssDepth = 16,
    int MaxCssRules = 10000);

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
