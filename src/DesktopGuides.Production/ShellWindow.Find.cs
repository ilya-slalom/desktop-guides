using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T18.1: while the bar is open, progress is paused, so moving to
    // matches never saves the reading place; Back returns to findStart.
    private IDisposable? findPause;
    private ReaderLocation? findStart;
    private CancellationTokenSource? findCancel;

    private void InitializeFind()
    {
        ReaderFind.SearchRequested += (_, direction) => _ = FindStepAsync(direction);
        ReaderFind.BackRequested += (_, _) => _ = FindBackAsync();
        ReaderFind.CloseRequested += (_, _) => CloseFind();
        ReaderActions.FindRequested += (_, _) => _ = OpenFindAsync();
    }

    private async Task OpenFindAsync()
    {
        if (readerSession is not IFindableReader)
        {
            ShowStatus("Search isn't available for this guide.", InfoBarSeverity.Informational, true, true);
            AnnounceStatus("Search isn't available for this guide.");
            return;
        }
        if (ReaderFind.Visibility != Visibility.Visible)
        {
            IReaderSession session = readerSession;
            findStart = await session.GetLocationAsync(CancellationToken.None);
            findPause = progressTracking is null
                ? null
                : await progressTracking.PauseAsync(CancellationToken.None);
            if (!ReferenceEquals(session, readerSession))
            {
                findPause?.Dispose();
                findPause = null;
                return;
            }
            ReaderFind.Reset();
            ReaderFind.Visibility = Visibility.Visible;
        }
        ReaderFind.FocusQuery();
    }

    private async Task FindStepAsync(FindDirection direction)
    {
        if (ReaderFind.Visibility != Visibility.Visible || readerSession is not IFindableReader findable)
        {
            return;
        }
        FindQuery query;
        try
        {
            query = new FindQuery(ReaderFind.Query);
        }
        catch (ArgumentException)
        {
            AnnounceStatus("Enter text to find.");
            return;
        }
        findCancel?.Cancel();
        findCancel = new CancellationTokenSource();
        FindResult result;
        try
        {
            result = await findable.FindAsync(query, direction, findCancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        ReaderFind.ShowResult(result, query.Text);
        AnnounceStatus(result.Outcome == FindOutcome.Found
            ? $"Match {result.Index} of {result.Total}{(result.TotalIsPartial ? " so far" : "")}."
            : $"No matches for {query.Text}.");
    }

    private async Task FindBackAsync()
    {
        if (readerSession is IReaderSession session && findStart is ReaderLocation start)
        {
            await session.RestoreLocationAsync(start, CancellationToken.None);
        }
    }

    // Also called when the reader closes; never saves the place.
    private void CloseFind()
    {
        findCancel?.Cancel();
        findCancel = null;
        if (readerSession is IFindableReader findable) findable.ClearFind();
        findPause?.Dispose();
        findPause = null;
        findStart = null;
        if (ReaderFind.Visibility == Visibility.Visible)
        {
            ReaderFind.Visibility = Visibility.Collapsed;
            // Keyboard stays in the reader: its keys route from here.
            ReaderActions.Focus(FocusState.Programmatic);
        }
    }
}
