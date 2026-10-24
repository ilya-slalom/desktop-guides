using System.Globalization;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Maps;

namespace DesktopGuides.Core.Library;

// Facts lines for Library and Game-page rows. The clock and culture are
// parameters so tests can pin them; times are shown in the clock's zone.
public static class CatalogPresentation
{
    public static IReadOnlyList<CatalogFact> GameFacts(
        LibraryGameSummary summary, string? matchedGuideTitle = null, int attentionCount = 0)
    {
        List<string> labels = [];
        if (!string.IsNullOrWhiteSpace(summary.Game.Platform))
        {
            labels.Add(summary.Game.Platform.Trim());
        }
        labels.Add(summary.Game.Link is null ? "Manual" : "IGDB");
        string guides = summary.GuideCount switch
        {
            0 => "No guides",
            1 => "1 guide",
            int count => $"{count} guides"
        };
        labels.Add(summary.MapCount switch
        {
            0 => guides,
            1 => $"{guides}, 1 map",
            int maps => $"{guides}, {maps} maps"
        });
        if (attentionCount > 0)
        {
            labels.Add(GuideFilePresentation.Attention(attentionCount));
        }
        // A search that matched only a guide names it, so the row explains itself.
        if (matchedGuideTitle is not null)
        {
            labels.Add($"Guide: {matchedGuideTitle}");
        }
        return labels.Select(label => new CatalogFact(label, label)).ToList();
    }

    public static IReadOnlyList<CatalogFact> GuideFacts(
        GuideSummary summary, TimeProvider clock, CultureInfo culture,
        GuideFileStatus fileStatus = GuideFileStatus.Ok, GuideSource? source = null)
    {
        string format = ImportPresentation.FormatLabel(summary.Guide.Format);
        List<CatalogFact> facts = [];
        // A broken file leads the row, before the facts that assume it opens.
        if (fileStatus != GuideFileStatus.Ok)
        {
            string status = GuideFilePresentation.StatusLabel(fileStatus);
            facts.Add(new(status, status));
        }
        // T21.2: a map has no reading state, only its type and size.
        if (summary.Guide is { Format: GuideFormat.Image, PixelWidth: int width, PixelHeight: int height })
        {
            string kind = MapPresentation.KindOf(summary.Guide).Label();
            facts.Add(new(kind, kind));
            facts.Add(MapPresentation.SizeFact(width, height));
            return facts;
        }
        facts.Add(new(format, format));
        // S22: design 5.4, where a saved guide came from and who wrote it.
        if (source is not null)
        {
            string site = SiteName(source.Site);
            facts.Add(source.Author is { } author
                ? new($"{site} · {author}", $"Saved from {site}, by {author}")
                : new(site, $"Saved from {site}"));
            if (source is { CapturedPages: int captured, TotalPages: int total })
            {
                facts.Add(new($"Pages 1–{captured} of {total}", $"Pages 1 to {captured} of {total} saved"));
            }
        }
        facts.Add(ReadingStateFact(summary.State));
        if (summary.State?.LastOpenedUtc is DateTimeOffset opened)
        {
            facts.Add(OpenedFact(opened, clock, culture));
        }
        return facts;
    }

    public static string SiteName(SourceSite site) => site switch
    {
        SourceSite.GameFaqs => "GameFAQs",
        SourceSite.Neoseeker => "Neoseeker",
        SourceSite.Steam => "Steam Community",
        _ => "VGMaps",
    };

    public static string AccessibleText(IReadOnlyList<CatalogFact> facts) =>
        string.Join(", ", facts.Select(fact => fact.AccessibleLabel));

    // First match wins. An estimate of 1 without a completion time is ~100%.
    private static CatalogFact ReadingStateFact(ReadingState? state)
    {
        if (state?.CompletedUtc is not null)
        {
            return new("Completed", "Completed");
        }
        if (state?.EstimatedFraction is double fraction)
        {
            int percent = (int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);
            return new($"~{percent}%", $"about {percent} percent");
        }
        return state?.LastOpenedUtc is not null
            ? new("In progress", "In progress")
            : new("Not started", "Not started");
    }

    // Today shows the time, the previous local date says yesterday, and
    // anything else, including a future time from clock skew, shows the date.
    private static CatalogFact OpenedFact(
        DateTimeOffset opened, TimeProvider clock, CultureInfo culture)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(opened, clock.LocalTimeZone);
        DateTimeOffset now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone);
        if (local <= now && local.Date == now.Date)
        {
            string time = local.ToString("t", culture);
            return new($"Opened {time}", $"opened today at {time}");
        }
        if (local <= now && local.Date == now.Date.AddDays(-1))
        {
            return new("Opened yesterday", "opened yesterday");
        }
        string date = local.ToString("d MMM yyyy", culture);
        return new($"Opened {date}", $"opened on {date}");
    }
}
