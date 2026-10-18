using System.Buffers.Binary;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Maps;

namespace DesktopGuides.Infrastructure.Tests.Import;

// Map headers built in the test, with padding so the files have bytes to hash.
internal static class MapBytes
{
    public static byte[] Png(int width, int height, int padding = 0)
    {
        byte[] bytes = new byte[33 + padding];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        bytes[24] = 8;
        bytes[25] = 6;
        for (int i = 33; i < bytes.Length; i++) bytes[i] = (byte)i;
        return bytes;
    }

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
        return [0xFF, 0xD8, .. frame, .. new byte[padding], 0xFF, 0xD9];
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
