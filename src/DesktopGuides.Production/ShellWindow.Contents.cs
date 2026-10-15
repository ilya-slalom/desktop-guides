using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T18.2: the Contents pane. Its open state lasts for the app session;
    // each guide's outline builds in the background once tracking starts.
    private const double ContentsOverlayBelow = 720;
    private const double ContentsGap = 12;
    private bool contentsOpen;
    private Outline? outline;
    private CancellationTokenSource? outlineCancel;

    private bool ContentsNarrow => ReaderBody.ActualWidth < ContentsOverlayBelow;

    private void InitializeContents()
    {
        ReaderContents.EntryInvoked += (_, entry) => _ = FollowContentsEntryAsync(entry);
        ReaderContents.CloseRequested += (_, _) => CloseContents(focusGuide: true);
        ReaderContents.SearchRequested += (_, _) =>
        {
            if (ContentsNarrow) CloseContents(focusGuide: false);
            _ = OpenFindAsync();
        };
        ReaderActions.ContentsRequested += (_, _) => ToggleContents();
        ReaderBody.SizeChanged += (_, _) => UpdateContentsLayout();
    }

    // Called once a session's tracking starts, so a restore has run.
    private void StartContents(IReaderSession session)
    {
        StopContents();
        if (session is not IOutlineReader reader) return;
        CancellationTokenSource cancel = new();
        outlineCancel = cancel;
        if (contentsOpen)
        {
            ShowContentsHost();
            ReaderContents.ShowBuilding();
        }
        _ = BuildOutlineAsync(session, reader, cancel.Token);
    }

    private async Task BuildOutlineAsync(IReaderSession session, IOutlineReader reader, CancellationToken token)
    {
        Outline built;
        try
        {
            built = await reader.GetOutlineAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // A failure is no outline; reading goes on.
            built = Outline.Empty;
        }
        // The guide was left, or another opened, while this one built.
        if (token.IsCancellationRequested || !ReferenceEquals(session, readerSession)) return;
        outline = built;
        if (ReaderContentsHost.Visibility == Visibility.Visible) await ShowOutlineAsync(focus: false);
    }

    private void ToggleContents()
    {
        if (ReaderContentsHost.Visibility == Visibility.Visible) CloseContents(focusGuide: true);
        else _ = OpenContentsAsync();
    }

    private bool ToggleContentsFromKeyboard()
    {
        // Before tracking starts there's no outline to show yet.
        if (readerSession is not IOutlineReader || outlineCancel is null) return false;
        ToggleContents();
        return true;
    }

    private async Task OpenContentsAsync()
    {
        if (readerSession is not IOutlineReader || outlineCancel is null) return;
        contentsOpen = true;
        ShowContentsHost();
        if (outline is null)
        {
            ReaderContents.ShowBuilding();
            ReaderContents.FocusContent();
            return;
        }
        await ShowOutlineAsync(focus: true);
    }

    private async Task ShowOutlineAsync(bool focus)
    {
        IReaderSession? session = readerSession;
        if (outline is not Outline shown || session is not IOutlineReader reader) return;
        ReaderContents.ShowOutline(shown, (session.Capabilities & ReaderCapabilities.Find) != 0);
        OutlineEntry? current = null;
        try
        {
            ReaderLocation place = await session.GetLocationAsync(CancellationToken.None);
            current = OutlineTree.EntryAt(shown, reader.OrderOf(place));
        }
        catch (Exception)
        {
            // No place yet: the first entry is selected.
        }
        if (!ReferenceEquals(session, readerSession) || !ReferenceEquals(shown, outline)) return;
        ReaderContents.Select(current);
        if (focus) ReaderContents.FocusContent();
        if (focus && shown.Entries.Count == 0) AnnounceStatus("No sections found in this guide.");
    }

    // A deliberate move: the restore raises LocationChanged, so the place saves.
    private async Task FollowContentsEntryAsync(OutlineEntry entry)
    {
        if (readerSession is not IReaderSession session) return;
        RestoreOutcome outcome;
        try
        {
            outcome = await session.RestoreLocationAsync(entry.Location, CancellationToken.None);
        }
        catch (Exception)
        {
            ShowErrorStatus("Couldn't go to that section.");
            return;
        }
        if (!ReferenceEquals(session, readerSession)) return;
        if (outcome.Kind == RestoreKind.Approximate) ShowReaderNotice(ApproximateRestoreMessage);
        else if (outcome.Kind == RestoreKind.Unavailable) ShowErrorStatus("Couldn't go to that section.");
        if (ContentsNarrow) CloseContents(focusGuide: false);
        FocusReaderContent();
    }

    private void CloseContents(bool focusGuide)
    {
        contentsOpen = false;
        HideContentsHost();
        if (focusGuide) FocusReaderContent();
    }

    // Also called when the reader closes. The open state is kept, so the
    // next guide opens with the pane as the user left it.
    private void StopContents()
    {
        outlineCancel?.Cancel();
        outlineCancel?.Dispose();
        outlineCancel = null;
        outline = null;
        ReaderContents.Clear();
        HideContentsHost();
    }

    private void ShowContentsHost()
    {
        ReaderContentsHost.Visibility = Visibility.Visible;
        UpdateContentsLayout();
    }

    private void HideContentsHost() => ReaderContentsHost.Visibility = Visibility.Collapsed;

    // Beside the guide when there's room; over it, without the gap, when not.
    private void UpdateContentsLayout()
    {
        bool narrow = ContentsNarrow;
        Grid.SetColumn(ReaderContentsHost, narrow ? 1 : 0);
        Canvas.SetZIndex(ReaderContentsHost, narrow ? 1 : 0);
        ReaderContentsHost.Margin = new Thickness(0, 0, narrow ? 0 : ContentsGap, 0);
    }

    private void FocusReaderContent()
    {
        bool focused = readerSession switch
        {
            TextReaderSession text => text.View.FocusLines(),
            PdfReaderSession pdf => pdf.View.FocusPreview(),
            HtmlReaderSession html => html.View.Focus(FocusState.Programmatic),
            _ => false
        };
        if (!focused) ReaderActions.Focus(FocusState.Programmatic);
    }
}
