using DesktopGuides.Core.Backup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

/// <summary>Confirms replacing the library. Cancel is the default, so Enter and Escape both cancel.</summary>
internal static class ReplaceLibraryDialog
{
    public static ContentDialog Create(int currentGames, int currentGuides, LibraryRestoreStage stage, XamlRoot root)
    {
        TextBlock message = new()
        {
            Text = LibraryBackupMessages.ReplaceBody(currentGames, currentGuides, stage.Games, stage.Guides),
            Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(message, "ReplaceLibraryMessage");
        ContentDialog dialog = new()
        {
            Title = LibraryBackupMessages.ReplaceTitle,
            Content = message,
            PrimaryButtonText = "Replace",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        AutomationProperties.SetAutomationId(dialog, "ReplaceLibraryDialog");
        return dialog;
    }
}
