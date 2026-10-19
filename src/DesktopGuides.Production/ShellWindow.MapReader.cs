using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Production;

// T21.2: a map opens fitted, with no saved place and no progress tracking.
public sealed partial class ShellWindow
{
    private const string MapLoadMessage = "This map can't be shown. Its file may be missing or damaged.";

    private const string PreparingMapText = "Preparing the map…";

    private async Task<bool> OpenMapAsync(Guide guide, int generation)
    {
        ShowReaderSurface(loading: true);
        readerLoad = new CancellationTokenSource();
        CancellationToken token = readerLoad.Token;
        ImageReaderSession session = new(guide, mapStore!, mapCache!, cacheRoot!, ImageReaderSession.DiagnosticsEnabledForTest());
        try
        {
            string path = libraryPaths!.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);
            Task open = session.OpenAsync(new ManagedGuideSource(guide, path), token);
            // An uncached map waits for its overview to be built.
            if (session.Preparing && !open.IsCompleted)
            {
                ShowReaderSurface(loading: true, loadingText: PreparingMapText);
            }
            await open;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await session.DisposeAsync();
            return false;
        }
        catch (Exception error)
        {
            await session.DisposeAsync();
            if (generation != renderGeneration)
            {
                return false;
            }
            MarkReaderGuide(error is FileNotFoundException or DirectoryNotFoundException
                ? GuideFileStatus.Missing
                : GuideFileStatus.Damaged);
            ShowReaderSurface(loading: false, error: MapLoadMessage);
            ShowWarningStatus(MapLoadMessage);
            return true;
        }
        if (generation != renderGeneration)
        {
            await session.DisposeAsync();
            return false;
        }
        readerSession = session;
        MarkReaderGuide(GuideFileStatus.Ok);
        ShowReaderSurface(loading: false, view: session.View);
        ReaderActions.SetSession(session);
        // SetSession resets these, so they follow it.
        ReaderActions.KeysEnabled = true;
        ReaderActions.SetZoomAvailability(session.CanZoomIn, session.CanZoomOut);
        session.ZoomChanged += OnMapZoomChanged;
        // T21.2: back to the saved view, with no notice and no open recorded.
        return await OpenAtSavedPlaceAsync(
            guide, session, generation, guide.ContentSha256, null, token, quiet: true, recordOpen: false);
    }

    private void OnMapZoomChanged(object? sender, EventArgs args)
    {
        if (sender is ImageReaderSession session && ReferenceEquals(session, readerSession))
        {
            ReaderActions.SetZoomAvailability(session.CanZoomIn, session.CanZoomOut);
        }
    }
}
