using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Import;

public static class ImportPresentation
{
    public const int MaxShownWarnings = 20;
    public const int MaxTargetLength = 80;
    public const string PdfPasswordIncorrect = "That password didn't open this PDF. Try again.";
    public const string PasswordProtectedFact = "Password protected";

    // T21.2: a multi-page guide's pages, counted and listed in reading order.
    public const int ShownPages = 20;

    public static string PageCount(int count) =>
        count == 1 ? "1 page" : string.Create(CultureInfo.CurrentCulture, $"{count:N0} pages");

    public static string PageList(IReadOnlyList<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        List<string> lines = titles.Take(ShownPages).Select((title, index) => $"{index + 1}. {title}").ToList();
        if (titles.Count > ShownPages)
        {
            lines.Add(string.Create(CultureInfo.CurrentCulture, $"and {titles.Count - ShownPages:N0} more"));
        }
        return string.Join('\n', lines);
    }

    public static string FormatLabel(GuideFormat format) => format switch
    {
        GuideFormat.Txt => "Text (TXT)",
        GuideFormat.Html => "Web page (HTML)",
        GuideFormat.Pdf => "PDF",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static string EncodingLabel(int? codePage) => codePage switch
    {
        null => "UTF-8",
        437 => "DOS (CP437)",
        1252 => "Western (Windows-1252)",
        _ => throw new ArgumentOutOfRangeException(nameof(codePage)),
    };

    public static string FormatSize(long bytes, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < 1024)
        {
            return bytes == 1 ? "1 byte" : string.Create(culture, $"{bytes} bytes");
        }
        string[] units = ["KB", "MB", "GB"];
        double value = bytes / 1024.0;
        int unit = 0;
        // Promote when rounding to one decimal would show 1024 of a unit.
        while (unit < units.Length - 1 && Math.Round(value, 1) >= 1024)
        {
            value /= 1024;
            unit++;
        }
        return Math.Round(value, 1).ToString("0.#", culture) + " " + units[unit];
    }

    public static (IReadOnlyList<ImportWarning> Shown, int Hidden) LimitWarnings(
        IReadOnlyList<ImportWarning> warnings, int max = MaxShownWarnings) =>
        warnings.Count <= max
            ? (warnings, 0)
            : (warnings.Take(max).ToArray(), warnings.Count - max);

    public static string MoreWarnings(int hidden) =>
        hidden == 1 ? "1 more warning" : $"{hidden} more warnings";

    public static string ShortenTarget(string target)
    {
        if (target.Length <= MaxTargetLength)
        {
            return target;
        }
        int tail = 24;
        int head = MaxTargetLength - tail - 1;
        if (char.IsHighSurrogate(target[head - 1])) head--;
        if (char.IsLowSurrogate(target[^tail])) tail--;
        string shortened = target[..head] + "…" + target[^tail..];
        return shortened;
    }

    public static string WarningLine(ImportWarning warning) =>
        $"{ShortenTarget(warning.RelativePath)}: {warning.Message}";
}
