using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>
/// S22: Neoseeker text FAQs (one <c>pre</c>, identified by the canonical
/// FAQ's number so a FAQ posted for two platforms is one item) and wiki
/// walkthroughs (the hub and the walkthrough pages its navigation lists).
/// </summary>
public sealed partial class NeoseekerAdapter : IGuideSourceAdapter
{
    private const string Host = "www.neoseeker.com";
    private const string Notice = "© Neo Era Media Inc.";

    public SourceSite Site => SourceSite.Neoseeker;

    public GuidePageMatch? Match(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps || !url.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) ||
            url.Query.Length > 0)
        {
            return null;
        }
        if (FaqPath().Match(url.AbsolutePath) is { Success: true } faq)
        {
            return new GuidePageMatch(SourceSite.Neoseeker, faq.Groups["id"].Value, "");
        }
        if (WalkthroughPath().Match(url.AbsolutePath) is { Success: true } walkthrough)
        {
            string page = walkthrough.Groups["page"].Value;
            return new GuidePageMatch(SourceSite.Neoseeker, walkthrough.Groups["game"].Value + "/walkthrough",
                page.Length == 0 ? "index" : PageKey(page));
        }
        return null;
    }

    public GuidePageReading? Read(Uri url, string html)
    {
        GuidePageMatch? match = Match(url);
        if (match is null) return null;
        IDocument document = new HtmlParser().ParseDocument(html);
        return match.PageKey.Length == 0 ? ReadFaq(url, match, document) : ReadWalkthrough(url, match, document);
    }

    private GuidePageReading? ReadFaq(Uri url, GuidePageMatch match, IDocument document)
    {
        IElement? text = document.QuerySelector("#faqtxt > pre");
        if (text is null) return null;
        if (document.QuerySelector("link[rel='canonical']")?.GetAttribute("href") is { } canonical &&
            Uri.TryCreate(url, canonical, out Uri? canonicalUrl) && Match(canonicalUrl) is { PageKey.Length: 0 } original)
        {
            match = match with { ItemId = original.ItemId };
        }
        string content = text.TextContent;
        string title = document.QuerySelector("#markupfaq > h1")?.TextContent.Trim() is { Length: > 0 } heading
            ? heading : "Untitled guide";
        return new GuidePageReading(match, url, CapturedFormat.Text, title,
            document.QuerySelector(".author_area span.author a")?.TextContent.Trim(),
            NoticeRules.Find(content),
            Time(document.QuerySelector(".author_area time[datetime]")),
            [], content, []);
    }

    private GuidePageReading? ReadWalkthrough(Uri url, GuidePageMatch match, IDocument document)
    {
        IElement? output = Content(document, url);
        if (output is null) return null;
        string game = Game(match);
        List<GuidePageLink> pages = [new GuidePageLink("index", new Uri($"https://{Host}/{game}/walkthrough"), null)];
        foreach (IElement link in document.QuerySelectorAll("#wiki-navigation a[href]"))
        {
            // An accordion toggle (href="#toc-1") names this page, and a red
            // link (class "new", "?edit") a page that doesn't exist.
            string href = link.GetAttribute("href") ?? "";
            if (href.StartsWith('#') || href.Contains('?') || link.ClassList.Contains("new") ||
                !Uri.TryCreate(url, href, out Uri? target) ||
                !target.AbsolutePath.StartsWith($"/{game}/walkthrough/", StringComparison.Ordinal) ||
                Match(new Uri(target.GetLeftPart(UriPartial.Path))) is not { } found ||
                pages.Any(page => page.PageKey == found.PageKey))
            {
                continue;
            }
            pages.Add(new GuidePageLink(found.PageKey, new Uri(target.GetLeftPart(UriPartial.Path)),
                link.TextContent.Trim() is { Length: > 0 } name ? name : null));
        }

        return new GuidePageReading(match, url, CapturedFormat.Html,
            WalkthroughTitle(document),
            null, Notice, Edited(document), pages, output.InnerHtml, Images(output, url))
        {
            Linked = Articles(output, game),
        };
    }

    /// <summary>A wiki article a walkthrough page links to, read as a page of that walkthrough.</summary>
    public GuidePageReading? ReadLinked(GuidePageLink link, string html, GuidePageMatch item)
    {
        if (item.Site != SourceSite.Neoseeker || !item.ItemId.EndsWith("/walkthrough", StringComparison.Ordinal)) return null;
        IDocument document = new HtmlParser().ParseDocument(html);
        IElement? output = Content(document, link.Url);
        if (output is null) return null;
        string title = document.QuerySelector("#page-title h1")?.TextContent.Trim() is { Length: > 0 } heading
            ? heading : link.Title ?? "Article";
        return new GuidePageReading(item with { PageKey = link.PageKey }, link.Url, CapturedFormat.Html,
            title, null, Notice, Edited(document), [], output.InnerHtml, Images(output, link.Url));
    }

    // Every Neoseeker page carries its title block and Neo Era's footer; a
    // challenge or an error page from in front of the site doesn't.
    public bool IsSitePage(string html)
    {
        IDocument document = new HtmlParser().ParseDocument(html);
        return document.QuerySelector("#page-title") is not null &&
               document.QuerySelector("footer")?.TextContent.Contains("Neo Era Media", StringComparison.Ordinal) == true;
    }

    // The wiki page's own content, cleaned: no ads, edit links or comments,
    // its addresses absolute, and its images unlinked from their file pages.
    private static IElement? Content(IDocument document, Uri url)
    {
        IElement? output = document.QuerySelector("article#wiki-content .mw-parser-output");
        if (output is null) return null;
        foreach (IElement removed in output.QuerySelectorAll(".section-ad, .noprint").ToArray()) removed.Remove();
        foreach (INode comment in output.Descendants().Where(node => node.NodeType == NodeType.Comment).ToArray())
        {
            comment.RemoveFromParent();
        }
        OnlineHtml.Absolutize(output, url);
        // An image links to its wiki file page; offline it's just the image.
        foreach (IElement link in output.QuerySelectorAll("a[href]").ToArray())
        {
            if (Uri.TryCreate(url, link.GetAttribute("href"), out Uri? target) &&
                Uri.UnescapeDataString(target.AbsolutePath).Contains("/File:", StringComparison.OrdinalIgnoreCase))
            {
                link.Replace([.. link.ChildNodes]);
            }
        }
        return output;
    }

    // The same game's wiki articles the content links to, once each:
    // not the walkthrough, a file, a special or talk page, the game's other
    // site sections, a red link or a link with a query.
    private static GuidePageLink[] Articles(IElement output, string game)
    {
        List<GuidePageLink> articles = [];
        foreach (IElement link in output.QuerySelectorAll("a[href]"))
        {
            if (link.ClassList.Contains("new") ||
                !Uri.TryCreate(link.GetAttribute("href"), UriKind.Absolute, out Uri? target) ||
                target.Scheme != Uri.UriSchemeHttps || !target.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) ||
                target.Query.Length > 0 ||
                !target.AbsolutePath.StartsWith($"/{game}/", StringComparison.Ordinal))
            {
                continue;
            }
            string path = target.AbsolutePath[(game.Length + 2)..].TrimEnd('/');
            string first = Uri.UnescapeDataString(path.Split('/')[0]);
            if (path.Length == 0 || NotArticles.Contains(first) ||
                NotArticleNamespaces.Any(space => first.StartsWith(space, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            string key = "article-" + PageKey(path);
            if (articles.Any(article => article.PageKey == key)) continue;
            articles.Add(new GuidePageLink(key, new Uri(target.GetLeftPart(UriPartial.Path)),
                link.TextContent.Trim() is { Length: > 0 } name ? name : null, Linked: true));
        }
        return articles.ToArray();
    }

    private static readonly HashSet<string> NotArticles = new(StringComparer.OrdinalIgnoreCase)
    {
        "walkthrough", "faqs", "cheats", "screenshots", "videos", "news", "reviews", "forums", "previews", "downloads",
    };

    private static readonly string[] NotArticleNamespaces =
        ["File:", "Special:", "Category:", "Template:", "User:", "User_talk:", "Talk:", "Help:", "MediaWiki:", "Module:"];

    private static string Game(GuidePageMatch match) => match.ItemId[..^"/walkthrough".Length];

    private static DateTimeOffset? Edited(IDocument document) =>
        document.QuerySelectorAll("article#wiki-content time[datetime]").Select(Time).Where(time => time is not null).Max();

    private static Uri[] Images(IElement output, Uri url) =>
        output.QuerySelectorAll("img").Select(image => OnlineHtml.ImageUrl(image, url)).OfType<Uri>().Distinct().ToArray();

    // A page names its game above its heading; the hub's heading is
    // "{Game} Walkthrough and Guide".
    private static string WalkthroughTitle(IDocument document)
    {
        if (document.QuerySelector("#page-title strong")?.TextContent.Trim() is { Length: > 0 } game)
        {
            return game + " Walkthrough";
        }
        string heading = document.QuerySelector("#page-title h1")?.TextContent.Trim() ?? "";
        if (heading.EndsWith(" and Guide", StringComparison.OrdinalIgnoreCase)) heading = heading[..^" and Guide".Length].TrimEnd();
        return heading.Length == 0 ? "Walkthrough"
            : heading.Contains("walkthrough", StringComparison.OrdinalIgnoreCase) ? heading : heading + " Walkthrough";
    }

    private static DateTimeOffset? Time(IElement? element) =>
        element?.GetAttribute("datetime") is { } value &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset time)
            ? time : null;

    // A wiki page segment as a stable file-name key: "A_Golden_Key" is "a-golden-key".
    private static string PageKey(string segment) =>
        NonKey().Replace(Uri.UnescapeDataString(segment).ToLowerInvariant(), "-").Trim('-') is { Length: > 0 } key
            ? key : "page";

    [GeneratedRegex(@"^/(?<game>[a-z0-9-]+)/faqs/(?<id>\d+)-(?<slug>[^/.]+)\.html$", RegexOptions.CultureInvariant)]
    private static partial Regex FaqPath();

    [GeneratedRegex(@"^/(?<game>[a-z0-9-]+)/walkthrough(?:/(?<page>[^/?#]+))?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex WalkthroughPath();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonKey();
}
