using DesktopGuides.Core.Library;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production.Materials;

internal static class WindowMaterials
{
    public static WindowMaterial Resolve(WindowMaterial requested) => requested switch
    {
        WindowMaterial.Mica when MicaController.IsSupported() => WindowMaterial.Mica,
        WindowMaterial.Acrylic when DesktopAcrylicController.IsSupported() =>
            WindowMaterial.Acrylic,
        _ => WindowMaterial.Solid
    };

    public static SystemBackdrop? CreateBackdrop(WindowMaterial effective) => effective switch
    {
        WindowMaterial.Mica => new MicaBackdrop { Kind = MicaKind.Base },
        WindowMaterial.Acrylic => new ThinAcrylicBackdrop(),
        _ => null
    };
}
