using System.Security.Cryptography;
using DesktopGuides.Core.Html;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure.Reading;

public enum HtmlAssetReadStatus { Served, Missing, Changed }

public sealed record HtmlAssetRead(HtmlAssetReadStatus Status, byte[]? Bytes = null);

/// <summary>
/// Opens a row's managed file, never the request path, and returns its
/// bytes only when they still match the imported length and hash.
/// </summary>
public sealed class ManagedHtmlAssetReader(ManagedPathResolver paths, Guid guideId)
{
    private static readonly HtmlAssetRead Missing = new(HtmlAssetReadStatus.Missing);
    private static readonly HtmlAssetRead Changed = new(HtmlAssetReadStatus.Changed);

    public HtmlAssetRead Read(GuideAsset asset)
    {
        try
        {
            string path = paths.ResolveExistingGuideFile(guideId, asset.RelativePath);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != asset.ByteCount || asset.ByteCount > Array.MaxLength) return Changed;
            byte[] bytes = new byte[asset.ByteCount];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) return Changed;
            byte[] expected = Convert.FromHexString(asset.Sha256);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), expected)
                ? new HtmlAssetRead(HtmlAssetReadStatus.Served, bytes)
                : Changed;
        }
        catch (EndOfStreamException)
        {
            return Changed;
        }
        catch (FormatException)
        {
            return Changed;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException)
        {
            return Missing;
        }
    }
}
