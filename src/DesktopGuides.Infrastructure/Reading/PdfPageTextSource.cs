using DesktopGuides.Infrastructure.Import;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DesktopGuides.Infrastructure.Reading;

public sealed record PdfPageText(string Text, bool HasLetters, bool Truncated);

public sealed class PdfPageTextException(int pageIndex, Exception inner)
    : Exception($"Page {pageIndex + 1}'s text couldn't be read.", inner)
{
    public int PageIndex { get; } = pageIndex;
}

/// <summary>
/// Page text from the managed copy, one page at a time. PdfPig isn't
/// thread-safe, so one extraction runs at a time; recently read pages are
/// kept within page and character limits.
/// </summary>
public sealed class PdfPageTextSource : IDisposable
{
    public const int DefaultMaxPageCharacters = 1_048_576;
    public const int DefaultMaxPages = 8;
    public const long DefaultMaxCharacters = 4_194_304;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Stream file;
    private readonly CancellableReadStream reader;
    private readonly PdfDocument document;
    private readonly int maxPageCharacters;
    private readonly int maxPages;
    private readonly long maxCharacters;
    private readonly LinkedList<(int Page, PdfPageText Text)> recent = new();
    private long recentCharacters;
    private bool disposed;

    private PdfPageTextSource(Stream file, CancellableReadStream reader, PdfDocument document,
        int maxPageCharacters, int maxPages, long maxCharacters)
    {
        this.file = file;
        this.reader = reader;
        this.document = document;
        this.maxPageCharacters = maxPageCharacters;
        this.maxPages = maxPages;
        this.maxCharacters = maxCharacters;
        PageCount = document.NumberOfPages;
    }

    public int PageCount { get; }
    public int PeakPages { get; private set; }
    public long PeakCharacters { get; private set; }

    // On success the source owns file; on failure the caller still does.
    internal static PdfPageTextSource Open(Stream file, CancellationToken token,
        int maxPageCharacters = DefaultMaxPageCharacters, int maxPages = DefaultMaxPages,
        long maxCharacters = DefaultMaxCharacters)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPageCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, maxPageCharacters);
        CancellableReadStream reader = new(file, token);
        PdfDocument document = PdfDocument.Open(reader);
        reader.Token = CancellationToken.None;
        return new PdfPageTextSource(file, reader, document, maxPageCharacters, maxPages, maxCharacters);
    }

    public async Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (TakeRecent(pageIndex) is PdfPageText hit) return hit;
            PdfPageText text = await Task.Run(() => Extract(pageIndex, token), token).ConfigureAwait(false);
            Remember(pageIndex, text);
            return text;
        }
        finally
        {
            gate.Release();
        }
    }

    // Waits for a running extraction, so the document is never closed under it.
    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            recent.Clear();
            recentCharacters = 0;
            document.Dispose();
            file.Dispose();
        }
        finally
        {
            gate.Release();
        }
    }

    private PdfPageText Extract(int pageIndex, CancellationToken token)
    {
        reader.Token = token;
        try
        {
            string text = ContentOrderTextExtractor.GetText(document.GetPage(pageIndex + 1));
            bool truncated = text.Length > maxPageCharacters;
            if (truncated)
            {
                int length = maxPageCharacters;
                if (char.IsHighSurrogate(text[length - 1])) length--;
                text = text[..length];
            }
            return new PdfPageText(text, text.Any(char.IsLetter), truncated);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // PdfPig may wrap the stream's cancellation in its own exception.
            throw new OperationCanceledException(token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new PdfPageTextException(pageIndex, error);
        }
        finally
        {
            reader.Token = CancellationToken.None;
        }
    }

    private PdfPageText? TakeRecent(int pageIndex)
    {
        for (LinkedListNode<(int Page, PdfPageText Text)>? node = recent.First; node is not null; node = node.Next)
        {
            if (node.Value.Page != pageIndex) continue;
            recent.Remove(node);
            recent.AddFirst(node);
            return node.Value.Text;
        }
        return null;
    }

    private void Remember(int pageIndex, PdfPageText text)
    {
        recent.AddFirst((pageIndex, text));
        recentCharacters += text.Text.Length;
        while (recent.Count > 1 && (recent.Count > maxPages || recentCharacters > maxCharacters))
        {
            recentCharacters -= recent.Last!.Value.Text.Text.Length;
            recent.RemoveLast();
        }
        PeakPages = Math.Max(PeakPages, recent.Count);
        PeakCharacters = Math.Max(PeakCharacters, recentCharacters);
    }
}
