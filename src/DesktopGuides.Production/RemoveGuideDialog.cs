using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

/// <summary>Confirms a guide removal. Cancel is the default, so Enter and Escape both cancel.</summary>
internal static class RemoveGuideDialog
{
    public static ContentDialog Create(GuideRemovalPreview preview, XamlRoot root)
    {
        TextBlock message = new()
        {
            Text = GuideRemovalPresentation.DialogBody(preview.FileCount),
            Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(message, "RemoveGuideMessage");
        ContentDialog dialog = new()
        {
            Title = GuideRemovalPresentation.DialogTitle(preview.Title),
            Content = message,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        AutomationProperties.SetAutomationId(dialog, "RemoveGuideDialog");
        return dialog;
    }
}
