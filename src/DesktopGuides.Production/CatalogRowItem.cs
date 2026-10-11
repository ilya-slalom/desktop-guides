using CommunityToolkit.WinUI.Controls;
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production;

// The view model behind DesktopGuidesCatalogRowTemplate: a tile icon (a
// glyph, or a T11.5 colour image), the title, and a facts line. The facts' accessible text becomes the row's UIA
// help text; the name stays the title.
public abstract class CatalogRowItem : ArtworkItem
{
    protected CatalogRowItem(string title, AppIconChoice icon, IReadOnlyList<CatalogFact> facts)
        : base(title, null, null, title)
    {
        Glyph = icon.Glyph ?? string.Empty;
        IconImage = icon.IsAsset ? AppIcons.Image(icon.AssetFile!) : null;
        GlyphVisibility = icon.IsAsset ? Visibility.Collapsed : Visibility.Visible;
        IconImageVisibility = icon.IsAsset ? Visibility.Visible : Visibility.Collapsed;
        Facts = facts
            .Select(fact => new MetadataItem { Label = fact.Label, AccessibleLabel = fact.AccessibleLabel })
            .ToList();
        HelpText = CatalogPresentation.AccessibleText(facts);
    }

    public string Glyph { get; }
    public ImageSource? IconImage { get; }
    public Visibility GlyphVisibility { get; }
    public Visibility IconImageVisibility { get; }
    public IEnumerable<MetadataItem> Facts { get; }
    internal string HelpText { get; }
}
