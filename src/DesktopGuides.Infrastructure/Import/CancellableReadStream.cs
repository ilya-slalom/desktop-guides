namespace DesktopGuides.Infrastructure.Import;

// PdfPig ignores cancellation tokens and reads lazily, so the token is
// checked on every read. Token is settable so a long-lived document can
// honor each caller's token in turn. Doesn't own the inner stream.
internal sealed class CancellableReadStream(Stream inner, CancellationToken token) : Stream
{
    public CancellationToken Token { get; set; } = token;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set
        {
            Token.ThrowIfCancellationRequested();
            inner.Position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Token.ThrowIfCancellationRequested();
        return inner.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        Token.ThrowIfCancellationRequested();
        return inner.Read(buffer);
    }

    public override int ReadByte()
    {
        Token.ThrowIfCancellationRequested();
        return inner.ReadByte();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Token.ThrowIfCancellationRequested();
        return inner.Seek(offset, origin);
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
