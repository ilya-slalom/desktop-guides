using System.Text;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ReflowHighlightsTests
{
    private const string Wrapped =
        "The forest path starts east of the village. Follow the stream until the\n" +
        "old bridge, then take the left fork past the ruined watchtower. Wolves\n" +
        "roam here at night, so travel by day and keep a torch in your pack.";

    private static (TextGuideDocument Document, ReflowPlan Plan) Planned(string text)
    {
        TextGuideDocument document = TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));
        return (document, TextReflowPlanner.Plan(document, [], default)!);
    }

    private static string Shown(ReflowPlan plan, (int Block, int Start, int Length) range) =>
        plan.Blocks[range.Block].Text.Substring(range.Start, range.Length);

    [Fact]
    public void AMatchInsideALineHighlightsThoseCharacters()
    {
        (TextGuideDocument document, ReflowPlan plan) = Planned(Wrapped + "\n\n" + Wrapped + "\n");
        int start = document.Text.IndexOf("village. Follow", StringComparison.Ordinal);

        var range = Assert.Single(ReflowHighlights.For(plan, start, "village. Follow".Length));

        Assert.Equal(0, range.Block);
        Assert.Equal("village. Follow", Shown(plan, range));
    }

    // A match across a source line break shows across the joining space.
    [Fact]
    public void AMatchAcrossALineBreakHighlightsTheJoinedText()
    {
        (TextGuideDocument document, ReflowPlan plan) = Planned(Wrapped + "\n\n" + Wrapped + "\n");
        int start = document.Text.IndexOf("the\nold", StringComparison.Ordinal);

        var range = Assert.Single(ReflowHighlights.For(plan, start, "the\nold".Length));

        Assert.Equal("the old", Shown(plan, range));
    }

    [Fact]
    public void AMatchInTheSecondBlockNamesThatBlock()
    {
        (TextGuideDocument document, ReflowPlan plan) = Planned(Wrapped + "\n\n" + Wrapped + "\n");
        int start = document.Text.LastIndexOf("Wolves", StringComparison.Ordinal);

        var range = Assert.Single(ReflowHighlights.For(plan, start, "Wolves".Length));

        Assert.Equal(1, range.Block);
        Assert.Equal("Wolves", Shown(plan, range));
    }

    [Fact]
    public void AnEmptyMatchHighlightsNothing()
    {
        (_, ReflowPlan plan) = Planned(Wrapped + "\n\n" + Wrapped + "\n");

        Assert.Empty(ReflowHighlights.For(plan, 10, 0));
    }

    [Theory]
    [InlineData(TextViewMode.Original, "Original view.")]
    [InlineData(TextViewMode.Reflow, "Reflow view.")]
    public void EachViewIsAnnounced(TextViewMode mode, string message) =>
        Assert.Equal(message, TextViewMessages.For(mode));

    [Fact]
    public void ReflowIsItsOwnCapability() =>
        Assert.Equal(512, (int)Reading.ReaderCapabilities.Reflow);
}
