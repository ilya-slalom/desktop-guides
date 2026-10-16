using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml;

namespace DesktopGuides.Production;

// The Reader's session for one loaded TXT guide. The shell builds it from a
// document T08.1's loader returned.
internal sealed class TextReaderSession : IReaderSession, IFindableReader, IOutlineReader
{
    private readonly TextGuideDocument document;
    private bool disposed;
    private readonly string? placeFile;
    private double textScale;
    private bool planned;
    private ReflowPlan? plan;
    private TextReflowView? reflowView;
    private TextMatch? shownMatch;

    // T19.2: the place file for installed tests, behind the progress gate.
    public static string? PlaceFileForTest(string cacheRoot) =>
        TestGate.IsOpen(AppLane.Current.LocalEvent("ProgressDiagnostics", Environment.ProcessId))
            ? Path.Combine(cacheRoot, "diagnostics", $"txt-place-{Environment.ProcessId}.json")
            : null;

    public TextViewMode Mode { get; private set; } = TextViewMode.Original;
    public UIElement Surface => Mode == TextViewMode.Reflow && reflowView is not null ? reflowView : View;
    public event EventHandler? ViewChanged;

    private int FirstVisibleOffset => Mode == TextViewMode.Reflow && reflowView is not null
        ? reflowView.FirstVisibleOffset
        : document.LineStarts[Math.Clamp(View.FirstVisibleIndex, 0, document.LineStarts.Count - 1)];

    // T19.2: shows the guide in the other view at the same block. Reflow is
    // planned once, off the UI thread, within the planner's budget. False
    // when Reflow has no plan: no prose, a failure, or out of time.
    public async Task<bool> SetViewAsync(TextViewMode mode, CancellationToken token)
    {
        if (disposed) return false;
        if (mode == Mode) return true;
        int offset = FirstVisibleOffset;
        if (mode == TextViewMode.Reflow)
        {
            if (!planned)
            {
                planned = true;
                TextGuideDocument source = document;
                using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(token);
                budget.CancelAfter(TextReflowPlanner.Budget);
                try
                {
                    plan = await Task.Run(
                        () => TextReflowPlanner.Plan(source, TextSectionDetector.Detect(source.Text, budget.Token), budget.Token),
                        CancellationToken.None);
                }
                catch (Exception)
                {
                    // A planning failure is no plan; the guide stays readable in Original.
                    plan = null;
                }
                token.ThrowIfCancellationRequested();
            }
            if (disposed || plan is null) return false;
            if (reflowView is null)
            {
                reflowView = new TextReflowView(plan, textScale);
                reflowView.TopBlockChanged += OnTopLineChanged;
                if (shownMatch is TextMatch match) reflowView.ShowMatch(ReflowHighlights.For(plan, match.Start, match.Length));
            }
            reflowView.ScrollToOffset(offset);
        }
        else
        {
            View.ScrollToLine(document.LineAtOffset(offset));
        }
        Mode = mode;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        WritePlaceForTest();
        return true;
    }

    public bool FocusSurface() =>
        Mode == TextViewMode.Reflow && reflowView is not null ? reflowView.FocusBlocks() : View.FocusLines();

