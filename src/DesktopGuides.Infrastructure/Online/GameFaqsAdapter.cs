using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Infrastructure.Online;

/// <summary>
/// S22: GameFAQs guides. A text FAQ is one page of <c>pre#faqspan-N</c>
/// chunks; an HTML guide lists its pages in <c>div.ftoc</c>, as slugs or as
/// <c>?page=N</c> (0-based; the bare URL is page 0).
/// </summary>
public sealed partial class GameFaqsAdapter : IGuideSourceAdapter
{
    private const string Host = "gamefaqs.gamespot.com";

    public SourceSite Site => SourceSite.GameFaqs;

    public GuidePageMatch? Match(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps || !url.Host.Equals(Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        Match path = GuidePath().Match(url.AbsolutePath);
        if (!path.Success) return null;
        string page = path.Groups["page"].Value;
        Match query = PageQuery().Match(url.Query);
        if (url.Query.Length > 0 && !query.Success) return null;
        if (query.Success)
        {
            if (page.Length > 0) return null;
            page = "page-" + int.Parse(query.Groups["n"].Value, CultureInfo.InvariantCulture);
        }
        return new GuidePageMatch(SourceSite.GameFaqs, path.Groups["faq"].Value, page);
    }

    public GuidePageReading? Read(Uri url, string html)
    {
        GuidePageMatch? match = Match(url);
        if (match is null) return null;
        IDocument document = new HtmlParser().ParseDocument(html);
        (string? headline, string? author, DateTimeOffset? updated) = ReadJsonLd(document);
        string title = headline ?? TitleFromHead(document) ?? "Untitled guide";
        author ??= document.QuerySelector("h2.title a.contrib1")?.TextContent.Trim();
        updated ??= UpdatedFromChrome(document);

        IHtmlCollection<IElement> chunks = document.QuerySelectorAll("#faqtext > pre[id^='faqspan-']");
        if (chunks.Length > 0)
        {
            // Each chunk after the first starts with a newline that HTML
            // parsing drops (as it does after any <pre>), so the join puts it back.
            string text = string.Join("\n", chunks.Select(chunk => chunk.TextContent));
            return new GuidePageReading(match, url, CapturedFormat.Text, title, author,
                NoticeRules.Find(text), updated, [], text, []);
        }

        IElement? wrap = document.QuerySelector("#faqwrap");
        if (wrap is null) return null;
        Uri baseUrl = document.QuerySelector("base[href]")?.GetAttribute("href") is { } href &&
                      Uri.TryCreate(url, href, out Uri? based) ? based : url;
        IElement? toc = wrap.QuerySelector(":scope > div.ftoc");
        List<GuidePageLink> pages = ReadPages(toc, baseUrl);
        bool paged = pages.Any(page => page.PageKey.StartsWith("page-", StringComparison.Ordinal));
        if (paged && match.PageKey.Length == 0) match = match with { PageKey = "page-0" };
        if (!paged && match.PageKey.Length == 0 && pages.Count > 0) match = match with { PageKey = pages[0].PageKey };

        INode[] content = toc is null
            ? wrap.ChildNodes.ToArray()
            : wrap.ChildNodes.SkipWhile(node => node != toc).Skip(1).ToArray();
        foreach (IElement element in content.OfType<IElement>()) OnlineHtml.Absolutize(element, baseUrl);
        StringBuilder body = new();
        foreach (INode node in content) body.Append(node.ToHtml());
        Uri[] images = content.OfType<IElement>()
            .SelectMany(element => element.Matches("img") ? [element] : element.QuerySelectorAll("img").ToArray())
            .Select(image => OnlineHtml.ImageUrl(image, baseUrl)).OfType<Uri>().Distinct().ToArray();
        string pageText = string.Concat(content.Select(node => node.TextContent));
        return new GuidePageReading(match, url, CapturedFormat.Html, title, author,
            NoticeRules.Find(pageText), updated, pages, body.ToString(), images);
    }

    private List<GuidePageLink> ReadPages(IElement? toc, Uri baseUrl)
    {
        List<GuidePageLink> pages = [];
        if (toc is null) return pages;
        foreach (IElement link in toc.QuerySelectorAll("ol a[href]"))
        {
            if (!Uri.TryCreate(baseUrl, link.GetAttribute("href"), out Uri? target)) continue;
            Uri page = new(target.GetLeftPart(UriPartial.Query));
            if (Match(page) is not { } found) continue;
            string key = found.PageKey.Length == 0 ? "page-0" : found.PageKey;
            if (pages.Any(existing => existing.PageKey == key)) continue;
            pages.Add(new GuidePageLink(key, page, link.TextContent.Trim() is { Length: > 0 } text ? text : null));
        }
        return pages;
    }

    private static (string? Headline, string? Author, DateTimeOffset? Updated) ReadJsonLd(IDocument document)
    {
        foreach (IElement script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using JsonDocument json = JsonDocument.Parse(script.TextContent);
                JsonElement root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("@type", out JsonElement type) || type.GetString() != "WebPage")
                {
                    continue;
                }
                string? headline = Text(root, "headline");
                // The author is a name, or a Person with one.
                string? author = !root.TryGetProperty("author", out JsonElement person) ? null
                    : person.ValueKind == JsonValueKind.Object ? Text(person, "name")
                    : person.ValueKind == JsonValueKind.String ? Clean(person.GetString()) : null;
                DateTimeOffset? updated = Date(root, "dateModified") ?? Date(root, "datePublished");
                return (headline, author, updated);
            }
            catch (JsonException)
            {
            }
        }
        return (null, null, null);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? Clean(value.GetString()) : null;

    // GameFAQs' JSON-LD carries HTML entities ("Assassin&#039;s Creed").
    private static string? Clean(string? text) =>
        System.Net.WebUtility.HtmlDecode(text ?? "").Trim() is { Length: > 0 } clean ? clean : null;

    private static DateTimeOffset? Date(JsonElement element, string name) =>
        Text(element, name) is { } text &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date)
            ? date : null;

    private static string? TitleFromHead(IDocument document)
    {
        string title = document.Title ?? "";
        int end = title.LastIndexOf(" - GameFAQs", StringComparison.Ordinal);
        string trimmed = (end >= 0 ? title[..end] : title).Trim();
        return trimmed.Length > 0 ? trimmed : null;
    }

    private static DateTimeOffset? UpdatedFromChrome(IDocument document)
    {
        Match updated = UpdatedText().Match(document.Body?.TextContent ?? "");
        return updated.Success && DateTime.TryParseExact(updated.Groups[1].Value, "MM/dd/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime date)
            ? new DateTimeOffset(date, TimeSpan.Zero) : null;
    }

    [GeneratedRegex(@"^/(?<plat>[a-z0-9]+)/(?<gid>\d+)-(?<slug>[a-z0-9-]+)/faqs/(?<faq>\d+)(?:/(?<page>[a-z0-9-]+))?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex GuidePath();

    [GeneratedRegex(@"^\?page=(?<n>\d{1,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex PageQuery();

    [GeneratedRegex(@"Updated:\s*(\d{2}/\d{2}/\d{4})", RegexOptions.CultureInvariant)]
    private static partial Regex UpdatedText();
}
