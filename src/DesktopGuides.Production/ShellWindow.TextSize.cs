using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

// T14.1: the open TXT or HTML guide's text size, read once at open and
// saved per guide. A size change isn't reader movement: no place is saved.
public sealed partial class ShellWindow
{
    private readonly SemaphoreSlim textSizeSaveGate = new(1, 1);
    // The size on screen, and the last one storage accepted, for the guide
    // of the current reader session (Ruling 6).
    private double readerTextScale = TextSizeSteps.Default;
    private double committedTextScale = TextSizeSteps.Default;
    private Guid textSizeGuideId;
    // T14.3: the size whose restore came back only by fraction. The save
    // and the restore can finish in either order; both show the notice.
    private double? shiftedTextScale;

    // A failed read gives the default: the size never blocks reading.
    private async Task<double> ReadTextScaleAsync(Guid guideId)
    {
        try
        {
            ReaderPreferences? preferences = await RequireRepository().GetReaderPreferencesAsync(guideId);
            return TextSizeSteps.Normalize(preferences?.TextScale);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return TextSizeSteps.Default;
        }
    }

    // After the toolbar has the session: it shows the guide's size.
    private void BeginTextSize(Guid guideId, double scale)
    {
        textSizeGuideId = guideId;
        readerTextScale = scale;
        committedTextScale = scale;
        ReaderActions.SetTextSize(scale);
    }

    // The toolbar has already applied the size (Ruling 3). Saves run one at
    // a time; only the latest change for the open guide counts (Ruling 8).
    private async void OnTextSizeChanged(IReaderSession session, double scale)
    {
        if (!ReferenceEquals(session, readerSession) || repository is null)
        {
            return;
        }
        Guid guideId = textSizeGuideId;
        readerTextScale = scale;
        shiftedTextScale = null;
        await textSizeSaveGate.WaitAsync();
        try
        {
            if (ReferenceEquals(session, readerSession) && scale != readerTextScale)
            {
                // A newer change is pending; its own save stores the size.
                return;
            }
            await repository.SaveReaderPreferencesAsync(guideId, scale);
            if (ReferenceEquals(session, readerSession))
            {
                committedTextScale = scale;
                if (scale == readerTextScale)
                {
                    AnnounceStatus(shiftedTextScale == scale
                        ? TextSizeSteps.ShiftedStatus(scale)
                        : TextSizeSteps.Status(scale));
                }
            }
        }
        catch (Exception error)
        {
            // A closed guide's save, or one a newer change replaced, leaves the screen alone.
            if (ReferenceEquals(session, readerSession) && scale == readerTextScale)
            {
                await RevertTextSizeAsync(session);
                ShowErrorStatus(TextSizeSteps.SaveFailed(error.Message));
            }
        }
        finally
        {
            textSizeSaveGate.Release();
        }
    }

    // T14.3: an HTML page that came back only by fraction says so, in the
    // reading card (T14.4).
    private void OnAppearanceRestored(object? sender, AppearanceRestoredEventArgs args)
    {
        if (!ReferenceEquals(sender, readerSession)) return;
        if (args.Outcome.Kind == RestoreKind.Exact)
        {
            shiftedTextScale = null;
            return;
        }
        shiftedTextScale = readerTextScale;
        ShowReaderNotice(TextSizeSteps.ShiftedNotice);
        AnnounceStatus(TextSizeSteps.ShiftedStatus(readerTextScale));
    }

    private async Task RevertTextSizeAsync(IReaderSession session)
    {
        double stored = committedTextScale;
        readerTextScale = stored;
        ReaderActions.SetTextSize(stored);
        try
        {
            await session.ExecuteAsync(new TextSizeAction(stored), CancellationToken.None);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
        {
            // A session closed mid-revert has nothing left to size.
        }
    }
}
