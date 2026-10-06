using System.Globalization;
using DesktopGuides.Core.Pdf;

namespace DesktopGuides.Core.Reading;

/// <summary>The Go to page entry: digits only, within the open document's pages.</summary>
public static class PageEntry
{
    public static bool TryParse(string? text, int pageCount, out int pageNumber)
    {
        pageNumber = 0;
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
            !PdfLocationRules.IsPageInRange(value, pageCount))
        {
            return false;
        }
        pageNumber = value;
        return true;
    }

    public static string RangeMessage(int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        return string.Create(CultureInfo.InvariantCulture, $"Enter a page from 1 to {pageCount}.");
    }
}
