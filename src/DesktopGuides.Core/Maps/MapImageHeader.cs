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

    // A truncated map can still decode, with its missing rows filled in. A
    // PNG's chunks must run whole to IEND; a JPEG needs its end marker after
    // its first scan, found by segment lengths so that a thumbnail inside
    // EXIF, itself a whole JPEG, doesn't count.
    public static bool IsComplete(Stream stream, MapImageKind kind)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return kind == MapImageKind.Png ? PngIsComplete(stream) : JpegIsComplete(stream);
    }

    private static bool PngIsComplete(Stream stream)
    {
        Span<byte> head = stackalloc byte[8];
        if (ReadUpTo(stream, head) < 8 || !head.SequenceEqual(PngSignature)) return false;
        Span<byte> chunk = stackalloc byte[8];
        while (ReadUpTo(stream, chunk) == 8)
        {
            long length = BinaryPrimitives.ReadUInt32BigEndian(chunk);
            if (chunk[4..8].SequenceEqual("IEND"u8))
            {
                return stream.Length - stream.Position >= length + 4;
            }
            if (stream.Position + length + 4 > stream.Length) return false;
            stream.Seek(length + 4, SeekOrigin.Current);
        }
        return false;
    }

    private static bool JpegIsComplete(Stream stream)
    {
        if (!SkipToFirstScan(stream)) return false;
        // Reads in blocks without wrapping the stream: the caller still uses it.
        byte[] buffer = new byte[81920];
        int previous = -1;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                int current = buffer[i];
                if (previous == 0xFF && current == 0xD9) return true;
                previous = current;
            }
        }
        return false;
    }

    // Leaves the stream just past the first scan's header.
    private static bool SkipToFirstScan(Stream stream)
    {
        using BufferedReader input = new(stream, []);
        if (input.Next() != 0xFF || input.Next() != 0xD8) return false;
        while (true)
        {
            if (input.Next() != 0xFF) return false;
            int marker;
            do
            {
                marker = input.Next();
            }
            while (marker == 0xFF);
            if (marker is < 0 or 0xD9) return false;
            int high = input.Next(), low = input.Next();
            if (low < 0) return false;
            int length = (high << 8) | low;
            if (length < 2 || !input.Skip(length - 2)) return false;
            if (marker == 0xDA) return true;
        }
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
