using System.Buffers.Binary;

namespace DesktopGuides.Core.Maps;

public enum MapImageKind { Png, Jpeg }

public sealed record MapImageSize(MapImageKind Kind, int Width, int Height);

// T21.2: a map's kind and size, read before anything decodes it.
public static class MapImageHeader
{
    private const int MaxJpegScan = 1024 * 1024;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static MapImageSize? Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> head = stackalloc byte[24];
        int read = ReadUpTo(stream, head);
        if (read >= 24 && head[..8].SequenceEqual(PngSignature) && head[12..16].SequenceEqual("IHDR"u8))
        {
            return Sized(MapImageKind.Png,
                BinaryPrimitives.ReadInt32BigEndian(head[16..]), BinaryPrimitives.ReadInt32BigEndian(head[20..]));
        }
        if (read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return ReadJpeg(stream, head[2..read].ToArray());
        }
        return null;
    }

    // Walks segments by their lengths, skipping fill bytes, to the frame header.
    private static MapImageSize? ReadJpeg(Stream stream, byte[] start)
    {
        using BufferedReader input = new(stream, start);
        long scanned = 0;
        while (scanned < MaxJpegScan)
        {
            if (input.Next() != 0xFF) return null;
            int marker;
            do
            {
                marker = input.Next();
            }
            while (marker == 0xFF);
            if (marker < 0) return null;
            int high = input.Next(), low = input.Next();
            if (low < 0) return null;
            int length = (high << 8) | low;
            if (length < 2) return null;
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                Span<byte> frame = stackalloc byte[5];
                for (int i = 0; i < 5; i++)
                {
                    int value = input.Next();
                    if (value < 0) return null;
                    frame[i] = (byte)value;
                }
                return Sized(MapImageKind.Jpeg,
                    BinaryPrimitives.ReadUInt16BigEndian(frame[3..]), BinaryPrimitives.ReadUInt16BigEndian(frame[1..]));
            }
            if (!input.Skip(length - 2)) return null;
            scanned += 2 + length;
        }
        return null;
    }

    private static MapImageSize? Sized(MapImageKind kind, int width, int height) =>
        width > 0 && height > 0 ? new MapImageSize(kind, width, height) : null;

    private static int ReadUpTo(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    // The bytes already read, then the stream; -1 at the end.
    private sealed class BufferedReader(Stream stream, byte[] start) : IDisposable
    {
        private int position;

        public int Next() => position < start.Length ? start[position++] : stream.ReadByte();

        public bool Skip(int count)
        {
            for (; count > 0 && position < start.Length; count--) position++;
            if (count == 0) return true;
            if (stream.CanSeek)
            {
                if (stream.Position + count > stream.Length) return false;
                stream.Seek(count, SeekOrigin.Current);
                return true;
            }
            for (; count > 0; count--)
            {
                if (stream.ReadByte() < 0) return false;
            }
            return true;
        }

        public void Dispose()
        {
        }
    }
}
