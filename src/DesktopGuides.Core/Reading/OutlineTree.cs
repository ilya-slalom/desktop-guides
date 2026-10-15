namespace DesktopGuides.Core.Reading;

public static class OutlineTree
{
    public const int MaxTitleLength = 120;

    // Each item nests under the nearest earlier item of a lower level, so a
    // jump of levels (h1 then h4) nests one down, never into empty levels.
    public static Outline Build(OutlineSource source, IEnumerable<OutlineItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        List<Node> roots = [];
        Stack<Node> path = new();
        foreach (OutlineItem item in items)
        {
            while (path.Count > 0 && path.Peek().Item.Level >= item.Level) path.Pop();
            Node node = new(item, path.Count + 1);
            (path.Count == 0 ? roots : path.Peek().Children).Add(node);
            path.Push(node);
        }
        return roots.Count == 0 ? Outline.Empty : new Outline(source, roots.Select(Freeze).ToArray());
    }

    public static OutlineEntry? EntryAt(Outline outline, long order)
    {
        ArgumentNullException.ThrowIfNull(outline);
        OutlineEntry? found = null;
        foreach (OutlineEntry entry in Flatten(outline.Entries))
        {
            if (entry.Order > order) break;
            found = entry;
        }
        return found;
    }

    public static IEnumerable<OutlineEntry> Flatten(IEnumerable<OutlineEntry> entries)
    {
        foreach (OutlineEntry entry in entries)
        {
            yield return entry;
            foreach (OutlineEntry child in Flatten(entry.Children)) yield return child;
        }
    }

    private static OutlineEntry Freeze(Node node) =>
        new(Trim(node.Item.Title), node.Depth, node.Item.Location, node.Item.Order,
            node.Children.Select(Freeze).ToArray());

    private static string Trim(string title)
    {
        string trimmed = title.Trim();
        if (trimmed.Length <= MaxTitleLength) return trimmed;
        int length = MaxTitleLength - 1;
        if (char.IsHighSurrogate(trimmed[length - 1])) length--;
        return trimmed[..length] + "…";
    }

    private sealed record Node(OutlineItem Item, int Depth)
    {
        public List<Node> Children { get; } = [];
    }
}
