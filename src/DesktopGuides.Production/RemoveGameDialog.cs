using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

/// <summary>
/// Confirms removing a game, stating its guide and file counts. Cancel is the
/// default, so Enter and Escape both cancel.
/// </summary>
internal static class RemoveGameDialog
{
    public static ContentDialog Create(GameRemovalPreview preview, bool countChanged, XamlRoot root)
    {
        StackPanel content = new() { Spacing = 12 };
        if (countChanged)
        {
            content.Children.Add(Line(
                GameRemovalPresentation.CountChanged(preview.GuideCount), "RemoveGameCountChanged"));
        }
        content.Children.Add(Line(
            GameRemovalPresentation.DialogBody(preview.GuideCount, preview.FileCount), "RemoveGameMessage"));
        ContentDialog dialog = new()
        {
            Title = GameRemovalPresentation.DialogTitle(preview.Title, preview.GuideCount),
            Content = content,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        AutomationProperties.SetAutomationId(dialog, "RemoveGameDialog");
        return dialog;
    }

    private static TextBlock Line(string text, string automationId)
    {
        TextBlock line = new()
        {
            Text = text,
            Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(line, automationId);
        return line;
    }
}
