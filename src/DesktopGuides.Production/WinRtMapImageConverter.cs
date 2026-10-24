using DesktopGuides.Infrastructure.Online;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DesktopGuides.Production;

/// <summary>S22 PR 5: a GIF map's first frame as PNG, through Windows' imaging codecs; an animated GIF's default frame is its first.</summary>
internal sealed class WinRtMapImageConverter : IMapImageConverter
{
    public async Task<byte[]> ToPngAsync(byte[] gif, CancellationToken token)
    {
        using InMemoryRandomAccessStream input = new();
        using (DataWriter writer = new(input.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(gif);
            await writer.StoreAsync().AsTask(token);
            await writer.FlushAsync().AsTask(token);
            writer.DetachStream();
        }
        input.Seek(0);
        // The codec is chosen by the bytes: a map listed as GIF can be a PNG.
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input).AsTask(token);
        using SoftwareBitmap frame = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(token);
        using InMemoryRandomAccessStream output = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output).AsTask(token);
        encoder.SetSoftwareBitmap(frame);
        await encoder.FlushAsync().AsTask(token);
        byte[] png = new byte[output.Size];
        output.Seek(0);
        using DataReader reader = new(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size).AsTask(token);
        reader.ReadBytes(png);
        return png;
    }
}
