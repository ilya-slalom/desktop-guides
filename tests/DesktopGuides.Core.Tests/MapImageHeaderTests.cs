using System.Buffers.Binary;
using DesktopGuides.Core.Maps;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class MapImageHeaderTests
{
    private static MapImageSize? Read(byte[] bytes) => MapImageHeader.Read(new MemoryStream(bytes));

    private static byte[] Png(int w, int h)
    {
        byte[] bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), w);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), h);
        bytes[24] = 8;
        bytes[25] = 6;
        return bytes;
    }

    private static byte[] Segment(byte marker, int length)
    {
        byte[] segment = new byte[2 + length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)length);
        return segment;
    }

    private static byte[] Sof(byte sof, int w, int h)
    {
        byte[] frame = Segment(sof, 11);
        frame[4] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5), (ushort)h);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7), (ushort)w);
        frame[9] = 1;
        frame[10] = 1;
        frame[11] = 0x11;
        return frame;
    }

    private static byte[] Jpeg(byte sof, int w, int h, int app1Bytes = 0) =>
        [0xFF, 0xD8, .. app1Bytes > 0 ? Segment(0xE1, app1Bytes) : [], .. Sof(sof, w, h), 0xFF, 0xD9];

    private static byte[] JpegWithDhtFirst(int w, int h) =>
        [0xFF, 0xD8, .. Segment(0xC4, 20), .. Sof(0xC0, w, h), 0xFF, 0xD9];

    [Fact]
    public void APngReadsItsSize() =>
        Assert.Equal(new MapImageSize(MapImageKind.Png, 15456, 10304), Read(Png(15456, 10304)));

    [Fact]
    public void ABaselineJpegReadsItsSize() =>
        Assert.Equal(new MapImageSize(MapImageKind.Jpeg, 640, 480), Read(Jpeg(0xC0, 640, 480)));

    // Review Focus 2.
    [Fact]
    public void AProgressiveJpegReadsItsSize() =>
        Assert.Equal(new MapImageSize(MapImageKind.Jpeg, 1200, 900), Read(Jpeg(0xC2, 1200, 900)));

    // Review Focus 1.
    [Fact]
    public void AJpegWithALargeExifSegmentReadsItsSize() =>
        Assert.Equal(new MapImageSize(MapImageKind.Jpeg, 2048, 1536), Read(Jpeg(0xC0, 2048, 1536, app1Bytes: 65533)));

    [Fact]
    public void AHuffmanTableIsNotAFrame() =>
        Assert.Equal(new MapImageSize(MapImageKind.Jpeg, 64, 48), Read(JpegWithDhtFirst(64, 48)));

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    public void AnythingElseHasNoSize(byte[] bytes) => Assert.Null(Read(bytes));

    [Fact]
    public void AZeroSizeIsNoSize() => Assert.Null(Read(Png(0, 10)));

    [Theory]
    [InlineData(32768, 1, null)]
    [InlineData(32769, 1, MapLimit.Side)]
    [InlineData(1, 40000, MapLimit.Side)]
    [InlineData(16384, 16384, null)]
    [InlineData(16385, 16384, MapLimit.Pixels)]
    [InlineData(20000, 20000, MapLimit.Pixels)]
    public void LimitsRefuseASideOrTooManyPixels(int w, int h, MapLimit? expected) =>
        Assert.Equal(expected, MapImageLimits.Check(new MapImageSize(MapImageKind.Png, w, h)));

    // A truncated map can still decode, with its missing rows filled in, so
    // completeness is checked on its own.
    private static byte[] Chunk(string kind, byte[] data)
    {
        byte[] chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        System.Text.Encoding.ASCII.GetBytes(kind).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] WholePng() =>
        [.. Png(4, 4)[..8], .. Chunk("IHDR", Png(4, 4)[16..29]), .. Chunk("IDAT", new byte[40]), .. Chunk("IEND", [])];

    private static bool Complete(byte[] bytes, MapImageKind kind) =>
        MapImageHeader.IsComplete(new MemoryStream(bytes), kind);

    [Fact]
    public void AWholePngIsComplete() => Assert.True(Complete(WholePng(), MapImageKind.Png));

    [Fact]
    public void APngCutShortIsIncomplete()
    {
        byte[] whole = WholePng();
        Assert.False(Complete(whole[..(whole.Length / 2)], MapImageKind.Png));
        Assert.False(Complete(whole[..^12], MapImageKind.Png));   // no IEND
    }

    private static byte[] WholeJpeg(bool exifThumbnail = false) =>
        [0xFF, 0xD8,
            // A real thumbnail is a whole JPEG, with its own scan and end.
            .. exifThumbnail ? [0xFF, 0xE1, 0x00, 0x0C, 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0x56, 0xFF, 0xD9, 0x00] : Array.Empty<byte>(),
            .. Sof(0xC0, 8, 8), .. Segment(0xDA, 8), 0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD9];

    [Fact]
    public void AWholeJpegIsComplete() => Assert.True(Complete(WholeJpeg(), MapImageKind.Jpeg));

    [Fact]
    public void AJpegCutBeforeItsEndIsIncomplete() =>
        Assert.False(Complete(WholeJpeg()[..^2], MapImageKind.Jpeg));

    // An EXIF thumbnail's own scan and end come before the map's, so they don't count.
    [Fact]
    public void AThumbnailsEndIsNotTheMapsEnd()
    {
        byte[] cut = WholeJpeg(exifThumbnail: true)[..^2];
        Assert.False(Complete(cut, MapImageKind.Jpeg));
        Assert.True(Complete(WholeJpeg(exifThumbnail: true), MapImageKind.Jpeg));
    }

    [Fact]
    public void AJpegCutAfterItsThumbnailIsIncomplete()
    {
        byte[] whole = WholeJpeg(exifThumbnail: true);
        Assert.False(Complete(whole[..18], MapImageKind.Jpeg));
    }
}
