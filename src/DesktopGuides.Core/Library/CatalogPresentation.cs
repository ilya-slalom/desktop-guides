using System.Globalization;
using DesktopGuides.Core.Import;

namespace DesktopGuides.Core.Library;

// Facts lines for Library and Game-page rows. The clock and culture are
// parameters so tests can pin them; times are shown in the clock's zone.
public static class CatalogPresentation
{
    public static IReadOnlyList<CatalogFact> GameFacts(
        LibraryGameSummary summary, string? matchedGuideTitle = null)
    {
        List<string> labels = [];
        if (!string.IsNullOrWhiteSpace(summary.Game.Platform))
        {
            labels.Add(summary.Game.Platform.Trim());
        }
        labels.Add(summary.Game.Link is null ? "Manual" : "IGDB");
        labels.Add(summary.GuideCount switch
        {
            0 => "No guides",
            1 => "1 guide",
            int count => $"{count} guides"
        });
        // A search that matched only a guide names it, so the row explains itself.
        if (matchedGuideTitle is not null)
        {
            labels.Add($"Guide: {matchedGuideTitle}");
        }
        return labels.Select(label => new CatalogFact(label, label)).ToList();
    }

    public static IReadOnlyList<CatalogFact> GuideFacts(
        GuideSummary summary, TimeProvider clock, CultureInfo culture)
    {
        string format = ImportPresentation.FormatLabel(summary.Guide.Format);
        List<CatalogFact> facts = [new(format, format), ReadingStateFact(summary.State)];
        if (summary.State?.LastOpenedUtc is DateTimeOffset opened)
        {
            facts.Add(OpenedFact(opened, clock, culture));
        }
        return facts;
    }

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
