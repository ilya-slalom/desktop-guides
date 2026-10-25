using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DesktopGuides.Core.Maps;

namespace DesktopGuides.Infrastructure.Online;

public enum VgMapsSaveable { Yes, Convert, NotSaveable, TooLarge }

/// <summary>A map as its game's table lists it; sizes are as listed, and null when the row has none.</summary>
public sealed record VgMapsMap(
    Uri Url, string Area, int? Width, int? Height, long? Bytes, string Format, string? Ripper, VgMapsSaveable Saveable);

public sealed record VgMapsGamePage(VgMapsGame Game, string? Copyright, Uri PageUrl, IReadOnlyList<VgMapsMap> Maps);

/// <summary>
/// S22 PR 5: a game's table on a system page: the title row holding its
/// anchor, then "{Game} Maps" beside the copyright line, then a row per
/// map: the file's link, area, "W x H", file size, format, method and
/// credit. Rows without a file link, a note say, aren't maps.
/// </summary>
public static partial class VgMapsSystemPage
{
    public static VgMapsGamePage? Read(string html, Uri pageUrl, VgMapsGame game)
    {
        IDocument document = new HtmlParser().ParseDocument(html);
        // The parser keeps NAME as name; an anchor can sit in a letter heading too, so it's the one in a table.
        IElement? anchor = document.QuerySelectorAll("a[name]")
            .FirstOrDefault(element => element.GetAttribute("name") == game.Anchor && element.Closest("table") is not null);
        if (anchor?.Closest("table") is not { } table) return null;

        string? copyright = null;
        List<VgMapsMap> maps = [];
        foreach (IElement row in table.QuerySelectorAll("tr"))
        {
            if (row.Closest("table") != table) continue;
            IElement[] cells = row.Children.Where(cell => cell.LocalName == "td").ToArray();
            string text = Space().Replace(row.TextContent, " ").Trim();
            if (copyright is null && text.Contains('©')) copyright = text[text.IndexOf('©')..].Trim();
            if (cells.Length < 6 || cells[0].QuerySelector("a[href]") is not { } link ||
                !Uri.TryCreate(pageUrl, link.GetAttribute("href"), out Uri? url) ||
                url.Host != pageUrl.Host || url.AbsolutePath.StartsWith("/cdn-cgi/", StringComparison.Ordinal))
            {
                continue;
            }
            // A row naming no area (an image link) is named by its place.
            string area = Cell(cells, 1) is { Length: > 0 } named ? named
                : Space().Replace(link.TextContent, " ").Trim() is { Length: > 0 } linked ? linked
                : $"Map {maps.Count + 1}";
            (int? width, int? height) = Dimensions(Cell(cells, 2));
            long? bytes = FileSize(Cell(cells, 3));
            string format = (Cell(cells, 4) ?? "").ToUpperInvariant();
            string? ripper = Cell(cells, cells.Length - 1) is { Length: > 0 } credit && cells.Length >= 7 ? credit : null;
            maps.Add(new VgMapsMap(new Uri(url.GetLeftPart(UriPartial.Path)), area, width, height, bytes, format, ripper,
                Saveable(format, width, height, bytes)));
        }
        return new VgMapsGamePage(game, copyright, new Uri($"{pageUrl.GetLeftPart(UriPartial.Path)}#{game.Anchor}"), maps);
    }

    internal static VgMapsSaveable Saveable(string format, int? width, int? height, long? bytes)
    {
        if (format is not ("PNG" or "JPG" or "JPEG" or "GIF")) return VgMapsSaveable.NotSaveable;
        if (bytes > MapImageLimits.MaxBytes || width > MapImageLimits.MaxSide || height > MapImageLimits.MaxSide ||
            (long?)width * height > MapImageLimits.MaxPixels)
        {
            return VgMapsSaveable.TooLarge;
        }
        return format == "GIF" ? VgMapsSaveable.Convert : VgMapsSaveable.Yes;
    }

    private static string? Cell(IElement[] cells, int index) =>
        index < cells.Length ? Space().Replace(cells[index].TextContent, " ").Trim() : null;

    private static (int?, int?) Dimensions(string? text) =>
        text is not null && DimensionText().Match(text.Replace(",", "", StringComparison.Ordinal)) is { Success: true } size &&
        int.TryParse(size.Groups["w"].Value, CultureInfo.InvariantCulture, out int width) &&
        int.TryParse(size.Groups["h"].Value, CultureInfo.InvariantCulture, out int height)
            ? (width, height) : (null, null);

    private static long? FileSize(string? text)
    {
        // "1,234 kB": the comma separates thousands.
        if (text is null || SizeText().Match(text.Replace(",", "", StringComparison.Ordinal)) is not { Success: true } size ||
            !double.TryParse(size.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            return null;
        }
        double unit = size.Groups["u"].Value.ToUpperInvariant() switch { "GB" => 1 << 30, "MB" => 1 << 20, "KB" => 1 << 10, _ => 1 };
        return (long)Math.Round(number * unit);
    }

    [GeneratedRegex(@"(?<w>\d{1,6})\s*[x×]\s*(?<h>\d{1,6})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DimensionText();

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?<u>GB|MB|kB|KB|B)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SizeText();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Space();
}
