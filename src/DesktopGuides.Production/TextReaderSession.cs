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

    // The initial text scale applies before the first measure (Ruling 5);
    // with a diagnostics folder the view reports each measure there.
    public TextReaderSession(
        TextGuideDocument document, int maxColumns, double textScale, string? diagnosticsFolder)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.document = document;
        string? diagnosticsPath = diagnosticsFolder is null
            ? null
            : Path.Combine(diagnosticsFolder, $"txt-text-size-{Environment.ProcessId}.json");
        View = new TextReaderView(new TextLineList(document), maxColumns, textScale, diagnosticsPath);
        View.TopLineChanged += OnTopLineChanged;
    }

    // Installed tests open the gate to read the view's applied size.
    public static string? DiagnosticsFolderForTest(string cacheRoot) =>
        TestGate.IsOpen($@"Local\DesktopGuides.Preview.TextDiagnostics.{Environment.ProcessId}")
            ? Path.Combine(cacheRoot, "diagnostics")
            : null;

    public TextReaderView View { get; }
    public GuideFormat Format => GuideFormat.Txt;
    public ReaderCapabilities Capabilities =>
        ReaderCapabilities.Scroll | ReaderCapabilities.PageNavigation | ReaderCapabilities.TextSize;

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
            case TextSizeAction size:
                View.TextScale = size.Scale;
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
