using System.Text;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextLocatorTests
{
    private static readonly string OtherHash = new('0', 64);

    private static TextGuideDocument Doc(string text) =>
        TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));

    private static ReaderLocation Location(int offset, string context, double? fraction) =>
        new(GuideFormat.Txt, ReaderLocationCodec.CurrentVersion, OtherHash,
            new TextPosition(offset, context), fraction);

    [Fact]
    public void CaptureUsesLineStartAndFollowingText()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");

        ReaderLocation location = TextLocator.Capture(document, 1);

        Assert.Equal(GuideFormat.Txt, location.Format);
        Assert.Equal(ReaderLocationCodec.CurrentVersion, location.SchemaVersion);
        Assert.Equal(new TextPosition(6, "beta\ngamma\n"), location.Payload);
        Assert.Equal(6.0 / 17, location.EstimatedFraction);
    }

    [Fact]
    public void CaptureLowerCasesTheDocumentHash()
    {
        TextGuideDocument document = Doc("alpha\n");

        ReaderLocation location = TextLocator.Capture(document, 0);

        Assert.Matches("^[0-9A-F]{64}$", document.ContentSha256);
        Assert.Equal(document.ContentSha256.ToLowerInvariant(), location.ContentSha256);
    }

    [Fact]
    public void CapturedLocationRoundTripsThroughTheCodec()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");
        ReaderLocation location = TextLocator.Capture(document, 2);

        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            ReaderLocationCodec.Serialize(location), GuideFormat.Txt,
            document.ContentSha256.ToLowerInvariant());

        Assert.Equal(LocationDecodeStatus.Valid, decoded.Status);
        Assert.Equal(location, decoded.Location);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99, 3)]
    public void CaptureClampsTheLine(int requested, int expected)
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");

        ReaderLocation location = TextLocator.Capture(document, requested);

        Assert.Equal(document.LineStarts[expected],
            Assert.IsType<TextPosition>(location.Payload).CharacterOffset);
    }

    [Fact]
    public void CaptureLimitsContextLength()
    {
        ReaderLocation location = TextLocator.Capture(Doc(new string('a', 300)), 0);

        Assert.Equal(TextLocator.ContextLength,
            Assert.IsType<TextPosition>(location.Payload).Context.Length);
    }

    [Fact]
    public void CaptureStopsContextBeforeNul()
    {
        ReaderLocation location = TextLocator.Capture(Doc("ab\0cd"), 0);

        Assert.Equal("ab", Assert.IsType<TextPosition>(location.Payload).Context);
        _ = ReaderLocationCodec.Serialize(location);
    }

    [Fact]
    public void CaptureDropsATrailingHighSurrogate()
    {
        string text = new string('a', TextLocator.ContextLength - 1) + "\U0001F600z";

        ReaderLocation location = TextLocator.Capture(Doc(text), 0);

        Assert.Equal(new string('a', TextLocator.ContextLength - 1),
            Assert.IsType<TextPosition>(location.Payload).Context);
        _ = ReaderLocationCodec.Serialize(location);
    }

    [Fact]
    public void EmptyGuideCapturesAndRestoresLineZero()
    {
        TextGuideDocument document = Doc("");

        ReaderLocation location = TextLocator.Capture(document, 3);
        TextRestore restore = TextLocator.Restore(document, location);

        Assert.Equal(new TextPosition(0, ""), location.Payload);
        Assert.Equal(0.0, location.EstimatedFraction);
        Assert.Equal(new TextRestore(0, new RestoreOutcome(RestoreKind.Exact)), restore);
    }

    [Fact]
    public void UnchangedContentRestoresExactly()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");

        TextRestore restore = TextLocator.Restore(document, TextLocator.Capture(document, 2));

        Assert.Equal(new TextRestore(2, new RestoreOutcome(RestoreKind.Exact)), restore);
    }

    [Fact]
    public void MidLineOffsetSnapsToItsLine()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");
        ReaderLocation location = TextLocator.Capture(document, 0) with
        {
            Payload = new TextPosition(8, "")
        };

        Assert.Equal(new TextRestore(1, new RestoreOutcome(RestoreKind.Exact)),
            TextLocator.Restore(document, location));
    }

    [Fact]
    public void OffsetBeyondTextFallsBackToContext()
    {
        TextGuideDocument document = Doc("alpha\nbeta\ngamma\n");
        ReaderLocation location = TextLocator.Capture(document, 0) with
        {
            Payload = new TextPosition(999, "beta\n")
        };

        Assert.Equal(new TextRestore(1, new RestoreOutcome(RestoreKind.Context)),
            TextLocator.Restore(document, location));
    }

    [Fact]
    public void ChangedGuideFindsContextOnTheMovedLine()
    {
        ReaderLocation location = TextLocator.Capture(Doc("alpha\nbeta\ngamma\n"), 2);

        TextRestore restore = TextLocator.Restore(Doc("intro\nalpha\nbeta\ngamma\n"), location);

        Assert.Equal(new TextRestore(3, new RestoreOutcome(RestoreKind.Context)), restore);
    }

    [Fact]
    public void NearestRepeatedContextWins()
    {
        // "Boss" starts at offsets 0 and 15; 15 is nearer to 13.
        TextRestore restore = TextLocator.Restore(
            Doc("Boss\naaaa\naaaa\nBoss\naaaa\n"), Location(13, "Boss", null));

        Assert.Equal(new TextRestore(3, new RestoreOutcome(RestoreKind.Context)), restore);
    }

    [Fact]
    public void EquallyNearContextsFallBackToApproximate()
    {
        // "Boss" starts at 0 and 10, both 5 from the saved offset.
        TextRestore restore = TextLocator.Restore(
            Doc("Boss\naaaa\nBoss\n"), Location(5, "Boss", 0.5));

        Assert.Equal(new TextRestore(1,
            new RestoreOutcome(RestoreKind.Approximate, TextLocator.ApproximateReason)), restore);
    }

    [Fact]
    public void MissingContextUsesTheFraction()
    {
        TextRestore restore = TextLocator.Restore(Doc("a\nb\nc"), Location(0, "zzz", 1.0));

        Assert.Equal(new TextRestore(2,
            new RestoreOutcome(RestoreKind.Approximate, TextLocator.ApproximateReason)), restore);
    }

    [Fact]
    public void MissingContextAndFractionIsUnavailable()
    {
        TextRestore restore = TextLocator.Restore(Doc("a\nb\nc"), Location(0, "zzz", null));

        Assert.Equal(new TextRestore(0,
            new RestoreOutcome(RestoreKind.Unavailable, TextLocator.UnavailableReason)), restore);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("payload")]
    [InlineData("version")]
    public void WrongFormatPayloadOrVersionIsUnavailable(string defect)
    {
        ReaderLocation usable = Location(0, "a", 0);
        ReaderLocation location = defect switch
        {
            "format" => usable with { Format = GuideFormat.Pdf },
            "payload" => usable with { Payload = new PdfPosition(0, 0) },
            _ => usable with { SchemaVersion = ReaderLocationCodec.CurrentVersion + 1 }
        };

        Assert.Equal(new TextRestore(0,
                new RestoreOutcome(RestoreKind.Unavailable, TextLocator.UnavailableReason)),
            TextLocator.Restore(Doc("a\nb\n"), location));
    }

    [Fact]
    public void AnotherGuidesLocationIsNeverExact()
    {
        TextGuideDocument first = Doc("Chapter 1\nBoss fight\n");
        TextGuideDocument second = Doc("Other guide\nBoss fight\nEnd\n");

        TextRestore shared = TextLocator.Restore(second, TextLocator.Capture(first, 1));
        TextRestore unique = TextLocator.Restore(second, TextLocator.Capture(first, 0));

        Assert.Equal(new TextRestore(1, new RestoreOutcome(RestoreKind.Context)), shared);
        Assert.Equal(RestoreKind.Approximate, unique.Outcome.Kind);
    }

    [Fact]
    public void EveryLineRoundTripsExactly()
    {
        string text = string.Join("\n",
            Enumerable.Range(0, 50).Select(index => index % 7 == 0 ? "" : $"Row {index}")) + "\n";
        TextGuideDocument document = Doc(text);

        for (int line = 0; line < document.LineStarts.Count; line++)
        {
            Assert.Equal(new TextRestore(line, new RestoreOutcome(RestoreKind.Exact)),
                TextLocator.Restore(document, TextLocator.Capture(document, line)));
        }
    }

    [Fact]
    public void CaptureOfTrailingEmptyLineRestoresExactly()
    {
        TextGuideDocument document = Doc("alpha\nbeta\n");
        int last = document.LineStarts.Count - 1;

        TextRestore restore = TextLocator.Restore(document, TextLocator.Capture(document, last));

        Assert.Equal(2, last);
        Assert.Equal(new TextRestore(last, new RestoreOutcome(RestoreKind.Exact)), restore);
    }

    [Fact]
    public void NearestMatchStopsAfterPassingOffset()
    {
        // 200,000 identical lines, like txt-long; the saved offset is line 10.
        string line = "Area 001 | Proceed north, then open the chest. |\n";
        TextGuideDocument document = Doc(string.Concat(Enumerable.Repeat(line, 200_000)));
        ReaderLocation location = Location(10 * line.Length + 1, "Area 001", null);

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        TextRestore restore = TextLocator.Restore(document, location);
        clock.Stop();

        Assert.Equal(new TextRestore(10, new RestoreOutcome(RestoreKind.Context)), restore);
        Assert.True(clock.ElapsedMilliseconds < 50, $"Restore took {clock.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void ChangedGuideWithoutItsContextRestoresNearTheStoredEstimate()
    {
        static string Lines(string word) => string.Concat(
            Enumerable.Range(1, 400).Select(i => $"Line {i:0000} | {word} guide text.\n"));
        TextGuideDocument numbered = Doc(Lines("Numbered"));
        TextGuideDocument edited = Doc(Lines("Edited"));
        ReaderLocation captured = TextLocator.Capture(numbered, 150);
        string json = ReaderLocationCodec.Serialize(captured with
        {
            EstimatedFraction = ProgressEstimate.Bound(captured.EstimatedFraction),
        });

        LocationDecodeResult decoded = ReaderLocationCodec.Deserialize(
            json, GuideFormat.Txt, edited.ContentSha256.ToLowerInvariant());
        Assert.Equal(LocationDecodeStatus.ContentChanged, decoded.Status);
        TextRestore restore = TextLocator.Restore(edited, decoded.Location!);

        Assert.Equal(new TextRestore(150,
            new RestoreOutcome(RestoreKind.Approximate, TextLocator.ApproximateReason)), restore);
    }
}
