using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace DesktopGuides.Production;

// T20.2: export and restore in Settings. While either runs, libraryBusy
// keeps the user on Settings: export holds the library's write gate, and
// restore is about to replace the library.
public sealed partial class ShellWindow
{
    private ManagedPathResolver? libraryPaths;
    private CancellationTokenSource? backupCancel;
    private Task backupTask = Task.CompletedTask;
    private bool libraryBusy;

    private static LibraryExporter CreateExporter(
        SqliteLibraryRepository library, ManagedPathResolver paths, bool packaged) =>
        new(library, paths, new LibraryExportOptions(
            typeof(ShellWindow).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            packaged ? "msix" : "portable",
            BackupDestinationPolicy.ProtectedRoots(
                packaged, paths.DataRoot,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))));

    private void SetLibraryBusy(bool busy)
    {
        libraryBusy = busy;
        LibraryItem.IsEnabled = !busy;
        AppThemeSelector.IsEnabled = !busy && ready;
        WindowMaterialSelector.IsEnabled = !busy && ready;
        AppTitleBar.IsBackButtonEnabled = !busy && navigator.CanGoBack;
    }

    private async void ExportBackupClicked(object sender, RoutedEventArgs args)
    {
        if (libraryBusy || !ready || closeRequested
            || repository is not SqliteLibraryRepository library
            || libraryPaths is not ManagedPathResolver paths)
        {
            return;
        }
        // Built here, not at startup: the library must open even when a
        // protected folder (such as Roaming AppData) can't be resolved.
        LibraryExporter active;
        try
        {
            active = CreateExporter(library, paths, AppDataRoot.HasPackageIdentity());
        }
        catch (ArgumentException)
        {
            ShowErrorStatus(LibraryBackupMessages.ExportFailed(LibraryExportIssue.DestinationUnavailable, []));
            return;
        }
        string? path;
        try
        {
            path = await PickBackupDestinationAsync();
        }
        catch (Exception)
        {
            ShowWarningStatus(LibraryBackupMessages.PickerFailed);
            return;
        }
        if (path is null || closeRequested || libraryBusy)
        {
            return;
        }
        backupTask = RunExportAsync(active, path);
        await backupTask;
    }

    // After the first import: uninstalling removes the live library.
    private async Task RemindToExportAsync()
    {
        if (repository is not SqliteLibraryRepository library)
        {
            return;
        }
        try
        {
            if ((await library.GetSettingsAsync()).ExportReminderShown)
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }
        try
        {
            await library.UpdateSettingsAsync(settings => settings with { ExportReminderShown = true });
        }
        catch (Exception)
        {
            // It shows again after the next import.
        }
        if (closeRequested)
        {
            return;
        }
        ShowStatus(LibraryBackupMessages.ExportReminder, InfoBarSeverity.Informational, true, false);
        ShowStatusAction(LibraryBackupMessages.GoToExport, GoToExportAsync);
    }

    private async Task GoToExportAsync()
    {
        if (libraryBusy)
        {
            return;
        }
        CancelReaderLoad();
        await RunNavigationAsync(async () =>
        {
            navigator.OpenSettings();
            await RenderCurrentAsync();
            // Settings was collapsed until this render; lay it out so the
            // button can take focus from the status action that closed.
            SettingsPanel.UpdateLayout();
            ExportSettingsCard.StartBringIntoView();
            if (!ExportBackupButton.Focus(FocusState.Programmatic))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (navigator.Current is SettingsRoute)
                    {
                        ExportBackupButton.Focus(FocusState.Programmatic);
                    }
                });
            }
        });
    }

    private async Task<string?> PickBackupDestinationAsync()
    {
        FileSavePicker picker = new(AppWindow.Id)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(
                LibraryBackupMessages.SuggestedFileName(DateTime.Now))
        };
        picker.FileTypeChoices.Add("Desktop Guides backup", new List<string> { ".zip" });
        PickFileResult? result = await picker.PickSaveFileAsync();
        return result?.Path;
    }

    private async Task RunExportAsync(LibraryExporter active, string path)
    {
        // Ruling 5: the picker may have created an empty file for us.
        bool emptyWhenPicked = new FileInfo(path) is { Exists: true, Length: 0 };
        using CancellationTokenSource cancel = new();
        backupCancel = cancel;
        bool saved = false;
        SetLibraryBusy(true);
        ExportBackupButton.IsEnabled = false;
        ExportCancelButton.IsEnabled = true;
        ExportCancelButton.Visibility = Visibility.Visible;
        ShowExportProgress(new LibraryExportProgress(LibraryExportPhase.Preparing, 0, 0));
        ExportCancelButton.Focus(FocusState.Programmatic);
        try
        {
            await PauseForTestAsync("Backup", cancel.Token);
            Progress<LibraryExportProgress> progress = new(report =>
            {
                if (ReferenceEquals(backupCancel, cancel) && !closeRequested)
                {
                    ShowExportProgress(report);
                }
            });
            LibraryExportResult result = await active.ExportAsync(path, true, progress, cancel.Token);
            saved = true;
            if (!closeRequested)
            {
                ShowStatus(LibraryBackupMessages.ExportSaved(
                        Path.GetFileName(result.Path), result.Games, result.Guides, result.Bytes),
                    InfoBarSeverity.Success, true, false);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (!closeRequested)
            {
                ShowTransientStatus(LibraryBackupMessages.ExportCanceled);
            }
        }
        catch (LibraryExportException error)
        {
            if (!closeRequested)
            {
                IReadOnlyList<string> titles = await DamagedTitlesAsync(error.GuideIds, error.GameIds);
                ShowErrorStatus(LibraryBackupMessages.ExportFailed(error.Issue, titles));
            }
        }
        catch (Exception)
        {
            if (!closeRequested)
            {
                ShowErrorStatus(LibraryBackupMessages.ExportFailed(LibraryExportIssue.WriteFailed, []));
            }
        }
        finally
        {
            if (!saved && emptyWhenPicked)
            {
                DeleteIfStillEmpty(path);
            }
            backupCancel = null;
            ExportProgress.Visibility = Visibility.Collapsed;
            ExportCancelButton.Visibility = Visibility.Collapsed;
            if (!closeRequested)
            {
                SetLibraryBusy(false);
                ExportBackupButton.IsEnabled = ready;
                ExportBackupButton.Focus(FocusState.Programmatic);
            }
        }
    }

    private void ShowExportProgress(LibraryExportProgress report)
    {
        ExportProgress.Visibility = Visibility.Visible;
        ExportProgress.IsIndeterminate = report.BytesTotal <= 0;
        ExportProgress.Value = report.BytesTotal > 0 ? 100.0 * report.BytesDone / report.BytesTotal : 0;
        AutomationProperties.SetName(ExportProgress, LibraryBackupMessages.ExportPhase(report.Phase));
    }

    private void ExportCancelClicked(object sender, RoutedEventArgs args)
    {
        ExportCancelButton.IsEnabled = false;
        backupCancel?.Cancel();
    }

    private static void DeleteIfStillEmpty(string path)
    {
        try
        {
            if (new FileInfo(path) is { Exists: true, Length: 0 })
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Titles for an error message. A row that can't be read is left out.
    private async Task<IReadOnlyList<string>> DamagedTitlesAsync(
        IReadOnlyList<Guid> guideIds, IReadOnlyList<Guid> gameIds)
    {
        List<string> titles = [];
        if (repository is not SqliteLibraryRepository library)
        {
            return titles;
        }
        foreach (Guid id in guideIds)
        {
            try
            {
                if (await library.GetGuideAsync(id) is Guide guide) titles.Add(guide.Title);
            }
            catch (Exception) { }
        }
        foreach (Guid id in gameIds)
        {
            try
            {
                if (await library.GetGameAsync(id) is Game game) titles.Add(game.Title);
            }
            catch (Exception) { }
        }
        return titles;
    }

    private void ShowStatusAction(string label, Func<Task> action)
    {
        ShellStatusAction.Content = label;
        statusAction = action;
        ShellStatusAction.Visibility = Visibility.Visible;
    }
}
