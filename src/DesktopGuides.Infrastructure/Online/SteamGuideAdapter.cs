using System.Text;
using System.Web;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Online;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>
/// S22 PR 4: a Steam Community guide page, one page of sections: each
/// <c>div.subSection</c>'s title becomes an <c>h2</c> above its content.
/// Images are their full size, links leave Steam's link filter, and an
/// emoticon is its text.
/// </summary>
public sealed class SteamGuideAdapter : IGuideSourceAdapter
{
    public SourceSite Site => SourceSite.Steam;

    public GuidePageMatch? Match(Uri url) =>
        SteamUrls.GuideId(url) is { } id ? new GuidePageMatch(SourceSite.Steam, id, "") : null;

    public GuidePageReading? Read(Uri url, string html)
    {
        if (Match(url) is not { } match) return null;
        IDocument document = new HtmlParser().ParseDocument(html);
        IElement[] sections = document.QuerySelectorAll("div.guide.subSections > div.subSection").ToArray();
        if (sections.Length == 0) return null;

        StringBuilder body = new();
        if (document.QuerySelector("div.guideTopDescription") is { } summary && summary.TextContent.Trim().Length > 0)
        {
            body.Append("<p class=\"dg-summary\">").Append(Clean(summary, url).InnerHtml).Append("</p>");
        }
        foreach (IElement section in sections)
        {
            string title = section.QuerySelector(":scope > div.subSectionTitle")?.TextContent.Trim() ?? "";
            if (title.Length > 0) body.Append("<h2>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</h2>");
            if (section.QuerySelector(":scope > div.subSectionDesc") is { } content)
            {
                body.Append("<div class=\"dg-section\">").Append(Clean(content, url).InnerHtml).Append("</div>");
            }
        }

        IDocument built = new HtmlParser().ParseDocument("<body>" + body + "</body>");
        Uri[] images = built.QuerySelectorAll("img").Select(image => OnlineHtml.ImageUrl(image, url)).OfType<Uri>().Distinct().ToArray();
        string text = built.Body?.TextContent ?? "";
        string guideTitle = document.QuerySelector("div.workshopItemTitle")?.TextContent.Trim() is { Length: > 0 } heading
            ? heading : "Untitled guide";
        string? author = document.QuerySelector("div.guideAuthors")?.TextContent.Trim() is { Length: > 0 } by
            ? (by.StartsWith("By ", StringComparison.Ordinal) ? by[3..].Trim() : by) : null;
        return new GuidePageReading(match, url, CapturedFormat.Html, guideTitle, author,
            NoticeRules.Find(text), null, [], built.Body!.InnerHtml, images);
    }

    private static IElement Clean(IElement element, Uri url)
    {
        foreach (IElement removed in element.QuerySelectorAll("script, noscript, iframe").ToArray()) removed.Remove();
        // An emoticon is part of the sentence, not an image of the guide.
        foreach (IElement emoticon in element.QuerySelectorAll("img.emoticon").ToArray())
        {
            emoticon.Replace(element.Owner!.CreateTextNode(emoticon.GetAttribute("alt") ?? ""));
        }
        // A thumbnail links to its full image: keep the full image, unlinked.
        foreach (IElement link in element.QuerySelectorAll("a.modalContentLink").ToArray())
        {
            if (link.QuerySelector("img") is { } image && OnlineHtml.Web(url, link.GetAttribute("href")) is { } full)
            {
                image.SetAttribute("src", full.AbsoluteUri);
                link.Replace(image);
            }
        }
        OnlineHtml.Absolutize(element, url);
        foreach (IElement link in element.QuerySelectorAll("a[href]"))
        {
            if (Uri.TryCreate(link.GetAttribute("href"), UriKind.Absolute, out Uri? target) &&
                target.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) &&
                target.AbsolutePath.TrimEnd('/') == "/linkfilter" &&
                HttpUtility.ParseQueryString(target.Query)["u"] is { } real &&
                OnlineHtml.Web(target, real) is { } unwrapped)
            {
                link.SetAttribute("href", unwrapped.AbsoluteUri);
            }
        }
        return element;
    }
}
