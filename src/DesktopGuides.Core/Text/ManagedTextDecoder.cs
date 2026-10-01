using System.Text;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Text;

/// <summary>Decodes a TXT guide's managed copy by the encoding chosen at import.</summary>
public static class ManagedTextDecoder
{
    public static bool HasValidMetadata(Guide guide)
    {
        ArgumentNullException.ThrowIfNull(guide);
        return guide.Format == GuideFormat.Txt && guide.TextCodePage is null or 437 or 1252;
    }

    public static TextGuideLoad Decode(byte[] bytes, Guide guide)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!HasValidMetadata(guide))
        {
            return new TextGuideLoadFailed(TextGuideLoadError.InvalidMetadata);
        }

        TextGuideDocument document;
        try
        {
            document = TextGuideDocument.Decode(bytes, guide.TextCodePage);
        }
        catch (EncodingSelectionRequiredException)
        {
            return new TextGuideLoadFailed(TextGuideLoadError.NotUtf8);
        }
        catch (DecoderFallbackException)
        {
            return new TextGuideLoadFailed(TextGuideLoadError.Undecodable);
        }

        bool contentChanged = bytes.LongLength != guide.ContentBytes ||
            !string.Equals(document.ContentSha256, guide.ContentSha256, StringComparison.OrdinalIgnoreCase);
        return new TextGuideLoaded(document, contentChanged);
    }
}
