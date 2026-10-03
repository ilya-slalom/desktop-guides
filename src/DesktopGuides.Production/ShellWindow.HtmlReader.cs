using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
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
        ShowTransientStatus("Guide ready.");
        return true;
    }

    private void ShowHtmlLoadError(HtmlGuideLoadError error)
    {
        string message = HtmlGuideLoadMessages.For(error);
        ShowReaderSurface(placeholder: false, error: message);
        ShowWarningStatus(message);
    }

    // A newer link replaces the URL in an open bar.
    private void OnExternalLinkRequested(object? sender, Uri uri)
    {
        if (!ReferenceEquals(sender, readerSession))
        {
            return;
        }
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

    private async void ReaderExternalLinkOpenClicked(object sender, RoutedEventArgs args)
    {
        Uri? uri = pendingExternalLink;
        HideExternalLinkBar();
        if (uri is null || cacheRoot is null)
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

    private void ReaderExternalLinkDismissClicked(object sender, RoutedEventArgs args) => HideExternalLinkBar();

    // The InfoBar's own close button behaves like Dismiss.
    private void ReaderExternalLinkBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (pendingExternalLink is not null)
        {
            HideExternalLinkBar();
        }
    }
}
