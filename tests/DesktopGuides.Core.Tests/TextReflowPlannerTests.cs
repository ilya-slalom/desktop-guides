using System.Text;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextReflowPlannerTests
{
    private static TextGuideDocument Doc(string text) =>
        TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));

    private static ReflowPlan? Plan(TextGuideDocument document) =>
        TextReflowPlanner.Plan(document, TextSectionDetector.Detect(document.Text, default), default);

    private static TextGuideDocument Fixture() =>
        TextGuideDocument.Decode(File.ReadAllBytes(Path.Combine(RepoRoot(), "tests", "fixtures", "p2", "txt-reflow.txt")));

    private static string Prose(params string[] paragraphs) => string.Join("\n\n", paragraphs) + "\n";

    // Three wrapped lines near seventy columns: prose by every rule.
    private const string Wrapped =
        "The forest path starts east of the village. Follow the stream until the\n" +
        "old bridge, then take the left fork past the ruined watchtower. Wolves\n" +
        "roam here at night, so travel by day and keep a torch in your pack.";

    [Fact]
    public void TheFixtureSplitsIntoProseAndPreservedBlocks()
    {
        ReflowPlan plan = Plan(Fixture())!;

        Assert.Equal(
            [
                ReflowKind.Preserved, ReflowKind.Prose, ReflowKind.Prose, ReflowKind.Preserved,
                ReflowKind.Preserved, ReflowKind.Preserved, ReflowKind.Preserved, ReflowKind.Preserved,
                ReflowKind.Prose
            ],
            plan.Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void ProseJoinsLinesWithOneSpaceAndKeepsLineEndHyphens()
    {
        ReflowBlock last = Plan(Fixture())!.Blocks[^1];

        Assert.DoesNotContain('\n', last.Text);
        Assert.Contains("rises again. A well-timed dash", last.Text);
        Assert.Contains("they're right- handed archers", last.Text);
    }

    [Fact]
    public void PreservedBlocksKeepTheirSourceText()
    {
        TextGuideDocument document = Fixture();
        ReflowBlock map = Plan(document)!.Blocks[4];

        Assert.StartsWith("      N\n      |\n", map.Text);
        Assert.Equal(document.Text.Substring(map.Segments[0].SourceStart, map.Text.Length), map.Text);
    }

    [Fact]
    public void BlocksCoverTheTextInOrder()
    {
        TextGuideDocument document = Fixture();
        IReadOnlyList<ReflowBlock> blocks = Plan(document)!.Blocks;

        Assert.Equal(0, blocks[0].Start);
        for (int i = 1; i < blocks.Count; i++) Assert.Equal(blocks[i - 1].End, blocks[i].Start);
        Assert.Equal(document.Text.Length, blocks[^1].End);
    }

    // The exit's offset round trip: every display index maps to a source
    // offset inside its block, and back to itself.
    [Fact]
    public void EveryDisplayIndexRoundTrips()
    {
        foreach (ReflowBlock block in Plan(Fixture())!.Blocks)
        {
            for (int index = 0; index <= block.Text.Length; index++)
            {
                int source = block.SourceOffsetAt(index);
                Assert.InRange(source, block.Start, block.End);
                Assert.Equal(index, block.DisplayIndexOf(source));
            }
        }
    }

    [Fact]
    public void EverySourceOffsetFindsItsBlockInOrder()
    {
        TextGuideDocument document = Fixture();
        ReflowPlan plan = Plan(document)!;
        int lastBlock = 0;
        int lastIndex = 0;
        for (int offset = 0; offset < document.Text.Length; offset++)
        {
            int at = plan.BlockAt(offset);
            ReflowBlock block = plan.Blocks[at];
            Assert.InRange(offset, block.Start, block.End - 1);
            int index = block.DisplayIndexOf(offset);
            if (at == lastBlock) Assert.True(index >= lastIndex, $"Offset {offset} went back in block {at}.");
            (lastBlock, lastIndex) = (at, index);
        }
    }

    [Fact]
    public void ProseMayHoldNumbersAndQuotes()
    {
        string text = Prose(Wrapped,
            "Level 12 is 'hard', isn't it? The bandits wait by the eastern marsh\n" +
            "gate, and the guards say (quietly) that \"nobody\" crosses at noon.");

        Assert.All(Plan(Doc(text))!.Blocks, block => Assert.Equal(ReflowKind.Prose, block.Kind));
    }

    // Final review: guides write HP/MP, 50%, +10 Attack, [WLK3], curly
    // quotes and dashes in ordinary prose.
    [Fact]
    public void ProseMayHoldGameSymbols()
    {
        string text = Prose(Wrapped,
            "Equip the Iron Sword for +10 Attack and keep HP/MP above 50% before the\n" +
            "boss in [WLK3]. It\u2019s \u201ceasy\u201d \u2014 if you heal at $500 & save first.");

        Assert.All(Plan(Doc(text))!.Blocks, block => Assert.Equal(ReflowKind.Prose, block.Kind));
    }

    // Final review: older ASCII guides put two spaces after a sentence.
    [Fact]
    public void TwoSpacesAfterASentenceStayProse()
    {
        string text = Prose(Wrapped.Replace("village. Follow", "village.  Follow"),
            Wrapped.Replace("watchtower. Wolves", "watchtower.  Wolves"));

        Assert.All(Plan(Doc(text))!.Blocks, block => Assert.Equal(ReflowKind.Prose, block.Kind));
    }

    // Final review: a list of long items must not run together.
    [Fact]
    public void AListOfLongItemsIsPreserved()
    {
        string list =
            "1. Talk to the elder in the village and take the Sword from the chest.\n" +
            "2. Go to the cave north of the village and fight the boss at the end.";

        Assert.Equal([ReflowKind.Prose, ReflowKind.Preserved], Plan(Doc(Prose(Wrapped, list)))!.Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void AnInteriorDoubleSpaceIsPreserved()
    {
        string text = Prose(Wrapped, Wrapped.Replace("fork past", "fork  past"));

        Assert.Equal([ReflowKind.Prose, ReflowKind.Preserved], Plan(Doc(text))!.Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void AHeadingParagraphIsPreserved()
    {
        TextGuideDocument document = Doc(Prose(Wrapped, Wrapped));
        int second = document.Text.IndexOf("\n\n", StringComparison.Ordinal) + 2;

        ReflowPlan plan = TextReflowPlanner.Plan(document, [new DetectedSection(second, 1, "Forest")], default)!;

        Assert.Equal([ReflowKind.Prose, ReflowKind.Preserved], plan.Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void LeadingBlankLinesBelongToTheFirstBlock()
    {
        ReflowPlan plan = Plan(Doc("\n\n" + Prose(Wrapped, Wrapped)))!;

        Assert.Equal(0, plan.Blocks[0].Start);
        Assert.StartsWith("The forest path", plan.Blocks[0].Text);
        Assert.Equal(2, plan.Blocks[0].SourceOffsetAt(0));
    }

    [Fact]
    public void AGuideWithNoProseHasNoPlan() =>
        Assert.Null(Plan(Doc(string.Concat(Enumerable.Range(1, 400).Select(n => $"Line {n:0000} | Numbered guide text.\n")))));

    [Theory]
    [InlineData("")]
    [InlineData("\n\n   \n")]
    public void AnEmptyGuideHasNoPlan(string text) => Assert.Null(Plan(Doc(text)));

    [Fact]
    public void ACancelledPlanIsNone()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Null(TextReflowPlanner.Plan(Fixture(), [], cancelled.Token));
    }

    // TR19.1: planning only reads; the text, hash and locators are unchanged.
    [Fact]
    public void PlanningLeavesTheDocumentUnchanged()
    {
        TextGuideDocument document = Fixture();
        string text = document.Text;
        string hash = document.ContentSha256;
        int[] starts = [.. document.LineStarts];
        Reading.ReaderLocation before = TextLocator.Capture(document, 12);

        Assert.NotNull(Plan(document));

        Assert.Same(text, document.Text);
        Assert.Equal(hash, document.ContentSha256);
        Assert.Equal(starts, document.LineStarts);
        Assert.Equal(before, TextLocator.Capture(document, 12));
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DesktopGuides.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("DesktopGuides.sln not found.");
    }
}
