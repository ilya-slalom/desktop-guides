namespace DesktopGuides.Core.Reading;

// T18.2: a guide's table of contents. A session that implements
// IOutlineReader declares ReaderCapabilities.Outline.
public enum OutlineSource { Document, Detected, None }

// Level counts from 1 at the top of the tree; Order is the place in
// reading order (the TXT offset, the HTML walk offset, the PDF page).
public sealed record OutlineEntry(
    string Title, int Level, ReaderLocation Location, long Order, IReadOnlyList<OutlineEntry> Children);

public sealed record Outline(OutlineSource Source, IReadOnlyList<OutlineEntry> Entries)
{
    public static Outline Empty { get; } = new(OutlineSource.None, []);
}

// A flat entry before nesting; Level is the source's own level.
public readonly record struct OutlineItem(string Title, int Level, ReaderLocation Location, long Order);

public interface IOutlineReader
{
    Task<Outline> GetOutlineAsync(CancellationToken token);

    // The current place's Order, to select the entry it falls in.
    long OrderOf(ReaderLocation location);
}
