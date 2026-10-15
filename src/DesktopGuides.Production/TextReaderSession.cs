using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;

namespace DesktopGuides.Production;

// The Reader's session for one loaded TXT guide. The shell builds it from a
// document T08.1's loader returned.
internal sealed class TextReaderSession : IReaderSession, IFindableReader
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
        TestGate.IsOpen(AppLane.Current.LocalEvent("TextDiagnostics", Environment.ProcessId))
            ? Path.Combine(cacheRoot, "diagnostics")
            : null;

    public TextReaderView View { get; }
    public GuideFormat Format => GuideFormat.Txt;
    public ReaderCapabilities Capabilities =>
        ReaderCapabilities.Scroll | ReaderCapabilities.PageNavigation | ReaderCapabilities.TextSize |
        ReaderCapabilities.Find;

    // Capabilities don't change during a TXT session.
    public event EventHandler? CapabilitiesChanged { add { } remove { } }
    // T14.3: their layout keeps its own place; nothing to report.
    public event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored { add { } remove { } }
    public event EventHandler<LocationChangedEventArgs>? LocationChanged;
    // T18.1: a step's result is final when it returns.
    public event EventHandler<FindResult>? ResultUpdated { add { } remove { } }

    // T18.1: the last query's matches, so Next and Previous step through them.
    private string? findText;
    private IReadOnlyList<TextMatch> matches = [];
    private int? current;

    public async Task<FindResult> FindAsync(FindQuery query, FindDirection direction, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Text != findText)
        {
            string text = document.Text;
            matches = await Task.Run(() => TextMatcher.FindAll(text, query), token);
            findText = query.Text;
            current = null;
        }
        token.ThrowIfCancellationRequested();
        int top = document.LineStarts[Math.Clamp(View.FirstVisibleIndex, 0, document.LineStarts.Count - 1)];
        current = TextMatcher.Step(matches, current, top, direction);
        if (current is not int index)
        {
            View.ClearMatch();
            return FindResult.None;
        }
        TextMatch match = matches[index];
        View.ShowMatch(Segments(match));
        // A little context above the match, as a reader expects.
        View.ScrollToLine(Math.Max(0, document.LineAtOffset(match.Start) - 3));
        return new FindResult(FindOutcome.Found, index + 1, matches.Count, false, 0);
    }

    public void ClearFind()
    {
        findText = null;
        matches = [];
        current = null;
        View.ClearMatch();
    }

    private IReadOnlyList<(int Line, int Column, int Length)> Segments(TextMatch match)
    {
        int first = document.LineAtOffset(match.Start);
        int last = document.LineAtOffset(match.Start + match.Length - 1);
        List<(int Line, int Column, int Length)> segments = [];
        for (int line = first; line <= last; line++)
        {
            int lineStart = document.LineStarts[line];
            int from = Math.Max(match.Start, lineStart);
            int to = match.Start + match.Length;
            int column = TextLineView.ColumnOf(document, line, from);
            int endColumn = TextLineView.ColumnOf(document, line, to);
            segments.Add((line, column, Math.Max(0, endColumn - column)));
        }
        return segments;
    }

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
