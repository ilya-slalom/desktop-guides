# T09.3 HTML Locator and Restore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture where the reader is in an HTML guide's entry document,
keep that point across window resizes, restore a saved point by exact
offset, then text context, then scroll fraction, and report links to pages
that weren't imported (TR09.1, TR09.2).

**Architecture:** Every decision lives in a new pure Core class,
`DesktopGuides.Core/Html/HtmlLocationRules`: it parses the host scripts'
replies as untrusted JSON, builds and decodes locators through the T12.1
codec, plans a restore, picks the context match and maps the outcome.
`HtmlNavigationPolicy` gains an `Unavailable` kind. In Production, fixed
scripts in `HtmlPositionScripts` run through `ExecuteScriptAsync` and only
measure and scroll; `HtmlReaderSession` checks the page's origin before
each script, polls the scroll every 500 ms, re-applies the point after a
resize and raises a new event for unimported links, which the shell shows
in a second InfoBar. Production has no unit test host, so a new installed
smoke mode, `html-position`, covers the wiring.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), WebView2, xUnit,
PowerShell 5.1 UI Automation smoke.

**Spec:** [t09-3-html-locator-design.md](t09-3-html-locator-design.md)

## Global Constraints

**Branch:** `feat/p1-t09-3-html-locator` (already checked out; the spec is
commit `2746175`).

**Tooling:**
- There is no local `dotnet` or `pwsh` on the authoring Mac. Every "Run" step
  is a CI run:
  1. Push, then trigger it with `gh workflow run windows-ci.yml --ref feat/p1-t09-3-html-locator`.
  2. Get its ID with `gh run list --workflow windows-ci.yml --branch feat/p1-t09-3-html-locator --limit 1 --json databaseId,headSha -q '.[0]'`
     and check that `headSha` is the commit you pushed.
  3. Watch it with `gh run watch <id> --exit-status --interval 60`.
  4. If it fails, read `gh run view <id> --log-failed`. Smoke failures are
     in the `error` field of the report JSON in the `*shell*` artifact
     (`gh run download <id> --pattern '*shell*'`; the JSON has a BOM).
- The `core-tests` job (about 3 minutes) runs Core.Tests and
  Infrastructure.Tests; every other job needs it. The `production-shell-ui`
  job (about 20 minutes) builds Production and runs the installed smoke. A
  core-only step may cancel the run with `gh run cancel <id>` once
  `core-tests` has finished.
- Pass counts: `gh api repos/ilya-slalom/desktop-guides/actions/jobs/<job-id>/logs | grep "Passed!"`.
- A RED run may be batched with the previous task's GREEN run only when they
  land in different jobs.
- PowerShell files stay ASCII-only. Check with
  `LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/*.ps1` (expected: no output).

**Values (verbatim):**
- `ChangedReason`: "The guide changed, so this is an approximate position."
  (equal to `TextLocator.ApproximateReason`).
- `UnavailableReason`: "This reading position can't be used with this guide."
  (equal to `TextLocator.UnavailableReason`).
- Unavailable-link message: "This link goes to a page that isn't part of the imported guide."
- Quote: at most **160** UTF-16 code units; element id: at most **128**; neither contains NUL.
- Script replies are read with a cap of **4096** UTF-8 bytes and a JSON depth of **4**.
- A find reply holds at most **64** offsets per list.
- Scroll poll: **500 ms**; movement threshold: more than **1** CSS px in any of
  `scrollY`, `scrollHeight`, `innerHeight`.
- Resize settle: **300 ms** without another `SizeChanged`.
- ~~Image wait after a restore scroll~~: dropped in Task 5. With page scripts off, lazy images load eagerly and the open waits for them (see the design's `Restore`).
- Script call timeout: **5 s**.
- `HtmlAssetDelay` gate: image responses are delayed by **1 s**.
- Text walk: the body's text nodes in document order, skipping any node
  inside `script`, `style`, `template` or `noscript`; offsets count UTF-16
  code units of the nodes' raw `data`.
- Gate names: `Local\DesktopGuides.Preview.HtmlPosition.<pid>` and
  `Local\DesktopGuides.Preview.HtmlAssetDelay.<pid>`.
- Gate files: `<cache root>\diagnostics\html-position-<pid>.json` (written by
  the app) and `<data root>\test\html-restore.json` (read by the app).
- New AutomationIds: `ReaderUnavailableLinkBar`.

**Rules:**
- In-session only. Nothing here writes `ReadingStates` or restores on reopen
  (T12.2); the restore file is a test gate, not a feature.
- T07.3's WebView2 settings are unchanged: `IsScriptEnabled`,
  `IsWebMessageEnabled` and `AreHostObjectsAllowed` stay `false`. No web
  messages, no host objects, no DevTools protocol calls.
- Host values enter a script only as `JsonSerializer.Serialize` literals.
- Every script reply is parsed in Core before it is used or stored.
- No guide text is logged. `HtmlSessionDiagnostics` holds counts only. The
  `HtmlPosition` gate file is the one place guide text is written, and only
  while that gate is open.
- Everything in the session runs on the UI thread and stops once
  `DisposeAsync` starts.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

**Spec refinements made while planning** (each one is recorded in the
design doc by Task 7):

1. **The restore choice moves into Core.** The spec's single `Restore(plan)`
   script that searches and scrolls becomes two measuring scripts and two
   scrolling scripts: `Find(args)` reports whether the quote is at the saved
   offset and lists the quote's occurrences (inside the `ElementId` element
   and in the whole walk); Core's `Resolve` picks the step and offset
   (nearest occurrence, a tie is no match); `ScrollToOffset` or
   `ScrollToFraction` scrolls and reports how many images are still
   loading. The host polls `PendingImages`, then scrolls to the same target
   again. The spec's restore reply `{"step", "offset"}` is therefore not
   parsed; `Outcome` maps Core's own chosen step. Reason: the spec's
   principle "every decision lives in Core", and the tie rule becomes a unit
   test instead of script-only behavior.
2. **Capture measures text boxes, not `caretRangeFromPoint`.** The capture
   script binary-searches the walk for the first character whose box
   bottom is below the viewport top. This is deterministic, needs no hit
   test, and is not fooled by images, gaps, padding or body margins, which
   the spec's 8 px step-down handled approximately. Restore scrolls that
   character's box top to the viewport top, so capture after restore
   returns the same offset.
3. **Exact needs a quote.** `PlanRestore` plans the exact step only when the
   position has a quote. A page with no text (image-only guides) captures
   offset 0 and no quote; restoring it uses the fraction.
4. **A fraction restore of unchanged bytes is `Approximate` without a
   reason.** "The guide changed" would be false there. Changed bytes keep
   `ChangedReason`.
5. **`RestoreLocationAsync` receives a `ReaderLocation`, not JSON.**
   `HtmlLocationRules.Decode` round-trips it through the codec
   (`Serialize`, then `Deserialize` with `Html`, the guide's SHA-256 and the
   entry path), so a typed location gets the same checks as stored JSON. An
   invalid one is `Invalid`.
6. **An unimported link is counted as a denied navigation.** Navigations
   aren't requests, so before T09.3 they were never counted. A new
   `HtmlDenyReason.UnimportedPage` is recorded with context `Navigation`;
   this is the spec's "denied count rises by one".
7. **The top line is read through UI Automation's TextPattern.**
   `RangeFromPoint` just inside the page's top-left, expanded to a line,
   gives the visible top line independently of the app's own capture. Task
   3's early check proves it works on CI before later tasks rely on it.
8. **`html-position` is its own seed and smoke mode**
   (`seed-html-position`, game "Web Position Game"), so the canary passes'
   session counts are unchanged. The page-tree helpers from `html-reader`
   move to the shared reader scope so both modes use them.
9. **Origin check shares the entry comparison.** `HtmlNavigationPolicy`
   exposes `IsEntryDocument(Uri, Uri)` (the existing private `IsEntry`), used
   by `ParseCapture` for `href` and by the session for `CoreWebView2.Source`.

## Review Focus

1. **Repeated separator lines.** GameFAQs-style guides repeat lines such as
   `====` dozens of times. A quote that is only a separator must restore to
   the occurrence nearest the saved offset, and a tie must fall through to
   the fraction rather than jump to the first copy. Pinned by Task 1's
   `ContextTieFallsThroughToTheFraction`.
2. **Titled "Save Page As" entries.** Chromium reports `location.href` with
   `( ) '` left literal and `%25` for `%`. Captures from such an entry must be
   accepted. Pinned by Task 1's `CaptureFromATitledEntryWithLiteralSubDelimsIsAccepted`.
3. **A page without text.** An image-only HTML guide captures offset 0 and no
   quote. Restore must use the fraction, not report `Unavailable` and not
   snap to the top. Pinned by Task 1's `APositionWithoutAQuoteHasNoExactStep`.
4. **A saved offset past the end of changed bytes.** After an edit that
   shortens the guide, the saved offset may exceed the text. Exact must never
   be tried; context or fraction applies. Pinned by Task 1's
   `ChangedBytesNeverPlanExact` and Task 5's changed-guide smoke step.
5. **DOM-controlled sizes.** A page can make the nearest `id` longer than 128
   characters or put NUL in its text. The script sends `null` for an id over
   128 and Core rejects a reply over the caps, keeping the last point. Pinned
   by Task 1's parse rows and Task 3's script (`id.length > 128 ? null : id`).

## File map

| File | Change | Responsibility |
| --- | --- | --- |
| `src/DesktopGuides.Core/Html/HtmlLocationRules.cs` | create | Parse replies, build, decode, plan, resolve, outcome |
| `src/DesktopGuides.Core/Html/HtmlNavigationPolicy.cs` | modify | `Unavailable` kind, public `IsEntryDocument` |
| `src/DesktopGuides.Core/Html/HtmlRequestPolicy.cs` | modify | `HtmlDenyReason.UnimportedPage` |
| `src/DesktopGuides.Core/Html/HtmlSessionDiagnostics.cs` | modify | Rejected-capture and restore-kind counts |
| `tests/DesktopGuides.Core.Tests/HtmlLocationRulesTests.cs` | create | Rules tests |
| `tests/DesktopGuides.Core.Tests/HtmlNavigationPolicyTests.cs` | modify | Unavailable rows |
| `tests/DesktopGuides.Core.Tests/HtmlSessionDiagnosticsTests.cs` | modify | New counts |
| `src/DesktopGuides.Production/HtmlPositionScripts.cs` | create | Fixed host scripts |
| `src/DesktopGuides.Production/HtmlReaderSession.cs` | modify | Capture, tracking, resize, restore, gates, link event |
| `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` | modify | Unavailable-link bar |
| `src/DesktopGuides.Production/ShellWindow.xaml(.cs)` | modify | Bar XAML, data root field, close paths |
| `tests/fixtures/p1/html-long/guide.html`, `images/map.png`, `images/route.png` | create | Long `<pre>` fixture |
| `tests/fixtures/p1/html-long-changed/guide.html`, `images/map.png`, `images/route.png` | create | Changed copy |
| `tools/p1/DesktopGuides.ShellSeed/Program.cs` | modify | `seed-html-position` |
| `tools/p1/windows_shell_ui_smoke.ps1` | modify | `html-position` mode |
| `tools/p1/windows_shell_install.ps1` | modify | `Run-HtmlPositionScenarios` |
| docs | modify | Task 7 |

---

### Task 1: `HtmlLocationRules`

**Files:**
- Create: `src/DesktopGuides.Core/Html/HtmlLocationRules.cs`
- Modify: `src/DesktopGuides.Core/Html/HtmlNavigationPolicy.cs` (rename the private `IsEntry` to public `IsEntryDocument`)
- Test: `tests/DesktopGuides.Core.Tests/HtmlLocationRulesTests.cs`

**Interfaces:**
- Consumes: `ReaderLocationCodec.Serialize/Deserialize/CurrentVersion/MaxBytes`,
  `HtmlPosition`, `ReaderLocation`, `LocationDecodeResult`, `RestoreOutcome`,
  `GuideWebOrigin.EntryUri`.
- Produces (namespace `DesktopGuides.Core.Html`):
  - `record HtmlCapture(int Offset, string? Quote, string? ElementId, double Fraction)`
  - `record HtmlScroll(double Y, double Height, double ViewportHeight)`
  - `enum HtmlRestoreStep { Exact, Context, Fraction }`
  - `record HtmlRestorePlan(IReadOnlyList<HtmlRestoreStep> Steps, HtmlPosition? Position, bool ContentChanged)` with `bool NeedsFind`
  - `record HtmlFindResult(bool ExactMatch, IReadOnlyList<int> InElement, IReadOnlyList<int> Anywhere)`
  - `record HtmlRestoreTarget(HtmlRestoreStep Step, int Offset, double Fraction)`
  - `static class HtmlLocationRules`: `ChangedReason`, `UnavailableReason`,
    `MaxQuote = 160`, `MaxElementId = 128`, `MaxReplyBytes = 4096`, `MaxOffsets = 64`;
    `HtmlCapture? ParseCapture(string? json, Uri entry)`,
    `HtmlScroll? ParseScroll(string? json)`, `int? ParsePending(string? json)`,
    `HtmlFindResult? ParseFind(string? json)`, `bool Moved(HtmlScroll? last, HtmlScroll now)`,
    `ReaderLocation Capture(string contentSha256, string documentPath, HtmlCapture capture)`,
    `LocationDecodeResult Decode(ReaderLocation location, string contentSha256, string documentPath)`,
    `HtmlRestorePlan PlanRestore(LocationDecodeResult result)`,
    `string FindArgs(HtmlPosition position)`,
    `HtmlRestoreTarget? Resolve(HtmlRestorePlan plan, HtmlFindResult? find)`,
    `RestoreOutcome Outcome(HtmlRestorePlan plan, HtmlRestoreTarget? target)`.
  - `HtmlNavigationPolicy.IsEntryDocument(Uri target, Uri entry)` (public; fragment ignored).

- [ ] **Step 1: Write the failing tests**

Create `tests/DesktopGuides.Core.Tests/HtmlLocationRulesTests.cs`:

