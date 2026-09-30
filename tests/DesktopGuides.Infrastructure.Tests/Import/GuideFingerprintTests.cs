using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class GuideFingerprintTests
{
    private static readonly (string, string)[] Files =
    [
        ("guide.html", new string('a', 64)),
        ("styles/site.css", new string('b', 64)),
    ];

    [Fact]
    public void HtmlFingerprintHashesSortedPathAndHashLines() =>
        Assert.Equal(
            "4124251947a2ecaab5072f8a3d6c7e914af2b8ab1bf2920001d050d131f3759a",
            GuideFingerprint.OfHtml(Files));

    [Fact]
    public void HtmlFingerprintIgnoresInputOrder() =>
        Assert.Equal(GuideFingerprint.OfHtml(Files), GuideFingerprint.OfHtml(Files.Reverse()));

    [Fact]
    public void StreamHashMatchesTheByteHashAcrossBuffers()
    {
        byte[] bytes = new byte[200_000];
        new Random(4).NextBytes(bytes);

        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            GuideFingerprint.OfBytes(bytes));
        Assert.Equal(GuideFingerprint.OfBytes(bytes), GuideFingerprint.OfStream(new MemoryStream(bytes), CancellationToken.None));
    }

    [Fact]
    public void StreamHashStopsWhenCancelled()
    {
        using CancellationTokenSource cancel = new();
        using CancelOnReadStream stream = new(new byte[200_000], cancel);

        Assert.ThrowsAny<OperationCanceledException>(() => GuideFingerprint.OfStream(stream, cancel.Token));
        Assert.Equal(1, stream.Reads);
    }

    private sealed class CancelOnReadStream(byte[] bytes, CancellationTokenSource cancel)
        : MemoryStream(bytes, writable: false)
    {
        public int Reads { get; private set; }

        // A derived MemoryStream's span Read falls back to this overload.
        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            cancel.Cancel();
            return base.Read(buffer, offset, count);
        }
    }
}
