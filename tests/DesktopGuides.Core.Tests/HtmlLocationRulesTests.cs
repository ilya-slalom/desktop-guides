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
    // A cut through a surrogate pair leaves a lone half in the JSON text.
    [InlineData("quote", "\"ab\\ud83d\"")]
    [InlineData("id", "\"\\udc00x\"")]
    [InlineData("href", "\"https://example.invalid/\\ud83d\"")]
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