```csharp
using System.Text.Json;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlLocationRulesTests
{
    private static readonly Guid Id = Guid.Parse("3f2a9c0e-4b7d-1a65-f08c-2e9d3b4a7c10");
    private static readonly Uri Entry = GuideWebOrigin.EntryUri(Id, "guide.html");
    private static readonly string Sha = new('a', 64);
    private static readonly string OtherSha = new('b', 64);
    private const string Path = "guide.html";

    private static string Str(string? value) => JsonSerializer.Serialize(value);

    private static Dictionary<string, string> GoodRaw() => new()
    {
        ["offset"] = "420",
        ["quote"] = Str("MARK-0420"),
        ["id"] = Str("target"),
        ["fraction"] = "0.25",
        ["href"] = Str(Entry.AbsoluteUri + "#target")
    };

    private static string Build(Dictionary<string, string> raw) =>
        "{" + string.Join(",", raw.Select(pair => $"\"{pair.Key}\":{pair.Value}")) + "}";

    private static string With(string field, string raw)
    {
        Dictionary<string, string> fields = GoodRaw();
        fields[field] = raw;
        return Build(fields);
    }

    // ---- ParseCapture ----

    [Fact]
    public void AGoodCaptureIsAccepted() =>
        Assert.Equal(new HtmlCapture(420, "MARK-0420", "target", 0.25),
            HtmlLocationRules.ParseCapture(Build(GoodRaw()), Entry));

    [Fact]
    public void ATextlessPageIsAccepted()
    {
        Dictionary<string, string> fields = GoodRaw();
        fields["offset"] = "0";
        fields["quote"] = "null";
        fields["id"] = "null";
        fields["fraction"] = "0";
        Assert.Equal(new HtmlCapture(0, null, null, 0), HtmlLocationRules.ParseCapture(Build(fields), Entry));
    }

    [Theory]
    [InlineData("offset", "-1")]
    [InlineData("offset", "1.5")]
    [InlineData("offset", "2147483648")]
    [InlineData("offset", "\"420\"")]
    [InlineData("offset", "null")]
    [InlineData("quote", "42")]
    [InlineData("quote", "\"a\\u0000b\"")]
    [InlineData("id", "false")]
    [InlineData("id", "\"a\\u0000b\"")]
    [InlineData("fraction", "-0.01")]
    [InlineData("fraction", "1.01")]
    [InlineData("fraction", "null")]
    [InlineData("fraction", "1e400")]
    [InlineData("fraction", "\"0.5\"")]
    [InlineData("fraction", "[[[[0]]]]")]
    [InlineData("href", "null")]
    [InlineData("href", "\"not a uri\"")]
    public void ABadFieldIsRejected(string field, string raw) =>
        Assert.Null(HtmlLocationRules.ParseCapture(With(field, raw), Entry));

    [Fact]
    public void QuoteAndIdLimitsAreInclusive()
    {
        Assert.NotNull(HtmlLocationRules.ParseCapture(With("quote", Str(new string('q', 160))), Entry));
        Assert.Null(HtmlLocationRules.ParseCapture(With("quote", Str(new string('q', 161))), Entry));
        Assert.NotNull(HtmlLocationRules.ParseCapture(With("id", Str(new string('i', 128))), Entry));
        Assert.Null(HtmlLocationRules.ParseCapture(With("id", Str(new string('i', 129))), Entry));
    }

    [Fact]
    public void AnExtraMissingOrRepeatedFieldIsRejected()
    {
        Dictionary<string, string> extra = GoodRaw();
        extra["step"] = Str("exact");
        Dictionary<string, string> missing = GoodRaw();
        missing.Remove("id");

        Assert.Null(HtmlLocationRules.ParseCapture(Build(extra), Entry));
        Assert.Null(HtmlLocationRules.ParseCapture(Build(missing), Entry));
        Assert.Null(HtmlLocationRules.ParseCapture(Build(GoodRaw()).Replace("{", "{\"offset\":1,"), Entry));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{")]
    [InlineData("")]
    public void ANonObjectReplyIsRejected(string reply) =>
        Assert.Null(HtmlLocationRules.ParseCapture(reply, Entry));

    [Fact]
    public void AnOversizedReplyIsRejectedBeforeParsing() =>
        // Valid JSON once trailing whitespace is ignored; only the size cap rejects it.
        Assert.Null(HtmlLocationRules.ParseCapture(Build(GoodRaw()) + new string(' ', 4096), Entry));

    [Fact]
    public void AnotherPageGuideDirectoryOrQueryIsRejected()
    {
        Uri[] others =
        [
            GuideWebOrigin.EntryUri(Id, "part2.html"),
            GuideWebOrigin.EntryUri(Guid.NewGuid(), "guide.html"),
            GuideWebOrigin.EntryUri(Id, "sub/guide.html"),
            new(Entry.AbsoluteUri + "?page=2"),
            new("https://example.com/guide.html")
        ];
        foreach (Uri other in others)
        {
            Assert.Null(HtmlLocationRules.ParseCapture(With("href", Str(other.AbsoluteUri)), Entry));
        }
    }

    [Fact]
    public void AFragmentOnlyDifferenceIsAccepted()
    {
        Assert.NotNull(HtmlLocationRules.ParseCapture(With("href", Str(Entry.AbsoluteUri)), Entry));
        Assert.NotNull(HtmlLocationRules.ParseCapture(With("href", Str(Entry.AbsoluteUri + "#other")), Entry));
    }

    [Fact]
    public void CaptureFromATitledEntryWithLiteralSubDelimsIsAccepted()
    {
        Uri titled = GuideWebOrigin.EntryUri(Id, "Canary Guide B (PS1) - Walkthrough's 100% Café.html");
        string reported = titled.AbsoluteUri.Replace("%28", "(").Replace("%29", ")").Replace("%27", "'");

        Assert.NotNull(HtmlLocationRules.ParseCapture(With("href", Str(reported + "#mark")), titled));
    }

    // ---- ParseScroll, ParsePending, Moved ----

    [Fact]
    public void ScrollIsThreeFiniteNonNegativeNumbers()
    {
        Assert.Equal(new HtmlScroll(10.5, 4000, 800), HtmlLocationRules.ParseScroll("[10.5,4000,800]"));
        foreach (string bad in new[] { "[1,2]", "[1,2,3,4]", "[-1,2,3]", "[1,null,3]", "[1,\"2\",3]", "{}", "null", "" })
        {
            Assert.Null(HtmlLocationRules.ParseScroll(bad));
        }
    }

    [Fact]
    public void PendingIsANonNegativeInteger()
    {
        Assert.Equal(2, HtmlLocationRules.ParsePending("2"));
        foreach (string bad in new[] { "-1", "1.5", "null", "\"2\"", "" })
        {
            Assert.Null(HtmlLocationRules.ParsePending(bad));
        }
    }

    [Fact]
    public void MovementNeedsMoreThanOnePixel()
    {
        HtmlScroll at = new(100, 4000, 800);
        Assert.True(HtmlLocationRules.Moved(null, at));
        Assert.False(HtmlLocationRules.Moved(at, at with { Y = 101 }));
        Assert.True(HtmlLocationRules.Moved(at, at with { Y = 101.5 }));
        Assert.True(HtmlLocationRules.Moved(at, at with { Height = 3998 }));
        Assert.True(HtmlLocationRules.Moved(at, at with { ViewportHeight = 600 }));
    }

    // ---- Capture, Decode ----

    [Fact]
    public void CaptureBuildsAnHtmlLocationTheCodecRoundTrips()
    {
        ReaderLocation location = HtmlLocationRules.Capture(
            Sha.ToUpperInvariant(), Path, new HtmlCapture(420, "MARK-0420", "target", 0.25));

        Assert.Equal(GuideFormat.Html, location.Format);
        Assert.Equal(Sha, location.ContentSha256);
        Assert.Equal(0.25, location.EstimatedFraction);
        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(location), GuideFormat.Html, Sha, Path);
        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Equal(new HtmlPosition(Path, "target", "MARK-0420", 420, 0.25), decoded.Location!.Payload);
    }

    private static ReaderLocation Saved(string sha = "", string path = Path, string? quote = "MARK-0420") =>
        HtmlLocationRules.Capture(sha.Length == 0 ? Sha : sha, path, new HtmlCapture(420, quote, "target", 0.25));

    [Fact]
    public void DecodeChecksHashPathAndFormat()
    {
        Assert.Equal(LocationDecodeStatus.Valid, HtmlLocationRules.Decode(Saved(), Sha, Path).Status);
        Assert.Equal(LocationDecodeStatus.ContentChanged, HtmlLocationRules.Decode(Saved(), OtherSha, Path).Status);
        Assert.Equal(LocationDecodeStatus.Invalid, HtmlLocationRules.Decode(Saved(path: "other.html"), Sha, Path).Status);
        ReaderLocation text = new(GuideFormat.Txt, 1, Sha, new TextPosition(0, "x"), null);
        Assert.Equal(LocationDecodeStatus.Invalid, HtmlLocationRules.Decode(text, Sha, Path).Status);
        ReaderLocation broken = Saved() with { Payload = new HtmlPosition(Path, null, "q", -1, 0) };
        Assert.Equal(LocationDecodeStatus.Invalid, HtmlLocationRules.Decode(broken, Sha, Path).Status);
    }

    // ---- PlanRestore ----

    private static HtmlRestorePlan Plan(ReaderLocation location, string sha) =>
        HtmlLocationRules.PlanRestore(HtmlLocationRules.Decode(location, sha, Path));

    [Fact]
    public void SameBytesPlanExactThenContextThenFraction()
    {
        HtmlRestorePlan plan = Plan(Saved(), Sha);
        Assert.Equal([HtmlRestoreStep.Exact, HtmlRestoreStep.Context, HtmlRestoreStep.Fraction], plan.Steps);
        Assert.False(plan.ContentChanged);
        Assert.True(plan.NeedsFind);
    }

    [Fact]
    public void ChangedBytesNeverPlanExact()
    {
        HtmlRestorePlan plan = Plan(Saved(), OtherSha);
        Assert.Equal([HtmlRestoreStep.Context, HtmlRestoreStep.Fraction], plan.Steps);
        Assert.True(plan.ContentChanged);
    }

    [Fact]
    public void APositionWithoutAQuoteHasNoExactStep()
    {
        HtmlRestorePlan plan = Plan(Saved(quote: null), Sha);
        Assert.Equal([HtmlRestoreStep.Fraction], plan.Steps);
        Assert.False(plan.NeedsFind);
    }

    [Theory]
    [InlineData(LocationDecodeStatus.UnsupportedVersion)]
    [InlineData(LocationDecodeStatus.Invalid)]
    public void AnUnusableDecodePlansNothing(LocationDecodeStatus status)
    {
        HtmlRestorePlan plan = HtmlLocationRules.PlanRestore(new LocationDecodeResult(status, null));
        Assert.Empty(plan.Steps);
        Assert.Null(plan.Position);
    }

    [Fact]
    public void FindArgsCarryOnlyOffsetQuoteAndId()
    {
        using JsonDocument args = JsonDocument.Parse(
            HtmlLocationRules.FindArgs(new HtmlPosition(Path, "t\"</script>", "MARK-0420", 420, 0.25)));
        Assert.Equal(["offset", "quote", "id"], args.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(420, args.RootElement.GetProperty("offset").GetInt32());
        Assert.Equal("MARK-0420", args.RootElement.GetProperty("quote").GetString());
        Assert.Equal("t\"</script>", args.RootElement.GetProperty("id").GetString());
    }

    // ---- ParseFind ----

    [Fact]
    public void AGoodFindIsAccepted()
    {
        HtmlFindResult find = HtmlLocationRules.ParseFind("{\"exact\":true,\"element\":[420],\"all\":[10,420]}")!;
        Assert.True(find.ExactMatch);
        Assert.Equal([420], find.InElement);
        Assert.Equal([10, 420], find.Anywhere);
    }

    [Theory]
    [InlineData("{\"exact\":1,\"element\":[],\"all\":[]}")]
    [InlineData("{\"exact\":true,\"element\":[-1],\"all\":[]}")]
    [InlineData("{\"exact\":true,\"element\":[1.5],\"all\":[]}")]
    [InlineData("{\"exact\":true,\"element\":null,\"all\":[]}")]
    [InlineData("{\"exact\":true,\"element\":[]}")]
    [InlineData("{\"exact\":true,\"element\":[],\"all\":[],\"step\":\"exact\"}")]
    [InlineData("null")]
    public void ABadFindIsRejected(string reply) =>
        Assert.Null(HtmlLocationRules.ParseFind(reply));

    [Fact]
    public void AFindListOverTheCapIsRejected()
    {
        string list(int n) => "[" + string.Join(",", Enumerable.Range(0, n)) + "]";
        Assert.NotNull(HtmlLocationRules.ParseFind($"{{\"exact\":false,\"element\":[],\"all\":{list(64)}}}"));
        Assert.Null(HtmlLocationRules.ParseFind($"{{\"exact\":false,\"element\":[],\"all\":{list(65)}}}"));
    }

    // ---- Resolve ----

    private static HtmlFindResult Found(bool exact, int[] element, int[] all) => new(exact, element, all);

    [Fact]
    public void AnExactMatchResolvesToTheSavedOffset() =>
        Assert.Equal(new HtmlRestoreTarget(HtmlRestoreStep.Exact, 420, 0.25),
            HtmlLocationRules.Resolve(Plan(Saved(), Sha), Found(true, [420], [420])));

    [Fact]
    public void ContextPrefersTheElementThenTheNearestAnywhere()
    {
        HtmlRestorePlan plan = Plan(Saved(), OtherSha);
        Assert.Equal(new HtmlRestoreTarget(HtmlRestoreStep.Context, 900, 0.25),
            HtmlLocationRules.Resolve(plan, Found(false, [900], [430, 900])));
        Assert.Equal(new HtmlRestoreTarget(HtmlRestoreStep.Context, 430, 0.25),
            HtmlLocationRules.Resolve(plan, Found(false, [], [10, 430, 900])));
    }

    [Fact]
    public void ContextTieFallsThroughToTheFraction()
    {
        // Two separator copies 20 characters either side of the saved offset.
        Assert.Equal(new HtmlRestoreTarget(HtmlRestoreStep.Fraction, 0, 0.25),
            HtmlLocationRules.Resolve(Plan(Saved(), OtherSha), Found(false, [], [400, 440])));
    }

    [Fact]
    public void ExactWithoutAMatchFallsToContextInTheSameBytes() =>
        Assert.Equal(new HtmlRestoreTarget(HtmlRestoreStep.Context, 460, 0.25),
            HtmlLocationRules.Resolve(Plan(Saved(), Sha), Found(false, [], [460])));

    [Fact]
    public void ChangedBytesIgnoreAnExactFlag() =>
        Assert.Equal(new HtmlRestoreTarget(HtmlRestoreStep.Fraction, 0, 0.25),
            HtmlLocationRules.Resolve(Plan(Saved(), OtherSha), Found(true, [], [])));

    [Fact]
    public void AMissingFindIsNoTargetWhenThePlanNeedsOne()
    {
        Assert.Null(HtmlLocationRules.Resolve(Plan(Saved(), Sha), null));
        Assert.Equal(new HtmlRestoreTarget(HtmlRestoreStep.Fraction, 0, 0.25),
            HtmlLocationRules.Resolve(Plan(Saved(quote: null), Sha), null));
    }

    [Fact]
    public void AnEmptyPlanHasNoTarget() =>
        Assert.Null(HtmlLocationRules.Resolve(
            HtmlLocationRules.PlanRestore(new LocationDecodeResult(LocationDecodeStatus.Invalid, null)),
            Found(true, [], [])));

    // ---- Outcome ----

    [Fact]
    public void OutcomesMapEachStep()
    {
        HtmlRestorePlan same = Plan(Saved(), Sha);
        HtmlRestorePlan changed = Plan(Saved(), OtherSha);
        HtmlRestoreTarget exact = new(HtmlRestoreStep.Exact, 420, 0.25);
        HtmlRestoreTarget context = new(HtmlRestoreStep.Context, 430, 0.25);
        HtmlRestoreTarget fraction = new(HtmlRestoreStep.Fraction, 0, 0.25);

        Assert.Equal(new RestoreOutcome(RestoreKind.Exact), HtmlLocationRules.Outcome(same, exact));
        Assert.Equal(new RestoreOutcome(RestoreKind.Context), HtmlLocationRules.Outcome(same, context));
        Assert.Equal(new RestoreOutcome(RestoreKind.Approximate), HtmlLocationRules.Outcome(same, fraction));
        Assert.Equal(new RestoreOutcome(RestoreKind.Approximate, HtmlLocationRules.ChangedReason),
            HtmlLocationRules.Outcome(changed, context));
        Assert.Equal(new RestoreOutcome(RestoreKind.Approximate, HtmlLocationRules.ChangedReason),
            HtmlLocationRules.Outcome(changed, fraction));
        Assert.Equal(new RestoreOutcome(RestoreKind.Unavailable, HtmlLocationRules.UnavailableReason),
            HtmlLocationRules.Outcome(same, null));
    }

    [Fact]
    public void AStepThePlanDidNotAllowIsUnavailable() =>
        Assert.Equal(RestoreKind.Unavailable,
            HtmlLocationRules.Outcome(Plan(Saved(), OtherSha), new HtmlRestoreTarget(HtmlRestoreStep.Exact, 420, 0.25)).Kind);

    [Fact]
    public void ReasonsAreTheTextReadersReasons()
    {
        Assert.Equal(TextLocator.ApproximateReason, HtmlLocationRules.ChangedReason);
        Assert.Equal(TextLocator.UnavailableReason, HtmlLocationRules.UnavailableReason);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Commit the test file alone (`git add tests/DesktopGuides.Core.Tests/HtmlLocationRulesTests.cs && git commit -m "test(p1): T09.3 HTML location rules"`), push and run CI as in Global Constraints.
Expected: `core-tests` fails to build with `CS0246: The type or namespace name 'HtmlCapture' could not be found` (and the other new names). Cancel the run once `core-tests` has failed.

- [ ] **Step 3: Make `IsEntryDocument` public**

In `src/DesktopGuides.Core/Html/HtmlNavigationPolicy.cs`, rename the private
`IsEntry` to a public `IsEntryDocument` and update its one caller in
`Classify`. The body is unchanged; it already ignores the fragment:

```csharp
    // Chromium may report a self-link with sub-delims such as ( ) ' left
    // literal, so paths are compared decoded, as the request policy does.
    // The fragment is ignored.
    public static bool IsEntryDocument(Uri target, Uri entry) =>
        Uri.Compare(target, entry, UriComponents.SchemeAndServer,
            UriFormat.UriEscaped, StringComparison.Ordinal) == 0 &&
        string.Equals(target.Query, entry.Query, StringComparison.Ordinal) &&
        string.Equals(Uri.UnescapeDataString(target.AbsolutePath), Uri.UnescapeDataString(entry.AbsolutePath),
            StringComparison.Ordinal);
