using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DesktopGuides.Production;

// The page shown when the library can't be opened. Nothing can write while
// it is up, because ready stays false.
public sealed partial class ShellWindow
{
    private void ShowLibraryUnavailable(LibraryOpenException error)
    {
        HideRouteProgress();
        HideStatus();
        LibraryPanel.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Collapsed;
        ReaderPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        LibraryOpenMessage message = LibraryOpenMessages.For(error.Issue);
        LibraryUnavailableTitle.Text = message.Title;
        LibraryUnavailableBody.Text = message.Body;
        LibraryUnavailableDataFolder.Text = LibraryOpenMessages.DataFolder(dataRoot ?? "");
        LibraryUnavailableRecoveryPath.Text = error.RecoveryCopyPath is { } copy
            ? LibraryOpenMessages.RecoveryCopy(copy)
            : "";
        LibraryUnavailableRecoveryPath.Visibility = error.RecoveryCopyPath is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        LibraryUnavailablePanel.Visibility = Visibility.Visible;
        LibraryUnavailableRetry.IsEnabled = true;
        LibraryUnavailableOpenFolder.IsEnabled = dataRoot is not null;
        LibraryUnavailableRetry.Focus(FocusState.Programmatic);
        AnnounceStatus(message.Title);
    }

    // Retry runs as the initialization task, so closing waits for it.
    private async void LibraryUnavailableRetryClicked(object sender, RoutedEventArgs args)
    {
        if (ready || closeRequested || !initializationTask.IsCompleted)
        {
            return;
        }
        LibraryUnavailableRetry.IsEnabled = false;
        LibraryUnavailableOpenFolder.IsEnabled = false;
        try
        {
            initializationTask = InitializeCoreAsync();
            await initializationTask;
        }
        finally
        {
            if (!ready && !closeRequested)
            {
                LibraryUnavailableRetry.IsEnabled = true;
                LibraryUnavailableOpenFolder.IsEnabled = dataRoot is not null;
            }
        }
    }

    private async void LibraryUnavailableOpenFolderClicked(object sender, RoutedEventArgs args)
    {
        if (dataRoot is not string root)
        {
            return;
        }
        bool launched;
        try
        {
            launched = await Launcher.LaunchFolderPathAsync(root);
        }
        catch (Exception)
        {
            launched = false;
        }
        if (!launched && !closeRequested)
        {
            ShowWarningStatus("Couldn't open the data folder.");
        }
    }

    // T15.1: one background probe once the library is ready. The warning
    // doesn't hide route progress, because a click may already be loading
    // a page.
    private async Task WarnIfRuntimeMissingAsync(string probedCacheRoot)
    {
        bool available = await Task.Run(
            () => HtmlReaderSession.IsRuntimeAvailable(probedCacheRoot));
        if (available || closeRequested)
        {
            return;
        }
        ShowStatus(HtmlGuideLoadMessages.RuntimeMissingAtStartup, InfoBarSeverity.Warning, true, false);
        ShowStatusAction(
            HtmlGuideLoadMessages.ActionLabel(GuideLoadAction.GetRuntime),
            () => LaunchExternalAsync(new Uri(HtmlGuideLoadMessages.RuntimeDownloadUrl)));
    }

    private async void ShellStatusActionClicked(object sender, RoutedEventArgs args)
    {
        if (statusAction is { } action)
        {
            await action();
        }
    }
}
