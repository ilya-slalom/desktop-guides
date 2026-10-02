using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;

namespace DesktopGuides.Production;

// The Reader's session for one loaded TXT guide. The shell builds it from a
// document T08.1's loader returned.
internal sealed class TextReaderSession : IReaderSession
{
    private readonly TextGuideDocument document;
    private bool disposed;

    public TextReaderSession(TextGuideDocument document, int maxColumns)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.document = document;
        View = new TextReaderView(new TextLineList(document), maxColumns);
        View.TopLineChanged += OnTopLineChanged;
    }

    public TextReaderView View { get; }
    public GuideFormat Format => GuideFormat.Txt;
    public ReaderCapabilities Capabilities =>
        ReaderCapabilities.Scroll | ReaderCapabilities.PageNavigation;

    // Capabilities don't change during a TXT session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;

    public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
        throw new NotSupportedException("A TXT session is built from a loaded document.");

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(TextLocator.Capture(document, View.FirstVisibleIndex));
    }

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        TextRestore restore = TextLocator.Restore(document, location);
        token.ThrowIfCancellationRequested();
        if (!disposed && restore.Outcome.Kind != RestoreKind.Unavailable)
        {
            View.ScrollToLine(restore.Line);
        }
        return Task.FromResult(restore.Outcome);
    }

    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        token.ThrowIfCancellationRequested();
        switch (action)
        {
            case ScrollAction scroll:
                View.ScrollByViewport(scroll.VerticalViewportFraction);
                break;
            case PageTurnAction page:
                View.PageBy(page.Delta);
                break;
            case PageEdgeAction edge:
                View.ScrollToEdge(edge.Edge);
                break;
            default:
                throw new NotSupportedException($"TXT guides don't support {action.Command}.");
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            View.TopLineChanged -= OnTopLineChanged;
            View.Clear();
        }
        return ValueTask.CompletedTask;
    }

    private void OnTopLineChanged(object? sender, EventArgs args) =>
        LocationChanged?.Invoke(this, new LocationChangedEventArgs());
}
