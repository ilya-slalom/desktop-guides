using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Production;

// T21.2: a map opens fitted, with no saved place and no progress tracking.
public sealed partial class ShellWindow
{
    private const string MapLoadMessage = "This map can't be shown. Its file may be missing or damaged.";

    private async Task<bool> OpenMapAsync(Guide guide, int generation)
    {
        ShowReaderSurface(loading: true);
        readerLoad = new CancellationTokenSource();
        CancellationToken token = readerLoad.Token;
        ImageReaderSession session = new(guide);
        try
        {
            string path = libraryPaths!.ResolveExistingGuideFile(guide.Id, guide.PrimaryRelativePath);
            await session.OpenAsync(new ManagedGuideSource(guide, path), token);
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
        return true;
    }
}
