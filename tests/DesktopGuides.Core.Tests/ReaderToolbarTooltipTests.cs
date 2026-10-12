using System.Text.RegularExpressions;
using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

// Ruling 7: the toolbar's tooltips name the keys KeyboardShortcuts lists.
public sealed class ReaderToolbarTooltipTests
{
    [Theory]
    [InlineData("PageStart", ShortcutCommand.PageStart)]
    [InlineData("PreviousPage", ShortcutCommand.PreviousPage)]
    [InlineData("NextPage", ShortcutCommand.NextPage)]
    [InlineData("PageEnd", ShortcutCommand.PageEnd)]
    [InlineData("SmallerText", ShortcutCommand.ZoomOut)]
    [InlineData("LargerText", ShortcutCommand.ZoomIn)]
    [InlineData("ZoomOut", ShortcutCommand.ZoomOut)]
    [InlineData("ZoomIn", ShortcutCommand.ZoomIn)]
    [InlineData("GoToPage", ShortcutCommand.GoToPage)]
    [InlineData("FitToWidth", ShortcutCommand.ZoomReset)]
    [InlineData("ResetTextSize", ShortcutCommand.ZoomReset)]
    public void EachToolbarTooltipNamesItsKeys(string name, ShortcutCommand command)
    {
        string xaml = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "DesktopGuides.Production", "ReaderToolbar.xaml"));
        Match button = Regex.Match(xaml,
            $"<AppBarButton x:Name=\"{name}\"(?<attributes>[^>]*)>", RegexOptions.Singleline);
        Assert.True(button.Success, $"No AppBarButton named {name}.");
        Match tooltip = Regex.Match(button.Groups["attributes"].Value, "ToolTipService.ToolTip=\"(?<text>[^\"]*)\"");
        Assert.True(tooltip.Success, $"{name} has no tooltip.");
        Assert.EndsWith($"({KeyboardShortcuts.For(command).Keys})", tooltip.Groups["text"].Value);
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
