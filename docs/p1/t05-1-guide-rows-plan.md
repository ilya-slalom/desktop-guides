# T05.1 Library and Game Rows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Library lists games most recently used first and the Game page
lists guides the same way. Each row shows a tile, the title and a compact
facts line, and an unread guide says `Not started`.

**Architecture:** Core gains `LibraryGameSummary`, `GuideSummary`,
`CatalogFact` and a static `CatalogPresentation` that turns summaries into
facts and accessible text, with the clock and culture as parameters.
`ILibraryRepository` gains `ListGameSummariesAsync` and
`ListGuideSummariesAsync`. Each is one SQLite statement that sorts by last
activity. Production gains a `CatalogRowItem : ArtworkItem` base, with
`LibraryGameItem` and a new `GuideRowItem` deriving from it, and a
`DesktopGuidesCatalogRowTemplate` with a `MetadataControl` facts line. Both
lists use the new template. `ArtworkListLoader` puts the facts in each row's
UIA `HelpText`.

**Tech Stack:** .NET 10, WinUI 3, CommunityToolkit `MetadataControl`,
Microsoft.Data.Sqlite, xUnit, the PowerShell 5.1 UI Automation harness.

**Spec:** `docs/p1/t05-1-guide-rows-design.md`

**Target:** T05.1. **Prerequisites:** T03.2 (PR #4), T04.4 (PR #14), T05.4
(PR #16) and T11.1 (PR #6), all merged.

## Global Constraints

- No schema change and no new package. `MetadataControl` is already locked.
- Listing reads only SQLite. It never opens guide files and never calls a
  provider.
- Both lists stay virtualized `ListView`s with one fixed element tree per row.
- Game order: last activity descending, then `Title`, then `Id`. Last
  activity is the latest of `CreatedUtcMs`, any guide's `ImportedUtcMs` and
  any guide's `LastOpenedUtcMs`.
- Guide order: the later of `ImportedUtcMs` and `LastOpenedUtcMs`
  descending, then `Title`, then `Id`.
- Copy, verbatim:
  - source facts: `IGDB` (the game has a provider link) and `Manual`;
  - guide counts: `No guides`, `1 guide`, `{N} guides`;
  - format labels from `ImportPresentation.FormatLabel`: `Text (TXT)`,
    `Web page (HTML)`, `PDF`;
  - reading states: `Completed`, `~{N}%`, `In progress`, `Not started`;
  - open times: `Opened {time}`, `Opened yesterday`, `Opened {date}`;
  - accessible forms: `about {N} percent`, `opened today at {time}`,
    `opened yesterday`, `opened on {date}`;
  - accessible text joins the accessible facts with `, `.
- `{time}` is `ToString("t", culture)` and `{date}` is
  `ToString("d MMM yyyy", culture)`, both in the given local zone.
- A row's UIA Name stays the title. The facts go in UIA HelpText only.
- Selection, focus, Open, Remove, Resume and Back match guides by `Guide.Id`.
- UI tests assert only what app code controls.
- PowerShell scripts stay ASCII-only. Build non-ASCII strings from code
  points.
- Never print, copy or log provider credential values.

## Rulings against the spec

Task 5 records these rulings in the design's verification record.

1. **`CatalogFact(Label, AccessibleLabel)` lives in Core.** Production
   maps each fact to a `MetadataItem`, so Core stays free of WinUI and the
   copy is testable.
2. **Dates use the `d MMM yyyy` pattern with the culture's month names,**
   and times use the culture's `t` pattern. The tests read the expected
   month name from the culture, because ICU's en-GB may abbreviate
   September as `Sept`.
3. **The clock and culture are parameters** (`TimeProvider`,
   `CultureInfo`). The shell passes `TimeProvider.System` and
   `CultureInfo.CurrentCulture`.
4. **A game fact's accessible label equals its label.** Only guide facts
   need a spoken form.
5. **The `MetadataControl` uses `AccessibilityView="Raw"`,** and the facts
   reach UIA once, through the container's HelpText.
6. **`ArtworkListLoader` sets HelpText on every realized row,** using an
   empty string for items that aren't `CatalogRowItem`s. A recycled
   container therefore never keeps an earlier row's facts.
7. **`LibraryGamePresentation` is removed.** Its platform tests move to
   `CatalogPresentationTests`. The installed smoke checks that each row's
   Name is still the title.
8. **`seed-facts` rewrites `Format` to `Html` and `Pdf` on rows whose
   content is TXT.** The smoke never opens those guides, and listing never
   reads content.
9. **The smoke pattern-matches the "today" and date HelpText** (`-like`),
   because the host's culture and clock set the exact time. If a run
   crosses local midnight between the seed and the smoke, `opened today`
   becomes `opened yesterday`. This is an accepted risk.
10. **The `GuideList` realized-row limit is 60 of 99 rows,** below the
    `GameList` limit of 80 of 500. The long-list game has 97 long-list
    guides plus the base seed's 2.
11. **`seed-catalog` sets every game's `CreatedUtcMs` to one value,** so
    title order (and every existing catalog assertion) still holds.
12. **`Run-ShellSmoke` uses the 120 s timeout for `catalog*` modes.**
13. **The `catalog` smoke also checks HelpText** on the long-title row and,
    after scrolling to the end, on the last row and on Catalog Game 483
    (linked) and 481 (manual). Those rows use recycled containers.

## Review Focus

1. **An estimate without an open time.** T12.3 might write the estimate
   first. The row must show `~N%` and no `Opened` fact. Test
   `EstimateWithoutAnOpenTimeHasNoOpenedFact` (Task 1).
2. **Rows are recreated on every render,** so reference equality with the
   previous `SelectedItem` fails. Selection and focus must match by
   `Guide.Id`. Checked in code review (Task 3) and by the `long-list`
   Back-focus phase (Task 4).
3. **Midpoint rounding.** An estimate of 0.125 must read `~13%`, not the
   banker's `~12%`. Test `EstimateRoundsHalfAwayFromZero` (Task 1).
4. **A recycled container keeps stale HelpText.** Rows at the end of a
   scrolled list must describe their own item. Checked by the `catalog`
   end-of-list HelpText assertions (Task 4).
5. **Facts overflow at narrow widths.** A long platform plus source plus
   count must trim, not wrap or grow the row. Checked in code review
   against the row facts style (Task 3) and in the `catalog-facts`
   screenshots (Task 4).

## Host commands

The Mac has no dotnet, so every build and test runs on `pcsx2-win`:

```bash
s(){ ssh -o BatchMode=yes -o LogLevel=ERROR pcsx2-win "$@"; }
stage(){
  s 'powershell -NoProfile -Command "if (Test-Path E:\work\desktop-guides\t05-1) { Remove-Item -Recurse -Force E:\work\desktop-guides\t05-1 }; New-Item -ItemType Directory E:\work\desktop-guides\t05-1 | Out-Null"'
  COPYFILE_DISABLE=1 tar --exclude=.claude --exclude=.git --exclude=.superpowers -cf - . | s 'tar -xf - -C E:\work\desktop-guides\t05-1'
}
```

- **Core tests:** `stage && s 'cd /d E:\work\desktop-guides\t05-1 && dotnet test tests\DesktopGuides.Core.Tests\DesktopGuides.Core.Tests.csproj -c Release'`
- **Infrastructure tests:** `stage && s 'cd /d E:\work\desktop-guides\t05-1 && dotnet test tests\DesktopGuides.Infrastructure.Tests\DesktopGuides.Infrastructure.Tests.csproj -c Release'`
- **One class:** append `--filter "FullyQualifiedName~<Class>"` inside the
  quoted command.
- **Production build:** `stage && s 'cd /d E:\work\desktop-guides\t05-1 && dotnet build src\DesktopGuides.Production\DesktopGuides.Production.csproj -c Release -p:Platform=x64'`
- **Seed build:** `stage && s 'cd /d E:\work\desktop-guides\t05-1 && dotnet build tools\p1\DesktopGuides.ShellSeed\DesktopGuides.ShellSeed.csproj -c Release'`

---

### Task 1: Core summaries and catalog presentation

**Files:**
- Create: `src/DesktopGuides.Core/Library/CatalogSummaries.cs`
- Create: `src/DesktopGuides.Core/Library/CatalogPresentation.cs`
- Create: `tests/DesktopGuides.Core.Tests/CatalogPresentationTests.cs`
- Delete: `tests/DesktopGuides.Core.Tests/LibraryGamePresentationTests.cs`

The repository interface changes in Task 2, together with its only
implementation, so every project still builds after this task.

`LibraryGamePresentation` is used only by `LibraryGameItem`. Its deletion
waits for Task 3, which rewrites that class; this task deletes only the
presentation tests after porting them. Task 3 deletes the class.

**Interfaces:**
- Consumes: `Game`, `Guide`, `ReadingState`, `GuideFormat`
  (`Core/Library/LibraryModels.cs`), `ProviderGameLink`, and
  `ImportPresentation.FormatLabel(GuideFormat)` (namespace
  `DesktopGuides.Core.Import`).
- Produces (namespace `DesktopGuides.Core.Library`):
  - `public sealed record CatalogFact(string Label, string AccessibleLabel);`
  - `public sealed record LibraryGameSummary(Game Game, int GuideCount, DateTimeOffset LastActivityUtc);`
  - `public sealed record GuideSummary(Guide Guide, ReadingState? State);`
  - `public static IReadOnlyList<CatalogFact> CatalogPresentation.GameFacts(LibraryGameSummary summary)`
  - `public static IReadOnlyList<CatalogFact> CatalogPresentation.GuideFacts(GuideSummary summary, TimeProvider clock, CultureInfo culture)`
  - `public static string CatalogPresentation.AccessibleText(IReadOnlyList<CatalogFact> facts)`

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/CatalogPresentationTests.cs`. The
clock sits in a fixed UTC+9 zone, so a UTC-date implementation fails the
midnight cases.

```csharp
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
        CatalogPresentation.GameFacts(new LibraryGameSummary(GameWith(platform, linked), guides, Now))
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
            new LibraryGameSummary(GameWith("PC", linked: true), 4, Now));
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
}
```

Delete `tests/DesktopGuides.Core.Tests/LibraryGamePresentationTests.cs`.
Its platform cases are now `GameFactsTrimThePlatform` and
`GameFactsOmitABlankPlatform`. Its accessible-name cases are covered by the
installed smoke, which checks each row's Name (Ruling 7).

- [ ] **Step 2: Run the tests to verify they fail**

Run: Core tests with `--filter "FullyQualifiedName~CatalogPresentationTests"`.
Expected: build FAIL with `CS0246` for `LibraryGameSummary`, `GuideSummary`
and `CatalogFact`, and `CS0103` for `CatalogPresentation`.

- [ ] **Step 3: Write the minimal implementation**

Create `src/DesktopGuides.Core/Library/CatalogSummaries.cs`:

```csharp
namespace DesktopGuides.Core.Library;

