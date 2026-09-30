using CommunityToolkit.WinUI.Controls;
using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

// The view model behind DesktopGuidesCatalogRowTemplate: a tile glyph, the
// title, and a facts line. The facts' accessible text becomes the row's UIA
// help text; the name stays the title.
public abstract class CatalogRowItem : ArtworkItem
{
    protected CatalogRowItem(string title, string glyph, IReadOnlyList<CatalogFact> facts)
        : base(title, null, null, title)
    {
        Glyph = glyph;
        Facts = facts
            .Select(fact => new MetadataItem { Label = fact.Label, AccessibleLabel = fact.AccessibleLabel })
            .ToList();
        HelpText = CatalogPresentation.AccessibleText(facts);
    }

    public string Glyph { get; }
    public IEnumerable<MetadataItem> Facts { get; }
    internal string HelpText { get; }
}
