using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ReaderCommandPolicyTests
{
    private const ReaderCapabilities Html =
        ReaderCapabilities.Scroll | ReaderCapabilities.TextSize | ReaderCapabilities.Find | ReaderCapabilities.Outline;

    // Review Focus 4.
    [Fact]
    public void OnePageGuidesShowNoPageCommands()
    {
        IReadOnlyList<ReaderCommand> commands = ReaderCommandPolicy.VisibleCommands(Html);

        Assert.DoesNotContain(ReaderCommand.PageTurn, commands);
        Assert.DoesNotContain(ReaderCommand.PageEdge, commands);
    }

    [Fact]
    public void AGuideOfSeveralPagesShowsThemOnce()
    {
        IReadOnlyList<ReaderCommand> commands = ReaderCommandPolicy.VisibleCommands(Html | ReaderCapabilities.GuidePages);

        Assert.Single(commands, command => command == ReaderCommand.PageTurn);
        Assert.Single(commands, command => command == ReaderCommand.PageEdge);
    }

    [Theory]
    [InlineData(PageKey.PageUp, false, PageKeyCommand.PreviousPage)]
    [InlineData(PageKey.PageDown, false, PageKeyCommand.NextPage)]
    [InlineData(PageKey.Home, true, PageKeyCommand.PageStart)]
    [InlineData(PageKey.End, true, PageKeyCommand.PageEnd)]
    [InlineData(PageKey.PageDown, true, null)]
    public void TxtAndPdfPageWithPageKeys(PageKey key, bool control, PageKeyCommand? expected) =>
        Assert.Equal(expected, PageKeyRouting.For(ReaderCapabilities.PageNavigation, key, control));

    // The user's choice (18 October 2026): the page scrolls with its own keys.
    [Theory]
    [InlineData(PageKey.PageUp, true, PageKeyCommand.PreviousPage)]
    [InlineData(PageKey.PageDown, true, PageKeyCommand.NextPage)]
    [InlineData(PageKey.PageUp, false, null)]
    [InlineData(PageKey.PageDown, false, null)]
    [InlineData(PageKey.Home, true, null)]
    [InlineData(PageKey.End, true, null)]
    public void AGuideOfSeveralPagesLeavesPageKeysToThePage(PageKey key, bool control, PageKeyCommand? expected) =>
        Assert.Equal(expected, PageKeyRouting.For(Html | ReaderCapabilities.GuidePages, key, control));

    [Fact]
    public void WithoutPagesNoKeyPages() =>
        Assert.Null(PageKeyRouting.For(Html, PageKey.PageDown, false));
}