// One Library row: the game, its guide count, and the latest of its creation,
// any guide import and any guide open.
public sealed record LibraryGameSummary(Game Game, int GuideCount, DateTimeOffset LastActivityUtc);

// One Game-page row. State is null when the guide has no reading-state row.
public sealed record GuideSummary(Guide Guide, ReadingState? State);

// A row fact: Label is shown, AccessibleLabel is read by screen readers.
public sealed record CatalogFact(string Label, string AccessibleLabel);
```

Create `src/DesktopGuides.Core/Library/CatalogPresentation.cs`:

```csharp
using System.Globalization;
using DesktopGuides.Core.Import;

namespace DesktopGuides.Core.Library;

// Facts lines for Library and Game-page rows. The clock and culture are
// parameters so tests can pin them; times are shown in the clock's zone.
public static class CatalogPresentation
{
    public static IReadOnlyList<CatalogFact> GameFacts(LibraryGameSummary summary)
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: Core tests with `--filter "FullyQualifiedName~CatalogPresentationTests"`.
Expected: PASS, 33 tests.

Run: Core tests (whole project).
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Core/Library/CatalogSummaries.cs src/DesktopGuides.Core/Library/CatalogPresentation.cs tests/DesktopGuides.Core.Tests/CatalogPresentationTests.cs tests/DesktopGuides.Core.Tests/LibraryGamePresentationTests.cs
git commit -m "feat(core): add catalog row facts" \
  -m "LibraryGameSummary and GuideSummary carry one Library or Game-page row. CatalogPresentation turns them into facts: platform, IGDB or Manual, and guide count for a game; format, reading state and last open for a guide. An unread guide says Not started, estimates round half away from zero, and open times are local: the time today, yesterday, or a date. Accessible text reads ~N% as about N percent. The LibraryGamePresentation platform tests move here." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Repository summary queries

**Files:**
- Modify: `src/DesktopGuides.Core/Library/ILibraryRepository.cs:16-17` (two methods after `ListGuidesAsync`)
- Modify: `src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs` (two methods after `ListGuidesAsync` ~232, `GetReadingStateAsync` ~306, a `ReadReadingState` helper beside `ReadGuide`)
- Modify: `tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs:56` (two stubs)
- Modify: `tests/DesktopGuides.Infrastructure.Tests/RemovalLibrary.cs` (an `Execute` helper after `Scalar`)
- Create: `tests/DesktopGuides.Infrastructure.Tests/LibrarySummaryTests.cs`

**Interfaces:**
- Consumes: `LibraryGameSummary`, `GuideSummary` (Task 1); the existing
  `ReadAsync`, `OpenConnection`, `ReadGame`, `ReadGuide`, `NullableString`
  and `FromUnixMilliseconds` helpers; `GuideRemover(SqliteLibraryRepository, ILibraryPaths)`
  and `RemoveAsync(Guid, CancellationToken)`.
- Produces, on `ILibraryRepository` and `SqliteLibraryRepository`:
  - `Task<IReadOnlyList<LibraryGameSummary>> ListGameSummariesAsync(CancellationToken token = default);`
  - `Task<IReadOnlyList<GuideSummary>> ListGuideSummariesAsync(Guid gameId, CancellationToken token = default);`
- Produces, on `RemovalLibrary`: `public void Execute(string sql)`.

- [ ] **Step 1: Add the test helper and write the failing tests**

In `RemovalLibrary.cs`, after `Scalar`:

```csharp
    public void Execute(string sql)
    {
        using SqliteConnection connection = new($"Data Source={Paths.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
```

Create `tests/DesktopGuides.Infrastructure.Tests/LibrarySummaryTests.cs`.
`AddGuideAsync` creates the `ReadingStates` and `ReaderPreferences` rows,
as a real import does. The tests set timestamps with SQL, because no writer
for them exists yet.

```csharp
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibrarySummaryTests : IAsyncLifetime
{
    private const long Day = 86_400_000;
    private const long Base = 1_790_000_000_000;
    private RemovalLibrary library = null!;

    public async Task InitializeAsync() => library = await RemovalLibrary.CreateAsync();

    public async Task DisposeAsync() => await library.DisposeAsync();

    private static string N(Guid id) => id.ToString("N");

    private async Task<Guid> GameAt(string title, long created)
    {
        Guid id = await library.AddGameAsync(title);
        library.Execute($"UPDATE Games SET CreatedUtcMs = {created}, UpdatedUtcMs = {created} WHERE Id = '{N(id)}'");
        return id;
    }

    private async Task<Guid> GuideAt(Guid gameId, string title, long imported)
    {
        Guid id = await library.AddGuideAsync(gameId, title);
        library.Execute($"UPDATE Guides SET ImportedUtcMs = {imported}, UpdatedUtcMs = {imported} WHERE Id = '{N(id)}'");
        return id;
    }

    private void Opened(Guid guideId, long opened) =>
        library.Execute($"UPDATE ReadingStates SET LastOpenedUtcMs = {opened} WHERE GuideId = '{N(guideId)}'");

    private async Task<string[]> GameTitles() =>
        (await library.Repository.ListGameSummariesAsync()).Select(summary => summary.Game.Title).ToArray();

    private async Task<string[]> GuideTitles(Guid gameId) =>
        (await library.Repository.ListGuideSummariesAsync(gameId)).Select(summary => summary.Guide.Title).ToArray();

    [Fact]
    public async Task GamesSortByCreationWhenTheyHaveNoGuides()
    {
        await GameAt("Older", Base);
        await GameAt("Newer", Base + Day);

        Assert.Equal(["Newer", "Older"], await GameTitles());
    }

    [Fact]
    public async Task ANewerImportMovesAnOlderGameFirst()
    {
        Guid older = await GameAt("Older", Base);
        await GameAt("Newer", Base + Day);
        await GuideAt(older, "Fresh", Base + 2 * Day);

        Assert.Equal(["Older", "Newer"], await GameTitles());
    }

    [Fact]
    public async Task ANewerOpenMovesAnOlderGameFirst()
    {
        Guid older = await GameAt("Older", Base);
        Guid newer = await GameAt("Newer", Base + 2 * Day);
        Guid guide = await GuideAt(older, "Read", Base);
        await GuideAt(newer, "Unread", Base + 2 * Day);
        Opened(guide, Base + 3 * Day);

        IReadOnlyList<LibraryGameSummary> summaries = await library.Repository.ListGameSummariesAsync();

        Assert.Equal(["Older", "Newer"], summaries.Select(summary => summary.Game.Title));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Base + 3 * Day), summaries[0].LastActivityUtc);
    }

    [Fact]
    public async Task AGuideWithoutAReadingStateRowStillCounts()
    {
        Guid game = await GameAt("Game", Base);
        Guid guide = await GuideAt(game, "Guide", Base + Day);
        library.Execute($"DELETE FROM ReadingStates WHERE GuideId = '{N(guide)}'");

        LibraryGameSummary summary = Assert.Single(await library.Repository.ListGameSummariesAsync());

        Assert.Equal(1, summary.GuideCount);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Base + Day), summary.LastActivityUtc);
    }

    [Fact]
    public async Task GuideCountsAreNotInflatedByReadingStates()
    {
        Guid game = await GameAt("Game", Base);
        foreach (string title in new[] { "A", "B", "C" })
        {
            Opened(await GuideAt(game, title, Base), Base + Day);
        }
        await GameAt("Empty", Base);

        IReadOnlyList<LibraryGameSummary> summaries = await library.Repository.ListGameSummariesAsync();

        Assert.Equal([("Game", 3), ("Empty", 0)], summaries.Select(summary => (summary.Game.Title, summary.GuideCount)));
    }

    [Fact]
    public async Task EqualGameActivitySortsByTitleThenId()
    {
        Guid first = await GameAt("Twin", Base);
        Guid second = await GameAt("Twin", Base);
        await GameAt("Alpha", Base);
        Guid[] twins = [.. new[] { first, second }.OrderBy(N, StringComparer.Ordinal)];

        IReadOnlyList<LibraryGameSummary> summaries = await library.Repository.ListGameSummariesAsync();

        Assert.Equal(["Alpha", "Twin", "Twin"], summaries.Select(summary => summary.Game.Title));
        Assert.Equal(twins, summaries.Skip(1).Select(summary => summary.Game.Id));
    }

    [Fact]
    public async Task RemovingAGuideUpdatesTheCountAndTheOrder()
    {
        Guid older = await GameAt("Older", Base);
        await GameAt("Newer", Base + Day);
        Guid fresh = await GuideAt(older, "Fresh", Base + 2 * Day);
        await GuideAt(older, "Kept", Base);
        RemovalLibrary.WriteFile(library.Paths.GetGuideRoot(fresh), "guide.txt", "x");
        Assert.Equal(["Older", "Newer"], await GameTitles());

        GuideRemovalResult result = await new GuideRemover(library.Repository, library.Paths).RemoveAsync(fresh);

        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        IReadOnlyList<LibraryGameSummary> summaries = await library.Repository.ListGameSummariesAsync();
        Assert.Equal([("Newer", 0), ("Older", 1)], summaries.Select(summary => (summary.Game.Title, summary.GuideCount)));
    }

    [Fact]
    public async Task GuideSummariesJoinTheirOwnStateAndExcludeOtherGames()
    {
        Guid game = await GameAt("Game", Base);
        Guid other = await GameAt("Other", Base);
        Guid read = await GuideAt(game, "Read", Base);
        Guid unread = await GuideAt(game, "Unread", Base);
        Guid missing = await GuideAt(game, "Missing", Base);
        Guid elsewhere = await GuideAt(other, "Elsewhere", Base);
        library.Execute($"""
            UPDATE ReadingStates SET EstimatedFraction = 0.45, LastOpenedUtcMs = {Base + Day},
                CompletedUtcMs = {Base + Day} WHERE GuideId = '{N(read)}';
            UPDATE ReadingStates SET EstimatedFraction = 0.9 WHERE GuideId = '{N(elsewhere)}';
            DELETE FROM ReadingStates WHERE GuideId = '{N(missing)}';
            """);

        IReadOnlyList<GuideSummary> summaries = await library.Repository.ListGuideSummariesAsync(game);

        Assert.Equal([read, missing, unread], summaries.Select(summary => summary.Guide.Id));
        Assert.Equal(
            new ReadingState(read, null, 0.45,
                DateTimeOffset.FromUnixTimeMilliseconds(Base + Day),
                DateTimeOffset.FromUnixTimeMilliseconds(Base + Day)),
            summaries[0].State);
        Assert.Null(summaries[1].State);
        Assert.Equal(new ReadingState(unread, null, null, null, null), summaries[2].State);
        Assert.Equal(await library.Repository.GetGuideAsync(read), summaries[0].Guide);
    }

    [Fact]
    public async Task ANewerImportSortsFirst()
    {
        Guid game = await GameAt("Game", Base);
        await GuideAt(game, "Alpha", Base);
        await GuideAt(game, "Beta", Base + Day);

        Assert.Equal(["Beta", "Alpha"], await GuideTitles(game));
    }

    [Fact]
    public async Task ANewerOpenBeatsANewerImport()
    {
        Guid game = await GameAt("Game", Base);
        Guid alpha = await GuideAt(game, "Alpha", Base);
        await GuideAt(game, "Beta", Base + Day);
        Opened(alpha, Base + 2 * Day);

        Assert.Equal(["Alpha", "Beta"], await GuideTitles(game));
    }

    [Fact]
    public async Task EqualGuideActivitySortsByTitleThenId()
    {
        Guid game = await GameAt("Game", Base);
        Guid first = await GuideAt(game, "Twin", Base);
        Guid second = await GuideAt(game, "Twin", Base);
        await GuideAt(game, "Alpha", Base);
        Guid[] twins = [.. new[] { first, second }.OrderBy(N, StringComparer.Ordinal)];

        IReadOnlyList<GuideSummary> summaries = await library.Repository.ListGuideSummariesAsync(game);

        Assert.Equal(["Alpha", "Twin", "Twin"], summaries.Select(summary => summary.Guide.Title));
        Assert.Equal(twins, summaries.Skip(1).Select(summary => summary.Guide.Id));
    }

    [Fact]
    public async Task ListingNeverReadsGuideContent()
    {
        Guid game = await GameAt("Game", Base);
        Guid guide = await GuideAt(game, "Guide", Base);
        RemovalLibrary.WriteFile(library.Paths.GetGuideRoot(guide), "guide.txt", "x");
        Directory.Delete(library.Paths.GetGuideRoot(guide), true);

        Assert.Equal(1, Assert.Single(await library.Repository.ListGameSummariesAsync()).GuideCount);
        Assert.Equal(guide, Assert.Single(await library.Repository.ListGuideSummariesAsync(game)).Guide.Id);
    }
}
```

In `ImporterFakes.cs`, after the `ListGuidesAsync` stub:

```csharp
    public Task<IReadOnlyList<LibraryGameSummary>> ListGameSummariesAsync(CancellationToken token = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<GuideSummary>> ListGuideSummariesAsync(Guid gameId, CancellationToken token = default) => throw new NotSupportedException();
```

In `ILibraryRepository.cs`, after `ListGuidesAsync`:

```csharp
    // Newest activity first, then title, then ID. Reads only SQLite.
    Task<IReadOnlyList<LibraryGameSummary>> ListGameSummariesAsync(
        CancellationToken token = default);
    Task<IReadOnlyList<GuideSummary>> ListGuideSummariesAsync(
        Guid gameId, CancellationToken token = default);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibrarySummaryTests"`.
Expected: build FAIL with `CS0535`: `SqliteLibraryRepository` does not
implement `ListGameSummariesAsync` or `ListGuideSummariesAsync`.

- [ ] **Step 3: Write the minimal implementation**

In `SqliteLibraryRepository.cs`, after `ListGuidesAsync`:

```csharp
    // A game's last activity is the latest of its creation, any guide import
    // and any guide open. ReadingStates is keyed by GuideId, so the second
    // join adds at most one row per guide and COUNT stays exact.
    public Task<IReadOnlyList<LibraryGameSummary>> ListGameSummariesAsync(
        CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<LibraryGameSummary>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT g.Id, g.Title, g.Platform, g.Notes, g.CreatedUtcMs, g.UpdatedUtcMs,
                       g.ProviderName, g.ProviderGameId, g.MetadataJson,
                       g.MetadataRetrievedUtcMs, g.ArtworkRelativePath,
                       COUNT(gu.Id),
                       MAX(g.CreatedUtcMs,
                           COALESCE(MAX(gu.ImportedUtcMs), 0),
                           COALESCE(MAX(rs.LastOpenedUtcMs), 0)) AS LastActivityUtcMs
                FROM Games g
                LEFT JOIN Guides gu ON gu.GameId = g.Id
                LEFT JOIN ReadingStates rs ON rs.GuideId = gu.Id
                GROUP BY g.Id
                ORDER BY LastActivityUtcMs DESC, g.Title, g.Id
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            List<LibraryGameSummary> summaries = [];
            while (reader.Read())
            {
                summaries.Add(new LibraryGameSummary(
                    ReadGame(reader), reader.GetInt32(11), FromUnixMilliseconds(reader.GetInt64(12))));
            }
            return summaries;
        }, token);

    public Task<IReadOnlyList<GuideSummary>> ListGuideSummariesAsync(
        Guid gameId, CancellationToken token = default) =>
        ReadAsync<IReadOnlyList<GuideSummary>>(() =>
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT gu.Id, gu.GameId, gu.Title, gu.Format, gu.ManagedRelativeRoot,
                       gu.PrimaryRelativePath, gu.ContentSha256, gu.ContentBytes,
                       gu.SourceLabel, gu.TextCodePage, gu.ImportedUtcMs, gu.UpdatedUtcMs,
                       rs.GuideId, rs.LocatorJson, rs.EstimatedFraction,
                       rs.LastOpenedUtcMs, rs.CompletedUtcMs
                FROM Guides gu
                LEFT JOIN ReadingStates rs ON rs.GuideId = gu.Id
                WHERE gu.GameId = $gameId
                ORDER BY MAX(gu.ImportedUtcMs, COALESCE(rs.LastOpenedUtcMs, 0)) DESC,
                         gu.Title, gu.Id
                """;
            command.Parameters.AddWithValue("$gameId", gameId.ToString("N"));
            using SqliteDataReader reader = command.ExecuteReader();
            List<GuideSummary> summaries = [];
            while (reader.Read())
            {
                summaries.Add(new GuideSummary(
                    ReadGuide(reader), reader.IsDBNull(12) ? null : ReadReadingState(reader, 12)));
            }
            return summaries;
        }, token);
```

Replace the inline mapping in `GetReadingStateAsync` with the shared helper:

```csharp
            return reader.Read() ? ReadReadingState(reader, 0) : null;
```

Add the helper beside `ReadGuide`:

```csharp
    private static ReadingState ReadReadingState(SqliteDataReader reader, int first) => new(
        Guid.ParseExact(reader.GetString(first), "N"),
        NullableString(reader, first + 1),
        reader.IsDBNull(first + 2) ? null : reader.GetDouble(first + 2),
        reader.IsDBNull(first + 3) ? null : FromUnixMilliseconds(reader.GetInt64(first + 3)),
        reader.IsDBNull(first + 4) ? null : FromUnixMilliseconds(reader.GetInt64(first + 4)));
```

Before running, confirm the column order of `GameColumns` and `ReadGuide`
still matches the two `SELECT` lists above (ordinals 0–10 and 0–11).

- [ ] **Step 4: Run the tests to verify they pass**

Run: Infrastructure tests with `--filter "FullyQualifiedName~LibrarySummaryTests"`.
Expected: PASS, 12 tests.

Run: Infrastructure tests (whole project), then Core tests (whole project).
Expected: both PASS, 0 failed. The `GetReadingStateAsync` tests still pass
through the shared helper.

- [ ] **Step 5: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Core/Library/ILibraryRepository.cs src/DesktopGuides.Infrastructure/Storage/SqliteLibraryRepository.cs tests/DesktopGuides.Core.Tests/Providers/ImporterFakes.cs tests/DesktopGuides.Infrastructure.Tests/RemovalLibrary.cs tests/DesktopGuides.Infrastructure.Tests/LibrarySummaryTests.cs
git commit -m "feat(storage): list games and guides by last activity" \
  -m "ListGameSummariesAsync returns each game with its guide count and last activity, the latest of its creation, any guide import and any guide open, newest first, then title, then ID. ListGuideSummariesAsync returns a game's guides with their reading state (null when no row exists), ordered by the later of import and last open, then title, then ID. Each is one SQLite statement and neither reads guide files. GetReadingStateAsync now shares the reading-state mapping." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Catalog rows in the shell

**Files:**
- Create: `src/DesktopGuides.Production/CatalogRowItem.cs`
- Create: `src/DesktopGuides.Production/GuideRowItem.cs`
- Modify: `src/DesktopGuides.Production/LibraryGameItem.cs` (whole file)
- Modify: `src/DesktopGuides.Production/ArtworkListLoader.cs` (phase 0 of `ContainerContentChanging`)
- Modify: `src/DesktopGuides.Production/Styles/Catalog.xaml` (after `DesktopGuidesFactsStyle`, ~line 68)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml:146` (GameList template) and `:312-318` (GuideList)
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (constructor ~88, lines 424–516, 822–835, 1112–1122, 1141, 1175–1221)
- Delete: `src/DesktopGuides.Core/Library/LibraryGamePresentation.cs`

**Interfaces:**
- Consumes: `LibraryGameSummary`, `GuideSummary`, `CatalogFact`,
  `CatalogPresentation.GameFacts`, `CatalogPresentation.GuideFacts`,
  `CatalogPresentation.AccessibleText` (Task 1);
  `SqliteLibraryRepository.ListGameSummariesAsync()` and
  `ListGuideSummariesAsync(Guid)` (Task 2); `ArtworkItem(string title,
  string? summary, string? detail, string accessibleName)`.
- Produces:
  - `public abstract class CatalogRowItem : ArtworkItem` with public `Glyph`
    and `Facts` (`IEnumerable<MetadataItem>`) and internal `HelpText`;
  - `LibraryGameItem(LibraryGameSummary)` keeping `internal Game Game` and
    `internal string? ArtworkRelativePath`;
  - `GuideRowItem(GuideSummary, TimeProvider, CultureInfo)` with
    `internal Guide Guide`;
  - the `DesktopGuidesCatalogRowTemplate` resource;
  - UIA: every `GameList` and `GuideList` row has Name = title and
    HelpText = the accessible facts. Task 4's smoke reads both.

TDD skip: Production has no test project. The gates are the Production
build, the `is Guide` grep in Step 6, and Task 4's installed smoke, which
checks every row's Name and HelpText. The logic worth unit-testing lives in
Tasks 1 and 2.

- [ ] **Step 1: Row view models**

Create `src/DesktopGuides.Production/CatalogRowItem.cs`:

```csharp
using CommunityToolkit.WinUI.Controls;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

// The view model behind DesktopGuidesCatalogRowTemplate: a tile glyph, the
// title, and a facts line. The facts' accessible text becomes the row's UIA
// help text; the name stays the title.
public abstract class CatalogRowItem : ArtworkItem
{
    protected CatalogRowItem(string title, string glyph, IReadOnlyList<CatalogFact> facts)
        : base(title, null, null, title)
    {
        Glyph = glyph;
        Facts = facts
            .Select(fact => new MetadataItem { Label = fact.Label, AccessibleLabel = fact.AccessibleLabel })
            .ToList();
        HelpText = CatalogPresentation.AccessibleText(facts);
    }

