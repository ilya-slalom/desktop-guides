using DesktopGuides.Core.Online;
using Xunit;

namespace DesktopGuides.Core.Tests.Online;

public sealed class OnlineNavigationTests
{
    private const string Faq = "https://www.neoseeker.com/chrono-trigger/faqs/131223-o.html";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ANewTabOnTheSourceOpensInTheBrowser(bool userInitiated) =>
        Assert.Equal(NewWindowAction.OpenHere,
            OnlineNavigation.ForNewWindow(new Uri(Faq), capturing: false, userInitiated));

    [Theory]
    [InlineData(Faq, true)]
    [InlineData("https://www.example.com/", true)]
    public void NothingOpensDuringACapture(string url, bool userInitiated) =>
        Assert.Equal(NewWindowAction.Ignore,
            OnlineNavigation.ForNewWindow(new Uri(url), capturing: true, userInitiated));

    [Theory]
    [InlineData("https://www.example.com/")]
    [InlineData("http://www.neoseeker.com/x")]
    public void AClickedNewTabOffTheSourcesOpensExternally(string url) =>
        Assert.Equal(NewWindowAction.OpenExternally,
            OnlineNavigation.ForNewWindow(new Uri(url), capturing: false, userInitiated: true));

    [Fact]
    public void AnUnclickedNewTabOffTheSourcesIsIgnored() =>
        Assert.Equal(NewWindowAction.Ignore,
            OnlineNavigation.ForNewWindow(new Uri("https://www.example.com/"), capturing: false, userInitiated: false));

    [Theory]
    [InlineData("mailto:a@b.c")]
    [InlineData(null)]
    public void ANewTabThatIsNotAWebPageIsIgnored(string? url) =>
        Assert.Equal(NewWindowAction.Ignore,
            OnlineNavigation.ForNewWindow(url is null ? null : new Uri(url), capturing: false, userInitiated: true));
}
