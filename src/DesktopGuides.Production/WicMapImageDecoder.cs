using DesktopGuides.Core.Import;
using DesktopGuides.Core.Maps;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DesktopGuides.Production;

// T21.2: proves a map decodes, at most 256 pixels on its longer side, and
// that the decoder agrees with the header's size. A bad image is false.
internal sealed class WicMapImageDecoder : IMapImageDecoder
{
    private const double CheckSide = 256;

    public Task<bool> DecodesAsync(string fullPath, MapImageSize expected, CancellationToken token) =>
        Task.Run(async () =>
        {
            try
            {
                using FileStream file = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using IRandomAccessStream stream = file.AsRandomAccessStream();
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
                if (decoder.PixelWidth != expected.Width || decoder.PixelHeight != expected.Height)
                {
                    return false;
                }
                double scale = Math.Min(1, CheckSide / Math.Max(expected.Width, expected.Height));
                BitmapTransform transform = new()
                {
                    ScaledWidth = (uint)Math.Max(1, Math.Round(expected.Width * scale)),
                    ScaledHeight = (uint)Math.Max(1, Math.Round(expected.Height * scale)),
                    InterpolationMode = BitmapInterpolationMode.Fant,
                };
                await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }, token);
}
