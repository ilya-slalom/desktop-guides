using System.Collections;

namespace DesktopGuides.Core.Text;

public sealed record TextLineItem(int Index, string Text);

/// <summary>The rows of a TXT guide for a virtualizing list. Each access
/// builds a new item with <see cref="TextLineView.DisplayText"/>; nothing
/// is cached, so only realized rows hold line strings. The empty line after
/// a final newline is not a row.</summary>
public sealed class TextLineList : IReadOnlyList<TextLineItem>, IList
{
    public TextLineList(TextGuideDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        IReadOnlyList<int> starts = document.LineStarts;
        Count = starts[^1] == document.Text.Length ? starts.Count - 1 : starts.Count;
    }

    public TextGuideDocument Document { get; }
    public int Count { get; }
    public bool IsFixedSize => true;
    public bool IsReadOnly => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public TextLineItem this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return new TextLineItem(index, TextLineView.DisplayText(Document, index));
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw ReadOnly();
    }

    // Items are rebuilt on each access, so a list control finds one by its index.
    public int IndexOf(object? value) =>
        value is TextLineItem item && item.Index >= 0 && item.Index < Count ? item.Index : -1;

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (int line = 0; line < Count; line++)
        {
            array.SetValue(this[line], index + line);
        }
    }

    public IEnumerator<TextLineItem> GetEnumerator()
    {
        for (int line = 0; line < Count; line++)
        {
            yield return this[line];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Add(object? value) => throw ReadOnly();
    public void Clear() => throw ReadOnly();
    public void Insert(int index, object? value) => throw ReadOnly();
    public void Remove(object? value) => throw ReadOnly();
    public void RemoveAt(int index) => throw ReadOnly();

    private static NotSupportedException ReadOnly() => new("A guide's lines can't be changed.");
}