    public string Glyph { get; }
    public IEnumerable<MetadataItem> Facts { get; }
    internal string HelpText { get; }
}
```

Replace `src/DesktopGuides.Production/LibraryGameItem.cs`:

```csharp
using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed class LibraryGameItem : CatalogRowItem
{
    internal LibraryGameItem(LibraryGameSummary summary)
        : base(summary.Game.Title, "\uE7FC", CatalogPresentation.GameFacts(summary))
    {
        Game = summary.Game;
    }

    internal Game Game { get; }
    internal string? ArtworkRelativePath => Game.ArtworkRelativePath;
}
```

Create `src/DesktopGuides.Production/GuideRowItem.cs`:

```csharp
using System.Globalization;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

// A Game-page row. Rows are rebuilt on every render, so callers match guides
// by Guide.Id, never by row identity.
public sealed class GuideRowItem : CatalogRowItem
{
    internal GuideRowItem(GuideSummary summary, TimeProvider clock, CultureInfo culture)
        : base(
            summary.Guide.Title,
            FormatGlyph(summary.Guide.Format),
            CatalogPresentation.GuideFacts(summary, clock, culture))
    {
        Guide = summary.Guide;
    }

    internal Guide Guide { get; }

    private static string FormatGlyph(GuideFormat format) => format switch
    {
        GuideFormat.Html => "\uE774", // Globe
        GuideFormat.Pdf => "\uEA90",  // PDF
        _ => "\uE8A5"                 // Document
    };
}
```

Delete `src/DesktopGuides.Core/Library/LibraryGamePresentation.cs`; its only
caller was the old `LibraryGameItem`.

- [ ] **Step 2: Help text on every realized row**

In `ArtworkListLoader.ContainerContentChanging`, phase 0 becomes:

```csharp
        if (args.Phase == 0)
        {
            AutomationProperties.SetName(container, item.AccessibleName);
            // Set on every row, so a recycled container never keeps earlier facts.
            AutomationProperties.SetHelpText(container, (item as CatalogRowItem)?.HelpText ?? string.Empty);
            if (load is not null) args.RegisterUpdateCallback(1, ContainerContentChanging);
            return;
        }
```

- [ ] **Step 3: The row template**

In `Styles/Catalog.xaml`, after `DesktopGuidesFactsStyle` (the new template
uses `StaticResource`, so it must follow the styles it names):

```xml
    <Style x:Key="DesktopGuidesRowFactsTextStyle"
           TargetType="TextBlock"
           BasedOn="{StaticResource DesktopGuidesMetadataStyle}">
        <Setter Property="TextWrapping" Value="NoWrap" />
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
    </Style>

