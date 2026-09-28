using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production.Materials;

internal static class DialogSurface
{
    public static void Apply(ContentDialog dialog, WindowMaterial material) =>
        dialog.Style = (Style)Application.Current.Resources[
            material == WindowMaterial.Acrylic
                ? "DesktopGuidesAcrylicDialogStyle"
                : "DefaultContentDialogStyle"];
}
