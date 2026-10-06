using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

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
        // The installed checks read the theme the dialog resolved.
        dialog.Opened += (sender, _) =>
            AutomationProperties.SetItemStatus(sender, sender.ActualTheme.ToString());
    }
}
