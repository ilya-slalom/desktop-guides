namespace DesktopGuides.Core.Pdf;

public readonly record struct PdfRenderKey(string Fingerprint, int PageIndex, int PixelWidth);

/// <summary>
/// Rendered page images, least recently used first out, bounded by the
/// bytes their decoded pixels take. Used from the UI thread only.
/// </summary>
public sealed class PdfRenderCache<TImage> where TImage : class
{
    private readonly LinkedList<Entry> order = new();
    private readonly Dictionary<PdfRenderKey, LinkedListNode<Entry>> entries = [];

    public PdfRenderCache(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; }
    public long CachedBytes { get; private set; }
    // The highest total after eviction, so never above MaxBytes.
    public long PeakBytes { get; private set; }
    public int Count => entries.Count;

    public static long MeasureBytes(int pixelWidth, int pixelHeight) => (long)pixelWidth * pixelHeight * 4;

    public bool TryGet(PdfRenderKey key, out TImage? image)
    {
        if (!entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            image = null;
            return false;
        }
        order.Remove(node);
        order.AddFirst(node);
        image = node.Value.Image;
        return true;
    }

    // The new entry always stays; the raster budget keeps it under the cap.
    public void Add(PdfRenderKey key, TImage image, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        long bytes = MeasureBytes(pixelWidth, pixelHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes, MaxBytes);
        if (entries.Remove(key, out LinkedListNode<Entry>? existing))
        {
            order.Remove(existing);
            CachedBytes -= existing.Value.Bytes;
        }
        entries[key] = order.AddFirst(new Entry(key, image, bytes));
        CachedBytes += bytes;
        while (CachedBytes > MaxBytes)
        {
            LinkedListNode<Entry> oldest = order.Last!;
            order.RemoveLast();
            entries.Remove(oldest.Value.Key);
            CachedBytes -= oldest.Value.Bytes;
        }
        PeakBytes = Math.Max(PeakBytes, CachedBytes);
    }

    public void Clear()
    {
        order.Clear();
        entries.Clear();
        CachedBytes = 0;
    }

    private sealed record Entry(PdfRenderKey Key, TImage Image, long Bytes);
}
