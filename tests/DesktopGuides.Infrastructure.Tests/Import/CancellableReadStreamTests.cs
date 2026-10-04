using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class CancellableReadStreamTests
{
    [Fact]
    public void ReadsThrowOnceTheTokenIsCancelled()
    {
        using CancellationTokenSource cancel = new();
        using CancellableReadStream stream = new(new MemoryStream([1, 2, 3]), cancel.Token);
        Assert.Equal(1, stream.ReadByte());

        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => stream.ReadByte());
        Assert.Throws<OperationCanceledException>(() => stream.Read(new byte[1], 0, 1));
        Assert.Throws<OperationCanceledException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<OperationCanceledException>(() => stream.Position = 0);
    }

    [Fact]
    public void AReplacedTokenAppliesToLaterReads()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        using CancellableReadStream stream = new(new MemoryStream([1, 2, 3]), cancel.Token);

        stream.Token = CancellationToken.None;

        Assert.Equal(1, stream.ReadByte());
    }

    [Fact]
    public void ItIsReadOnly()
    {
        using CancellableReadStream stream = new(new MemoryStream([1]), CancellationToken.None);

        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
    }
}
