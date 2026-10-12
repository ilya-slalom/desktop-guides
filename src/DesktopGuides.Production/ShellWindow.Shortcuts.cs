using CommunityToolkit.WinUI.Controls;
using DesktopGuides.Core.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T16.1: the Settings list comes from KeyboardShortcuts, so it can't drift.
    private void InitializeShortcutList()
    {
        SettingsHelpHeading.Text = KeyboardShortcuts.SectionHeading;
        KeyboardShortcutsExpander.Header = KeyboardShortcuts.ExpanderHeader;
        KeyboardShortcutsExpander.Description = KeyboardShortcuts.ExpanderDescription;
        AutomationProperties.SetName(KeyboardShortcutsExpander,
            $"{KeyboardShortcuts.ExpanderHeader}. {KeyboardShortcuts.ExpanderDescription}");
        foreach (IGrouping<ShortcutContext, KeyboardShortcut> group in
                 KeyboardShortcuts.All.GroupBy(shortcut => shortcut.Context))
        {
            TextBlock heading = new()
            {
                Text = KeyboardShortcuts.ContextHeading(group.Key),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                Margin = new Thickness(0, 8, 0, 0)
            };
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level3);
            KeyboardShortcutsExpander.Items.Add(heading);
            foreach (KeyboardShortcut shortcut in group)
            {
                SettingsCard row = new()
                {
                    Header = shortcut.Description,
                    Content = new TextBlock { Text = shortcut.Keys }
                };
                AutomationProperties.SetAutomationId(row, $"Shortcut{shortcut.Command}");
                AutomationProperties.SetName(row, KeyboardShortcuts.AutomationName(shortcut));
                KeyboardShortcutsExpander.Items.Add(row);
            }
        }
    }
}
