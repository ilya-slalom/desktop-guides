using DesktopGuides.Infrastructure.Artwork;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Artwork;

public sealed class ArtworkValidatorTests
{
    public static TheoryData<byte[], string, int, int> Valid => new()
    {
        { TestImages.Png(600, 900), "png", 600, 900 },
        { TestImages.Jpeg(264, 352), "jpg", 264, 352 },
        { TestImages.WebPExtended(600, 900), "webp", 600, 900 },
        { TestImages.WebPLossless(512, 768), "webp", 512, 768 },
        { TestImages.WebPLossy(300, 450), "webp", 300, 450 },
        { TestImages.Png(4096, 4096), "png", 4096, 4096 },
    };

    [Theory, MemberData(nameof(Valid))]
    public void AcceptsSupportedHeaders(byte[] data, string extension, int width, int height) =>
        Assert.Equal(new ArtworkInfo(extension, width, height), ArtworkValidator.Validate(data));

    public static TheoryData<byte[]> Invalid => new()
    {
        Array.Empty<byte>(),
        "GIF89a\u0001\u0000\u0001\u0000"u8.ToArray(),
        "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(),
        TestImages.Png(4097, 10),
        TestImages.Png(0, 10),
        TestImages.Jpeg(10, 5000),
        TestImages.WebPExtended(5000, 10),
        TestImages.Png(10, 10)[..20],
        TestImages.Jpeg(10, 10)[..12],
    };

    [Theory, MemberData(nameof(Invalid))]
    public void RejectsOtherFormatsTruncatedAndOversizedImages(byte[] data) =>
        Assert.Throws<InvalidDataException>(() => ArtworkValidator.Validate(data));

    [Fact]
    public void RejectsFilesOverFiveMegabytes()
    {
        byte[] data = new byte[ArtworkValidator.MaxBytes + 1];
        TestImages.Png(10, 10).CopyTo(data, 0);
        Assert.Throws<InvalidDataException>(() => ArtworkValidator.Validate(data));
    }
}
