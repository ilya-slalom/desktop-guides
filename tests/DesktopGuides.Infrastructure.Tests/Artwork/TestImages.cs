using System.Buffers.Binary;

namespace DesktopGuides.Infrastructure.Tests.Artwork;

internal static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        byte[] data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }
            .CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), height);
        return data;
    }

    public static byte[] Jpeg(int width, int height)
    {
        List<byte> data = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        data.AddRange(new byte[14]);
        data.AddRange([0xFF, 0xC4, 0x00, 0x04, 0x00, 0x00]); // DHT is not a frame header
        data.AddRange([0xFF, 0xC2, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width]);
        data.AddRange(new byte[12]);
        return [.. data];
    }

    public static byte[] WebPExtended(int width, int height)
    {
        byte[] data = Riff("VP8X", 30);
        WriteUInt24(data, 24, width - 1);
        WriteUInt24(data, 27, height - 1);
        return data;
    }

    public static byte[] WebPLossless(int width, int height)
    {
        byte[] data = Riff("VP8L", 30);
        data[20] = 0x2F;
        uint bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(21), bits);
        return data;
    }

    public static byte[] WebPLossy(int width, int height)
    {
        byte[] data = Riff("VP8 ", 30);
        data[23] = 0x9D; data[24] = 0x01; data[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), (ushort)height);
        return data;
    }

    private static byte[] Riff(string chunk, int length)
    {
        byte[] data = new byte[length];
        "RIFF"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), length - 8);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(data, 12);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), length - 20);
        return data;
    }

    private static void WriteUInt24(byte[] data, int offset, int value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
    }
}
