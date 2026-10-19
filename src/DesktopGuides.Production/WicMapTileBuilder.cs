using DesktopGuides.Core.Maps;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DesktopGuides.Production;

// T21.2: a large map's cache, decoded with WinRT's WIC. The overview is one
// Fant-scaled decode; the tiles come from level 0 in bands of one tile row,
// cropped by BitmapTransform.Bounds, through MapPyramidBuilder. Decoding
// ignores EXIF orientation, as the import check does, so sizes match the
// header. A band is at most 32768 × 512 × 4 bytes (64 MiB). WinRT's decode
// and encode run to the end once started, so a stop is checked before each
// band and each tile: removing a map waits for one band, not the build.
internal static class WicMapTileBuilder
{
    public static async Task WriteOverviewAsync(
        string sourcePath, MapPyramid pyramid, string overviewPath, CancellationToken token)
    {
        (int width, int height) = pyramid.OverviewSize;
        BitmapTransform transform = new()
        {
            ScaledWidth = (uint)width,
            ScaledHeight = (uint)height,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        byte[] pixels = await DecodeAsync(sourcePath, pyramid, transform, token);
        token.ThrowIfCancellationRequested();
        await WritePngAsync(overviewPath, width, height, pixels, token);
    }

    public static async Task<int> WriteTilesAsync(
        string sourcePath, MapPyramid pyramid, Func<MapTileKey, string> tilePath, CancellationToken token)
    {
        List<(MapTileKey Key, int Width, int Height, byte[] Pixels)> ready = [];
        MapPyramidBuilder builder = new(pyramid, (key, width, height, pixels) => ready.Add((key, width, height, pixels)));
        using FileStream file = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using IRandomAccessStream stream = file.AsRandomAccessStream();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        Check(decoder, pyramid);
        int written = 0;
        for (int top = 0; top < pyramid.Height; top += pyramid.TileSize)
        {
            token.ThrowIfCancellationRequested();
            int rows = Math.Min(pyramid.TileSize, pyramid.Height - top);
            BitmapTransform band = new()
            {
                Bounds = new BitmapBounds { X = 0, Y = (uint)top, Width = (uint)pyramid.Width, Height = (uint)rows },
            };
            PixelDataProvider data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, band,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
            builder.AddBand(data.DetachPixelData(), rows);
            if (top + rows == pyramid.Height) builder.Complete();
            foreach (var tile in ready)
            {
                token.ThrowIfCancellationRequested();
                await WritePngAsync(tilePath(tile.Key), tile.Width, tile.Height, tile.Pixels, token);
                written++;
            }
            ready.Clear();
        }
        return written;
    }

    private static async Task<byte[]> DecodeAsync(
        string sourcePath, MapPyramid pyramid, BitmapTransform transform, CancellationToken token)
    {
        using FileStream file = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using IRandomAccessStream stream = file.AsRandomAccessStream();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        Check(decoder, pyramid);
        PixelDataProvider data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
        return data.DetachPixelData();
    }

    // The file was checked at import; a different size now means it changed.
    private static void Check(BitmapDecoder decoder, MapPyramid pyramid)
    {
        if (decoder.PixelWidth != pyramid.Width || decoder.PixelHeight != pyramid.Height)
        {
            throw new InvalidDataException("The map's file no longer matches its size.");
        }
    }

    private static async Task WritePngAsync(string path, int width, int height, byte[] pixels, CancellationToken token)
    {
        await using FileStream file = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using IRandomAccessStream stream = file.AsRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(token);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync().AsTask(token);
    }
}
