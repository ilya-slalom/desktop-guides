using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopGuides.Production;

// T11.5: turns a catalog choice into WinUI icons. Colour assets are content
// files: ms-appx in the MSIX, and the single-file exe's extraction folder
// in the portable build.
internal static class AppIcons
{
    public static IconElement Element(AppIconChoice choice)
    {
        IconElement icon = choice.IsAsset
            ? new ImageIcon { Source = Image(choice.AssetFile!) }
            : new FontIcon { Glyph = choice.Glyph! };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        return icon;
    }

    public static IconSource Source(AppIconChoice choice) => choice.IsAsset
        ? new ImageIconSource { ImageSource = Image(choice.AssetFile!) }
        : new FontIconSource { Glyph = choice.Glyph! };

    public static ImageSource Image(string file)
    {
        Uri uri = AppDataRoot.HasPackageIdentity()
            ? new Uri($"ms-appx:///{AppIconCatalog.AssetFolder}/{file}")
            : new Uri(Path.Combine(
                AppContext.BaseDirectory,
                AppIconCatalog.AssetFolder.Replace('/', Path.DirectorySeparatorChar),
                file));
        // Ruling 5: a PNG fallback is a catalog change only.
        return file.EndsWith(".png", StringComparison.Ordinal)
            ? new BitmapImage(uri)
            : new SvgImageSource(uri);
    }
}
