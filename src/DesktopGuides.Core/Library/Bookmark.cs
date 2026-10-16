using System.Text;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Library;

// T19.1: a named place in one guide. The locator stays text, so a bookmark
// whose locator no longer decodes is still listed, as a damaged bookmark.
public sealed record Bookmark(
    Guid Id, Guid GuideId, string LocatorJson, string Label,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

public static class BookmarkRules
{
    public const int MaxPerGuide = 500;
    public const int MaxLabelLength = 80;
    public const int MaxLocatorBytes = ReaderLocationCodec.MaxBytes;

    // Trimmed, 1 to 80 UTF-16 units: never longer than the column allows.
    public static string Label(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        string trimmed = label.Trim();
        if (trimmed.Length is 0 or > MaxLabelLength)
        {
            throw new ArgumentException(
                $"A bookmark label is 1 to {MaxLabelLength} characters.", nameof(label));
        }
        return trimmed;
    }

    public static string Locator(string locatorJson)
    {
        if (string.IsNullOrWhiteSpace(locatorJson) ||
            Encoding.UTF8.GetByteCount(locatorJson) > MaxLocatorBytes)
        {
            throw new ArgumentException("A bounded reader locator is required.", nameof(locatorJson));
        }
        return locatorJson;
    }
}

public sealed class BookmarkLimitException(Guid guideId)
    : InvalidOperationException($"This guide has {BookmarkRules.MaxPerGuide} bookmarks.")
{
    public Guid GuideId { get; } = guideId;
}
