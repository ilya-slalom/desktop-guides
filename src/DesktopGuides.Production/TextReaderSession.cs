using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;

namespace DesktopGuides.Production;

// The Reader's session for one loaded TXT guide. The shell builds it from a
// document T08.1's loader returned; paging and restore arrive with T08.3.
internal sealed class TextReaderSession : IReaderSession
{
    private readonly TextGuideDocument document;
    private bool disposed;

    public TextReaderSession(TextGuideDocument document, int maxColumns)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.document = document;
        View = new TextReaderView(new TextLineList(document), maxColumns);
    }

    public TextReaderView View { get; }
    public GuideFormat Format => GuideFormat.Txt;
    public ReaderCapabilities Capabilities => ReaderCapabilities.Scroll;

    // Neither changes during a T08.2 session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    public event EventHandler<LocationChangedEventArgs>? LocationChanged { add { } remove { } }

    public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
        throw new NotSupportedException("A TXT session is built from a loaded document.");

    public Task<ReaderLocation> GetLocationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IReadOnlyList<int> starts = document.LineStarts;
        int line = Math.Clamp(View.FirstVisibleIndex, 0, starts.Count - 1);
        TextLocation captured = document.Capture(starts[line]);
        return Task.FromResult(new ReaderLocation(
            GuideFormat.Txt, captured.SchemaVersion, captured.ContentSha256,
            new TextPosition(captured.CharacterOffset, captured.ContextQuote), captured.Fraction));
    }

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token) =>
        Task.FromResult(new RestoreOutcome(
            RestoreKind.Unavailable, "Restoring a reading position isn't available yet."));

    public Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token) => Task.CompletedTask;

    public Task ExecuteAsync(ReaderAction action, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        token.ThrowIfCancellationRequested();
        if (action is not ScrollAction scroll)
        {
            throw new NotSupportedException($"TXT guides don't support {action.Command}.");
        }
        View.ScrollByViewport(scroll.VerticalViewportFraction);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            View.Clear();
        }
        return ValueTask.CompletedTask;
    }
}
