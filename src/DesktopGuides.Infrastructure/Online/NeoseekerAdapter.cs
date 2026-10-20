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
        IElement? output = document.QuerySelector("article#wiki-content .mw-parser-output");
        if (output is null) return null;
        string game = match.ItemId[..^"/walkthrough".Length];
        foreach (IElement removed in output.QuerySelectorAll(".section-ad, .noprint").ToArray()) removed.Remove();
        foreach (INode comment in output.Descendants().Where(node => node.NodeType == NodeType.Comment).ToArray())
        {
            comment.RemoveFromParent();
        }

        List<GuidePageLink> pages = [new GuidePageLink("index", new Uri($"https://{Host}/{game}/walkthrough"), null)];
        foreach (IElement link in document.QuerySelectorAll("#wiki-navigation a[href]"))
        {
            // An accordion toggle (href="#toc-1") names this page, not a page of the guide.
            string href = link.GetAttribute("href") ?? "";
            if (href.StartsWith('#') ||
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

        string gameName = document.QuerySelector("#page-title strong")?.TextContent.Trim() ?? "";
        DateTimeOffset? edited = document.QuerySelectorAll("article#wiki-content time[datetime]")
            .Select(Time).Where(time => time is not null).Max();
        Uri[] images = output.QuerySelectorAll("img")
            .Select(image => GameFaqsAdapter.ImageUrl(image, url)).OfType<Uri>().Distinct().ToArray();
        return new GuidePageReading(match, url, CapturedFormat.Html,
            gameName.Length > 0 ? gameName + " Walkthrough" : "Walkthrough",
            null, Notice, edited, pages, output.InnerHtml, images);
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
