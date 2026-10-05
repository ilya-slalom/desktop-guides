using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // Returns false when a newer render took over, like the TXT path.
    private async Task<bool> OpenPdfGuideAsync(Guide guide, int generation)
    {
        ShowReaderSurface(placeholder: false);
        readerLoad = new CancellationTokenSource();
        CancellationToken token = readerLoad.Token;
        PdfGuideLoad load;
        try
        {
            load = await pdfLoader!.LoadAsync(guide, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        if (generation != renderGeneration)
        {
            // No session owns the text source yet.
            (load as PdfGuideLoaded)?.Text.Dispose();
            return false;
        }
        if (load is PdfGuideLoadFailed failed)
        {
            ShowPdfLoadError(failed.Error);
            return true;
        }
        PdfGuideLoaded loaded = (PdfGuideLoaded)load;
        PdfReaderSession session = new(guide, loaded, cacheRoot!, PdfReaderSession.DiagnosticsEnabledForTest());
        // The next render disposes it if this one is cancelled.
        readerSession = session;
        session.Failed += OnPdfSessionFailed;
        ShowReaderSurface(placeholder: false, view: session.View);
        try
        {
            await session.OpenAsync(new ManagedGuideSource(guide, loaded.FilePath), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (PdfGuideLoadException error)
        {
            if (generation != renderGeneration)
            {
                return false;
            }
            readerSession = null;
            ShowReaderSurface(placeholder: false);
            await session.DisposeAsync();
            ShowPdfLoadError(error.Error);
            return true;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        ReaderActions.SetSession(session);
        return await OpenAtSavedPlaceAsync(
            guide, session, generation, guide.ContentSha256.ToLowerInvariant(), null, token);
    }

    private void ShowPdfLoadError(PdfGuideLoadError error)
    {
        string message = PdfGuideLoadMessages.For(error);
        ShowReaderSurface(placeholder: false, error: message, action: PdfGuideLoadMessages.ActionFor(error));
        ShowWarningStatus(message);
    }

    // A late event from a session that a newer render replaced is ignored.
    private async void OnPdfSessionFailed(object? sender, PdfGuideLoadError error)
    {
        await RunNavigationAsync(async () =>
        {
            if (!ReferenceEquals(sender, readerSession))
            {
                return;
            }
            IReaderSession failed = readerSession!;
            readerSession = null;
            ReaderActions.SetSession(null);
            ShowReaderSurface(placeholder: false);
            await failed.DisposeAsync();
            ShowPdfLoadError(error);
        });
    }
}