```

- [ ] **Step 4: Implement `HtmlLocationRules`**

Create `src/DesktopGuides.Core/Html/HtmlLocationRules.cs`:

```csharp
using System.Text;
using System.Text.Json;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Html;

public sealed record HtmlCapture(int Offset, string? Quote, string? ElementId, double Fraction);

public sealed record HtmlScroll(double Y, double Height, double ViewportHeight);

public enum HtmlRestoreStep { Exact, Context, Fraction }

public sealed record HtmlRestorePlan(
    IReadOnlyList<HtmlRestoreStep> Steps, HtmlPosition? Position, bool ContentChanged)
{
    public bool NeedsFind => Steps.Contains(HtmlRestoreStep.Exact) || Steps.Contains(HtmlRestoreStep.Context);
}

public sealed record HtmlFindResult(bool ExactMatch, IReadOnlyList<int> InElement, IReadOnlyList<int> Anywhere);

public sealed record HtmlRestoreTarget(HtmlRestoreStep Step, int Offset, double Fraction);

// An HTML position is a character offset in the entry document's text walk,
// with a quote and the nearest id for changed bytes and a scroll fraction as
// the last resort. Every reply from the host scripts is untrusted page
// output and is checked here before it is used or stored.
public static class HtmlLocationRules
{
    public const string ChangedReason = "The guide changed, so this is an approximate position.";
    public const string UnavailableReason = "This reading position can't be used with this guide.";
    public const int MaxQuote = 160;
    public const int MaxElementId = 128;
    public const int MaxReplyBytes = ReaderLocationCodec.MaxBytes;
    public const int MaxOffsets = 64;
    private const int MaxDepth = 4;

    private static readonly HtmlRestorePlan Nothing = new([], null, false);

    public static HtmlCapture? ParseCapture(string? json, Uri entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using JsonDocument? document = Parse(json);
        if (document is null) return null;
        JsonElement root = document.RootElement;
        if (!HasExactly(root, "offset", "quote", "id", "fraction", "href") ||
            !Offset(root.GetProperty("offset"), out int offset) ||
            !OptionalText(root.GetProperty("quote"), MaxQuote, out string? quote) ||
            !OptionalText(root.GetProperty("id"), MaxElementId, out string? id) ||
            !Fraction(root.GetProperty("fraction"), out double fraction) ||
            root.GetProperty("href").ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(root.GetProperty("href").GetString(), UriKind.Absolute, out Uri? page) ||
            !HtmlNavigationPolicy.IsEntryDocument(page, entry))
        {
            return null;
        }
        return new HtmlCapture(offset, quote, id, fraction);
    }

    public static HtmlScroll? ParseScroll(string? json)
    {
        using JsonDocument? document = Parse(json);
        if (document is null) return null;
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 3) return null;
        double[] values = new double[3];
        for (int index = 0; index < 3; index++)
        {
            JsonElement item = root[index];
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double value) ||
                !double.IsFinite(value) || value < 0)
            {
                return null;
            }
            values[index] = value;
        }
        return new HtmlScroll(values[0], values[1], values[2]);
    }

    public static int? ParsePending(string? json)
    {
        using JsonDocument? document = Parse(json);
        return document is not null && Offset(document.RootElement, out int pending) ? pending : null;
    }

    public static HtmlFindResult? ParseFind(string? json)
    {
        using JsonDocument? document = Parse(json);
        if (document is null) return null;
        JsonElement root = document.RootElement;
        if (!HasExactly(root, "exact", "element", "all")) return null;
        JsonElement exact = root.GetProperty("exact");
        if (exact.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !Offsets(root.GetProperty("element"), out List<int> inElement) ||
            !Offsets(root.GetProperty("all"), out List<int> anywhere))
        {
            return null;
        }
        return new HtmlFindResult(exact.GetBoolean(), inElement, anywhere);
    }

    // Reflow and font rounding move values by fractions of a pixel.
    public static bool Moved(HtmlScroll? last, HtmlScroll now) =>
        last is null ||
        Math.Abs(last.Y - now.Y) > 1 ||
        Math.Abs(last.Height - now.Height) > 1 ||
        Math.Abs(last.ViewportHeight - now.ViewportHeight) > 1;

    public static ReaderLocation Capture(string contentSha256, string documentPath, HtmlCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return new ReaderLocation(
            GuideFormat.Html,
            ReaderLocationCodec.CurrentVersion,
            contentSha256.ToLowerInvariant(),
            new HtmlPosition(documentPath, capture.ElementId, capture.Quote, capture.Offset, capture.Fraction),
            capture.Fraction);
    }

    // A typed location gets the same checks as stored JSON.
    public static LocationDecodeResult Decode(ReaderLocation location, string contentSha256, string documentPath)
    {
        ArgumentNullException.ThrowIfNull(location);
        string json;
        try
        {
            json = ReaderLocationCodec.Serialize(location);
        }
        catch (InvalidDataException)
        {
            return new LocationDecodeResult(LocationDecodeStatus.Invalid, null);
        }
        return ReaderLocationCodec.Deserialize(
            json, GuideFormat.Html, contentSha256.ToLowerInvariant(), documentPath);
    }

    public static HtmlRestorePlan PlanRestore(LocationDecodeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Location?.Payload is not HtmlPosition position ||
            result.Status is not (LocationDecodeStatus.Valid or LocationDecodeStatus.ContentChanged))
        {
            return Nothing;
        }
        bool changed = result.Status == LocationDecodeStatus.ContentChanged;
        bool quoted = !string.IsNullOrEmpty(position.TextQuote);
        List<HtmlRestoreStep> steps = [];
        if (quoted && !changed) steps.Add(HtmlRestoreStep.Exact);
        if (quoted) steps.Add(HtmlRestoreStep.Context);
        steps.Add(HtmlRestoreStep.Fraction);
        return new HtmlRestorePlan(steps, position, changed);
    }

    public static string FindArgs(HtmlPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return JsonSerializer.Serialize(new { offset = position.TextOffset, quote = position.TextQuote, id = position.ElementId });
    }

    public static HtmlRestoreTarget? Resolve(HtmlRestorePlan plan, HtmlFindResult? find)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Position is not HtmlPosition position || (plan.NeedsFind && find is null))
        {
            return null;
        }
        foreach (HtmlRestoreStep step in plan.Steps)
        {
            switch (step)
            {
                case HtmlRestoreStep.Exact when find!.ExactMatch:
                    return new HtmlRestoreTarget(step, position.TextOffset, position.ScrollFraction);
                case HtmlRestoreStep.Context:
                    int? near = Nearest(find!.InElement, position.TextOffset) ??
                                Nearest(find.Anywhere, position.TextOffset);
                    if (near is int offset)
                    {
                        return new HtmlRestoreTarget(step, offset, position.ScrollFraction);
                    }
                    break;
                case HtmlRestoreStep.Fraction:
                    return new HtmlRestoreTarget(step, 0, position.ScrollFraction);
            }
        }
        return null;
    }

    public static RestoreOutcome Outcome(HtmlRestorePlan plan, HtmlRestoreTarget? target)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (target is null || !plan.Steps.Contains(target.Step))
        {
            return new RestoreOutcome(RestoreKind.Unavailable, UnavailableReason);
        }
        return target.Step switch
        {
            HtmlRestoreStep.Exact => new RestoreOutcome(RestoreKind.Exact),
            HtmlRestoreStep.Context when !plan.ContentChanged => new RestoreOutcome(RestoreKind.Context),
            // A fraction of unchanged bytes is approximate, but "the guide
            // changed" would be false.
            _ => plan.ContentChanged
                ? new RestoreOutcome(RestoreKind.Approximate, ChangedReason)
                : new RestoreOutcome(RestoreKind.Approximate)
        };
    }

    // A tie is no match: separators repeat, and either copy would be a guess.
    private static int? Nearest(IReadOnlyList<int> offsets, int saved)
    {
        int? best = null;
        long bestDistance = long.MaxValue;
        bool tie = false;
        foreach (int offset in offsets.Distinct())
        {
            long distance = Math.Abs((long)offset - saved);
            if (distance < bestDistance)
            {
                (best, bestDistance, tie) = (offset, distance, false);
            }
            else if (distance == bestDistance)
            {
                tie = true;
            }
        }
        return tie ? null : best;
    }

    private static JsonDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxReplyBytes)
        {
            return null;
        }
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasExactly(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            {
                return false;
            }
        }
        return seen.Count == names.Length;
    }

    private static bool Offset(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) && value >= 0;
    }

    private static bool Offsets(JsonElement element, out List<int> values)
    {
        values = [];
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaxOffsets) return false;
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (!Offset(item, out int value)) return false;
            values.Add(value);
        }
        return true;
    }

    private static bool OptionalText(JsonElement element, int limit, out string? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString()!;
        return value.Length <= limit && !value.Contains('\0');
    }

    private static bool Fraction(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value) &&
               double.IsFinite(value) && value is >= 0 and <= 1;
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Html/HtmlLocationRules.cs src/DesktopGuides.Core/Html/HtmlNavigationPolicy.cs
git commit -m "feat(p1): T09.3 HTML location rules

- Parse capture, scroll, find and pending replies as untrusted JSON
- Build and decode HTML locations through the T12.1 codec
- Plan exact, context and fraction steps; nearest match, ties fall through
- Map the chosen step to a restore outcome

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Run the tests to verify they pass**

Push and run CI. Expected: `core-tests` passes; its log shows the Core.Tests
`Passed!` count up by the new tests, with no failures. Cancel the run once
`core-tests` has passed (`HtmlNavigationPolicyTests` still pass: only the
name changed).

---

### Task 2: Unimported links and position counts in Core

**Files:**
- Modify: `src/DesktopGuides.Core/Html/HtmlNavigationPolicy.cs`
- Modify: `src/DesktopGuides.Core/Html/HtmlRequestPolicy.cs` (`HtmlDenyReason`)
- Modify: `src/DesktopGuides.Core/Html/HtmlSessionDiagnostics.cs`
- Test: `tests/DesktopGuides.Core.Tests/HtmlNavigationPolicyTests.cs`
- Test: `tests/DesktopGuides.Core.Tests/HtmlSessionDiagnosticsTests.cs`

**Interfaces:**
- Consumes: `HtmlNavigationPolicy.IsEntryDocument` (Task 1), `RestoreKind`.
- Produces:
  - `HtmlNavigationKind.Unavailable` (appended after `Deny`).
  - `HtmlDenyReason.UnimportedPage` (appended after `FileMissing`).
  - `HtmlSessionDiagnostics.RecordRejectedCapture()` and
    `RecordRestore(RestoreKind kind)`; `ToJson` gains
    `"rejectedCaptures": <int>` and `"restores": {"<Kind>": <int>, ...}`
    (only kinds seen, ordinal key order), after `denied`.

- [ ] **Step 1: Write the failing tests**

In `tests/DesktopGuides.Core.Tests/HtmlNavigationPolicyTests.cs`, remove these
two rows from `EverythingElseIsDenied` (a person's click there is now
`Unavailable`; the non-user form is covered below):

```csharp
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/other.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html?page=2")]
```

and add, before `EverythingElseIsDenied`:

```csharp
    [Theory]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/other.html")]
    [InlineData("https://G3F2A9C0E4B7D1A65F08C2E9D3B4A7C10.guide.invalid/part2.html#top")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html?page=2")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/sub/guide.html")]
    [InlineData("http://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/other.html")]
    public void AClickToAnotherPageOfThisGuideIsUnavailable(string uri) =>
        Assert.Equal(HtmlNavigationKind.Unavailable, Kind(uri));

    [Theory]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/other.html")]
    [InlineData("https://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/guide.html?page=2")]
    public void ANonUserNavigationToAnotherPageIsDenied(string uri) =>
        Assert.Equal(HtmlNavigationKind.Deny, Kind(uri, user: false));

    [Theory]
    [InlineData("https://g00000000000000000000000000000001.guide.invalid/other.html")]
    [InlineData("https://user@g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/other.html")]
    [InlineData("ftp://g3f2a9c0e4b7d1a65f08c2e9d3b4a7c10.guide.invalid/other.html")]
    public void AnotherGuideUserInfoOrSchemeIsStillDenied(string uri) =>
        Assert.Equal(HtmlNavigationKind.Deny, Kind(uri));

    [Fact]
    public void AClickToTheEntryItselfIsNotUnavailable() =>
        Assert.Equal(HtmlNavigationKind.Deny, Kind(Entry.AbsoluteUri));
```

In `tests/DesktopGuides.Core.Tests/HtmlSessionDiagnosticsTests.cs`, add:

```csharp
    [Fact]
    public void CountsRejectedCapturesAndRestoresByKind()
    {
        HtmlSessionDiagnostics diagnostics = new();
        diagnostics.RecordRejectedCapture();
        diagnostics.RecordRejectedCapture();
        diagnostics.RecordRestore(RestoreKind.Exact);
        diagnostics.RecordRestore(RestoreKind.Exact);
        diagnostics.RecordRestore(RestoreKind.Approximate);
        diagnostics.RecordDenied(HtmlDenyReason.UnimportedPage, "Navigation");

        using JsonDocument json = JsonDocument.Parse(diagnostics.ToJson(Guid.NewGuid()));

        Assert.Equal(2, json.RootElement.GetProperty("rejectedCaptures").GetInt32());
        JsonElement restores = json.RootElement.GetProperty("restores");
        Assert.Equal(["Approximate", "Exact"], restores.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, restores.GetProperty("Exact").GetInt32());
        Assert.Equal(1, restores.GetProperty("Approximate").GetInt32());
        JsonElement denied = json.RootElement.GetProperty("denied")[0];
        Assert.Equal("UnimportedPage", denied.GetProperty("reason").GetString());
        Assert.Equal("Navigation", denied.GetProperty("context").GetString());
    }

    [Fact]
    public void ANewSessionHasNoPositionCounts()
    {
        using JsonDocument json = JsonDocument.Parse(new HtmlSessionDiagnostics().ToJson(Guid.NewGuid()));
        Assert.Equal(0, json.RootElement.GetProperty("rejectedCaptures").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("restores").EnumerateObject());
    }
```

and add `using DesktopGuides.Core.Reading;` to its usings.

- [ ] **Step 2: Commit the tests**

```bash
git add tests/DesktopGuides.Core.Tests/HtmlNavigationPolicyTests.cs tests/DesktopGuides.Core.Tests/HtmlSessionDiagnosticsTests.cs
git commit -m "test(p1): T09.3 unimported links and position counts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 3: Run the tests to verify they fail**

Push and run CI. Expected: `core-tests` fails to build with
`CS0117: 'HtmlNavigationKind' does not contain a definition for 'Unavailable'`,
and the same for `UnimportedPage`, `RecordRejectedCapture` and `RecordRestore`.
Cancel the run once `core-tests` has failed.

- [ ] **Step 4: Implement**

`src/DesktopGuides.Core/Html/HtmlNavigationPolicy.cs`: append the kind and add
the branch after the `External` check:

```csharp
public enum HtmlNavigationKind { Entry, SameDocument, External, Deny, Unavailable }
```

```csharp
        // Only a person's click may raise the bar; redirects and refreshes can't.
        if (target.Scheme is "http" or "https" && userInitiated &&
            target.UserInfo.Length == 0 && !GuideWebOrigin.IsGuideHost(target.Host))
        {
            return new(HtmlNavigationKind.External, target);
        }
        // Another page of this guide's origin wasn't imported. A person is
        // told; anything else is cancelled silently.
        if (target.Scheme is "http" or "https" && userInitiated && target.UserInfo.Length == 0 &&
            string.Equals(target.Host, entry.Host, StringComparison.OrdinalIgnoreCase))
        {
            return new(HtmlNavigationKind.Unavailable);
        }
        return Denied;
