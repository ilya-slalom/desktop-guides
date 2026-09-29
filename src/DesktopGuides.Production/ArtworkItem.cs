using System.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.Production;

// The view model behind DesktopGuidesArtworkRowTemplate.
public abstract class ArtworkItem : INotifyPropertyChanged
{
    private ImageSource? thumbnail;

    protected ArtworkItem(string title, string? summary, string? detail, string accessibleName)
    {
        Title = title;
        Summary = summary;
        Detail = detail;
        AccessibleName = accessibleName;
    }

    public string Title { get; }
    public string? Summary { get; }
    public string? Detail { get; }
    public string AccessibleName { get; }
    public bool HasSummary => Summary is not null;
    public bool HasDetail => Detail is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Starts empty, so the placeholder tile shows, and is set once artwork has loaded.
    public ImageSource? Thumbnail
    {
        get => thumbnail;
        internal set
        {
            if (ReferenceEquals(thumbnail, value)) return;
            thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }
}
