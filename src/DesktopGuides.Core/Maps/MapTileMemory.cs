namespace DesktopGuides.Core.Maps;

/// <summary>
/// T21.2: a map's decoded tiles, least recently drawn first out, bounded by
/// their pixels' bytes within PDF's budget. The reader touches every visible
/// tile on each view change, so off-screen tiles go first. UI thread only.
/// </summary>
public sealed class MapTileMemory<TImage>(long maxBytes = MapTileMemory<TImage>.DefaultMaxBytes) where TImage : class
{
    public const long DefaultMaxBytes = 100_663_296;

    private readonly LinkedList<Entry> order = new();
    private readonly Dictionary<MapTileKey, LinkedListNode<Entry>> entries = [];

    public long MaxBytes { get; } = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
    public long CachedBytes { get; private set; }
    public long PeakBytes { get; private set; }
    public int Count => entries.Count;
    public int Evictions { get; private set; }

    public bool TryGet(MapTileKey key, out TImage? image)
    {
        if (!entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            image = null;
            return false;
        }
        MoveToFront(node);
        image = node.Value.Image;
        return true;
    }

    public void Touch(MapTileKey key)
    {
        if (entries.TryGetValue(key, out LinkedListNode<Entry>? node)) MoveToFront(node);
    }

    public void Add(MapTileKey key, TImage image, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        long bytes = (long)pixelWidth * pixelHeight * 4;
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
            Evictions++;
        }
        PeakBytes = Math.Max(PeakBytes, CachedBytes);
    }

    public void Clear()
    {
        order.Clear();
        entries.Clear();
        CachedBytes = 0;
    }

    private void MoveToFront(LinkedListNode<Entry> node)
    {
        order.Remove(node);
        order.AddFirst(node);
    }

    private sealed record Entry(MapTileKey Key, TImage Image, long Bytes);
}
