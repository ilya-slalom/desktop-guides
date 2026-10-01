using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

/// <summary>Confirms removing a game without guides. Cancel is the default, so Enter and Escape both cancel.</summary>
internal static class RemoveGameDialog
{
    public static ContentDialog Create(string title, XamlRoot root)
    {
        TextBlock message = new()
        {
            Text = GameRemovalPresentation.DialogBody,
            Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(message, "RemoveGameMessage");
        ContentDialog dialog = new()
        {
            Title = GameRemovalPresentation.DialogTitle(title),
            Content = message,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        AutomationProperties.SetAutomationId(dialog, "RemoveGameDialog");
        return dialog;
    }
}
