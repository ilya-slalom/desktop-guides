using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ContentsPanePolicyTests
{
    [Theory]
    [InlineData(null, true, false, true)]
    [InlineData(null, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(null, true, true, false)]
    public void ThePaneShowsAsLeftOrForAMultiPageGuideUntilChosen(bool? remembered, bool multiPageHtml, bool narrow, bool shows) =>
        Assert.Equal(shows, ContentsPanePolicy.ShowOnOpen(remembered, multiPageHtml, narrow));
}
