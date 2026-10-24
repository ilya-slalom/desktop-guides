using System.Globalization;
using System.Net;
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>A captured page, the image files the caller saved for it by address, and whether a guide page linked to it.</summary>
public sealed record CapturedPage(GuidePageReading Reading, IReadOnlyDictionary<Uri, string> ImageFiles, bool Linked = false);

public sealed record OfflineCopy(string EntryPath, CapturedFormat Format, int MissingImages);

/// <summary>
/// S22: writes captured pages as files the guide import already reads. HTML
/// pages are siblings, each starting with a saved-from-URL comment, with the
/// guide's own links made relative and its images local; the first page is
/// guide.html, the rest are named by their stable page keys. Scripts, forms
/// and handlers are dropped. A text guide is guide.txt, its text unchanged.
/// </summary>
public static class OfflineCopyBuilder
{
    private static readonly string[] Removed =
        ["script", "noscript", "iframe", "object", "embed", "form", "input", "button", "style", "link"];

    public static OfflineCopy Build(IReadOnlyList<CapturedPage> pages, string folder, DateTimeOffset savedUtc)
    {
        if (pages.Count == 0) throw new ArgumentException("A copy needs a page.", nameof(pages));
        GuidePageMatch item = pages[0].Reading.Match;
        if (pages.Any(page => page.Reading.Match.Site != item.Site || page.Reading.Match.ItemId != item.ItemId))
        {
            throw new ArgumentException("Every page must belong to one guide.", nameof(pages));
        }
        if (Directory.EnumerateFileSystemEntries(folder).Any())
        {
            throw new ArgumentException("The copy's folder must be empty.", nameof(folder));
        }
        GuidePageReading first = pages[0].Reading;
        if (first.Format == CapturedFormat.Text)
        {
            string text = Path.Combine(folder, "guide.txt");
            File.WriteAllText(text, first.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return new OfflineCopy(text, CapturedFormat.Text, 0);
        }

        Dictionary<string, string> files = pages.Select((page, index) =>
                (page.Reading.Match.PageKey, File: index == 0 ? "guide.html" : page.Reading.Match.PageKey + ".html"))
            .DistinctBy(entry => entry.PageKey)
            .ToDictionary(entry => entry.PageKey, entry => entry.File, StringComparer.Ordinal);
        Dictionary<string, string> keysByUrl = new(StringComparer.Ordinal);
        foreach (GuidePageReading reading in pages.Select(page => page.Reading))
        {
            keysByUrl.TryAdd(Key(reading.Url), reading.Match.PageKey);
            foreach (GuidePageLink link in reading.Pages) keysByUrl.TryAdd(Key(link.Url), link.PageKey);
        }
        // A guide's links can spell a page's slug with other hyphens
        // ("arts-plug-ins" for "arts-plugins"); a spelling two pages share is no match.
        Dictionary<string, string?> keysBySpelling = new(StringComparer.Ordinal);
        foreach ((string url, string key) in keysByUrl)
        {
            string spelling = Spelling(new Uri(url));
            keysBySpelling[spelling] = keysBySpelling.TryGetValue(spelling, out string? other) && other != key ? null : key;
        }

        File.WriteAllText(Path.Combine(folder, "guide.css"), StyleSheet(item.Site), new UTF8Encoding(false));

        Dictionary<Uri, string> savedImages = [];
        int missing = 0;
        foreach (CapturedPage page in pages.DistinctBy(page => page.Reading.Match.PageKey))
        {
            IDocument document = new HtmlParser().ParseDocument(
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><link rel=\"stylesheet\" href=\"guide.css\"></head>" +
                $"<body><main class=\"{SiteClass(item.Site)}\"></main></body></html>");
            IElement main = document.QuerySelector("main")!;
            // Neoseeker's content is a MediaWiki page's, and its rules name the wrapper.
            IElement content = main;
            if (item.Site == SourceSite.Neoseeker)
            {
                content = document.CreateElement("div");
                content.ClassName = "mw-parser-output";
                main.AppendChild(content);
            }
            content.InnerHtml = page.Reading.Content;
            foreach (IElement element in main.QuerySelectorAll(string.Join(",", Removed)).ToArray()) element.Remove();
            foreach (IElement element in main.QuerySelectorAll("*"))
            {
                foreach (IAttr handler in element.Attributes.Where(attr => attr.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    element.RemoveAttribute(handler.Name);
                }
            }
            foreach (IElement spoiler in main.QuerySelectorAll(".fspoiler")) spoiler.SetAttribute("tabindex", "0");
            RewriteLinks(main, page.Reading.Url, keysByUrl, keysBySpelling, files);
            missing += SaveImages(document, main, page, folder, savedImages);

            string title = page.Reading.Pages.FirstOrDefault(link => link.PageKey == page.Reading.Match.PageKey)?.Title
                ?? page.Reading.Title;
            document.Title = title;
            main.Before(Header(document, first, savedUtc));
            if (files.Count > 1) main.Before(Contents(document, pages, files));
            string url = page.Reading.Url.AbsoluteUri;
            string html = $"<!-- saved from url=({url.Length:D4}){url} -->\n" + document.ToHtml();
            File.WriteAllText(Path.Combine(folder, files[page.Reading.Match.PageKey]), html, new UTF8Encoding(false));
        }
        return new OfflineCopy(Path.Combine(folder, "guide.html"), CapturedFormat.Html, missing);
    }

    /// <summary>The copy's stylesheet: the shared base, then the site's own rules if it has any.</summary>
    internal static string StyleSheet(SourceSite site) => site switch
    {
        SourceSite.GameFaqs => Resource("base") + "\n" + Resource("gamefaqs"),
        SourceSite.Neoseeker => Resource("base") + "\n" + Resource("neoseeker"),
        _ => Resource("base"),
    };

    private static string SiteClass(SourceSite site) => site switch
    {
        SourceSite.GameFaqs => "dg-site-gamefaqs",
        SourceSite.Neoseeker => "dg-site-neoseeker",
        _ => "dg-site",
    };

    private static string Resource(string name)
    {
        using Stream stream = typeof(OfflineCopyBuilder).Assembly.GetManifestResourceStream($"DesktopGuides.Online.Styles.{name}.css")
            ?? throw new InvalidOperationException($"The {name} stylesheet is missing from the build.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static string Key(Uri url) => new Uri(url.GetLeftPart(UriPartial.Query)).AbsoluteUri;

    private static string Spelling(Uri url)
    {
        string key = Key(url);
        int segment = key.LastIndexOf('/') + 1;
        return key[..segment] + key[segment..].Replace("-", "", StringComparison.Ordinal);
    }

    private static void RewriteLinks(
        IElement main, Uri pageUrl, Dictionary<string, string> keysByUrl,
        Dictionary<string, string?> keysBySpelling, Dictionary<string, string> files)
    {
        foreach (IElement link in main.QuerySelectorAll("a[href]"))
        {
            string href = link.GetAttribute("href")!;
            if (href.StartsWith('#')) continue;
            if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                link.RemoveAttribute("href");
                continue;
            }
            if (OnlineHtml.Web(pageUrl, href) is not { } target)
            {
                link.RemoveAttribute("href");
                continue;
            }
            string? key = keysByUrl.TryGetValue(Key(target), out string? exact) ? exact
                : keysBySpelling.TryGetValue(Spelling(target), out string? spelled) ? spelled : null;
            link.SetAttribute("href",
                key is not null && files.TryGetValue(key, out string? file)
                    ? file + target.Fragment
                    : target.AbsoluteUri);
        }
    }

    private static int SaveImages(
        IDocument document, IElement main, CapturedPage page, string folder, Dictionary<Uri, string> savedImages)
    {
        int missing = 0;
        foreach (IElement image in main.QuerySelectorAll("img").ToArray())
        {
            Uri? url = OnlineHtml.ImageUrl(image, page.Reading.Url);
            if (url is not null && !savedImages.ContainsKey(url) && page.ImageFiles.TryGetValue(url, out string? source))
            {
                Directory.CreateDirectory(Path.Combine(folder, "images"));
                string extension = Path.GetExtension(source) is { Length: > 1 and <= 6 } ext ? ext.ToLowerInvariant() : ".img";
                string name = $"images/{savedImages.Count + 1}{extension}";
                File.Copy(source, Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar)));
                savedImages[url] = name;
            }
            if (url is not null && savedImages.TryGetValue(url, out string? local))
            {
                image.SetAttribute("src", local);
                foreach (string name in new[] { "data-src", "srcset", "data-srcset" }) image.RemoveAttribute(name);
                continue;
            }
            IElement placeholder = document.CreateElement("span");
            placeholder.ClassName = "dg-missing-image";
            placeholder.TextContent = "Image not saved";
            image.Replace(placeholder);
            missing++;
        }
        return missing;
    }

    // The guide's pages in order, then the articles they link to: the import
    // finds a guide's pages by following links from its entry, and the
    // site's own contents are dropped. Headings here would join the outline.
    private static IElement Contents(IDocument document, IReadOnlyList<CapturedPage> pages, Dictionary<string, string> files)
    {
        IElement nav = document.CreateElement("nav");
        nav.ClassName = "dg-pages";
        // Hidden: the reader's Contents pane lists the pages, and the import
        // follows the links regardless.
        nav.SetAttribute("hidden", "");
        StringBuilder items = new();
        StringBuilder articles = new();
        foreach (CapturedPage page in pages.DistinctBy(page => page.Reading.Match.PageKey))
        {
            GuidePageReading reading = page.Reading;
            string name = reading.Pages.FirstOrDefault(link => link.PageKey == reading.Match.PageKey)?.Title ?? reading.Title;
            (page.Linked ? articles : items).Append(
                $"<li><a href=\"{files[reading.Match.PageKey]}\">{WebUtility.HtmlEncode(name)}</a></li>");
        }
        nav.InnerHtml = $"<ol>{items}</ol>" + (articles.Length > 0 ? $"<p class=\"dg-pages-heading\">Linked articles</p><ul>{articles}</ul>" : "");
        return nav;
    }

    private static IElement Header(IDocument document, GuidePageReading guide, DateTimeOffset savedUtc)
    {
        IElement header = document.CreateElement("header");
        header.ClassName = "dg-source";
        string site = guide.Match.Site switch
        {
            SourceSite.GameFaqs => "GameFAQs",
            SourceSite.Neoseeker => "Neoseeker",
            SourceSite.Steam => "Steam Community",
            _ => "VGMaps"
        };
        string author = guide.Author is { } name ? $"<p>By {WebUtility.HtmlEncode(name)}</p>" : "";
        string url = guide.Pages.FirstOrDefault()?.Url.AbsoluteUri ?? guide.Url.AbsoluteUri;
        header.InnerHtml =
            // Not a heading: the reader shows the title, and a heading would
            // lead every page's outline in the Contents pane.
            $"<p class=\"dg-title\">{WebUtility.HtmlEncode(guide.Title)}</p>{author}" +
            $"<p>Saved from {site} on {savedUtc.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(url)}</a></p>";
        return header;
    }
}