    // Test gate only: the view and its first visible offset.
    private void WritePlaceForTest()
    {
        if (placeFile is null || disposed) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(placeFile)!);
            DiagnosticFile.Write(placeFile, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{{\"view\":\"{Mode}\",\"offset\":{FirstVisibleOffset},\"scale\":{textScale:R},\"lineScale\":{View.TextScale:R}}}"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must never affect reading.
        }
    }

    // The initial text scale applies before the first measure (Ruling 5);
    // with a diagnostics folder the view reports each measure there.
    public TextReaderSession(
        TextGuideDocument document, int maxColumns, double textScale, string? diagnosticsFolder,
        string? placeFile = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.document = document;
        this.textScale = textScale;
        this.placeFile = placeFile;
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
        ReaderCapabilities.Find | ReaderCapabilities.Outline | ReaderCapabilities.Reflow;

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
        int top = FirstVisibleOffset;
        current = TextMatcher.Step(matches, current, top, direction);
        if (current is not int index)
        {
            shownMatch = null;
            View.ClearMatch();
            reflowView?.ClearMatch();
            return FindResult.None;
        }
        TextMatch match = matches[index];
        shownMatch = match;
        View.ShowMatch(Segments(match));
        reflowView?.ShowMatch(ReflowHighlights.For(plan!, match.Start, match.Length));
        if (Mode == TextViewMode.Reflow && reflowView is not null)
        {
            reflowView.ScrollToOffset(match.Start);
        }
        else
        {
            // A little context above the match, as a reader expects.
            View.ScrollToLine(Math.Max(0, document.LineAtOffset(match.Start) - 3));
        }
        return new FindResult(FindOutcome.Found, index + 1, matches.Count, false, 0);
    }

    public void ClearFind()
    {
        findText = null;
        matches = [];
        current = null;
        shownMatch = null;
        View.ClearMatch();
        reflowView?.ClearMatch();
    }

    // T18.2: sections from the guide's own markup, found off the UI thread
    // within the detector's budget. None found, or out of time, is no outline.
    public async Task<Outline> GetOutlineAsync(CancellationToken token)
    {
        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TextSectionDetector.Budget);
        TextGuideDocument source = document;
        IReadOnlyList<DetectedSection> sections =
            await Task.Run(() => TextSectionDetector.Detect(source.Text, budget.Token), CancellationToken.None);
        token.ThrowIfCancellationRequested();
        return OutlineTree.Build(OutlineSource.Detected, sections.Select(section =>
        {
            int line = source.LineAtOffset(section.Offset);
            return new OutlineItem(section.Title, section.Level, TextLocator.Capture(source, line), source.LineStarts[line]);
        }));
    }

    public long OrderOf(ReaderLocation location) =>
        location.Payload is TextPosition position ? position.CharacterOffset : 0;

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
        return Task.FromResult(TextLocator.CaptureAt(document, FirstVisibleOffset));
    }

    public Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(location);
        TextRestore restore = TextLocator.Restore(document, location);
        token.ThrowIfCancellationRequested();
        if (!disposed && restore.Outcome.Kind != RestoreKind.Unavailable)
        {
            if (Mode == TextViewMode.Reflow && reflowView is not null)
            {
                reflowView.ScrollToOffset(document.LineStarts[Math.Clamp(restore.Line, 0, document.LineStarts.Count - 1)]);
            }
            else
            {
                View.ScrollToLine(restore.Line);
            }
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
                if (Mode == TextViewMode.Reflow && reflowView is not null) reflowView.ScrollByViewport(scroll.VerticalViewportFraction);
                else View.ScrollByViewport(scroll.VerticalViewportFraction);
                break;
            case PageTurnAction page:
                if (Mode == TextViewMode.Reflow && reflowView is not null) reflowView.PageBy(page.Delta);
                else View.PageBy(page.Delta);
                break;
            case PageEdgeAction edge:
                if (Mode == TextViewMode.Reflow && reflowView is not null) reflowView.ScrollToEdge(edge.Edge);
                else View.ScrollToEdge(edge.Edge);
                break;
            case TextSizeAction size:
                textScale = size.Scale;
                View.TextScale = size.Scale;
                if (reflowView is not null) reflowView.TextScale = size.Scale;
                WritePlaceForTest();
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
            if (reflowView is not null)
            {
                reflowView.TopBlockChanged -= OnTopLineChanged;
                reflowView.Clear();
            }
        }
        return ValueTask.CompletedTask;
    }

    private void OnTopLineChanged(object? sender, EventArgs args)
    {
        WritePlaceForTest();
        LocationChanged?.Invoke(this, new LocationChangedEventArgs());
    }
}