    <Style x:Key="DesktopGuidesRowFactsStyle"
           TargetType="toolkit:MetadataControl"
           BasedOn="{StaticResource DesktopGuidesFactsStyle}">
        <Setter Property="TextBlockStyle" Value="{StaticResource DesktopGuidesRowFactsTextStyle}" />
    </Style>

    <!-- Library and Game-page rows. One MetadataControl per row, which T05.4
         avoided; T05.1 asks for it, and each recycled container still holds
         one fixed tree. The facts reach UIA once, as the row's help text. -->
    <DataTemplate x:Key="DesktopGuidesCatalogRowTemplate" x:DataType="local:CatalogRowItem">
        <Grid Padding="0,8"
              ColumnSpacing="{StaticResource DesktopGuidesSpacing12}"
              AutomationProperties.Name="{x:Bind AccessibleName}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="45" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <Border Style="{StaticResource DesktopGuidesArtworkTileStyle}">
                <Grid>
                    <FontIcon Glyph="{x:Bind Glyph}"
                              FontSize="16"
                              Foreground="{ThemeResource DesktopGuidesSecondaryTextBrush}"
                              AutomationProperties.AccessibilityView="Raw" />
                    <Image Source="{x:Bind Thumbnail, Mode=OneWay}"
                           Stretch="UniformToFill"
                           AutomationProperties.AccessibilityView="Raw" />
                </Grid>
            </Border>
            <StackPanel Grid.Column="1"
                        VerticalAlignment="Center"
                        Spacing="{StaticResource DesktopGuidesSpacing4}">
                <TextBlock Text="{x:Bind Title}"
                           Style="{StaticResource DesktopGuidesBodyStyle}"
                           TextWrapping="NoWrap"
                           TextTrimming="CharacterEllipsis" />
                <toolkit:MetadataControl Items="{x:Bind Facts}"
                                         Style="{StaticResource DesktopGuidesRowFactsStyle}"
                                         AutomationProperties.AccessibilityView="Raw" />
            </StackPanel>
        </Grid>
    </DataTemplate>
```

Leave `DesktopGuidesArtworkRowTemplate` in place: Add game still uses it.

- [ ] **Step 4: Both lists use the template**

In `ShellWindow.xaml`, `GameList` (line 146) uses
`ItemTemplate="{StaticResource DesktopGuidesCatalogRowTemplate}"`.
`GuideList` drops `DisplayMemberPath="Title"` and gains the same template:

```xml
                    <ListView x:Name="GuideList"
                              SelectionMode="Single"
                              Style="{StaticResource DesktopGuidesListStyle}"
                              ItemTemplate="{StaticResource DesktopGuidesCatalogRowTemplate}"
                              SelectionChanged="GuideSelected"
                              AutomationProperties.AutomationId="GuideList"
                              AutomationProperties.Name="Guides" />
```

In the `ShellWindow` constructor, after the two `GuideList.AddHandler`
calls:

```csharp
        ArtworkListLoader.NameRows(GuideList);
```

- [ ] **Step 5: Guide access goes through two helpers**

Next to `GuideFromRow`, add:

```csharp
    private Guide? SelectedGuide => GuideAt(GuideList.SelectedItem);

