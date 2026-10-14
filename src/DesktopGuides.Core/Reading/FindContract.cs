namespace DesktopGuides.Core.Reading;

// T18.1: in-guide search. A session that implements IFindableReader
// declares ReaderCapabilities.Find; the shell shows the find bar only then.
public sealed record FindQuery
{
    public FindQuery(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string collapsed = TextMatcher.Collapse(text).Trim();
        if (collapsed.Length == 0)
        {
            throw new ArgumentException("Enter text to find.", nameof(text));
        }
        Text = collapsed;
    }

    public string Text { get; }
}

public enum FindDirection { Next, Previous }

public enum FindOutcome { Found, NotFound, Cancelled, Failed }

// Index is 1-based. TotalIsPartial means more pages are still being
// searched (PDF). UnsearchablePages counts image-only pages (PDF).
public sealed record FindResult(
    FindOutcome Outcome, int? Index, int? Total, bool TotalIsPartial, int UnsearchablePages)
{
    public static FindResult None { get; } = new(FindOutcome.NotFound, null, 0, false, 0);
}

public interface IFindableReader
{
    Task<FindResult> FindAsync(FindQuery query, FindDirection direction, CancellationToken token);
    void ClearFind();
}