```

`src/DesktopGuides.Core/Html/HtmlRequestPolicy.cs`:

```csharp
public enum HtmlDenyReason
{
    Method, CrossGuide, External, OtherScheme, Malformed, NotInManifest, UnsupportedType, HashMismatch, FileMissing,
    UnimportedPage
}
```

`src/DesktopGuides.Core/Html/HtmlSessionDiagnostics.cs`: update the summary
to "Holds request paths, reasons and counts, never external URLs or guide
text.", add `using DesktopGuides.Core.Reading;`, and:

```csharp
    private readonly SortedDictionary<string, int> restores = new(StringComparer.Ordinal);
    private int rejectedCaptures;

    public void RecordRejectedCapture()
    {
        lock (gate) rejectedCaptures++;
    }

    public void RecordRestore(RestoreKind kind)
    {
        lock (gate) restores[kind.ToString()] = restores.GetValueOrDefault(kind.ToString()) + 1;
    }
```

and in `ToJson`, after `denied = ...ToArray()`:

```csharp
                rejectedCaptures,
                restores = new Dictionary<string, int>(restores)
```

(`SortedDictionary` keeps ordinal key order; `JsonSerializer` writes a
`Dictionary<string, int>` as an object in insertion order.)

- [ ] **Step 5: Commit**

```bash
git add src/DesktopGuides.Core/Html/HtmlNavigationPolicy.cs src/DesktopGuides.Core/Html/HtmlRequestPolicy.cs src/DesktopGuides.Core/Html/HtmlSessionDiagnostics.cs
git commit -m "feat(p1): T09.3 unimported links and position counts

- A person's click to another page of the guide's origin is Unavailable
- New UnimportedPage deny reason for those navigations
- Diagnostics count rejected captures and restore outcomes by kind

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Run the tests to verify they pass**

Push and run CI. Expected: `core-tests` passes with no failures. Let the
rest of the run finish: Production compiles against the new enum members
(`HtmlReaderSession`'s `switch` on `HtmlNavigationKind` must still compile;
if it has no `default`, the new kind falls into its deny path until Task 6).
Expected: the whole run is green, with the existing `html-reader` passes
unchanged.

---

### Task 3: Tracking and the early check

This task proves the approach on CI before anything else depends on it:
`ExecuteScriptAsync` must run the host scripts under T07.3's settings and
`script-src 'none'` CSP, and the smoke must read the page's top line through
UI Automation's TextPattern. **If Step 9's run shows either one failing
(position file never written, or `Get-PageTopLine` throwing), stop and report
to the user for a revised approach; do not work around it.**

