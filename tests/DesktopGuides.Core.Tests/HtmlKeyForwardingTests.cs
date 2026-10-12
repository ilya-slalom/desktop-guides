using DesktopGuides.Core.Html;
using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class HtmlKeyForwardingTests
{
    [Theory]
    [InlineData("Alt+ArrowLeft", ShortcutCommand.Back)]
    [InlineData("Control+Equal", ShortcutCommand.ZoomIn)]
    [InlineData("Control+Shift+Equal", ShortcutCommand.ZoomIn)]
    [InlineData("Control+NumpadAdd", ShortcutCommand.ZoomIn)]
    [InlineData("Control+Minus", ShortcutCommand.ZoomOut)]
    [InlineData("Control+NumpadSubtract", ShortcutCommand.ZoomOut)]
    [InlineData("Control+Digit0", ShortcutCommand.ZoomReset)]
    [InlineData("Control+Numpad0", ShortcutCommand.ZoomReset)]
    [InlineData("Escape", ShortcutCommand.LeaveGuidePage)]
    public void TheForwardedKeysMapToTheirCommands(string payload, ShortcutCommand expected)
    {
        Assert.True(HtmlKeyForwarding.TryParse(payload, out ShortcutCommand command));
        Assert.Equal(expected, command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Control+KeyF")]
    [InlineData("Control+KeyO")]
    [InlineData("PageDown")]
    [InlineData("Control+Home")]
    [InlineData("Control+KeyG")]
    [InlineData("Alt+ArrowRight")]
    [InlineData("alt+arrowleft")]
    [InlineData("Ready")]
    public void EverythingElseIsRefused(string payload)
    {
        Assert.False(HtmlKeyForwarding.TryParse(payload, out _));
    }

    [Fact]
    public void TheScriptCallsOnlyItsBindingAndListsOnlyTheForwardedKeys()
    {
        string script = HtmlKeyForwarding.Script("desktopGuidesKey");

        Assert.Contains("desktopGuidesKey(", script);
        // The listener announces itself once installed, so a run can tell it's there.
        Assert.Contains("desktopGuidesKey('Ready')", script);
        Assert.Contains("'keydown'", script);
        foreach (string key in HtmlKeyForwarding.Payloads)
        {
            Assert.Contains($"'{key}'", script);
        }
        Assert.DoesNotContain("PageDown", script);
        Assert.DoesNotContain("KeyG", script);
    }
}
