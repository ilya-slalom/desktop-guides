using System.Buffers.Binary;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Maps;

namespace DesktopGuides.Infrastructure.Tests.Import;

// Map headers built in the test, with padding so the files have bytes to hash.
internal static class MapBytes
{
    private static byte[] Chunk(string kind, byte[] data)
    {
        byte[] chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        System.Text.Encoding.ASCII.GetBytes(kind).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    // A whole PNG: signature, IHDR, an IDAT of padding bytes, and IEND.
    public static byte[] Png(int width, int height, int padding = 0)
    {
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        byte[] data = new byte[padding];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 200);
        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            .. Chunk("IHDR", header), .. Chunk("IDAT", data), .. Chunk("IEND", [])];
    }

    public static byte[] WholePng(int width, int height) => Png(width, height, padding: 64);

    // A whole JPEG: SOI, a frame header, a scan of padding bytes, and EOI. 27 bytes plus the padding.
    public static byte[] Jpeg(byte sof, int width, int height, int padding = 0)
    {
        byte[] frame = new byte[13];
        frame[0] = 0xFF;
        frame[1] = sof;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), 11);
        frame[4] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7), (ushort)width);
        frame[9] = 1;
        frame[10] = 1;
        frame[11] = 0x11;
        byte[] scan = [0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00];
        byte[] data = new byte[padding];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 200);
        return [0xFF, 0xD8, .. frame, .. scan, .. data, 0xFF, 0xD9];
    }
}

internal sealed class FakeMapDecoder(bool decodes) : IMapImageDecoder
{
    public List<MapImageSize> Calls { get; } = [];

    public Task<bool> DecodesAsync(string fullPath, MapImageSize expected, CancellationToken token)
    {
        Calls.Add(expected);
        return Task.FromResult(decodes);
    }
}
