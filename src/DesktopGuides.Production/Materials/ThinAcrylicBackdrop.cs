using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production.Materials;

// DesktopAcrylicBackdrop has no Kind, so Thin acrylic needs its own controller.
// The default configuration follows the app theme and turns solid while the
// window is inactive.
internal sealed class ThinAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? controller;

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        controller.SetSystemBackdropConfiguration(
            GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
        controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        controller?.Dispose();
        controller = null;
    }
}
