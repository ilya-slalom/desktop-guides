using System.Globalization;
using System.Text;

namespace DesktopGuides.Core.Providers;

public static class GameMetadataNormalizer
{
    public const int TitleLimit = 160;
    public const int SummaryLimit = 4000;
    public const int ListCountLimit = 16;
    public const int ListItemLimit = 80;

    public static string NormalizeExternalId(string? raw)
    {
        string value = raw?.Trim() ?? "";
        if (value.Length is < 1 or > 20 || value[0] == '0' || !value.All(char.IsAsciiDigit))
        {
            throw Malformed("The provider returned an invalid game ID.");
        }
        return value;
    }

    public static string NormalizeTitle(string? raw) =>
        NormalizeText(raw, TitleLimit, keepNewlines: false) ??
        throw Malformed("The provider returned a game without a title.");

    public static string? NormalizeText(string? raw, int max) =>
        NormalizeText(raw, max, keepNewlines: true);

    public static IReadOnlyList<string> NormalizeList(IEnumerable<string?>? raw)
    {
        List<string> items = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? item in raw ?? [])
        {
            string? value = NormalizeText(item, ListItemLimit, keepNewlines: false);
            if (value is not null && seen.Add(value))
            {
                items.Add(value);
                if (items.Count == ListCountLimit) break;
            }
        }
        return items;
    }

    public static GameTypeTag MapType(string? igdbType, bool hasVersionParent)
    {
        if (hasVersionParent) return GameTypeTag.Edition;
        return igdbType?.Trim().ToLowerInvariant() switch
        {
            "main game" => GameTypeTag.MainGame,
            "remaster" => GameTypeTag.Remaster,
            "remake" => GameTypeTag.Remake,
            "port" => GameTypeTag.Port,
            "expanded game" => GameTypeTag.Edition,
            "expansion" or "standalone expansion" or "dlc" or "dlc addon" => GameTypeTag.Expansion,
            "bundle" or "pack / addon" or "pack" => GameTypeTag.Bundle,
            _ => GameTypeTag.Other
        };
    }

    public static string? NormalizeProviderUrl(string? raw) =>
        Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IdnHost == "www.igdb.com" &&
        uri.IsDefaultPort &&
        uri.AbsolutePath.StartsWith("/games/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : null;

    private static string? NormalizeText(string? raw, int max, bool keepNewlines)
    {
        if (raw is null) return null;
        StringBuilder text = new(Math.Min(raw.Length, max));
        foreach (char c in raw.Replace("\r\n", "\n"))
        {
            UnicodeCategory category = char.GetUnicodeCategory(c);
            if (c == '\n' && keepNewlines) text.Append(c);
            else if (c is '\n' or '\t') text.Append(' ');
            else if (category is not (UnicodeCategory.Control or UnicodeCategory.Format)) text.Append(c);
        }
        string value = text.ToString().Trim();
        if (value.Length > max) value = value[..max].TrimEnd();
        return value.Length == 0 ? null : value;
    }

    private static ProviderException Malformed(string message) =>
        new(ProviderErrorKind.MalformedData, message);
}