    private static Guide? GuideAt(object? item) => (item as GuideRowItem)?.Guide;
```

Then change each site. Behaviour is unchanged: every site already works
with a `Guide` and its `Id`.

| Line | Before | After |
| --- | --- | --- |
| 424 | `if (GuideList.SelectedItem is Guide guide)` | `if (SelectedGuide is Guide guide)` |
| 446 | `if (source is ListViewItem row && row.Content is Guide guide)` | `if (source is ListViewItem row && GuideAt(row.Content) is Guide guide)` |
| 467 | `if (GuideList.SelectedItem is Guide guide)` | `if (SelectedGuide is Guide guide)` |
| 488 | `GuideList.SelectedItem is Guide guide &&` | `SelectedGuide is Guide guide &&` |
| 513 | `GuideList.SelectedItem is not Guide selected \|\|` | `SelectedGuide is not Guide selected \|\|` |
| 516 | `GuideList.ContainerFromItem(selected) is not Control container)` | `GuideList.ContainerFromItem(GuideList.SelectedItem) is not Control container)` |
| 822 | `GuideList.SelectedItem is not Guide guide \|\|` | `SelectedGuide is not Guide guide \|\|` |
| 1141 | `(GuideList.SelectedItem as Guide)?.Id;` | `SelectedGuide?.Id;` |

Lines 834–835, the removal neighbour:

```csharp
        Guide? neighbor = GuideAt(index + 1 < GuideList.Items.Count ? GuideList.Items[index + 1]
            : index > 0 ? GuideList.Items[index - 1] : null);
```

The Library render (lines 1112 and 1122):

```csharp
                    IReadOnlyList<LibraryGameSummary> games = await library.ListGameSummariesAsync();
```

```csharp
                    GameList.ItemsSource = games.Select(summary => new LibraryGameItem(summary)).ToList();
```

The Game render: replace the `ListGuidesAsync` call (~1175) with

```csharp
                    IReadOnlyList<GuideRowItem> guides =
                        (await library.ListGuideSummariesAsync(gameRoute.GameId))
                        .Select(summary => new GuideRowItem(summary, TimeProvider.System, CultureInfo.CurrentCulture))
                        .ToList();
```

and the selected-row lookup (~1197) with

```csharp
                    GuideRowItem? selectedGuide = selectedGuideId is Guid id
                        ? guides.FirstOrDefault(item => item.Guide.Id == id)
                        : null;
