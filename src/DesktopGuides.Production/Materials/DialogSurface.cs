using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production.Materials;

internal static class DialogSurface
{
    // A ContentDialog sits in the popup root, so it doesn't inherit the
    // shell root's RequestedTheme.
    public static void Apply(ContentDialog dialog, WindowMaterial material, ElementTheme theme)
    {
        dialog.Style = (Style)Application.Current.Resources[
            material == WindowMaterial.Acrylic
                ? "DesktopGuidesAcrylicDialogStyle"
                : "DefaultContentDialogStyle"];
        dialog.RequestedTheme = theme;
        // The installed checks read the theme the dialog resolved. UIA shows
        // the dialog through its Popup host, which takes the dialog's
        // AutomationId but not its ItemStatus.
        dialog.Opened += (sender, _) =>
        {
            string resolved = sender.ActualTheme.ToString();
            AutomationProperties.SetItemStatus(sender, resolved);
            foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(sender.XamlRoot)
                .Where(popup => popup.Child == sender))
            {
                AutomationProperties.SetItemStatus(popup, resolved);
            }
        };
    }
}
