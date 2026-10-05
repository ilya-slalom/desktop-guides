using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    private Uri? pendingExternalLink;

    // Returns false when a newer render took over, like the TXT path.
    private async Task<bool> OpenHtmlGuideAsync(Guide guide, int generation)
    {
        ShowReaderSurface(placeholder: false);
        readerLoad = new CancellationTokenSource();
        CancellationToken token = readerLoad.Token;
        HtmlGuideLoad load;
        try
        {
            load = await htmlLoader!.LoadAsync(guide, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        if (load is HtmlGuideLoadFailed failed)
        {
            ShowHtmlLoadError(failed.Error);
            return true;
        }
        HtmlGuideLoaded loaded = (HtmlGuideLoaded)load;
        HtmlReaderSession session = new(loaded, cacheRoot!, HtmlReaderSession.DiagnosticsForTest());
        // The next render disposes it if this one is cancelled.
        readerSession = session;
        session.ExternalLinkRequested += OnExternalLinkRequested;
        session.UnavailableLinkRequested += OnUnavailableLinkRequested;
        session.Failed += OnReaderSessionFailed;
        ShowReaderSurface(placeholder: false, view: session.View);
        try
        {
            await session.OpenAsync(new ManagedGuideSource(guide, loaded.EntryFilePath), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (HtmlGuideLoadException error)
        {
            if (generation != renderGeneration)
            {
                return false;
            }
            readerSession = null;
            ShowReaderSurface(placeholder: false);
            await session.DisposeAsync();
            ShowHtmlLoadError(error.Error);
            return true;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        ReaderActions.SetSession(session);
        return await OpenAtSavedPlaceAsync(
            guide, session, generation, guide.ContentSha256.ToLowerInvariant(),
            loaded.Policy.Entry.RequestPath, token);
    }

    private void ShowHtmlLoadError(HtmlGuideLoadError error)
    {
        string message = HtmlGuideLoadMessages.For(error);
        ShowReaderSurface(placeholder: false, error: message, action: HtmlGuideLoadMessages.ActionFor(error));
        ShowWarningStatus(message);
    }

    // A late event from a session that a newer render replaced is ignored.
    private async void OnReaderSessionFailed(object? sender, HtmlGuideLoadError error)
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
            HideExternalLinkBar();
            HideUnavailableLinkBar();
            ShowReaderSurface(placeholder: false);
            await failed.DisposeAsync();
            ShowHtmlLoadError(error);
        });
    }

    // A newer link replaces the URL in an open bar.
    private void OnExternalLinkRequested(object? sender, Uri uri)
    {
        if (!ReferenceEquals(sender, readerSession))
        {
            return;
        }
        HideUnavailableLinkBar();
        pendingExternalLink = uri;
        ReaderExternalLinkUrl.Text = uri.AbsoluteUri;
        ReaderExternalLinkBar.Visibility = Visibility.Visible;
        ReaderExternalLinkBar.IsOpen = true;
    }

    private void HideExternalLinkBar()
    {
        pendingExternalLink = null;
        ReaderExternalLinkBar.IsOpen = false;
        ReaderExternalLinkBar.Visibility = Visibility.Collapsed;
        ReaderExternalLinkUrl.Text = string.Empty;
    }

    // Showing either link bar hides the other.
    private void OnUnavailableLinkRequested(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, readerSession))
        {
            return;
        }
        HideExternalLinkBar();
        ReaderUnavailableLinkBar.Visibility = Visibility.Visible;
        ReaderUnavailableLinkBar.IsOpen = true;
    }

    private void HideUnavailableLinkBar()
    {
        ReaderUnavailableLinkBar.IsOpen = false;
        ReaderUnavailableLinkBar.Visibility = Visibility.Collapsed;
    }

    private async void ReaderExternalLinkOpenClicked(object sender, RoutedEventArgs args)
    {
        Uri? uri = pendingExternalLink;
        HideExternalLinkBar();
        if (uri is not null)
        {
            await LaunchExternalAsync(uri);
        }
    }

    private async Task LaunchExternalAsync(Uri uri)
    {
        if (cacheRoot is null)
        {
            return;
        }
        bool launched;
        try
        {
            launched = await ExternalLinkLaunchers.Create(cacheRoot).LaunchAsync(uri);
        }
        catch (Exception)
        {
            launched = false;
        }
        if (!launched)
        {
            ShowWarningStatus("This link couldn't be opened.");
        }
    }

    private async void ReaderLoadErrorActionClicked(object sender, RoutedEventArgs args)
    {
        int generation = readerErrorGeneration;
        switch (readerErrorAction)
        {
            case HtmlGuideLoadAction.GetRuntime:
                await LaunchExternalAsync(new Uri(HtmlGuideLoadMessages.RuntimeDownloadUrl));
                break;
            case HtmlGuideLoadAction.Reopen:
                // A click queued behind a navigation away does nothing.
                await RunNavigationAsync(async () =>
                {
                    if (generation != renderGeneration || navigator.Current is not ReaderRoute)
                    {
                        return;
                    }
                    await RenderCurrentAsync();
                });
                break;
        }
    }

    private void ReaderExternalLinkDismissClicked(object sender, RoutedEventArgs args) => HideExternalLinkBar();

    // The InfoBar's own close button behaves like Dismiss.
    private void ReaderExternalLinkBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (pendingExternalLink is not null)
        {
            HideExternalLinkBar();
        }
    }

    // Collapsed once closed, so the Reader's layout returns to normal.
    private void ReaderUnavailableLinkBarClosed(InfoBar sender, InfoBarClosedEventArgs args) =>
        ReaderUnavailableLinkBar.Visibility = Visibility.Collapsed;
}
