namespace DesktopGuides.Core.Html;

public enum GuideAssetKind
{
    EntryHtml,
    StyleSheet,
    Image
}

/// <summary>
/// One approved file of an HTML guide. RequestPath is the decoded URL path
/// and only ever a lookup key; RelativePath is the managed file to open.
/// </summary>
public sealed record GuideAsset(
    string RequestPath, string RelativePath, GuideAssetKind Kind, long ByteCount, string Sha256);