**Files:**
- Create: `tests/fixtures/p1/html-long/guide.html`, `tests/fixtures/p1/html-long/images/map.png`, `images/route.png`
- Create: `tests/fixtures/p1/html-long-changed/guide.html`, `tests/fixtures/p1/html-long-changed/images/map.png`, `images/route.png`
- Modify: `tools/p1/DesktopGuides.ShellSeed/Program.cs` (`seed-html-position`)
- Create: `src/DesktopGuides.Production/HtmlPositionScripts.cs`
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs`
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`html-position` mode, hoisted page helpers)
- Modify: `tools/p1/windows_shell_install.ps1` (`Run-HtmlPositionScenarios`, `-AppDataRoot`/`-AppCacheRoot`)

**Interfaces:**
- Consumes: `HtmlLocationRules.ParseScroll/ParseCapture/Moved/Capture`,
  `HtmlNavigationPolicy.IsEntryDocument` (Task 1);
  `HtmlSessionDiagnostics.RecordRejectedCapture` (Task 2).
- Produces:
  - `HtmlPositionScripts.ReadScroll`, `.Capture`, `.PendingImages` (string
    constants) and `.Find(string argsJson)`, `.ScrollToOffset(int offset)`,
    `.ScrollToFraction(double fraction)` (string builders). Replies:
    `ReadScroll` → `[scrollY, scrollHeight, innerHeight]`; `Capture` →
    `{offset, quote, id, fraction, href}`; `Find` → `{exact, element, all}`;
    the other three → the number of images still loading, or `null` when the
    offset has no text.
  - In `HtmlReaderSession`: `Task<string?> RunScriptAsync(string script)`
    (origin check, 5 s timeout, `null` on any failure);
    `Task<HtmlCapture?> CaptureAsync()`; `void OnTrackerTick(...)`;
    `void RaiseLocationChanged()`; fields `HtmlCapture? current`,
    `HtmlScroll? lastScroll`, `bool ticking`, `DispatcherQueueTimer tracker`,
    `bool positionForTest`; `void WritePositionForTest()` writing
    `{"locator": <codec JSON string or null>, "kind": null, "step": null, "reason": null}`
    (Task 5 fills the last three).
  - Seeder: `seed-html-position <data root> <fixture root>` prints
    `{"guideLong": "<N>", "guideChanged": "<N>"}`.
  - Smoke: mode `html-position`, params `-AppDataRoot`, `-AppCacheRoot`;
    functions `Read-HtmlPosition`, `Wait-HtmlPosition`, `Get-PageTopLine`,
    `Wait-TopMark`; report fields `sessionsOpened` and `unimportedClicks`.
  - Installer: `Run-HtmlPositionScenarios`, which checks every session's
    served set and the `UnimportedPage`/`Navigation` total against those two
    report fields.

- [ ] **Step 1: Create the fixtures**

Run from the repo root (it writes both guides and copies the canary image).
Neither image has a `width` or `height`; both are scaled to the page width,
so a late load moves everything below it. The first is eager, near the top.
The second is `loading="lazy"` inside the `<pre>`, ten lines above the
target: it starts loading only when the page is scrolled near MARK-0420,
which is what makes a restore meet an image that is still loading
(`NavigationCompleted` waits for eager images, so the first one never is).
It is a separate file, `images/route.png`: the same URL as the eager image
would come from Chromium's memory cache and never be pending.

```bash
python3 - <<'PY'
import os, shutil
def page(changed):
    lines = []
    for n in range(1, 3001):
        text = f"MARK-{n:04d}  Step {n} of the long walkthrough; keep left at the fork."
        if n == 420:
            text = f'<span id="mark-0420">MARK-0420</span>  Step 420 of the long walkthrough; the target line.'
        if n == 425:
            text = 'MARK-0425  The story continues in <a href="part2.html">Part 2 of this guide</a>.'
        if n == 426:
            text = 'MARK-0426  Maps are also on <a href="https://example.com/desktop-guides-position">the website</a>.'
        if n % 50 == 0:
            lines.append("=" * 60)
        if n == 410:
            # Lazy, so it loads only once the reader is near the target:
            # NavigationCompleted waits for eager images, never for this one.
            # Its own file: a copy of map.png would come from memory.
            lines.append('<img src="images/route.png" alt="Route map" loading="lazy" style="display:block;width:100%">')
        lines.append(text)
    added = "".join(
        f"<p>Added in version 2: note {k} about the route, long enough to wrap at narrow widths "
        f"so the inserted text changes every offset below it.</p>\n" for k in range(1, 9)) if changed else ""
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Long Web Guide</title>
</head>
<body>
<h1>Long Web Guide</h1>
<form name="querySelectorAll"></form>
<form name="body"><input type="hidden" name="parentNode"><input type="hidden" name="nodeName"><input type="hidden" name="id"><p>Form-wrapped introduction text.</p></form>
<img name="createTreeWalker" alt="">
<img name="getElementById" alt="">
<p><a href="#mark-0420">Jump to MARK-0420</a></p>
<h2 id="contents">Contents</h2>
<p>The walkthrough below is one long preformatted block, as in text guides saved as HTML.</p>
<h2>Map</h2>
<p><img src="images/map.png" alt="Map" style="display:block;width:100%"></p>
{added}<h2 id="walkthrough">Walkthrough</h2>
<pre>
{chr(10).join(lines)}
</pre>
</body>
</html>
"""
for name, changed in (("html-long", False), ("html-long-changed", True)):
    root = os.path.join("tests", "fixtures", "p1", name)
    os.makedirs(os.path.join(root, "images"), exist_ok=True)
    with open(os.path.join(root, "guide.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(page(changed))
    for image in ("map.png", "route.png"):
        shutil.copyfile(os.path.join("tests", "fixtures", "p1", "html-canary", "a", "images", "a.png"),
                        os.path.join(root, "images", image))
PY
grep -c '^MARK-' tests/fixtures/p1/html-long/guide.html
grep -c 'Added in version 2' tests/fixtures/p1/html-long-changed/guide.html
```

Expected: `2999` (MARK-0420 starts with `<span`) and `8`.

Line 425 links to `part2.html`, which isn't imported, and line 426 to a
website: Task 6 clicks both while MARK-0420 is on top.

- [ ] **Step 2: Add `seed-html-position` to the seeder**

In `tools/p1/DesktopGuides.ShellSeed/Program.cs`, after the `seed-html-reader`
block, add:

```csharp
if (args.Length == 3 && args[0] == "seed-html-position")
{
    ManagedPathResolver positionPaths = new(args[1]);
    await using SqliteLibraryRepository positionRepository = new(positionPaths);
    await positionRepository.InitializeAsync();
    if ((await positionRepository.ListGamesAsync()).Count != 0)
    {
        throw new InvalidOperationException("The HTML position seed needs an empty library.");
    }
    string fixtures = Path.Combine(Path.GetFullPath(args[2]), "p1");
    Game positionGame = await positionRepository.AddGameAsync("Web Position Game", null, null);
    GuideImportValidator positionValidator = new();
    GuideImportPublisher positionPublisher = new(positionRepository, positionPaths);
    async Task<Guid> PublishLongAsync(string folder, string title)
    {
        ImportInspection inspection = await positionValidator.InspectAsync(
            Path.Combine(fixtures, folder, "guide.html"), CancellationToken.None);
        if (inspection is not ImportReady ready)
        {
            throw new InvalidOperationException($"The {folder} guide failed the import preview: {inspection}.");
        }
        return await positionPublisher.PublishAsync(
            ready.Manifest, positionGame.Id, title, false, null, CancellationToken.None);
    }
    Guid guideLong = await PublishLongAsync("html-long", "Long Web Guide");
    Guid guideChanged = await PublishLongAsync("html-long-changed", "Changed Long Web Guide");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        guideLong = guideLong.ToString("N"),
        guideChanged = guideChanged.ToString("N")
    }));
    return 0;
}
```

and add `seed-html-position <data root> <fixture root>` to the usage string,
after `seed-html-reader`.

- [ ] **Step 3: Add the host scripts**

Create `src/DesktopGuides.Production/HtmlPositionScripts.cs`:

```csharp
using System.Text.Json;

namespace DesktopGuides.Production;

// Fixed scripts the HTML session runs through ExecuteScriptAsync. They only
// measure and scroll; every decision is Core's (HtmlLocationRules). Page
// scripts stay off, but named elements can still shadow document and form
// properties, so DOM members are reached through their prototypes. Host
// values enter only as JsonSerializer literals.
internal static class HtmlPositionScripts
{
    // The text walk: the body's text nodes in document order, skipping
    // script, style, template and noscript. Offsets count UTF-16 code units.
    private const string Prelude = """
        'use strict';
        const getter = (proto, name) => Object.getOwnPropertyDescriptor(proto, name).get;
        const bodyOf = getter(Document.prototype, 'body');
        const scrollingOf = getter(Document.prototype, 'scrollingElement');
        const imagesOf = getter(Document.prototype, 'images');
        const parentOf = getter(Node.prototype, 'parentNode');
        const nameOf = getter(Node.prototype, 'nodeName');
        const dataOf = getter(CharacterData.prototype, 'data');
        const idOf = getter(Element.prototype, 'id');
        const heightOf = getter(Element.prototype, 'scrollHeight');
        const completeOf = getter(HTMLImageElement.prototype, 'complete');
        const contains = Node.prototype.contains;
        const elementRect = Element.prototype.getBoundingClientRect;
        const skipped = new Set(['SCRIPT', 'STYLE', 'TEMPLATE', 'NOSCRIPT']);
        const walk = () => {
          const body = bodyOf.call(document);
          const nodes = [], starts = [];
          let length = 0;
          if (!body) return { nodes, starts, length };
          const walker = Document.prototype.createTreeWalker.call(document, body, NodeFilter.SHOW_TEXT);
          for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            let hidden = false;
            for (let p = parentOf.call(node); p && p !== body; p = parentOf.call(p)) {
              if (skipped.has(String(nameOf.call(p)).toUpperCase())) { hidden = true; break; }
            }
            if (hidden) continue;
            nodes.push(node);
            starts.push(length);
            length += dataOf.call(node).length;
          }
          return { nodes, starts, length };
        };
        const locate = (w, offset) => {
          let lo = 0, hi = w.nodes.length - 1;
          while (lo < hi) {
            const mid = (lo + hi + 1) >> 1;
            if (w.starts[mid] <= offset) lo = mid; else hi = mid - 1;
          }
          return lo;
        };
        const range = Document.prototype.createRange.call(document);
        // A line break or a zero-width character has no box of its own; the
        // next character with a box, up to 64 on, stands in for it.
        const boxAt = (w, offset) => {
          for (let o = offset; o < Math.min(w.length, offset + 64); o++) {
            const i = locate(w, o), node = w.nodes[i], local = o - w.starts[i];
            const ch = dataOf.call(node)[local];
            if (ch === '\n' || ch === '\r') continue;
            range.setStart(node, local);
            range.setEnd(node, local + 1);
            const r = range.getBoundingClientRect();
            if (r.width > 0 && r.height > 0) return { offset: o, top: r.top, bottom: r.bottom };
          }
          return null;
        };
        const slice = (w, offset, count) => {
          const first = locate(w, offset);
          let out = dataOf.call(w.nodes[first]).substring(offset - w.starts[first]);
          for (let i = first + 1; i < w.nodes.length && out.length < count; i++) out += dataOf.call(w.nodes[i]);
          return out.substring(0, count);
        };
        const fraction = () => {
          const max = heightOf.call(scrollingOf.call(document)) - innerHeight;
          return max > 0 ? Math.min(1, Math.max(0, scrollY / max)) : 0;
        };
        // Only an image above the viewport's bottom can move the top line;
        // lazy images further down may never load and don't matter.
        const pending = () => {
          const images = imagesOf.call(document);
          let count = 0;
          for (let i = 0; i < images.length; i++) {
            const image = images[i];
            if (!completeOf.call(image) && elementRect.call(image).top < innerHeight) count++;
          }
          return count;
        };
        const scrollToBox = (box) => { scrollTo(0, scrollY + box.top); return pending(); };
        """;

    public const string ReadScroll =
        "(() => {" + Prelude + "return [scrollY, heightOf.call(scrollingOf.call(document)), innerHeight]; })()";

    // The first character whose box ends more than 1 px below the viewport
    // top. Boxes go down the page in walk order, so a binary search finds it.
    public const string Capture = "(() => {" + Prelude + """
        const w = walk();
        const below = (o) => { const b = boxAt(w, o); return b !== null && b.bottom > 1; };
        let lo = 0, hi = w.length;
        while (lo < hi) {
          const mid = (lo + hi) >> 1;
          if (below(mid)) hi = mid; else lo = mid + 1;
        }
        const box = lo < w.length ? boxAt(w, lo) : null;
        if (box === null) {
          return { offset: 0, quote: null, id: null, fraction: fraction(), href: location.href };
        }
        let id = null;
        for (let p = parentOf.call(w.nodes[locate(w, box.offset)]); p && p instanceof Element; p = parentOf.call(p)) {
          const value = idOf.call(p);
          if (value) { id = value.length > 128 ? null : value; break; }
        }
        return { offset: box.offset, quote: slice(w, box.offset, 160), id, fraction: fraction(), href: location.href };
        })()
        """;

    public const string PendingImages = "(() => {" + Prelude + "return pending(); })()";

    // Whether the quote is at the saved offset, and the quote's occurrences
    // nearest it: inside the id's element and in the whole walk.
    public static string Find(string argsJson) => "(() => {" + Prelude + "const args = " + argsJson + ";" + """
        const w = walk();
        let text = '';
        for (const node of w.nodes) text += dataOf.call(node);
        const quote = args.quote;
        const nearest = (list) => list
          .sort((a, b) => Math.abs(a - args.offset) - Math.abs(b - args.offset))
          .slice(0, 64);
        const all = [];
        for (let at = text.indexOf(quote); at >= 0 && quote.length > 0; at = text.indexOf(quote, at + 1)) all.push(at);
        let element = [];
        const target = args.id ? Document.prototype.getElementById.call(document, args.id) : null;
        if (target) {
          let start = -1, end = -1;
          for (let i = 0; i < w.nodes.length; i++) {
            if (contains.call(target, w.nodes[i])) {
              if (start < 0) start = w.starts[i];
              end = w.starts[i] + dataOf.call(w.nodes[i]).length;
            }
          }
          if (start >= 0) element = all.filter((at) => at >= start && at + quote.length <= end);
        }
        return { exact: text.substr(args.offset, quote.length) === quote, element: nearest(element), all: nearest(all) };
        })()
        """;

    // Puts the character's box top at the viewport top, clamped by the
    // browser to the scroll range.
    public static string ScrollToOffset(int offset) =>
        "(() => {" + Prelude + "const offset = " + JsonSerializer.Serialize(offset) + ";" + """
        const box = boxAt(walk(), offset);
        return box === null ? null : scrollToBox(box);
        })()
        """;

    public static string ScrollToFraction(double fraction) =>
        "(() => {" + Prelude + "const f = " + JsonSerializer.Serialize(fraction) + ";" + """
        const max = heightOf.call(scrollingOf.call(document)) - innerHeight;
        scrollTo(0, max > 0 ? f * max : 0);
        return pending();
        })()
        """;
}
```

Every script reads through the prelude's getters, including `ReadScroll`,
so `<form name="documentElement">` or `<img name="scrollingElement">` can't
change a reply. A getter that throws (no body, no scrolling element) makes
`ExecuteScriptAsync` return `"null"`, which every Core parser rejects.

The scripts aren't wired to anything yet, so they can't make the smoke
below pass; the session wiring comes after the RED run (Step 7).

- [ ] **Step 4: Move the page helpers to the shared reader scope**

In `tools/p1/windows_shell_ui_smoke.ps1`, cut the four functions
`Get-PageRoots`, `Find-PageByName`, `Wait-PageName` and `Wait-PageVisible`
(with the comment above `Get-PageRoots` and the one above
`Wait-PageVisible`) out of the `elseif ($Mode -eq 'html-reader')` branch.
Paste them, unchanged except for one less level of indentation (4 spaces),
right after `function Back-ToTextGame { ... }` in the shared reader block
(`elseif ($Mode -in @('txt-reader', ...))`). `if` blocks don't create
PowerShell scopes, so `html-reader` keeps finding them; the new mode's branch
needs them defined before it runs.

Then, in the same place, add the HTML position helpers:

```powershell
        # The app's own view of the position, written only while the
        # HtmlPosition gate is open. The locator is the T12.1 codec JSON.
        function Read-HtmlPosition {
            $path = Join-Path $AppCacheRoot "diagnostics\html-position-$ProcessId.json"
            if (-not (Test-Path -LiteralPath $path)) { return $null }
            try {
                $file = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
            }
            catch {
                return $null
            }
            $position = [ordered]@{
                locator = $file.locator; offset = $null; quote = $null
                kind = $file.kind; step = $file.step; reason = $file.reason
            }
            if ($file.locator) {
                $payload = ($file.locator | ConvertFrom-Json).payload
                $position.offset = [int] $payload.textOffset
                $position.quote = [string] $payload.textQuote
            }
            return [pscustomobject] $position
        }

        function Wait-HtmlPosition([scriptblock] $until, [string] $what) {
            $deadline = (Get-Date).AddSeconds(10)
            $position = $null
            do {
                $position = Read-HtmlPosition
                if ($position -and (& $until $position)) { return $position }
                Start-Sleep -Milliseconds 250
            } while ((Get-Date) -lt $deadline)
            [void](Save-WindowScreenshot 'html-position-timeout')
            throw "The HTML position never showed $what. Last: offset=$($position.offset) kind=$($position.kind) step=$($position.step)."
        }

        # The line at the page's top-left, read through Chromium's own
        # UI Automation text, independently of the app's capture.
        function Get-PageTopLine {
            $documentType = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Document)
            foreach ($page in Get-PageRoots) {
                try {
                    $document = if ($page.Current.ControlType -eq [System.Windows.Automation.ControlType]::Document) { $page }
                        else { $page.FindFirst($scope, $documentType) }
                    if (-not $document -or $document.Current.IsOffscreen) { continue }
                    $rect = $document.Current.BoundingRectangle
                    if ($rect.Width -lt 1 -or $rect.Height -lt 1) { continue }
                    $text = $document.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
                    $range = $text.RangeFromPoint([System.Windows.Point]::new($rect.Left + 40, $rect.Top + 3))
                    $range.ExpandToEnclosingUnit([System.Windows.Automation.Text.TextUnit]::Line)
                    return $range.GetText(200).Trim()
                }
                catch [System.Windows.Automation.ElementNotAvailableException] {
                    # The page tree was rebuilt; try the next window.
                }
            }
            return $null
        }

        # The fixture's lines start MARK-<4 digits>. "Within one line" allows
        # the neighbor on either side, for rounding at a line boundary.
        function Wait-TopMark([int] $mark, [string] $step) {
            $deadline = (Get-Date).AddSeconds(10)
            $seen = 'nothing'
            do {
                $line = Get-PageTopLine
                if ($line -match '^MARK-(\d{4})\b') {
                    $seen = "MARK-$($Matches[1])"
                    if ([Math]::Abs([int] $Matches[1] - $mark) -le 1) { return [int] $Matches[1] }
                }
                elseif ($line) {
                    $seen = 'a line without a mark'
                }
                Start-Sleep -Milliseconds 250
            } while ((Get-Date) -lt $deadline)
            [void](Save-WindowScreenshot "html-top-$step")
            throw "After $step the page's top line was $seen; expected MARK-$('{0:D4}' -f $mark) within one line."
        }
```

Add the parameters at the end of the `param(...)` block (after
`$ExpectedProviderFailure`; add the comma after its default):

```powershell
    # The app's LocalState and LocalCache folders, for HTML position gates.
    [string] $AppDataRoot = '',

    [string] $AppCacheRoot = ''
```

and append `'html-position'` to the `Mode` `ValidateSet` (after
`'pdf-reader'`) and to the shared reader block's mode list. In the same
block, extend the `$textGame` mapping:

```powershell
        $textGame = if ($Mode -in @('html-reader', 'html-runtime-missing')) { 'Web Reader Game' }
            elseif ($Mode -eq 'html-position') { 'Web Position Game' }
            elseif ($Mode -eq 'pdf-reader') { 'PDF Reader Game' }
            else { 'Text Reader Game' }
```

- [ ] **Step 5: Add the `html-position` smoke mode, step 1 only**

Before `elseif ($Mode -eq 'pdf-reader') {`, add:

```powershell
        elseif ($Mode -eq 'html-position') {
            if (-not $AppCacheRoot -or -not $AppDataRoot) {
                throw 'html-position needs -AppDataRoot and -AppCacheRoot.'
            }
            [void](Wait-Name 'LibraryHeading' 'Library')
            Select-Element $textGame
            [void](Wait-Name 'GameHeading' $textGame)
            [void](Wait-Status 'Game ready.')
            $report.sessionsOpened = 0
            $report.unimportedClicks = 0

            # position-fragment: a fragment link moves the page, the next
            # poll captures it, and the captured line is the visible one.
            Open-TextGuide 'Long Web Guide'
            $report.sessionsOpened++
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Long Web Guide')
            [void](Wait-HtmlPosition { param($p) $p.locator } 'a first capture')
            Click-Element (Wait-PageVisible 'Jump to MARK-0420')
            $target = Wait-HtmlPosition { param($p) $p.quote -like 'MARK-0420 *' } 'the MARK-0420 line'
            [void](Wait-TopMark 420 'the fragment link')
            $report.htmlPositionOffset = $target.offset
            $report.phases += 'position-fragment'

            Back-ToTextGame
        }
```

Expected after this step: on CI the smoke opens the guide and fails at the
first `Wait-HtmlPosition`, because nothing writes the file yet.

- [ ] **Step 6: Run the mode from the installer**

In `tools/p1/windows_shell_install.ps1`, give `Run-ShellSmoke` two more
parameters (after `$ExpectedGuideTitle`):

```powershell
    [string] $ExpectedGuideTitle = '',
    [string] $AppDataRoot = '',
    [string] $AppCacheRoot = '') {
```

and pass them after the `-ExpectedGuideTitle` block:

```powershell
    if ($AppDataRoot) {
        $arguments += ' -AppDataRoot "' + $AppDataRoot + '"'
    }
    if ($AppCacheRoot) {
        $arguments += ' -AppCacheRoot "' + $AppCacheRoot + '"'
    }
```

After `Run-HtmlReaderScenarios`' closing brace, add:

```powershell
function Invoke-HtmlPositionPass([string] $resultName) {
    Start-InstalledShell
    $processId = $report.launchedProcessId
    $gates = @(
        foreach ($name in @('HtmlDiagnostics', 'HtmlPosition')) {
            [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\DesktopGuides.Preview.$name.$processId")
        })
    try {
        $result = Run-ShellSmoke 'html-position' -ResultName $resultName `
            -AppDataRoot $dataRoot -AppCacheRoot (Get-HtmlCacheRoot)
        Close-InstalledShell
        return $result
    }
    finally {
        foreach ($gate in $gates) { $gate.Dispose() }
    }
}

function Assert-HtmlPositionPass([string] $pass, [string] $diagnostics, $result) {
    # Every session served the entry and the eager map, the lazy route map
    # only when the reader got near it, and nothing else; each unimported
    # click was denied once, as a navigation.
    $files = @(Get-ChildItem -LiteralPath $diagnostics -Filter 'html-session-*.json' -ErrorAction SilentlyContinue)
    if ($files.Count -ne [int] $result.sessionsOpened) {
        throw "The $pass pass wrote $($files.Count) HTML session diagnostics; the smoke opened $($result.sessionsOpened)."
    }
    $unimported = 0
    $sessions = @()
    foreach ($file in $files) {
        $session = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $served = @($session.served) -join ','
        if ($served -cne 'guide.html,images/map.png' -and $served -cne 'guide.html,images/map.png,images/route.png') {
            throw "Guide $($session.guideId) served '$served' in the $pass pass."
        }
        foreach ($deny in @($session.denied)) {
            if ($deny.reason -eq 'UnimportedPage' -and $deny.context -eq 'Navigation') {
                $unimported += [int] $deny.count
            }
        }
        $sessions += $session
    }
    if ($unimported -ne [int] $result.unimportedClicks) {
        throw "The $pass pass denied $unimported unimported pages; the smoke clicked $($result.unimportedClicks)."
    }
    return $sessions
}

function Run-HtmlPositionScenarios {
    # TR09.1-TR09.2: capture, resize, restore and unimported links on a
    # long <pre> guide. Light then dark.
    $fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\tests\fixtures')).Path
    $ids = Invoke-ShellSeed @('seed-html-position', $dataRoot, $fixtureRoot) | ConvertFrom-Json
    $diagnostics = Join-Path (Get-HtmlCacheRoot) 'diagnostics'
    $report.htmlPosition = [ordered]@{ guideLong = $ids.guideLong; guideChanged = $ids.guideChanged }
    $originalTheme = Get-AppThemePreference
    try {
        foreach ($pass in @(
            @{ name = 'html-position-light'; light = $true },
            @{ name = 'html-position-dark'; light = $false })) {
            Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath (Join-Path $dataRoot 'test') -Recurse -Force -ErrorAction SilentlyContinue
            Set-AppThemePreference $pass.light
            $result = Invoke-HtmlPositionPass $pass.name
            $report.htmlPosition[$pass.name] = $result
            Save-HtmlDiagnostics $pass.name (Get-HtmlCacheRoot)
            $report.htmlPosition["$($pass.name)-sessions"] = Assert-HtmlPositionPass $pass.name $diagnostics $result
        }
    }
    finally {
        Restore-AppThemePreference $originalTheme
        Remove-Item -LiteralPath (Join-Path $dataRoot 'test') -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $diagnostics -Recurse -Force -ErrorAction SilentlyContinue
    }
}
```

The `finally` deletes the position files, the one place fixture text is
written, after `Save-HtmlDiagnostics` has copied them into the evidence.

In the scenario order, after `Run-HtmlReaderScenarios`, add:

```powershell
    Get-ChildItem -LiteralPath $dataRoot -Force | Remove-Item -Recurse -Force
    Run-HtmlPositionScenarios
```

- [ ] **Step 7: Check ASCII, commit, and run CI (RED)**

```bash
LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/*.ps1
git add tests/fixtures/p1/html-long tests/fixtures/p1/html-long-changed \
  tools/p1/DesktopGuides.ShellSeed/Program.cs \
  src/DesktopGuides.Production/HtmlPositionScripts.cs \
  tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): T09.3 html-position smoke, fragment step

- html-long and html-long-changed fixtures: a 3000-line <pre> with an
  id target, unsized eager and lazy images and clobbering named elements
- seed-html-position seeds both guides in Web Position Game
- Fixed host scripts for scroll, capture, find and scroll-to
- html-position mode: the fragment link's line is captured and on top
- Page helpers move to the shared reader scope

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Expected: the grep prints nothing. Push and run CI. Expected: `core-tests`
green; `production-shell-ui` fails in `html-position-light` with
"The HTML position never showed a first capture." Any earlier failure
(seed, import preview, `Wait-PageName 'Long Web Guide'`) is a fixture or
seed bug: fix it before Step 8.

- [ ] **Step 8: Track and capture in the session**

In `src/DesktopGuides.Production/HtmlReaderSession.cs`:

1. Add `using System.Text.Json;` and `using Microsoft.UI.Dispatching;`.
2. Add fields after `private bool failed;`:

```csharp
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(5);
    private static readonly HtmlCapture Start = new(0, null, null, 0);
    private readonly bool positionForTest;
    private readonly DispatcherQueueTimer tracker;
    private HtmlCapture? current;
    private HtmlScroll? lastScroll;
    private bool ticking;
```

3. At the end of the constructor:

```csharp
        positionForTest = TestGate.IsOpen($@"Local\DesktopGuides.Preview.HtmlPosition.{Environment.ProcessId}");
        tracker = View.DispatcherQueue.CreateTimer();
        tracker.Interval = TimeSpan.FromMilliseconds(500);
        tracker.IsRepeating = true;
        tracker.Tick += OnTrackerTick;
```

4. Replace the stub event with a real one:

```csharp
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;
```

5. At the end of `OpenAsync`, after the `if (!success || !entryServed)`
   block:

```csharp
        tracker.Start();
```

6. Replace `GetLocationAsync`:

```csharp
    // A capture that fails or is rejected keeps the last good point.
    public async Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (await CaptureAsync() is HtmlCapture capture) current = capture;
        token.ThrowIfCancellationRequested();
        return HtmlLocationRules.Capture(contentSha256 ?? string.Empty, policy.Entry.RequestPath, current ?? Start);
    }
```

7. Add, before `OnWebResourceRequested`:

```csharp
    // Runs a fixed host script in the entry document only. Any failure,
    // including a page that navigated away or a renderer that died, is null.
    private async Task<string?> RunScriptAsync(string script)
    {
        if (disposed || failed || core is null) return null;
        try
        {
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out Uri? source) ||
                !HtmlNavigationPolicy.IsEntryDocument(source, policy.EntryUri))
            {
                return null;
            }
            return await core.ExecuteScriptAsync(script).AsTask().WaitAsync(ScriptTimeout);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<HtmlCapture?> CaptureAsync()
    {
        string? reply = await RunScriptAsync(HtmlPositionScripts.Capture);
        if (disposed) return null;
        HtmlCapture? capture = HtmlLocationRules.ParseCapture(reply, policy.EntryUri);
        if (capture is null) diagnostics?.RecordRejectedCapture();
        return capture;
    }

    // A cheap scroll read every tick; a capture only after a move.
    private async void OnTrackerTick(DispatcherQueueTimer sender, object args)
    {
        if (ticking || disposed) return;
        ticking = true;
        try
        {
            HtmlScroll? scroll = HtmlLocationRules.ParseScroll(await RunScriptAsync(HtmlPositionScripts.ReadScroll));
            if (scroll is null || disposed || !HtmlLocationRules.Moved(lastScroll, scroll)) return;
            HtmlCapture? capture = await CaptureAsync();
            if (capture is null || disposed) return;
            lastScroll = scroll;
            if (capture == current) return;
            current = capture;
            WritePositionForTest();
            RaiseLocationChanged();
        }
        catch (Exception)
        {
            // A tick must never take down the app.
        }
        finally
        {
            ticking = false;
        }
    }

    private void RaiseLocationChanged()
    {
        try
        {
            LocationChanged?.Invoke(this, new LocationChangedEventArgs());
        }
        catch (Exception)
        {
            // A throwing handler must not escape into the timer.
        }
    }
```

8. In `DisposeAsync`, right after `disposed = true;`:

```csharp
        tracker.Stop();
        tracker.Tick -= OnTrackerTick;
        LocationChanged = null;
```

9. After `WriteDiagnostics`, add:

```csharp
    // Test gate only: the current point as the codec writes it. Written to
    // a temporary file and moved, so the smoke never reads half a file.
    private void WritePositionForTest()
    {
        if (!positionForTest || disposed) return;
        try
        {
            string? locator = current is null ? null : ReaderLocationCodec.Serialize(
                HtmlLocationRules.Capture(contentSha256 ?? string.Empty, policy.Entry.RequestPath, current));
            string json = JsonSerializer.Serialize(new
            {
                locator,
                kind = (string?)null,
                step = (string?)null,
                reason = (string?)null
            });
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"html-position-{Environment.ProcessId}.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Test diagnostics must never affect reading.
        }
    }
```

Task 5 replaces the three `null`s with the last restore's kind, step and
reason.

`HtmlLocationRules.Capture` lower-cases the SHA-256, so the locator
round-trips through `ReaderLocationCodec.Deserialize` with the guide's hash.
The first tick always captures (`Moved(null, …)` is true), so the file
exists within about half a second of open.

- [ ] **Step 9: Commit and run CI (GREEN, the early check)**

```bash
git add src/DesktopGuides.Production/HtmlReaderSession.cs
git commit -m "feat(p1): T09.3 track and capture the HTML position

- Fixed scripts run only in the entry document, with a 5 s timeout
- A 500 ms poll reads the scroll and captures after a move
- Replies are parsed in Core; a rejected capture keeps the last point
- GetLocationAsync returns the captured position
- HtmlPosition test gate writes the current locator

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Push and run CI. Expected: the whole run is green; the report's
`htmlPosition.html-position-light.phases` and `-dark.phases` end with
`position-fragment`; `htmlPositionOffset` is the same positive number in
both passes; each pass's sessions served
`guide.html,images/map.png,images/route.png` (the jump brings the lazy map
into range).

**Stop condition.** If the light pass fails at "a first capture" with the
session code in place, or `Wait-TopMark` reports "nothing" (TextPattern
gave no line), stop and report to the user with the evidence (the
report's `error`, the screenshot, the copied `html-session-*.json` with its
`rejectedCaptures`). Don't change T07.3's settings or the CSP to get past
it.

If instead `Wait-TopMark` sees a line about 45 above MARK-0420, the lazy
image loaded after the jump and Chromium's scroll anchoring didn't hold the
line. That is browser behavior, not the app's: report it to the user with
the screenshot before changing the fixture.

---

### Task 4: Keep the point across a resize

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`html-position` step 2)
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs`

**Interfaces:**
- Consumes: Task 3's `current`, `lastScroll`, `tracker`, `RunScriptAsync`,
  `OnTrackerTick`, `GetLocationAsync`; `HtmlPositionScripts.ScrollToOffset`,
  `.ScrollToFraction`, `.ReadScroll`; `HtmlLocationRules.ParsePending`,
  `.ParseScroll`; smoke `Wait-TopMark`, `Read-HtmlPosition`,
  `Resize-ShellWindow`, `$target` from step 1.
- Produces:
  - In `HtmlReaderSession`: fields `int generation`, `bool opened`,
    `bool resizing`, `CancellationTokenSource? resizeDelay`; methods
    `void OnViewSizeChanged(object, SizeChangedEventArgs)` and
    `Task<(HtmlRestoreTarget Target, int Pending)?> ScrollToTargetAsync(HtmlRestoreTarget target)`
    (scrolls an `Exact` or `Context` target's character to the top; an
    offset without a box falls back to the target's fraction and returns
    the target with step `Fraction`; returns the images still loading, or
    `null` when nothing scrolled) and
    `Task ReapplyAsync()`. Task 5 reuses `ScrollToTargetAsync` and `generation`.
  - Smoke: report field `htmlResizeMarks` (the top mark after each width).

- [ ] **Step 1: Write the failing smoke step**

In the `html-position` branch, right after
`[void](Wait-Name 'LibraryHeading' 'Library')`, add:

```powershell
            # A known width first, so each resize below is a real change.
            Resize-ShellWindow 1500 720
```

Then, after `$report.phases += 'position-fragment'` and before
`Back-ToTextGame`, add:

```powershell
            # position-resize: the <pre> lines rewrap at each width; the same
            # line stays on top and the saved offset doesn't move.
            $report.htmlResizeMarks = @()
            foreach ($width in @(600, 1100, 1500)) {
                Resize-ShellWindow $width 720
                # Past the 300 ms settle, the re-apply and two polls.
                Start-Sleep -Milliseconds 1500
                $report.htmlResizeMarks += Wait-TopMark 420 "a resize to $width px"
                $after = Read-HtmlPosition
                if (-not $after -or $after.offset -ne $target.offset) {
                    throw "After a resize to $width px the position offset was $($after.offset); expected $($target.offset)."
                }
            }
            $report.phases += 'position-resize'
```

The fixture's lines are about 70 characters, so they wrap at 600 px and
not at 1500 px: the page height changes and, without the re-apply, Chromium
keeps `scrollY` and the top line drifts by hundreds of lines.

- [ ] **Step 2: Commit and run CI (RED)**

```bash
LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/*.ps1
git add tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(p1): T09.3 html-position resize step

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Push and run CI. Expected: `production-shell-ui` fails in
`html-position-light` with "After a resize to 600 px the page's top line
was MARK-…; expected MARK-0420 within one line." (or the offset message:
the poll captured the drifted line).

- [ ] **Step 3: Re-apply after the size settles**

In `src/DesktopGuides.Production/HtmlReaderSession.cs`:

1. Add fields after `private bool ticking;`:

```csharp
    private static readonly TimeSpan ResizeSettle = TimeSpan.FromMilliseconds(300);
    private CancellationTokenSource? resizeDelay;
    private bool opened;
    private bool resizing;
    // Bumped by every size change: a capture or baseline taken across one
    // is from a page mid-reflow and is dropped.
    private int generation;
```

2. In the constructor, after `tracker.Tick += OnTrackerTick;`:

```csharp
        View.SizeChanged += OnViewSizeChanged;
```

3. In `OpenAsync`, replace `tracker.Start();` with:

```csharp
        opened = true;
        tracker.Start();
```

4. In `GetLocationAsync`, replace
   `if (await CaptureAsync() is HtmlCapture capture) current = capture;` with:

```csharp
        // Mid-reflow, the page's top isn't the reader's point.
        if (!resizing && await CaptureAsync() is HtmlCapture capture) current = capture;
```

5. In `OnTrackerTick`, after `ticking = true;` and inside the `try`, make the
   body:

```csharp
            int started = generation;
            HtmlScroll? scroll = HtmlLocationRules.ParseScroll(await RunScriptAsync(HtmlPositionScripts.ReadScroll));
            if (scroll is null || disposed || started != generation ||
                !HtmlLocationRules.Moved(lastScroll, scroll))
            {
                return;
            }
            HtmlCapture? capture = await CaptureAsync();
            if (capture is null || disposed || started != generation) return;
            lastScroll = scroll;
            if (capture == current) return;
            current = capture;
            WritePositionForTest();
            RaiseLocationChanged();
```

6. Add after `OnTrackerTick`:

```csharp
    // Tracking pauses while the size changes; 300 ms after the last change
    // the current point goes back to the top and the scroll it leaves is the
    // new baseline. Chromium's own scroll change during the reflow is never
    // taken as the reader's move.
    private async void OnViewSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!opened || disposed) return;
        generation++;
        resizing = true;
        tracker.Stop();
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        CancellationTokenSource delay = new();
        resizeDelay = delay;
        try
        {
            await Task.Delay(ResizeSettle, delay.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            await ReapplyAsync();
        }
        catch (Exception)
        {
            // A failed re-apply leaves the page where the reflow put it.
        }
        finally
        {
            if (resizeDelay == delay && !disposed)
            {
                resizeDelay = null;
                delay.Dispose();
                resizing = false;
                tracker.Start();
            }
        }
    }

    private async Task ReapplyAsync()
    {
        int started = generation;
        if (current is HtmlCapture point)
        {
            // A page with no text has only its fraction.
            HtmlRestoreStep step = point.Quote is null ? HtmlRestoreStep.Fraction : HtmlRestoreStep.Exact;
            await ScrollToTargetAsync(new HtmlRestoreTarget(step, point.Offset, point.Fraction));
        }
        HtmlScroll? scroll = HtmlLocationRules.ParseScroll(await RunScriptAsync(HtmlPositionScripts.ReadScroll));
        if (scroll is not null && started == generation && !disposed) lastScroll = scroll;
    }

    // Puts the target character's box at the viewport top. An offset whose
    // text has no box (hidden since capture) falls back to the fraction, and
    // the returned target says so. Null when no script replied.
    private async Task<(HtmlRestoreTarget Target, int Pending)?> ScrollToTargetAsync(HtmlRestoreTarget target)
    {
        if (target.Step != HtmlRestoreStep.Fraction &&
            HtmlLocationRules.ParsePending(await RunScriptAsync(HtmlPositionScripts.ScrollToOffset(target.Offset))) is int pending)
        {
            return (target, pending);
        }
        HtmlRestoreTarget fraction = target with { Step = HtmlRestoreStep.Fraction };
        return HtmlLocationRules.ParsePending(await RunScriptAsync(HtmlPositionScripts.ScrollToFraction(fraction.Fraction))) is int left
            ? (fraction, left)
            : null;
    }
```

7. In `DisposeAsync`, after `LocationChanged = null;`:

```csharp
        View.SizeChanged -= OnViewSizeChanged;
        resizeDelay?.Cancel();
        resizeDelay?.Dispose();
        resizeDelay = null;
```

A resize doesn't call `RecordRestore`, raise `LocationChanged` or rewrite
the position file: the point is unchanged by construction.

- [ ] **Step 4: Commit and run CI (GREEN)**

```bash
git add src/DesktopGuides.Production/HtmlReaderSession.cs
git commit -m "feat(p1): T09.3 keep the HTML point across a resize

- Size changes pause tracking; 300 ms after the last one the current
  point is scrolled back to the top and the new scroll is the baseline
- A capture or baseline taken across a size change is dropped
- GetLocationAsync returns the held point while a resize settles

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Push and run CI. Expected: green; both passes' `phases` end with
`position-resize`; `htmlResizeMarks` is three values each within 419–421.

---

### Task 5: Restore

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`html-position` steps 3–5)
- Modify: `tools/p1/windows_shell_install.ps1` (restore counts in `Assert-HtmlPositionPass`)
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (`dataRoot` field)
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs` (constructor call)

**Interfaces:**
- Consumes: `HtmlLocationRules.PlanRestore/FindArgs/ParseFind/Resolve/Outcome/Decode/ParsePending/UnavailableReason`,
  `HtmlRestorePlan`, `HtmlRestoreTarget`, `HtmlFindResult` (Task 1);
  `HtmlSessionDiagnostics.RecordRestore` (Task 2);
  `HtmlPositionScripts.Find/PendingImages`, `RunScriptAsync`, `CaptureAsync`,
  `WritePositionForTest`, `RaiseLocationChanged`, smoke `Read-HtmlPosition`,
  `Wait-HtmlPosition`, `Wait-TopMark`, `Get-PageTopLine` (Task 3);
  `ScrollToTargetAsync`, `ReapplyAsync`, `generation`, `opened`, `resizing`,
  `ResizeSettle`, smoke `$target` (Task 4).
- Produces:
  - `HtmlReaderSession(HtmlGuideLoaded loaded, string dataRoot, string cacheRoot, HtmlSessionDiagnostics? diagnostics)`.
  - `RestoreLocationAsync` restores for real; it waits for the entry to load.
  - In `HtmlReaderSession`: `Task<RestoreOutcome> RestoreAsync(LocationDecodeResult decoded)`,
    `Task OpenEntryAsync(ManagedGuideSource, CancellationToken)` (the former
    body of `OpenAsync`), field `TaskCompletionSource<bool> entryLoad`, field
    `bool restoring`.
  - The position file gains the last restore: `kind` (`RestoreKind` name),
    `step` (`HtmlRestoreStep` name, null when `Unavailable`), `reason`, and
    `pending` (images still loading after the first scroll).
  - Smoke: `Read-HtmlPosition` returns `pending`; functions `Set-HtmlRestore`,
    `Clear-HtmlRestore`, `Clear-HtmlPosition`, `Open-RestoredGuide`,
    `Assert-Restore`; report fields `restoreKinds` (one entry per restore) and
    `htmlRestoreScreenshot`.

- [ ] **Step 1: Write the failing smoke steps**

In `tools/p1/windows_shell_ui_smoke.ps1`, in `Read-HtmlPosition`, replace

```powershell
                kind = $file.kind; step = $file.step; reason = $file.reason
```

with

```powershell
                kind = $file.kind; step = $file.step; reason = $file.reason; pending = $file.pending
```

After `Wait-TopMark` in the shared reader block, add:

```powershell
        # The restore file stands in for T12.2's reopen: the app reads it
        # once, after the entry loads, while the HtmlPosition gate is open.
        function Set-HtmlRestore([string] $json) {
            $folder = Join-Path $AppDataRoot 'test'
            [void](New-Item -ItemType Directory -Force -Path $folder)
            [System.IO.File]::WriteAllText((Join-Path $folder 'html-restore.json'), $json)
        }

        function Clear-HtmlRestore {
            Remove-Item -LiteralPath (Join-Path $AppDataRoot 'test\html-restore.json') -Force -ErrorAction SilentlyContinue
        }

        function Clear-HtmlPosition {
            Remove-Item -LiteralPath (Join-Path $AppCacheRoot "diagnostics\html-position-$ProcessId.json") `
                -Force -ErrorAction SilentlyContinue
        }

        function Open-RestoredGuide([string] $guide, [string] $restore) {
            Clear-HtmlPosition
            Set-HtmlRestore $restore
            try {
                Open-TextGuide $guide
                $report.sessionsOpened++
                [void](Wait-Status 'Guide ready.')
                return Wait-HtmlPosition { param($p) $p.kind } 'a restore outcome'
            }
            finally {
                Clear-HtmlRestore
            }
        }

        # Pass '' for a step or reason that must be null.
        function Assert-Restore($position, [string] $kind, [string] $step, [string] $reason, [string] $what) {
            if ([string] $position.kind -cne $kind -or [string] $position.step -cne $step -or
                [string] $position.reason -cne $reason) {
                throw "After $what the restore was kind=$($position.kind) step=$($position.step) reason=$($position.reason); expected kind=$kind step=$step reason=$reason."
            }
            $report.restoreKinds += $kind
        }
```

In the `html-position` branch, after `$report.unimportedClicks = 0`, add:

```powershell
            $report.restoreKinds = @()
```

Then replace the `Back-ToTextGame` that follows
`$report.phases += 'position-resize'` with:

```powershell
            $saved = (Read-HtmlPosition).locator
            Back-ToTextGame

            # position-restore-exact: the saved locator in a new session, at
            # another width than the capture's, puts the same line on top.
            Resize-ShellWindow 1100 720
            $restored = Open-RestoredGuide 'Long Web Guide' $saved
            Assert-Restore $restored 'Exact' 'Exact' '' 'an exact restore'
            if ($restored.offset -ne $target.offset) {
                throw "An exact restore captured offset $($restored.offset); expected $($target.offset)."
            }
            [void](Wait-TopMark 420 'an exact restore')
            $report.htmlRestoreScreenshot = Save-WindowScreenshot 'html-restore'
            Back-ToTextGame
            $report.phases += 'position-restore-exact'

            # position-restore-late-images: images answer 1 s late, so the
            # lazy route map above the target is still loading after the
            # first scroll; the second scroll puts the line back on top.
            $delay = [System.Threading.EventWaitHandle]::new(
                $false, [System.Threading.EventResetMode]::ManualReset,
                "Local\DesktopGuides.Preview.HtmlAssetDelay.$ProcessId")
            try {
                $delayed = Open-RestoredGuide 'Long Web Guide' $saved
            }
            finally {
                $delay.Dispose()
            }
            Assert-Restore $delayed 'Exact' 'Exact' '' 'a restore with late images'
            if ($null -eq $delayed.pending -or [int] $delayed.pending -lt 1) {
                throw "A restore with late images met no loading image (pending=$($delayed.pending)), so the image wait went untested."
            }
            if ($delayed.offset -ne $target.offset) {
                throw "A restore with late images captured offset $($delayed.offset); expected $($target.offset)."
            }
            [void](Wait-TopMark 420 'a restore with late images')
            Back-ToTextGame
            $report.phases += 'position-restore-late-images'

            # position-restore-changed: in changed bytes the quote is found
            # by context; the text inserted above moved every offset.
            $changed = Open-RestoredGuide 'Changed Long Web Guide' $saved
            Assert-Restore $changed 'Approximate' 'Context' `
                'The guide changed, so this is an approximate position.' 'a restore in changed bytes'
            if ($changed.offset -le $target.offset) {
                throw "A restore in changed bytes captured offset $($changed.offset); expected more than $($target.offset)."
            }
            [void](Wait-TopMark 420 'a restore in changed bytes')
            Back-ToTextGame
            $report.phases += 'position-restore-changed'

            # position-restore-invalid: a malformed locator is Unavailable
            # and the page stays at its start.
            $invalid = Open-RestoredGuide 'Long Web Guide' '{"format":"Html","schemaVersion":1,"payload":'
            Assert-Restore $invalid 'Unavailable' '' `
                "This reading position can't be used with this guide." 'a malformed locator'
            if ([string] $invalid.quote -notlike 'Long Web Guide*') {
                throw "After a malformed locator the position was not the page's start."
            }
            $top = Get-PageTopLine
            if ($top -match '^MARK-') {
                throw "After a malformed locator the page's top line was $top."
            }
            Back-ToTextGame
            $report.phases += 'position-restore-invalid'
```

The width stays 1100 for the rest of the mode. The fixture's lines wrap
there too, so the exact restore can only land on MARK-0420 by offset.

In `tools/p1/windows_shell_install.ps1`, in `Assert-HtmlPositionPass`, before
`return $sessions`, add:

```powershell
    # Each restore the smoke saw was counted once, by kind, in diagnostics.
    $counted = @{}
    foreach ($session in $sessions) {
        if (-not $session.restores) { continue }
        foreach ($kind in @($session.restores.PSObject.Properties)) {
            $counted[$kind.Name] = [int] $counted[$kind.Name] + [int] $kind.Value
        }
    }
    $seen = @{}
    foreach ($kind in @($result.restoreKinds)) {
        if ($kind) { $seen[$kind] = [int] $seen[$kind] + 1 }
    }
    $countedText = (@($counted.Keys) | Sort-Object | ForEach-Object { "$_=$($counted[$_])" }) -join ','
    $seenText = (@($seen.Keys) | Sort-Object | ForEach-Object { "$_=$($seen[$_])" }) -join ','
    if ($countedText -cne $seenText) {
        throw "The $pass pass counted restores '$countedText'; the smoke saw '$seenText'."
    }
```

- [ ] **Step 2: Check ASCII, commit, and run CI (RED)**

```bash
LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/*.ps1
git add tools/p1/windows_shell_ui_smoke.ps1 tools/p1/windows_shell_install.ps1
git commit -m "test(p1): T09.3 html-position restore steps

- Exact restore at another width, again with late images
- Context restore in changed bytes, Unavailable for a malformed locator
- The installer matches restore counts against the smoke

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Expected: the grep prints nothing. Push and run CI. Expected:
`production-shell-ui` fails in `html-position-light` with "The HTML position
never showed a restore outcome." (the app ignores the restore file and
writes `kind: null`).

- [ ] **Step 3: Restore in the session**

In `src/DesktopGuides.Production/HtmlReaderSession.cs`:

1. Add fields after `private int generation;`:

```csharp
    private static readonly TimeSpan ImageWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ImagePoll = TimeSpan.FromMilliseconds(100);
    private readonly TaskCompletionSource<bool> entryLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim restoreTurn = new(1, 1);
    private readonly string? restoreFileForTest;
    private readonly bool delayImagesForTest;
    private bool restoring;
    private RestoreOutcome? lastOutcome;
    private HtmlRestoreStep? lastStep;
    private int? lastPending;
```

2. Change the constructor's signature and add the gates after
   `positionForTest = ...`:

```csharp
    public HtmlReaderSession(
        HtmlGuideLoaded loaded, string dataRoot, string cacheRoot, HtmlSessionDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
```

(the rest of the body is unchanged up to `positionForTest = ...`), then:

```csharp
        restoreFileForTest = positionForTest ? Path.Combine(dataRoot, "test", "html-restore.json") : null;
        delayImagesForTest = TestGate.IsOpen($@"Local\DesktopGuides.Preview.HtmlAssetDelay.{Environment.ProcessId}");
```

3. Rename `public async Task OpenAsync(` to
   `private async Task OpenEntryAsync(`, delete the two lines at its end
   (`opened = true;` and `tracker.Start();`), and add above it:

```csharp
    // A saved point can be restored only once the entry has loaded;
    // RestoreLocationAsync waits on entryLoad.
    public async Task OpenAsync(ManagedGuideSource source, CancellationToken token)
    {
        bool entryLoaded = false;
        try
        {
            await OpenEntryAsync(source, token);
            entryLoaded = true;
        }
        finally
        {
            entryLoad.TrySetResult(entryLoaded);
        }
        if (RestoreRequestForTest() is LocationDecodeResult request) await RestoreAsync(request);
        token.ThrowIfCancellationRequested();
        if (disposed) return;
        opened = true;
        tracker.Start();
    }
```

4. Replace `RestoreLocationAsync`:

```csharp
    public async Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        token.ThrowIfCancellationRequested();
        if (!await entryLoad.Task.WaitAsync(token) || disposed || contentSha256 is null)
        {
            return new RestoreOutcome(RestoreKind.Unavailable, HtmlLocationRules.UnavailableReason);
        }
        return await RestoreAsync(HtmlLocationRules.Decode(location, contentSha256, policy.Entry.RequestPath));
    }
```

5. Add after `ScrollToTargetAsync`:

```csharp
    // Core plans the steps and picks the target; the scripts only measure
    // and scroll. An image above the target that is still loading would push
    // it down, so once images load (or after 2 s) the target goes to the top
    // again, and again after any resize during the restore.
    private async Task<RestoreOutcome> RestoreAsync(LocationDecodeResult decoded)
    {
        await restoreTurn.WaitAsync();
        restoring = true;
        try
        {
            HtmlRestorePlan plan = HtmlLocationRules.PlanRestore(decoded);
            HtmlFindResult? find = null;
            if (plan.NeedsFind && plan.Position is HtmlPosition position)
            {
                find = HtmlLocationRules.ParseFind(
                    await RunScriptAsync(HtmlPositionScripts.Find(HtmlLocationRules.FindArgs(position))));
            }
            HtmlRestoreTarget? target = HtmlLocationRules.Resolve(plan, find);
            int? pending = null;
            if (target is not null)
            {
                int started = generation;
                (HtmlRestoreTarget Target, int Pending)? first = await ScrollToTargetAsync(target);
                target = first?.Target;
                pending = first?.Pending;
                if (first is { Pending: > 0 }) await WaitForImagesAsync();
                while (target is not null && !disposed)
                {
                    target = (await ScrollToTargetAsync(target))?.Target ?? target;
                    if (started == generation) break;
                    // The window was resized during the restore.
                    started = generation;
                    await Task.Delay(ResizeSettle);
                }
            }
            RestoreOutcome outcome = HtmlLocationRules.Outcome(plan, target);
            if (disposed) return outcome;
            HtmlScroll? scroll = HtmlLocationRules.ParseScroll(await RunScriptAsync(HtmlPositionScripts.ReadScroll));
            if (scroll is not null) lastScroll = scroll;
            HtmlCapture? capture = await CaptureAsync();
            if (disposed) return outcome;
            diagnostics?.RecordRestore(outcome.Kind);
            lastOutcome = outcome;
            lastStep = outcome.Kind == RestoreKind.Unavailable ? null : target?.Step;
            lastPending = pending;
            bool moved = capture is not null && capture != current;
            if (capture is not null) current = capture;
            WritePositionForTest();
            if (moved) RaiseLocationChanged();
            return outcome;
        }
        finally
        {
            restoring = false;
            restoreTurn.Release();
        }
    }

    // Polls until no image above the viewport's bottom is loading, or 2 s.
    private async Task WaitForImagesAsync()
    {
        DateTime deadline = DateTime.UtcNow + ImageWait;
        while (DateTime.UtcNow < deadline && !disposed)
        {
            await Task.Delay(ImagePoll);
            if (HtmlLocationRules.ParsePending(await RunScriptAsync(HtmlPositionScripts.PendingImages)) is not > 0)
            {
                return;
            }
        }
    }

    // Test gate only: stands in for T12.2's reopen. The file is untrusted
    // and goes through the codec like a stored locator.
    private LocationDecodeResult? RestoreRequestForTest()
    {
        if (restoreFileForTest is null || contentSha256 is null) return null;
        try
        {
            FileInfo file = new(restoreFileForTest);
            if (!file.Exists) return null;
            if (file.Length > ReaderLocationCodec.MaxBytes) return new(LocationDecodeStatus.Invalid, null);
            return ReaderLocationCodec.Deserialize(
                File.ReadAllText(restoreFileForTest), GuideFormat.Html,
                contentSha256.ToLowerInvariant(), policy.Entry.RequestPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
```

6. Keep the tracker, `GetLocationAsync` and a resize's re-apply off the
   page while a restore moves it. In `OnTrackerTick`, change the first guard
   to:

```csharp
        if (ticking || disposed || restoring) return;
```

   In `GetLocationAsync`, change the capture line to:

```csharp
        // Mid-reflow or mid-restore, the page's top isn't the reader's point.
        if (!resizing && !restoring && await CaptureAsync() is HtmlCapture capture) current = capture;
```

   At the top of `ReapplyAsync`, add:

```csharp
        // A restore in progress scrolls to its own target again.
        if (restoring) return;
```

7. In `WritePositionForTest`, replace the anonymous object with:

```csharp
            string json = JsonSerializer.Serialize(new
            {
                locator,
                kind = lastOutcome?.Kind.ToString(),
                step = lastStep?.ToString(),
                reason = lastOutcome?.Reason,
                pending = lastPending
            });
```

8. In `OnWebResourceRequested`, at the start of `if (decision is HtmlServe serve)`:

```csharp
                // Test gate only: images answer late, as from a slow disk.
                if (delayImagesForTest && serve.Asset.Kind == GuideAssetKind.Image)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
```

9. In `DisposeAsync`, after `resizeDelay = null;`:

```csharp
        entryLoad.TrySetResult(false);
```

In `src/DesktopGuides.Production/ShellWindow.xaml.cs`, add after
`private string? cacheRoot;`:

```csharp
    private string? dataRoot;
```

and in `InitializeCoreAsync`, after the `string dataRoot = AppDataRoot.Resolve(...);`
statement:

```csharp
            this.dataRoot = dataRoot;
```

In `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs`, change the
session's construction to:

```csharp
        HtmlReaderSession session = new(loaded, dataRoot!, cacheRoot!, HtmlReaderSession.DiagnosticsForTest());
```

Notes for the implementer:
- `RestoreAsync` never throws for page reasons: every script goes through
  `RunScriptAsync`, and every reply through a Core parser.
- `first is { Pending: > 0 }` is false for a null tuple.
- `HtmlLocationRules.Resolve` returns null when a plan needs a find and the
  find reply was rejected, so a page that breaks `Find` is `Unavailable`
  rather than a guess.
- Nothing here writes `ReadingStates`; the shell still never calls
  `RestoreLocationAsync` (T12.2 will).

- [ ] **Step 4: Commit and run CI (GREEN)**

```bash
git add src/DesktopGuides.Production/HtmlReaderSession.cs \
  src/DesktopGuides.Production/ShellWindow.xaml.cs \
  src/DesktopGuides.Production/ShellWindow.HtmlReader.cs
git commit -m "feat(p1): T09.3 restore the HTML point

- Restore by exact offset, then text context, then fraction, as Core plans
- Scroll again once images above the target load, or after 2 s
- A restore asked for before the entry loads waits for it
- Test gates: a restore file read after open, and late image responses

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Push and run CI. Expected: green; both passes' `phases` end with
`position-restore-invalid`; `restoreKinds` is
`Exact, Exact, Approximate, Unavailable`; the late-image restore's position
file has `pending` ≥ 1; `htmlRestoreScreenshot` shows MARK-0420 at the top
in each theme.

If the late-image step fails with `pending=0`, the lazy image loaded before
the first scroll's reply. That is a fixture timing problem, not a restore
bug: report it with the evidence before changing the fixture or the gate's
delay.

---

### Task 6: Tell the reader about unimported links

**Files:**
- Modify: `tools/p1/windows_shell_ui_smoke.ps1` (`html-position` step 6)
- Modify: `src/DesktopGuides.Production/HtmlReaderSession.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml`
- Modify: `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs`
- Modify: `src/DesktopGuides.Production/ShellWindow.xaml.cs` (`CloseReaderSessionAsync`)

**Interfaces:**
- Consumes: `HtmlNavigationKind.Unavailable` and `HtmlDenyReason.UnimportedPage`
  (Task 2); the fixture's `Part 2 of this guide` and `the website` links on
  MARK-0425 and MARK-0426 (Task 3); smoke `Wait-HtmlPosition`,
  `Read-HtmlPosition`, `Wait-TopMark`, `Wait-PageVisible`, `Wait-PageName`,
  report field `unimportedClicks` and the installer's
  `UnimportedPage`/`Navigation` check (Task 3); the resize re-apply (Task 4),
  which keeps the point when a bar shrinks the page.
- Produces:
  - `HtmlReaderSession.UnavailableLinkRequested` (`EventHandler`, no payload:
    the shell never shows the target).
  - InfoBar `ReaderUnavailableLinkBar`, its `AutomationProperties.Name` equal to
    the unavailable-link message.
  - `ShellWindow.HideUnavailableLinkBar()`.
  - Smoke phase `position-unimported-link`; report field
    `htmlUnavailableLinkScreenshot`.

- [ ] **Step 1: Write the failing smoke step**

In `tools/p1/windows_shell_ui_smoke.ps1`, in the `html-position` branch, after
`$report.phases += 'position-restore-invalid'`, add:

```powershell
            # position-unimported-link: a link to a page that wasn't imported
            # shows the unavailable bar, and the point stays where it was.
            $unavailable = "This link goes to a page that isn't part of the imported guide."
            Open-TextGuide 'Long Web Guide'
            $report.sessionsOpened++
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Long Web Guide')
            Click-Element (Wait-PageVisible 'Jump to MARK-0420')
            $here = Wait-HtmlPosition { param($p) $p.quote -like 'MARK-0420 *' } 'the MARK-0420 line'
            [void](Wait-TopMark 420 'the fragment link')
            Click-Element (Wait-PageVisible 'Part 2 of this guide')
            $report.unimportedClicks++
            [void](Wait-VisibleById 'ReaderUnavailableLinkBar')
            [void](Wait-Name 'ReaderUnavailableLinkBar' $unavailable)
            # The bar makes the page shorter; the resize re-apply keeps the
            # point. One second covers the 300 ms settle and a 500 ms poll.
            Start-Sleep -Seconds 1
            [void](Wait-TopMark 420 'an unimported link')
            $after = Read-HtmlPosition
            if ($after.offset -ne $here.offset) {
                throw "An unimported link moved the point from offset $($here.offset) to $($after.offset)."
            }
            $report.htmlUnavailableLinkScreenshot = Save-WindowScreenshot 'html-unavailable-link'

            # Showing either link bar hides the other.
            Click-Element (Wait-PageVisible 'the website')
            [void](Wait-VisibleById 'ReaderExternalLinkBar')
            Wait-HiddenById 'ReaderUnavailableLinkBar'
            Click-Element (Wait-PageVisible 'Part 2 of this guide')
            $report.unimportedClicks++
            [void](Wait-VisibleById 'ReaderUnavailableLinkBar')
            Wait-HiddenById 'ReaderExternalLinkBar'
            Back-ToTextGame

            # A new session starts without the bar.
            Open-TextGuide 'Long Web Guide'
            $report.sessionsOpened++
            [void](Wait-Status 'Guide ready.')
            [void](Wait-PageName 'Long Web Guide')
            Wait-HiddenById 'ReaderUnavailableLinkBar'
            Back-ToTextGame
            $report.phases += 'position-unimported-link'
```

The installer needs no change: `Assert-HtmlPositionPass` already matches the
`UnimportedPage`/`Navigation` total against `unimportedClicks` (2 per pass)
and checks that no session served anything beyond the entry and its two
images.

- [ ] **Step 2: Check ASCII, commit, and run CI (RED)**

```bash
LC_ALL=C grep -n "$(printf '[\200-\377]')" tools/p1/*.ps1
git add tools/p1/windows_shell_ui_smoke.ps1
git commit -m "test(p1): T09.3 html-position unimported-link step

- A link to part2.html shows the unavailable bar; the point stays put
- Either link bar hides the other; a new session starts without it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Expected: the grep prints nothing. Push and run CI. Expected:
`production-shell-ui` fails in `html-position-light` with
"Expected visible 'ReaderUnavailableLinkBar'." (the bar doesn't exist yet).

- [ ] **Step 3: Raise the event in the session**

In `src/DesktopGuides.Production/HtmlReaderSession.cs`:

1. After `public event EventHandler<Uri>? ExternalLinkRequested;`:

```csharp
    // A person's link to a page of this guide that wasn't imported. It
    // carries nothing: the shell never shows the target.
    public event EventHandler? UnavailableLinkRequested;
```

2. In `OnNavigationStarting`, add a case before `default:`:

```csharp
            case HtmlNavigationKind.Unavailable:
                args.Cancel = true;
                RaiseUnavailableLink();
                break;
```

3. In `OnNewWindowRequested`, after the `External` branch's closing brace:

```csharp
        else if (navigation.Kind == HtmlNavigationKind.Unavailable)
        {
            RaiseUnavailableLink();
        }
```

4. After `OnNewWindowRequested`:

```csharp
    // Counted as a denied navigation; nothing is requested or served.
    private void RaiseUnavailableLink()
    {
        if (disposed) return;
        diagnostics?.RecordDenied(HtmlDenyReason.UnimportedPage, "Navigation");
        UnavailableLinkRequested?.Invoke(this, EventArgs.Empty);
    }
```

5. In `DisposeAsync`, after `ExternalLinkRequested = null;`:

```csharp
        UnavailableLinkRequested = null;
```

- [ ] **Step 4: Add the bar to the shell**

In `src/DesktopGuides.Production/ShellWindow.xaml`, after the
`ReaderExternalLinkBar` InfoBar's closing `</InfoBar>` and before the
`</StackPanel>` that holds it:

```xml
                    <!-- A fixed message: never the link's target or the page's text. -->
                    <InfoBar x:Name="ReaderUnavailableLinkBar"
                             Margin="0,12,0,0"
                             Visibility="Collapsed"
                             IsOpen="False"
                             IsClosable="True"
                             Severity="Informational"
                             Message="This link goes to a page that isn't part of the imported guide."
                             Closed="ReaderUnavailableLinkBarClosed"
                             AutomationProperties.Name="This link goes to a page that isn't part of the imported guide."
                             AutomationProperties.AutomationId="ReaderUnavailableLinkBar" />
```

In `src/DesktopGuides.Production/ShellWindow.HtmlReader.cs`:

1. After `session.ExternalLinkRequested += OnExternalLinkRequested;`:

```csharp
        session.UnavailableLinkRequested += OnUnavailableLinkRequested;
```

2. In `OnReaderSessionFailed`, after `HideExternalLinkBar();`:

```csharp
            HideUnavailableLinkBar();
```

3. In `OnExternalLinkRequested`, after the `ReferenceEquals` guard's closing
   brace:

```csharp
        HideUnavailableLinkBar();
```

4. After `HideExternalLinkBar()`:

```csharp
    // Showing either link bar hides the other.
    private void OnUnavailableLinkRequested(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, readerSession))
        {
            return;
        }
        HideExternalLinkBar();
        ReaderUnavailableLinkBar.Visibility = Visibility.Visible;
        ReaderUnavailableLinkBar.IsOpen = true;
    }

    private void HideUnavailableLinkBar()
    {
        ReaderUnavailableLinkBar.IsOpen = false;
        ReaderUnavailableLinkBar.Visibility = Visibility.Collapsed;
    }
```

5. After `ReaderExternalLinkBarClosed`:

```csharp
    // Collapsed once closed, so the Reader's layout returns to normal.
    private void ReaderUnavailableLinkBarClosed(InfoBar sender, InfoBarClosedEventArgs args) =>
        ReaderUnavailableLinkBar.Visibility = Visibility.Collapsed;
```

In `src/DesktopGuides.Production/ShellWindow.xaml.cs`, in
`CloseReaderSessionAsync`, after `HideExternalLinkBar();`:

```csharp
        HideUnavailableLinkBar();
```

Notes for the implementer:
- A click that arrives while the bar is open leaves it open; there is no
  per-link state to replace.
- `RaiseUnavailableLink` counts the denial before the event, so the
  diagnostics count it even if the shell has moved on to another session.
- The XAML is ASCII; the apostrophe in "isn't" needs no escaping inside a
  double-quoted attribute.

- [ ] **Step 5: Commit and run CI (GREEN)**

```bash
git add src/DesktopGuides.Production/HtmlReaderSession.cs \
  src/DesktopGuides.Production/ShellWindow.xaml \
  src/DesktopGuides.Production/ShellWindow.HtmlReader.cs \
  src/DesktopGuides.Production/ShellWindow.xaml.cs
git commit -m "feat(p1): T09.3 report links to unimported pages

- Unimported-page links raise UnavailableLinkRequested and count as denied
- ReaderUnavailableLinkBar shows a fixed message; either link bar hides
  the other, and both close with the guide

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Push and run CI. Expected: every job green; both passes' `phases` end with
`position-unimported-link`; `unimportedClicks` is 2 in each pass and the
installer's denied total matches; `htmlUnavailableLinkScreenshot` shows the
bar under the Reader toolbar with MARK-0420 at the page's top, in light and
dark. These two screenshots and `htmlRestoreScreenshot` go in the PR
description.

---

### Task 7: Docs and evidence

**Files:**
- Modify: `docs/p1/t09-3-html-locator-design.md` (status, implementation notes, verification)
- Create: `docs/p1/evidence/t09-3-html-locator/` (pass reports and screenshots)
- Modify: `docs/p1/implementation-plan.md` (status paragraph after T10.3's)
- Modify: `docs/work-breakdown.md` (S09 T09.3)
- Modify: `docs/p1-technical-design.md` (§6 S09 T09.3)

**Interfaces:**
- Consumes: the green CI run from Task 6 Step 5 (`<run>` below is its
  database ID; check its `headSha` is the Task 6 commit).
- Produces: nothing code reads.

This task is docs only: no test cycle of its own. Its check is that every
number written here is read from the run's artifacts.

- [ ] **Step 1: Collect the evidence**

```bash
rm -rf /tmp/t09-3-run && gh run download <run> --pattern '*shell*' -D /tmp/t09-3-run
find /tmp/t09-3-run \( -name 'html-position-*.json' -o -name 'html-position-*.png' \) | sort
mkdir -p docs/p1/evidence/t09-3-html-locator
```

Copy into `docs/p1/evidence/t09-3-html-locator/`, renaming:

| From the artifact | To |
| --- | --- |
| `html-position-light.json`, `html-position-dark.json` (the smoke's pass reports) | same names |
| `html-position-light.html-restore.png`, `-dark.` | `html-restore-light.png`, `html-restore-dark.png` |
| `html-position-light.html-unavailable-link.png`, `-dark.` | `html-unavailable-link-light.png`, `html-unavailable-link-dark.png` |

Do not copy the `html-position-<pid>.json` gate files: they hold fixture
quotes and are not needed once the pass reports record the offsets. Strip
the BOM from the copied JSON (`sed -i '' '1s/^\xEF\xBB\xBF//' <file>`) and
open each PNG to check it shows MARK-0420 at the page's top (and, for the
unavailable-link ones, the bar under the Reader toolbar).

- [ ] **Step 2: Update the design doc**

In `docs/p1/t09-3-html-locator-design.md`, replace the line
`Status: designed; not yet implemented.` with:

```markdown
Status: implemented; CI run <run> passed the installed `html-position`
mode in light and dark.
```

Replace the bullets under `## Docs` with one line:

```markdown
Done in the implementing branch; see the status lines in each.
```

Append after `## Out of scope`:

```markdown
## Implementation notes

Planning refinements, from the
[implementation plan](t09-3-html-locator-plan.md):

- **P1. The restore choice is in Core.** The `Restore(plan)` script became
  `Find`, which only measures (is the quote at the saved offset, and where
  else does it occur, inside the `ElementId` element and in the whole walk),
  and `ScrollToOffset` and `ScrollToFraction`, which only scroll and report
  images still loading. `HtmlLocationRules.Resolve` picks the step and the
  offset, so the tie rule is a unit test. `Outcome` maps Core's own step;
  there is no restore reply to parse.
- **P2. Capture measures text boxes.** The capture script binary-searches
  the walk for the first character whose box bottom is below the viewport
  top, instead of `caretRangeFromPoint` and an 8 px step-down. Images, gaps,
  padding and body margins don't affect it, and restore scrolls that
  character's box top to the viewport top, so a capture after a restore
  returns the same offset.
- **P3. Exact needs a quote.** A page without text captures offset 0 and no
  quote; its restore uses the fraction.
- **P4.** A fraction restore of unchanged bytes is `Approximate` without a
  reason, because "The guide changed" would be false.
- **P5.** `RestoreLocationAsync` takes a `ReaderLocation`, as
  `IReaderSession` defines it. `HtmlLocationRules.Decode` sends it through
  the codec's `Serialize` and `Deserialize`, so it gets the checks a stored
  locator gets.
- **P6.** An unimported link is recorded as a denied navigation:
  `HtmlDenyReason.UnimportedPage` with context `Navigation`.
- **P7.** The smoke reads the page's top line through UI Automation's
  `TextPattern.RangeFromPoint`, independently of the app's capture.
- **P8.** `html-position` is its own seed (`seed-html-position`, game "Web
  Position Game") and smoke mode, so the canary passes' counts are
  unchanged. The page-tree helpers moved to the shared reader scope.
- **P9.** `HtmlNavigationPolicy.IsEntryDocument` is the one entry
  comparison, used for a capture's `href` and the session's origin check.
- **Fixture.** The lazy image above the target is its own file,
  `images/route.png`. A second `<img>` with `map.png`'s URL is answered
  from Chromium's memory cache and is never pending, so the late-image
  restore would not have waited for anything.
- **Image wait.** `PendingImages` counts only images that are incomplete
  and start above the viewport's bottom. A lazy image far below would
  otherwise make every restore wait the full 2 s.

## Verification

- `HtmlLocationRulesTests` cover reply parsing (every type, cap and origin
  row, and a titled entry's literal sub-delimiters), capture through the
  T12.1 codec, a plan per decode status, `Resolve` including the tie, and
  every outcome row. `HtmlNavigationPolicyTests` and
  `HtmlSessionDiagnosticsTests` cover the `Unavailable` kind and the new
  counts.
- CI run <run>, `html-position` on `html-long`, light [dark]:

  | Phase | Result |
  | --- | --- |
  | `position-fragment` | MARK-0420 on top, offset <offset> [<offset>] |
  | `position-resize` | 600, 1100, 1500 px: same offset, MARK-0420 on top |
  | `position-restore-exact` | `Exact` at 1100 px |
  | `position-restore-late-images` | `Exact`, pending <n> [<n>] |
  | `position-restore-changed` | `Approximate`, context, offset <offset> [<offset>] |
  | `position-restore-invalid` | `Unavailable`, page at its start |
  | `position-unimported-link` | bar shown twice, offset unchanged, 2 denied navigations |

  The values are from
  [light](evidence/t09-3-html-locator/html-position-light.json) and
  [dark](evidence/t09-3-html-locator/html-position-dark.json).
  Screenshots: restored page
  [light](evidence/t09-3-html-locator/html-restore-light.png),
  [dark](evidence/t09-3-html-locator/html-restore-dark.png); unavailable
  link [light](evidence/t09-3-html-locator/html-unavailable-link-light.png),
  [dark](evidence/t09-3-html-locator/html-unavailable-link-dark.png).
- Not covered: saving and restoring on reopen, and showing restore reasons
  (T12.2); the point across theme and font changes (T09.2, T14.3); a
  `target="_blank"` link to an unimported page, which takes the same
  `RaiseUnavailableLink` path as a plain link and is classified by the
  `HtmlNavigationPolicyTests` rows only.
```

Fill each `<run>`, `<offset>` and `<n>` from the pass reports copied in
Step 1 (`htmlPositionOffset`, the restores' captured offsets, `pending`). If
a value isn't in a report, leave the row's cell as the phase's name and say
so in the task report rather than inventing a number. Add any further
deviation made while executing (each ledger `Ruling:`) as another bullet
under Implementation notes.

- [ ] **Step 3: Update the trace docs**

In `docs/p1/implementation-plan.md`, replace the paragraph that begins
`T10.3 is in review in PR #37 (CI run 37178208319)` with:

```markdown
T10.3 was merged through PR #37 on 4 October 2026 (merge commit
`541c245`; CI run 37178208319); see the
[design and implementation notes](t10-3-pdf-locator-design.md). A PDF
position is now a page and the share of the page above the viewport. It
survives narrow, medium and wide windows on `pdf-long` and resets to the
top on a page turn. A restore clamps a missing page to the nearest page's
top, keeps the point as `Approximate` when the bytes changed, and opens at
the first page for a malformed, wrong-format or future-version locator.
Saving it and restoring on reopen remain T12.2.

T09.3 is in review (CI run <run>); see the
[design and implementation notes](t09-3-html-locator-design.md). An HTML
position is now a character offset in the entry document with a text
quote, the nearest element id and the scroll fraction. It survives narrow,
medium and wide windows on `html-long`. A restore is `Exact` in the same
bytes, also when an image above the point loads late; `Approximate` through
the text context in changed bytes; and `Unavailable`, at the page's start,
for a malformed locator. A link to a page that wasn't imported shows an
informational bar and leaves the point where it was. Saving it and
restoring on reopen remain T12.2.
```

In `docs/work-breakdown.md`, replace the S09 T09.3 bullet with:

```markdown
- **T09.3** Capture and restore document-relative path, visible text/element
  context, and scroll-ratio fallback.
  Implemented (in-session capture and restore; saving is T12.2); see
  [p1/t09-3-html-locator-design.md](p1/t09-3-html-locator-design.md).
```

In `docs/p1-technical-design.md`, at the end of the S09 **T09.3** bullet
(after `S21 owns multi-document HTML.`), add:

```markdown
  See the [T09.3 design](p1/t09-3-html-locator-design.md). The session
  captures and restores in memory; T12.2 persists the envelope and restores
  it on reopen. Restore picks the step in Core: the host scripts only
  measure and scroll.
```

- [ ] **Step 4: Commit**

```bash
git add docs/p1/t09-3-html-locator-design.md docs/p1/evidence/t09-3-html-locator \
  docs/p1/implementation-plan.md docs/work-breakdown.md docs/p1-technical-design.md
git commit -m "docs(p1): T09.3 HTML locator notes and evidence

- Design status, planning refinements and CI evidence
- Light and dark pass reports and screenshots
- T10.3 merged through PR #37; T09.3 in review
- Work breakdown and technical design link the T09.3 design

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: After the PR opens, link it**

Once the PR exists (its body names T09.3, the prerequisites T09.1, T12.1 and
T07.3 as merged, the intended outcome, and embeds the restore and
unavailable-link screenshots in light and dark), replace `is in review` /
`Implemented` with the PR number in the three places Step 2 and Step 3
wrote them:

- design doc status: `Status: implemented in PR #<pr>; CI run <run> passed …`
- implementation-plan: `T09.3 is in review in PR #<pr> (CI run <run>)`
- work-breakdown: `Implemented in PR #<pr> (in-session capture and restore; saving is T12.2)`

```bash
git add docs/p1/t09-3-html-locator-design.md docs/p1/implementation-plan.md docs/work-breakdown.md
git commit -m "docs(p1): T09.3 link PR #<pr>

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Push. No merge without the user's explicit OK.
