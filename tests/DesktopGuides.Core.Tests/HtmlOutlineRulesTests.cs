using System.Text.Json;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlOutlineRulesTests
{
    private const string Sha = "ABCDEF";
    private const string Path = "guide.html";

    private static object Place(int offset, string id) =>
        new { offset, quote = $"text at {offset}", id, fraction = offset / 10_000.0 };

    private static object Heading(int level, string text, int offset) =>
        new { level, text, offset, quote = $"{text} text", id = text.ToLowerInvariant(), fraction = offset / 10_000.0 };

    private static object Link(string text, int container, int depth, int? target) =>
        new { text, container, depth, target = target is int at ? Place(at, text.ToLowerInvariant()) : null };

    private static Outline Build(object[] headings, object[] links) =>
        HtmlOutlineRules.Build(JsonSerializer.Serialize(new { headings, links }, TestJson.Options), Sha, Path);

    [Fact]
    public void HeadingsNestByLevel()
    {
        Outline outline = Build([Heading(1, "Guide", 0), Heading(2, "Forest", 100), Heading(2, "Castle", 200)], []);

        Assert.Equal(OutlineSource.Document, outline.Source);
        OutlineEntry guide = Assert.Single(outline.Entries);
        Assert.Equal(["Forest", "Castle"], guide.Children.Select(e => e.Title));
        Assert.Equal(200, guide.Children[1].Order);
        HtmlPosition position = Assert.IsType<HtmlPosition>(guide.Children[1].Location.Payload);
        Assert.Equal(200, position.TextOffset);
        Assert.Equal("castle", position.ElementId);
        Assert.Equal(Path, position.DocumentPath);
    }

    [Fact]
    public void ThePagesOwnContentsListWins()
    {
        Outline outline = Build([Heading(2, "Forest", 100), Heading(2, "Castle", 200)],
            [Link("Start here", 0, 1, 50), Link("The forest", 0, 1, 100), Link("North path", 0, 2, 150), Link("The castle", 0, 1, 200)]);

        Assert.Equal(["Start here", "The forest", "The castle"], outline.Entries.Select(e => e.Title));
        Assert.Equal(["North path"], outline.Entries[1].Children.Select(e => e.Title));
        Assert.Equal(150, outline.Entries[1].Children[0].Order);
    }

    [Fact]
    public void AShortListLeavesTheHeadings() =>
        Assert.Equal(["Forest", "Castle"], Build([Heading(2, "Forest", 100), Heading(2, "Castle", 200)],
            [Link("Top", 0, 1, 0), Link("Forest", 0, 1, 100)]).Entries.Select(e => e.Title));

    [Fact]
    public void LinksWithoutTargetsDontCount() =>
        Assert.Equal(["Forest", "Castle"], Build([Heading(2, "Forest", 100), Heading(2, "Castle", 200)],
            [Link("A", 0, 1, 10), Link("B", 0, 1, null), Link("C", 0, 1, 30)]).Entries.Select(e => e.Title));

    [Fact]
    public void TheLargestListWins() =>
        Assert.Equal(["One", "Two", "Three", "Four"], Build([],
            [Link("x", 0, 1, 1), Link("y", 0, 1, 2), Link("z", 0, 1, 3),
             Link("One", 1, 1, 10), Link("Two", 1, 1, 20), Link("Three", 1, 1, 30), Link("Four", 1, 1, 40)])
            .Entries.Select(e => e.Title));

    [Fact]
    public void TitlesCollapseWhitespaceAndEmptyOnesAreSkipped() =>
        Assert.Equal(["The forest"], Build([Heading(2, "The\n   forest", 100), Heading(2, "   ", 200)], [])
            .Entries.Select(e => e.Title));

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"headings\":[]}")]
    [InlineData("{\"headings\":[{\"level\":\"two\"}],\"links\":[]}")]
    public void ARepliesThatCantBeReadIsNoOutline(string? json) =>
        Assert.Same(Outline.Empty, HtmlOutlineRules.Build(json, Sha, Path));

    [Fact]
    public void NothingFoundIsNoOutline() => Assert.Same(Outline.Empty, Build([], []));
}
