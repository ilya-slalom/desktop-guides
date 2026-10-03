using DesktopGuides.Core.Html;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlBrowserEnvironmentTests
{
    [Fact]
    public void ArgumentsSendEveryNetworkConnectionToADeadProxy()
    {
        string[] arguments = HtmlBrowserEnvironment.Arguments.Split(' ');
        Assert.Contains("--proxy-server=http://0.0.0.0:9", arguments);
        // Chromium bypasses the proxy for loopback unless told not to.
        Assert.Contains("--proxy-bypass-list=<-loopback>", arguments);
    }
}
