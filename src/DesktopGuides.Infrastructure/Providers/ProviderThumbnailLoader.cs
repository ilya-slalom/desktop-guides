using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;

namespace DesktopGuides.Infrastructure.Providers;

// Search thumbnails go through ProviderHttp and the artwork checks like any other provider image.
public sealed class ProviderThumbnailLoader(ProviderHttp http)
{
    /// <summary>Returns the validated image bytes, or null when the thumbnail cannot be shown.</summary>
    public async Task<byte[]?> LoadAsync(string url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return null;
        try
        {
            byte[] bytes = await http.GetImageAsync(uri, token);
            ArtworkValidator.Validate(bytes);
            return bytes;
        }
        catch (ProviderException) { return null; }
        catch (InvalidDataException) { return null; }
    }
}