```

`GuideList.ItemsSource = guides`, `GuideList.SelectedItem = selectedGuide`,
`guides.Count` and `GuideList.ScrollIntoView(selectedGuide)` then work
unchanged, because each now holds the row item.

- [ ] **Step 6: Build and check for missed sites**

Run: `grep -n "is Guide\b\|as Guide\b\|is not Guide\b" src/DesktopGuides.Production/*.cs`
Expected: only the helper-based lines from the table above
(`SelectedGuide is ...` and `GuideAt(...) is Guide`), none reading
`SelectedItem`, `Content` or `Items[...]` directly.

Run: `grep -rn "LibraryGamePresentation" src tests tools`
Expected: no output.

Run: the Production build.
Expected: `Build succeeded`, 0 errors.

Run: Core tests (whole project).
Expected: PASS, 0 failed.

- [ ] **Step 7: Commit**

Show the message in chat first.

```bash
git add src/DesktopGuides.Production src/DesktopGuides.Core/Library/LibraryGamePresentation.cs
git commit -m "feat(shell): show facts in Library and Game rows" \
  -m "Library and Game-page rows share DesktopGuidesCatalogRowTemplate: a 45x60 tile, the title, and a MetadataControl facts line that trims at narrow widths. Game rows keep their artwork; guide rows show a document, globe or PDF glyph. Both lists come from the new summary queries, so they sort by last activity. Each row's UIA name stays the title and its help text carries the facts, set on every realized container. Guide selection, focus, Open, Remove, Resume and Back still match by guide ID. LibraryGamePresentation is gone." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Installed smoke for row facts and order

**Files:**
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (arg check and usage ~310–323, `seed-catalog` ~459, a new `seed-facts` block before `seed-import`, an `ExecuteSql` helper after `InsertGuideAsync`)
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (ValidateSet, two helpers after `Get-RealizedGameRows` ~527, the `catalog` mode ~1362 and ~1448, the `long-list` mode ~1805, a new `catalog-facts` mode)
- Modify: `tools/p1/windows_shell_install.ps1` (timeout ~707, `Run-CatalogFactsScenarios` after `Run-CatalogScenarios` ~797, calls ~1223 and ~1292)

**Interfaces:**
- Consumes: `ListGameSummariesAsync` and `ListGuideSummariesAsync` (Task 2);
  each row's UIA Name (title) and HelpText (accessible facts) from Task 3.
- Produces: the `seed-facts` seed command; the `catalog-facts` smoke mode;
  the `catalog-facts-light` and `catalog-facts-dark` results with screenshots
  `library-facts` and `game-facts`; `realizedGuideRows` in the `long-list`
  result.

TDD skip: the seed and scripts have no unit tests. The gates are the seed
build, its own read-back checks, the ASCII and parser checks in Step 6, and
the installed run in Step 8.

Smoke audit (Ruling 11 and the spec's audit item). Every mode except
`catalog` finds games and guides by name. The base, design and long-list
seeds give their guides one shared import time each, so title order still
breaks ties within them. `remove-guide` reads the first remaining row after
removal, and the remaining guide is the only one. `Focus-OtherGuideWithoutSelection`
picks by position relative to the selection, not by title. Only `catalog`
relies on title order, and Step 2 pins its creation times.

- [ ] **Step 1: Seed command and helper**

In the arg check add `or "seed-facts"` after `"seed-catalog"`, and add
`seed-facts|` after `seed-catalog|` in the usage string.

After `InsertGuideAsync`, add:

```csharp
static void ExecuteSql(ManagedPathResolver paths, string sql)
{
    using SqliteConnection connection = new(new SqliteConnectionStringBuilder
    {
        DataSource = paths.DatabasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false,
        ForeignKeys = true
    }.ToString());
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}
```

- [ ] **Step 2: Pin the catalog's creation times**

In `seed-catalog`, immediately before
`IReadOnlyList<Game> catalog = await repository.ListGamesAsync();`:

```csharp
    // One shared creation time, so activity order is title order and the
    // catalog smoke's head, tail and keyboard checks still hold.
    long catalogCreated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    ExecuteSql(paths, $"UPDATE Games SET CreatedUtcMs = {catalogCreated}, UpdatedUtcMs = {catalogCreated}");
```

After the existing read-back `if` block, before `Console.WriteLine("Seeded 500 catalog games.")`:

```csharp
    if (!(await repository.ListGameSummariesAsync()).Select(entry => entry.Game.Title)
            .SequenceEqual(catalog.Select(seeded => seeded.Title)))
    {
        throw new InvalidOperationException("The catalog seed's activity order differs from its title order.");
    }
```

- [ ] **Step 3: The facts seed**

Before `if (args[0] == "seed-import")`, add the block below. It uses
`facts`-prefixed names because top-level statements later declare `now`
and `game`, and C# rejects a nested local with the same name (CS0136).

The times give an activity order that differs from title order:

| Game | Created | Guides and reading states | Last activity |
| --- | --- | --- | --- |
| Zeta Archive Game (Windows, manual) | now − 30 d | Recent Notes, imported now − 30 d, opened now | now |
| Facts Test Game (PC, IGDB) | now − 15 d | four guides imported now − 15 d, below | opened today |
| Empty Test Game (no platform, manual) | now − 5 d | none | now − 5 d |

| Facts guide | Format | Reading state |
| --- | --- | --- |
| Main Story Walkthrough | Html | opened today (a minute ago, or local midnight if later) |
| Collectibles Map | Pdf | 0.45, opened yesterday at local noon |
| Weapon Upgrade Guide | Txt | 0.8, opened and completed now − 10 d |
| Achievement Checklist | Txt | not started |

```csharp
if (args[0] == "seed-facts")
{
    if ((await repository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The facts seed needs an empty library.");
    }
    const long factsDay = 86_400_000;
    long factsNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    DateTime factsLocalDate = TimeZoneInfo.ConvertTime(
        DateTimeOffset.FromUnixTimeMilliseconds(factsNow), TimeZoneInfo.Local).Date;
    long LocalMs(DateTime local) =>
        new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeMilliseconds();
    long openedToday = Math.Max(factsNow - 60_000, LocalMs(factsLocalDate));
    long openedYesterday = LocalMs(factsLocalDate.AddDays(-1).AddHours(12));
    static string N(Guid value) => value.ToString("N");

    Guid factsGameId = Guid.NewGuid();
    await repository.AddLinkedGameAsync(new NewLinkedGame(
        factsGameId, "Facts Test Game", "PC",
        new ProviderGameLink(ProviderGameLink.Igdb, "960000", DateTimeOffset.UtcNow),
        new GameMetadataSnapshot(
            GameMetadataSnapshot.CurrentSchemaVersion, null, null, [], [], [], [], null,
            GameTypeTag.MainGame),
        null), CancellationToken.None);
    Game zetaGame = await repository.AddGameAsync("Zeta Archive Game", "Windows", null);
    Game emptyGame = await repository.AddGameAsync("Empty Test Game", null, null);

    Guid checklistId = Guid.NewGuid();
    Guid storyId = Guid.NewGuid();
    Guid mapId = Guid.NewGuid();
    Guid upgradeId = Guid.NewGuid();
    Guid notesId = Guid.NewGuid();
    foreach ((Guid guideId, string title) in new[]
             {
                 (checklistId, "Achievement Checklist"),
                 (storyId, "Main Story Walkthrough"),
                 (mapId, "Collectibles Map"),
                 (upgradeId, "Weapon Upgrade Guide")
             })
    {
        await InsertGuideAsync(paths, factsGameId, guideId, title, factsNow - 15 * factsDay);
    }
    await InsertGuideAsync(paths, zetaGame.Id, notesId, "Recent Notes", factsNow - 30 * factsDay);

    // No writer for reading state exists yet (T12.3, T13.2), so set it here.
    // Html and Pdf rows keep TXT content: the smoke never opens them.
    ExecuteSql(paths, $"""
        UPDATE Games SET CreatedUtcMs = {factsNow - 15 * factsDay}, UpdatedUtcMs = {factsNow - 15 * factsDay}
            WHERE Id = '{N(factsGameId)}';
        UPDATE Games SET CreatedUtcMs = {factsNow - 30 * factsDay}, UpdatedUtcMs = {factsNow - 30 * factsDay}
            WHERE Id = '{N(zetaGame.Id)}';
        UPDATE Games SET CreatedUtcMs = {factsNow - 5 * factsDay}, UpdatedUtcMs = {factsNow - 5 * factsDay}
            WHERE Id = '{N(emptyGame.Id)}';
        UPDATE Guides SET Format = 'Html' WHERE Id = '{N(storyId)}';
        UPDATE Guides SET Format = 'Pdf' WHERE Id = '{N(mapId)}';
        UPDATE ReadingStates SET LastOpenedUtcMs = {openedToday} WHERE GuideId = '{N(storyId)}';
        UPDATE ReadingStates SET EstimatedFraction = 0.45, LastOpenedUtcMs = {openedYesterday}
            WHERE GuideId = '{N(mapId)}';
        UPDATE ReadingStates SET EstimatedFraction = 0.8, LastOpenedUtcMs = {factsNow - 10 * factsDay},
            CompletedUtcMs = {factsNow - 10 * factsDay} WHERE GuideId = '{N(upgradeId)}';
        UPDATE ReadingStates SET LastOpenedUtcMs = {factsNow} WHERE GuideId = '{N(notesId)}';
        """);

    string[] factsGames = [.. (await repository.ListGameSummariesAsync()).Select(entry => entry.Game.Title)];
    string[] factsGuides = [.. (await repository.ListGuideSummariesAsync(factsGameId)).Select(entry => entry.Guide.Title)];
    if (!factsGames.SequenceEqual(new[] { "Zeta Archive Game", "Facts Test Game", "Empty Test Game" }) ||
        !factsGuides.SequenceEqual(new[]
        {
            "Main Story Walkthrough", "Collectibles Map", "Weapon Upgrade Guide", "Achievement Checklist"
        }))
    {
        throw new InvalidOperationException("The facts seed did not read back in activity order.");
    }
    Console.WriteLine($"Seeded facts game {factsGameId:N} with four guides.");
    return 0;
}
```

If the build reports CS0136 for `entry`, `N` or `LocalMs`, rename the
local in this block only.

Run: the seed build.
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Smoke helpers and modes**

Add `'catalog-facts'` after `'catalog'` in the `$Mode` ValidateSet.

After `Get-RealizedGameRows`, add:

```powershell
    # Realized, named ListItems of a list, in display order.
    function Get-ListRows([string] $id) {
        $list = Wait-VisibleById $id
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        return @($list.FindAll($scope, $condition) | Where-Object { Test-RealizedGameRow $_ })
    }

    # $expected holds @(name, help-text pattern) pairs, in display order.
    # Names must match exactly; help text is matched with -like.
    function Assert-RowFacts([string] $id, $expected) {
        $deadline = (Get-Date).AddSeconds(10)
        do {
            $actual = @(Get-ListRows $id | ForEach-Object {
                [ordered]@{ Name = $_.Current.Name; HelpText = $_.Current.HelpText } })
            $matched = $actual.Count -eq $expected.Count
            for ($i = 0; $matched -and $i -lt $expected.Count; $i++) {
                $matched = $actual[$i].Name -eq $expected[$i][0] -and
                    $actual[$i].HelpText -like $expected[$i][1]
            }
            if ($matched) { return $actual }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        $shown = ($actual | ForEach-Object { "$($_.Name) [$($_.HelpText)]" }) -join '; '
        throw "$id rows were: $shown"
    }
```

Add the `catalog-facts` mode before `elseif ($Mode -eq 'catalog')`:

```powershell
    elseif ($Mode -eq 'catalog-facts') {
        [void](Wait-Name 'LibraryHeading' 'Library')
        $report.libraryRows = Assert-RowFacts 'GameList' @(
            @('Zeta Archive Game', 'Windows, Manual, 1 guide'),
            @('Facts Test Game', 'PC, IGDB, 4 guides'),
            @('Empty Test Game', 'Manual, No guides'))
        $report.phases += 'library-facts'
        [void](Wait-HiddenById 'ShellStatus')
        Save-WindowScreenshot 'library-facts'

        Select-Element 'Facts Test Game'
        [void](Wait-Name 'GameHeading' 'Facts Test Game')
        [void](Wait-Status 'Game ready.')
        # The host's culture sets the exact time and date (Ruling 9).
        $report.guideRows = Assert-RowFacts 'GuideList' @(
            @('Main Story Walkthrough', 'Web page (HTML), In progress, opened today at *'),
            @('Collectibles Map', 'PDF, about 45 percent, opened yesterday'),
            @('Weapon Upgrade Guide', 'Text (TXT), Completed, opened on *'),
            @('Achievement Checklist', 'Text (TXT), Not started'))
        $report.phases += 'guide-facts'
        [void](Wait-HiddenById 'ShellStatus')
        Save-WindowScreenshot 'game-facts'

        Go-Back
        [void](Wait-Name 'LibraryHeading' 'Library')
        [void](Wait-Status 'Library ready.')
    }
```

In the `catalog` mode, after `$shortRow = Wait-GameRow $shortTitle`:

```powershell
        if ($longRow.Current.HelpText -ne 'PC, IGDB, No guides') {
            throw "The long-title row's help text was '$($longRow.Current.HelpText)'."
        }
        $report.phases += 'catalog-row-facts'
```

In the `catalog` mode, after the post-scroll `[void](Wait-GameRow $lastTitle)`
and before `$endCount = ...`. Rows at the end use recycled containers (Ruling 13):

```powershell
        $endFacts = [ordered]@{
            $lastTitle = 'Nintendo Switch, IGDB, No guides'
            'Catalog Game 483' = 'IGDB, No guides'
            'Catalog Game 481' = 'Manual, No guides'
        }
        foreach ($title in $endFacts.Keys) {
            $help = (Wait-GameRow $title).Current.HelpText
            if ($help -ne $endFacts[$title]) {
                throw "Row '$title' had help text '$help' at the end of the list."
            }
        }
        $report.phases += 'catalog-end-row-facts'
```

In the `long-list` mode, after the first `[void](Wait-Status 'Game ready.')`
(Ruling 10):

```powershell
        $guideRows = @(Get-ListRows 'GuideList').Count
        $report.realizedGuideRows = $guideRows
        if ($guideRows -lt 1 -or $guideRows -ge 60) {
            throw "GuideList realized $guideRows rows for a 99-guide game."
        }
```

- [ ] **Step 5: Install script**

The `Run-ShellSmoke` timeout (Ruling 12):

```powershell
        elseif ($mode -like 'catalog*' -or $mode -like 'import-*') { 120 }
```

After `Run-CatalogScenarios`:

```powershell
function Run-CatalogFactsScenarios {
    Invoke-ShellSeed @('seed-facts', $dataRoot) | Out-Null
    $originalTheme = Get-AppThemePreference
    try {
        Set-AppThemePreference $true
        Start-InstalledShell
        $report.catalogFactsLight = Run-ShellSmoke 'catalog-facts' -ResultName 'catalog-facts-light'
        Close-InstalledShell

        Set-AppThemePreference $false
        Start-InstalledShell
        $report.catalogFactsDark = Run-ShellSmoke 'catalog-facts' -ResultName 'catalog-facts-dark'
        Close-InstalledShell
    }
    finally {
        Restore-AppThemePreference $originalTheme
    }
}
```

In both places that call `Run-CatalogScenarios` (the `-CatalogOnly`
branch and the full run), follow it with:

```powershell
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-CatalogFactsScenarios
```

In the `-CatalogOnly` branch this goes before `$report.success = $true`.

- [ ] **Step 6: Static checks**

```bash
perl -ne 'print "$ARGV:$.: non-ASCII\n" if /[^\x00-\x7F]/; close ARGV if eof' tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
```

Expected: no output.

Stage, then parse both scripts on the host:

```bash
s 'powershell -NoProfile -Command "foreach ($f in @(''E:\work\desktop-guides\t05-1\tools\p1\windows_shell_ui_smoke.ps1'',''E:\work\desktop-guides\t05-1\tools\p1\windows_shell_install.ps1'')) { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); if ($e) { $e; exit 1 } }; ''parsed''"'
```

Expected: `parsed`.

Run: the seed build again.
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 7: Commit**

Show the message in chat first.

```bash
git add tools/p1/DesktopGuides.ShellSeed/Program.cs tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): smoke-test catalog row facts and order" \
  -m "seed-facts builds a library whose activity order differs from title order: an older game with the most recently opened guide, a linked game with four guides in each reading state, and a game with no guides. The catalog-facts smoke checks each Library and Game-page row's name and help text in order, in light and dark, with screenshots. The catalog smoke now checks help text on its first row and on recycled rows at the end, and seed-catalog gives every game one creation time so title order still holds. long-list reports GuideList's realized rows and fails at 60 of 99." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Installed verification**

This needs a pushed branch, so ask the user first. After approval, push
and let CI's `production-shell-ui` job run. It is the gate of record.

Alternatively, with a CI-built x64 MSIX already on the host, run
`windows_shell_install.ps1 -PackagePath <msix> -ResultDirectory E:\work\desktop-guides\t05-1-results -CatalogOnly`
through an interactive scheduled task. Follow `docs/p1/e2e-testing.md`:
back up and restore the app data and `%LOCALAPPDATA%\DesktopGuides\P0-WebView`.

Expected:
- `success: true`;
- phases `library-facts` and `guide-facts` in `catalog-facts-light` and
  `catalog-facts-dark`;
- phases `catalog-row-facts` and `catalog-end-row-facts` in `catalog-light`
  and `catalog-dark`, with the existing realized-row and artwork checks
  unchanged;
- in the full run, `realizedGuideRows` between 1 and 59 in the `long-list`
  result, and every other mode passing unchanged.

Copy the screenshots to `docs/p1/evidence/t05-1-guide-rows/`:

| From | To |
| --- | --- |
| `catalog-facts-light.library-facts.png` | `library-light.png` |
| `catalog-facts-dark.library-facts.png` | `library-dark.png` |
| `catalog-facts-light.game-facts.png` | `game-light.png` |
| `catalog-facts-dark.game-facts.png` | `game-dark.png` |

Look at all four before copying them: the facts line must be one line,
trimmed rather than wrapped, and the glyph tiles must be visible in both
themes (Review Focus 5).

---

### Task 5: Documentation and verification record

**Files:**
- Modify: `docs/p1/t05-1-guide-rows-design.md` (status line, new verification record)
- Modify: `docs/p1/implementation-plan.md` (after the T15.3 paragraph ending "a dark Cancel and a light removal.", ~line 659)
- Modify: `docs/progress.md` (the "Updated" line, a new T05.1 row after the T15.3 row)
- Modify: `docs/p1/e2e-testing.md` (the `Library catalog` row ~line 248, a new `Catalog facts` row after it)
- Create: `docs/p1/evidence/t05-1-guide-rows/` (the four PNGs from Task 4 Step 8)

**Interfaces:**
- Consumes:
  - the CI run ID and conclusion from Task 4 Step 8;
  - the Core and Infrastructure test totals from the last host runs;
  - `realizedGuideRows` from the `long-list` result;
  - the four evidence PNGs;
  - every `Ruling:` line in the executor's ledger.

TDD skip: this task changes documentation only. The gate is the placeholder
grep, `git diff --check` and a read-through.

Fill every `<…>` below from the observed run before committing. Never
commit a placeholder.

- [ ] **Step 1: Spec**

In `t05-1-guide-rows-design.md`:

- Replace the status line's first sentence with:
  `Status: implemented on \`feat/p1-t05-1-guide-rows\`; verified by CI run <run id>.`
  Keep the prerequisite sentence.
- Append:

```markdown
## T05.1 verification record

- **Unit tests.** On `pcsx2-win`, Infrastructure <n>/<n> and Core <n>/<n>
  passed. The new tests are:
  - `CatalogPresentationTests`: game facts for blank and padded platforms,
    linked and manual games and each guide count; reading-state precedence
    (missing row, opened only, an estimate without an open time, `~0%`,
    `~100%` without completion, midpoint rounding, completion with an
    estimate); today, yesterday, older, both sides of local midnight and a
    future time in UTC+9, in en-US and one other culture; and the spoken
    forms, which never contain `~` or `·`;
  - `LibrarySummaryTests`: game order by creation, import and open time;
    ties by title then ID; guide counts, including after `GuideRemover`
    removes a guide; guide order by import and open time; each guide's own
    reading state, or none; and listing after a guide's content directory
    is deleted.
- **Installed.** CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>)
  passed `production-shell-ui`:
  - `catalog-facts`, light and dark: the Library listed Zeta Archive Game,
    Facts Test Game and Empty Test Game in activity order, and the Game
    page listed the four guides in activity order. Each row's Name was its
    title and its HelpText its spoken facts;
  - `catalog`, light and dark: the existing checks passed unchanged, and
    the long-title row and three recycled rows at the end had their own
    HelpText;
  - `long-list`: `GuideList` realized <n> of 99 rows.
- **Rulings.** Rulings 1–13 in the [plan](t05-1-guide-rows-plan.md#rulings-against-the-spec),
  plus <the ledger rulings made during implementation, each on one line,
  or "none">.
- **Evidence.**
  - [Library, light](evidence/t05-1-guide-rows/library-light.png)
  - [Library, dark](evidence/t05-1-guide-rows/library-dark.png)
  - [Game page, light](evidence/t05-1-guide-rows/game-light.png)
  - [Game page, dark](evidence/t05-1-guide-rows/game-dark.png)
```

- [ ] **Step 2: Implementation plan and E2E catalogue**

In `docs/p1/implementation-plan.md`, after the T15.3 paragraph, add:

```markdown
T05.1 is implemented on `feat/p1-t05-1-guide-rows`; see the
[design and verification record](t05-1-guide-rows-design.md). The Library
lists games by last activity (creation, import or open, newest first), and
the Game page lists guides by import or open time. Each row shows its tile,
title and a `MetadataControl` facts line: platform, source and guide count
for a game, and format, reading state and last opened for a guide. An
unread guide reads `Not started`. One SQLite query feeds each list, and
listing never opens guide files or calls a provider. CI run <run id>
passed `catalog-facts` in light and dark.
```

In `docs/p1/e2e-testing.md`, append this sentence to the end of the
`Library catalog` row's observation, before its `|`:

```markdown
 The long-title row's HelpText is `PC, IGDB, No guides`, and after scrolling to the end the last row and two recycled rows describe their own games.
```

and change that row's requirement cell to `T05.4, T05.1, TR05.3, TR11.3`.
After the row, add:

```markdown
| Catalog facts | Seed three games whose activity order differs from title order and four guides in each reading state. In light and dark: the Library lists Zeta Archive Game, Facts Test Game and Empty Test Game in that order with HelpText `Windows, Manual, 1 guide`, `PC, IGDB, 4 guides` and `Manual, No guides`; the Game page lists Main Story Walkthrough, Collectibles Map, Weapon Upgrade Guide and Achievement Checklist, each Name its title and each HelpText its format, reading state and last opened, with `Not started` for the unread guide. In `long-list`, a 99-guide game realizes fewer than 60 `GuideList` rows. | T05.1, TR05.2, TR05.3, TR11.3 |
```

- [ ] **Step 3: Progress**

In `docs/progress.md`:

- Keep the "Updated" date line current.
- After the T15.3 row, add:

```markdown
| P1 T05.1 Library and Game rows | Implemented on `feat/p1-t05-1-guide-rows`; PR open. | The Library lists games and the Game page lists guides by last activity, newest first. Each row shows a facts line: platform, source and guide count for a game; format, reading state and last opened for a guide, with `Not started` for an unread guide. Both lists stay virtualized and read only SQLite. CI run [<run id>](https://github.com/ilya-slalom/desktop-guides/actions/runs/<run id>) passed `catalog-facts`; see the [verification record](p1/t05-1-guide-rows-design.md#t051-verification-record). |
```

After the PR opens, change "PR open" to the linked PR number in a
follow-up commit on the same branch.

- [ ] **Step 4: Check and commit**

```bash
grep -n '<run id>\|<n>\|<the ledger' docs/p1/t05-1-guide-rows-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/e2e-testing.md
git diff --check
```

Expected: no output from either command.

Show the message in chat first.

```bash
git add docs/p1/t05-1-guide-rows-design.md docs/p1/implementation-plan.md docs/progress.md docs/p1/e2e-testing.md docs/p1/evidence/t05-1-guide-rows
git commit -m "docs(p1): record T05.1 catalog rows verification" \
  -m "Mark the T05.1 design implemented and add its verification record: unit tests, CI run <run id>, rulings and four screenshots. Add the T05.1 paragraph to the implementation plan, a Catalog facts row to the E2E catalogue and a T05.1 row to progress." \
  -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Traceability

| Requirement | Evidence in this plan |
| --- | --- |
| T05.1: games and guides sorted by last activity, rows with tile, title and facts | Task 2's `LibrarySummaryTests` game and guide order; Task 3's row template; Task 4's `catalog-facts` Name and HelpText order in light and dark |
| TR05.2: an unread guide reads `Not started`, never `0%` | Task 1's `CatalogPresentationTests` reading-state precedence; Task 4's `Achievement Checklist` row |
| TR05.3: virtualized lists, missing artwork, long and localized text | Task 4's `catalog` checks (unchanged, plus end-of-list HelpText) and the `long-list` `GuideList` realized-row limit |
| TR04.3: cached display offline, no provider request | The `catalog` no-provider-traffic phase, unchanged; Task 2's listing after content deletion |

## PR outcome

- **Target task:** T05.1.
- **Prerequisites:** T03.2 (PR #4), T04.4 (PR #14), T05.4 (PR #16) and
  T11.1 (PR #6), all merged.
- **Outcome:** the Library and the Game page list games and guides most
  recently used first. Each row shows its tile, its title and a facts line;
  an unread guide says `Not started`. Both lists stay virtualized and never
  read guide files or call a provider. The PR body shows the Library and
  Game page in light and dark.
