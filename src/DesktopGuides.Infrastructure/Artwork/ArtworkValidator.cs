using System.Buffers.Binary;

namespace DesktopGuides.Infrastructure.Artwork;

internal sealed record ArtworkInfo(string Extension, int Width, int Height);

internal static class ArtworkValidator
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxDimension = 4096;

    public static ArtworkInfo Validate(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxBytes) throw Invalid("The cover image is larger than 5 MB.");
        ArtworkInfo info = ReadHeader(data) ?? throw Invalid("The cover image is not a PNG, JPEG or WebP file.");
        if (info.Width is < 1 or > MaxDimension || info.Height is < 1 or > MaxDimension)
        {
            throw Invalid("The cover image dimensions are out of range.");
        }
        return info;
    }

    private static ArtworkInfo? ReadHeader(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 24 && d[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
            d[12..16].SequenceEqual("IHDR"u8))
        {
            return new("png", BinaryPrimitives.ReadInt32BigEndian(d[16..]), BinaryPrimitives.ReadInt32BigEndian(d[20..]));
        }
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ReadJpeg(d);
        if (d.Length >= 30 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WEBP"u8))
        {
            ReadOnlySpan<byte> chunk = d[12..16];
            if (chunk.SequenceEqual("VP8X"u8))
                return new("webp", ReadUInt24(d[24..]) + 1, ReadUInt24(d[27..]) + 1);
            if (chunk.SequenceEqual("VP8L"u8) && d[20] == 0x2F)
            {
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(d[21..]);
                return new("webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            }
            if (chunk.SequenceEqual("VP8 "u8) && d[23] == 0x9D && d[24] == 0x01 && d[25] == 0x2A)
                return new("webp", BinaryPrimitives.ReadUInt16LittleEndian(d[26..]) & 0x3FFF,
                    BinaryPrimitives.ReadUInt16LittleEndian(d[28..]) & 0x3FFF);
        }
        return null;
    }

    private static ArtworkInfo? ReadJpeg(ReadOnlySpan<byte> d)
    {
        int position = 2;
        while (position + 9 <= d.Length)
        {
            if (d[position] != 0xFF) return null;
            byte marker = d[position + 1];
            if (marker == 0xFF) { position++; continue; }
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return new("jpg", BinaryPrimitives.ReadUInt16BigEndian(d[(position + 7)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(d[(position + 5)..]));
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(d[(position + 2)..]);
            if (length < 2) return null;
            position += 2 + length;
        }
        return null;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> d) => d[0] | (d[1] << 8) | (d[2] << 16);

    private static InvalidDataException Invalid(string message) => new(message);
}
