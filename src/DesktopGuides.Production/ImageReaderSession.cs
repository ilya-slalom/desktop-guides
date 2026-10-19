using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopGuides.Production;

// T21.2 PR a: a map shown whole, fitted to the reader. It has no place,
// no progress and no commands; PR b adds zoom and the view place.
internal sealed class ImageReaderSession : IReaderSession
{
    private const int MaxSurface = 4096;
    private readonly Image image;

    public ImageReaderSession(Guide guide)
    {
        ArgumentNullException.ThrowIfNull(guide);
        image = new Image { Stretch = Stretch.Uniform };
        AutomationProperties.SetAutomationId(image, "MapImageView");
        AutomationProperties.SetName(image, guide.Title);
        View = new Grid { Children = { image } };
    }

    public Grid View { get; }

    public GuideFormat Format => GuideFormat.Image;

    public ReaderCapabilities Capabilities => ReaderCapabilities.None;

#pragma warning disable CS0067 // A map raises none of them.
    public event EventHandler? CapabilitiesChanged;
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;
    public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored;
#pragma warning restore CS0067

    // Neither side of the decoded surface exceeds 4096 pixels.
    public static int DecodeWidth(int width, int height) =>
        Math.Max(width, height) <= MaxSurface
            ? 0
            : (int)Math.Max(1, Math.Round(width * (double)MaxSurface / Math.Max(width, height)));

    public async Task OpenAsync(ManagedGuideSource source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        BitmapImage bitmap = new()
        {
            DecodePixelWidth = DecodeWidth(source.Guide.PixelWidth ?? 1, source.Guide.PixelHeight ?? 1),
        };
        await using FileStream file = new(
            source.PrimaryFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await bitmap.SetSourceAsync(file.AsRandomAccessStream()).AsTask(token);
        image.Source = bitmap;
    }

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token) =>
        throw new NotSupportedException("A map has no place yet.");

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token) =>
        Task.FromResult(new RestoreOutcome(RestoreKind.Unavailable));

    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token) =>
        throw new NotSupportedException("A map has no reader commands yet.");

    public ValueTask DisposeAsync()
    {
        image.Source = null;
        return ValueTask.CompletedTask;
    }
}
