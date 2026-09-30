using System.Globalization;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class CatalogPresentationTests
{
    private static readonly TimeZoneInfo Plus9 = TimeZoneInfo.CreateCustomTimeZone(
        "Test+09", TimeSpan.FromHours(9), "Test+09", "Test+09");

    // 14:30 on 30 Sep 2026 in the test zone.
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 5, 30, 0, TimeSpan.Zero);
    private static readonly Clock AtNow = new(Now, Plus9);
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    private sealed class Clock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => zone;
    }

    private static Game GameWith(string? platform, bool linked = false) => new(
        Guid.NewGuid(), "Zelda", platform, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        linked ? new ProviderGameLink(ProviderGameLink.Igdb, "1", DateTimeOffset.UnixEpoch) : null);

    private static Guide GuideWith(GuideFormat format) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Guide", format, "content/x", "guide.txt",
        "hash", 1, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static ReadingState State(
        double? estimate = null, DateTimeOffset? opened = null, DateTimeOffset? completed = null) =>
        new(Guid.NewGuid(), null, estimate, opened, completed);

    private static string[] GameLabels(string? platform, bool linked, int guides) =>
        CatalogPresentation.GameFacts(new LibraryGameSummary(GameWith(platform, linked), guides, Now, []))
            .Select(fact => fact.Label).ToArray();

    private static IReadOnlyList<CatalogFact> GuideFacts(
        ReadingState? state, GuideFormat format = GuideFormat.Txt, CultureInfo? culture = null) =>
        CatalogPresentation.GuideFacts(new GuideSummary(GuideWith(format), state), AtNow, culture ?? EnGb);

    private static string[] Labels(IReadOnlyList<CatalogFact> facts) =>
        facts.Select(fact => fact.Label).ToArray();

    [Fact]
    public void GameFactsAreThePlatformSourceAndCount()
    {
        Assert.Equal(["PC", "IGDB", "4 guides"], GameLabels("PC", linked: true, 4));
    }

    [Fact]
    public void GameFactsTrimThePlatform()
    {
        Assert.Equal(["Nintendo Switch", "Manual", "1 guide"], GameLabels("  Nintendo Switch ", false, 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GameFactsOmitABlankPlatform(string? platform)
    {
        Assert.Equal(["Manual", "No guides"], GameLabels(platform, false, 0));
    }

    [Theory]
    [InlineData(0, "No guides")]
    [InlineData(1, "1 guide")]
    [InlineData(2, "2 guides")]
    [InlineData(97, "97 guides")]
    public void GuideCountCopy(int count, string expected)
    {
        Assert.Equal(expected, GameLabels(null, false, count)[^1]);
    }

    [Fact]
    public void GameAccessibleTextJoinsTheLabels()
    {
        IReadOnlyList<CatalogFact> facts = CatalogPresentation.GameFacts(
            new LibraryGameSummary(GameWith("PC", linked: true), 4, Now, []));
        Assert.Equal("PC, IGDB, 4 guides", CatalogPresentation.AccessibleText(facts));
    }

    [Fact]
    public void AMissingReadingStateIsNotStarted()
    {
        Assert.Equal(["Text (TXT)", "Not started"], Labels(GuideFacts(null)));
    }

    [Fact]
    public void AnEmptyReadingStateIsNotStarted()
    {
        Assert.Equal(["Text (TXT)", "Not started"], Labels(GuideFacts(State())));
    }

    [Theory]
    [InlineData(GuideFormat.Html, "Web page (HTML)")]
    [InlineData(GuideFormat.Pdf, "PDF")]
    public void TheFirstFactIsTheFormatLabel(GuideFormat format, string expected)
    {
        Assert.Equal(expected, GuideFacts(null, format)[0].Label);
    }

    [Fact]
    public void OpenedWithoutAnEstimateIsInProgress()
    {
        Assert.Equal("In progress", GuideFacts(State(opened: Now.AddMinutes(-25)))[1].Label);
    }

    [Theory]
    [InlineData(0.0, "~0%")]
    [InlineData(0.45, "~45%")]
    [InlineData(1.0, "~100%")]
    public void AnEstimateShowsItsPercent(double estimate, string expected)
    {
        Assert.Equal(expected, GuideFacts(State(estimate, Now.AddMinutes(-25)))[1].Label);
    }

    [Theory]
    [InlineData(0.125, "~13%")]
    [InlineData(0.625, "~63%")]
    public void EstimateRoundsHalfAwayFromZero(double estimate, string expected)
    {
        Assert.Equal(expected, GuideFacts(State(estimate))[1].Label);
    }

    [Fact]
    public void CompletionWinsOverAnEstimate()
    {
        DateTimeOffset opened = Now.AddMinutes(-25);
        Assert.Equal("Completed", GuideFacts(State(0.4, opened, opened))[1].Label);
    }

    [Fact]
    public void EstimateWithoutAnOpenTimeHasNoOpenedFact()
    {
        Assert.Equal(["Text (TXT)", "~45%"], Labels(GuideFacts(State(0.45))));
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("de-DE")]
    public void AnOpenEarlierTodayShowsTheLocalTime(string cultureName)
    {
        IReadOnlyList<CatalogFact> facts = GuideFacts(
            State(opened: new DateTimeOffset(2026, 9, 30, 5, 5, 0, TimeSpan.Zero)),
            culture: CultureInfo.GetCultureInfo(cultureName));
        Assert.Equal("Opened 14:05", facts[2].Label);
        Assert.Equal("opened today at 14:05", facts[2].AccessibleLabel);
    }

    [Fact]
    public void LocalMidnightIsToday()
    {
        // 15:00 UTC on the 29th is 00:00 on the 30th in the test zone.
        IReadOnlyList<CatalogFact> facts = GuideFacts(
            State(opened: new DateTimeOffset(2026, 9, 29, 15, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Opened 00:00", facts[2].Label);
    }

    [Fact]
    public void AMinuteBeforeLocalMidnightIsYesterday()
    {
        IReadOnlyList<CatalogFact> facts = GuideFacts(
            State(opened: new DateTimeOffset(2026, 9, 29, 14, 59, 0, TimeSpan.Zero)));
        Assert.Equal("Opened yesterday", facts[2].Label);
        Assert.Equal("opened yesterday", facts[2].AccessibleLabel);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("de-DE")]
    public void AnOlderOpenShowsTheLocalDate(string cultureName)
    {
        // 23:59 on the 28th in the test zone.
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
        IReadOnlyList<CatalogFact> facts = GuideFacts(
            State(opened: new DateTimeOffset(2026, 9, 28, 14, 59, 0, TimeSpan.Zero)), culture: culture);
        string date = new DateTime(2026, 9, 28).ToString("d MMM yyyy", culture);
        Assert.Equal($"Opened {date}", facts[2].Label);
        Assert.Equal($"opened on {date}", facts[2].AccessibleLabel);
    }

    [Fact]
    public void TheEnglishDateReadsDayMonthYear()
    {
        string label = GuideFacts(
            State(opened: new DateTimeOffset(2026, 9, 28, 14, 59, 0, TimeSpan.Zero)))[2].Label;
        Assert.StartsWith("Opened 28 Sep", label);
        Assert.EndsWith(" 2026", label);
    }

    [Fact]
    public void AFutureOpenShowsADate()
    {
        // 15:00 today in the test zone: later than now, so clock skew.
        IReadOnlyList<CatalogFact> facts = GuideFacts(
            State(opened: new DateTimeOffset(2026, 9, 30, 6, 0, 0, TimeSpan.Zero)));
        Assert.Equal($"Opened {new DateTime(2026, 9, 30).ToString("d MMM yyyy", EnGb)}", facts[2].Label);
    }

    [Fact]
    public void TheTimeFollowsTheCulture()
    {
        CultureInfo enUs = CultureInfo.GetCultureInfo("en-US");
        string label = GuideFacts(
            State(opened: new DateTimeOffset(2026, 9, 30, 5, 5, 0, TimeSpan.Zero)), culture: enUs)[2].Label;
        Assert.StartsWith("Opened 2:05", label);
        Assert.Contains(enUs.DateTimeFormat.PMDesignator, label);
    }

    [Fact]
    public void GuideAccessibleTextSpeaksThePercent()
    {
        string text = CatalogPresentation.AccessibleText(GuideFacts(
            State(0.45, new DateTimeOffset(2026, 9, 29, 3, 0, 0, TimeSpan.Zero)), GuideFormat.Pdf));
        Assert.Equal("PDF, about 45 percent, opened yesterday", text);
        Assert.DoesNotContain("~", text);
        Assert.DoesNotContain("·", text);
    }

    [Fact]
    public void GuideAccessibleTextSaysToday()
    {
        string text = CatalogPresentation.AccessibleText(GuideFacts(
            State(opened: new DateTimeOffset(2026, 9, 30, 5, 5, 0, TimeSpan.Zero)), GuideFormat.Html));
        Assert.Equal("Web page (HTML), In progress, opened today at 14:05", text);
    }

    [Fact]
    public void AGuideMatchEndsTheGameFacts()
    {
        LibraryGameSummary summary = new(GameWith("PC"), 2, Now, ["Complete Walkthrough", "Maps"]);

        IReadOnlyList<CatalogFact> facts = CatalogPresentation.GameFacts(summary, "Complete Walkthrough");

        Assert.Equal(["PC", "Manual", "2 guides", "Guide: Complete Walkthrough"], Labels(facts));
        Assert.Equal("PC, Manual, 2 guides, Guide: Complete Walkthrough", CatalogPresentation.AccessibleText(facts));
    }

    [Fact]
    public void NoGuideMatchLeavesTheFactsUnchanged()
    {
        LibraryGameSummary summary = new(GameWith("PC"), 2, Now, ["Complete Walkthrough", "Maps"]);

        Assert.Equal(["PC", "Manual", "2 guides"], Labels(CatalogPresentation.GameFacts(summary, null)));
    }
}
