using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
public sealed partial class SteamGuideAdapter : IGuideSourceAdapter
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
        Uri? profile = document.QuerySelector(".creatorsBlock a.friendBlockLinkOverlay[href]")?.GetAttribute("href") is { } href &&
            Uri.TryCreate(href, UriKind.Absolute, out Uri? link) && link.Scheme == Uri.UriSchemeHttps &&
            link.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ? link : null;
        // Posted, then updated when it was: the last date the page gives.
        DateTimeOffset? updated = document.QuerySelectorAll(".detailsStatRight")
            .Select(stat => PageDate(stat.TextContent.Trim(), DateTimeOffset.UtcNow.Year)).LastOrDefault(date => date is not null);
        return new GuidePageReading(match, url, CapturedFormat.Html, guideTitle, author,
            NoticeRules.Find(text), updated, [], built.Body!.InnerHtml, images)
        {
            AuthorUrl = profile,
        };
    }

    /// <summary>
    /// A page date as Steam writes it, read as UTC: "9 Jul, 2024 @ 9:51am",
    /// "Dec 1, 2023 @ 12:00pm", or this year's "3 Mar @ 4:05pm". Null for anything else.
    /// </summary>
    internal static DateTimeOffset? PageDate(string text, int currentYear)
    {
        if (PageDateText().Match(text) is not { Success: true } date) return null;
        string day = date.Groups["d1"].Success ? date.Groups["d1"].Value : date.Groups["d2"].Value;
        string month = date.Groups["m1"].Success ? date.Groups["m1"].Value : date.Groups["m2"].Value;
        string year = date.Groups["y"].Success ? date.Groups["y"].Value : currentYear.ToString(CultureInfo.InvariantCulture);
        return DateTime.TryParseExact($"{day} {month} {year} {date.Groups["time"].Value}", "d MMM yyyy h:mmtt",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero) : null;
    }

    [GeneratedRegex(@"^(?:(?<d1>\d{1,2}) (?<m1>[A-Z][a-z]{2})|(?<m2>[A-Z][a-z]{2}) (?<d2>\d{1,2}))(?:, (?<y>\d{4}))? @ (?<time>\d{1,2}:\d{2}[ap]m)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PageDateText();

    private const string ImageHost = "images.steamusercontent.com";

    // steamuserimages-a.akamaihd.net/ugc/… is served as images.steamusercontent.com/ugc/…
    private static Uri? CurrentImageHost(Uri image) =>
        image.Host.Equals("steamuserimages-a.akamaihd.net", StringComparison.OrdinalIgnoreCase) &&
        image.AbsolutePath.StartsWith("/ugc/", StringComparison.Ordinal)
            ? new UriBuilder(image) { Scheme = Uri.UriSchemeHttps, Host = ImageHost, Port = -1 }.Uri
            : null;

    private static IElement Clean(IElement element, Uri url)
    {
        foreach (IElement removed in element.QuerySelectorAll("script, noscript, iframe").ToArray()) removed.Remove();
        // An emoticon is part of the sentence, not an image of the guide.
        foreach (IElement emoticon in element.QuerySelectorAll("img.emoticon").ToArray())
        {
            emoticon.Replace(element.Owner!.CreateTextNode(emoticon.GetAttribute("alt") ?? ""));
        }
        // A video is an empty frame Steam's script fills; offline it's a link to it.
        foreach (IElement video in element.QuerySelectorAll("div.sharedFilePreviewYouTubeVideo[id]").ToArray())
        {
            IElement link = element.Owner!.CreateElement("a");
            link.SetAttribute("href", "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(video.Id!));
            link.TextContent = "Video on YouTube";
            IElement paragraph = element.Owner.CreateElement("p");
            paragraph.AppendChild(link);
            video.Replace(paragraph);
        }
        // Older guides name the image host Steam has since replaced; the path still serves.
        foreach (IElement image in element.QuerySelectorAll("img[src]"))
        {
            if (OnlineHtml.Web(url, image.GetAttribute("src")) is { } source && CurrentImageHost(source) is { } current)
            {
                image.SetAttribute("src", current.AbsoluteUri);
            }
        }
        // A thumbnail links to its full image: keep the full image, unlinked.
        // One that links anywhere else, another guide say, keeps its link.
        foreach (IElement link in element.QuerySelectorAll("a.modalContentLink").ToArray())
        {
            if (link.QuerySelector("img") is { } image && OnlineHtml.Web(url, link.GetAttribute("href")) is { } target &&
                (CurrentImageHost(target) ?? target) is { Host: ImageHost } full)
            {
                image.SetAttribute("src", full.AbsoluteUri);
                link.Replace(image);
            }
            else
            {
                // Steam's popup class means nothing offline.
                link.RemoveAttribute("class");
                link.RemoveAttribute("data-modal-content-popup-url");
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
