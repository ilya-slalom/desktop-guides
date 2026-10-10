using CommunityToolkit.WinUI.Controls;
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Production.Materials;
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
            // Ruling 5: don't leave behind an empty file the picker created.
            if (path is not null)
            {
                DeleteIfStillEmpty(path);
            }
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
        // Verifying restarts its count at 0, so a bar would jump back from 100%.
        bool determinate = report.Phase != LibraryExportPhase.Verifying && report.BytesTotal > 0;
        ExportProgress.IsIndeterminate = !determinate;
        ExportProgress.Value = determinate ? 100.0 * report.BytesDone / report.BytesTotal : 0;
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

    private LibraryRestorer? restorer;
    private LibraryRestoreStage? stagedBackup;

    private async void ChooseBackupClicked(object sender, RoutedEventArgs args)
    {
        if (libraryBusy || !ready || closeRequested || restorer is not LibraryRestorer active)
        {
            return;
        }
        string? path;
        try
        {
            FileOpenPicker picker = new(AppWindow.Id);
            picker.FileTypeFilter.Add(".zip");
            path = (await picker.PickSingleFileAsync())?.Path;
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
        backupTask = StageBackupAsync(active, path);
        await backupTask;
    }

    private async Task StageBackupAsync(LibraryRestorer active, string path)
    {
        DiscardStagedBackup();
        using CancellationTokenSource cancel = new();
        backupCancel = cancel;
        SetLibraryBusy(true);
        ChooseBackupButton.IsEnabled = false;
        ExportBackupButton.IsEnabled = false;
        RestoreStatus.IsOpen = false;
        RestoreSettingsExpander.IsExpanded = true;
        RestoreCancelButton.IsEnabled = true;
        ShowRestoreProgress(new LibraryRestoreProgress(LibraryRestorePhase.Copying, 0, 0));
        FocusAfterLayout(RestoreCancelButton);
        try
        {
            await PauseForTestAsync("Backup", cancel.Token);
            Progress<LibraryRestoreProgress> progress = new(report =>
            {
                if (ReferenceEquals(backupCancel, cancel) && !closeRequested)
                {
                    ShowRestoreProgress(report);
                }
            });
            LibraryRestoreStage stage = await active.StageAsync(path, progress, cancel.Token);
            if (closeRequested)
            {
                active.DiscardStage(stage);
                return;
            }
            stagedBackup = stage;
            await ShowStagedBackupAsync(stage);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (!closeRequested)
            {
                ShowRestoreStatus(LibraryBackupMessages.RestoreCanceled, InfoBarSeverity.Informational);
            }
        }
        catch (LibraryRestoreException error)
        {
            if (!closeRequested)
            {
                ShowRestoreStatus(
                    LibraryBackupMessages.RestoreFailed(error.Issue, error.Titles, error.BytesNeeded),
                    InfoBarSeverity.Error);
            }
        }
        catch (Exception)
        {
            if (!closeRequested)
            {
                ShowRestoreStatus(
                    LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue.SourceUnavailable, [], null),
                    InfoBarSeverity.Error);
            }
        }
        finally
        {
            backupCancel = null;
            RestoreProgressCard.Visibility = Visibility.Collapsed;
            if (!closeRequested)
            {
                SetLibraryBusy(false);
                ChooseBackupButton.IsEnabled = ready;
                ExportBackupButton.IsEnabled = ready;
                FocusAfterLayout(stagedBackup is null ? ChooseBackupButton : ReplaceLibraryButton);
                if (stagedBackup is not null)
                {
                    // The details open below the expander, often below the fold.
                    RestoreMadeCard.StartBringIntoView(
                        new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
                }
            }
        }
    }

    // A card that just appeared may not be laid out yet; as GoToExportAsync does.
    private void FocusAfterLayout(Control control)
    {
        SettingsPanel.UpdateLayout();
        if (!control.Focus(FocusState.Programmatic))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (navigator.Current is SettingsRoute && !closeRequested)
                {
                    control.Focus(FocusState.Programmatic);
                }
            });
        }
    }

    private void ShowRestoreProgress(LibraryRestoreProgress report)
    {
        string label = LibraryBackupMessages.RestorePhase(report.Phase);
        RestoreProgressCard.Header = label;
        RestoreProgressCard.Visibility = Visibility.Visible;
        bool determinate = report.Phase != LibraryRestorePhase.Checking && report.BytesTotal > 0;
        RestoreProgress.IsIndeterminate = !determinate;
        RestoreProgress.Value = determinate ? 100.0 * report.BytesDone / report.BytesTotal : 0;
        AutomationProperties.SetName(RestoreProgress, label);
    }

    private void ShowRestoreStatus(string message, InfoBarSeverity severity)
    {
        RestoreStatus.Message = message;
        RestoreStatus.Severity = severity;
        AutomationProperties.SetName(RestoreStatus, message);
        RestoreStatus.IsOpen = true;
        AnnounceStatus(message);
    }

    private async Task ShowStagedBackupAsync(LibraryRestoreStage stage)
    {
        (int games, int guides) = await CurrentCountsAsync();
        ShowDetail(RestoreMadeCard,
            LibraryBackupMessages.BackupMade(stage.CreatedUtc.ToLocalTime().DateTime, stage.AppVersion));
        ShowDetail(RestoreHoldsCard, LibraryBackupMessages.BackupHolds(stage.Games, stage.Guides, stage.Bytes));
        ShowDetail(RestoreCurrentCard, LibraryBackupMessages.LibraryHas(games, guides));
        RestoreActionsCard.Visibility = Visibility.Visible;
        DiscardStageButton.IsEnabled = true;
        ReplaceLibraryButton.IsEnabled = true;
        AnnounceStatus("Backup checked. Review it, then replace your library or discard the backup.");
    }

    private static void ShowDetail(SettingsCard card, string text)
    {
        card.Header = text;
        AutomationProperties.SetName(card, text);
        card.Visibility = Visibility.Visible;
    }

    private async Task<(int Games, int Guides)> CurrentCountsAsync()
    {
        if (repository is not SqliteLibraryRepository library)
        {
            return (0, 0);
        }
        try
        {
            IReadOnlyList<LibraryGameSummary> games = await library.ListGameSummariesAsync();
            return (games.Count, games.Sum(game => game.GuideCount));
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private void RestoreCancelClicked(object sender, RoutedEventArgs args)
    {
        RestoreCancelButton.IsEnabled = false;
        backupCancel?.Cancel();
    }

    private void DiscardStageClicked(object sender, RoutedEventArgs args)
    {
        if (libraryBusy)
        {
            return;
        }
        DiscardStagedBackup();
        ChooseBackupButton.Focus(FocusState.Programmatic);
        AnnounceStatus("Backup discarded.");
    }

    // Deletes the staged copy and hides its details.
    private void DiscardStagedBackup()
    {
        if (stagedBackup is LibraryRestoreStage stage && restorer is LibraryRestorer active)
        {
            active.DiscardStage(stage);
        }
        stagedBackup = null;
        DiscardStagedDetails();
    }

    // Set by a swap for the next initialization to report.
    private string? pendingRestoreError;
    private LibraryRestoreStage? pendingRestore;

    private async void ReplaceLibraryClicked(object sender, RoutedEventArgs args)
    {
        if (libraryBusy || !ready || closeRequested || activeRemoveDialog is not null || DialogOpen() ||
            stagedBackup is not LibraryRestoreStage stage || restorer is not LibraryRestorer active)
        {
            return;
        }
        // A second click while the counts load or the dialog opens would
        // open another ContentDialog, which throws.
        ReplaceLibraryButton.IsEnabled = false;
        DiscardStageButton.IsEnabled = false;
        (int games, int guides) = await CurrentCountsAsync();
        ContentDialogResult result = ContentDialogResult.None;
        if (!closeRequested && !libraryBusy)
        {
            ContentDialog dialog = ReplaceLibraryDialog.Create(games, guides, stage, Navigation.XamlRoot);
            DialogSurface.Apply(dialog, EffectiveMaterial, DialogTheme);
            activeRemoveDialog = dialog;
            try
            {
                result = await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // Another dialog opened first; treat it as Cancel.
            }
            finally
            {
                activeRemoveDialog = null;
            }
        }
        if (result != ContentDialogResult.Primary || closeRequested || libraryBusy)
        {
            if (!closeRequested && ReferenceEquals(stagedBackup, stage))
            {
                ReplaceLibraryButton.IsEnabled = true;
                DiscardStageButton.IsEnabled = true;
                ReplaceLibraryButton.Focus(FocusState.Programmatic);
            }
            return;
        }
        backupTask = ReplaceLibraryAsync(active, stage);
        await backupTask;
    }

    // Closes everything that uses the library, swaps it, and opens the
    // restored one through startup, which confirms it or rolls it back.
    private async Task ReplaceLibraryAsync(LibraryRestorer active, LibraryRestoreStage stage)
    {
        SetLibraryBusy(true);
        foreach (Control control in new Control[]
                 { ReplaceLibraryButton, DiscardStageButton, ChooseBackupButton, ExportBackupButton })
        {
            control.IsEnabled = false;
        }
        ShowRouteProgress("Restoring library…");
        try
        {
            // Queued work finishes first; ready = false then stops new work,
            // including an action that takes the queue right after this one.
            await RunNavigationAsync(() =>
            {
                ready = false;
                return Task.CompletedTask;
            });
            ready = false;
            CancelReaderLoad();
            refreshCancel?.Cancel();
            try
            {
                await refreshTask;
            }
            catch (Exception)
            {
                // A cancelled refresh; nothing to report during a restore.
            }
            stagedBackup = null;
            DiscardStagedDetails();
            navigator.ResetToLibrary();
            // The next render loads the restored rows, not the old ones.
            librarySummaries = null;
            libraryFocusPending = true;
            try
            {
                await CloseReaderSessionAsync();
                await DisposeProgressTrackingAsync();
                // Cleared first, so startup doesn't dispose it again after a failure.
                SqliteLibraryRepository? closing = repository;
                repository = null;
                if (closing is not null)
                {
                    await closing.DisposeAsync();
                }
                await active.ReplaceAsync(stage, CancellationToken.None);
                pendingRestore = stage;
            }
            catch (LibraryRestoreException error)
            {
                pendingRestoreError = LibraryBackupMessages.RestoreFailed(error.Issue, error.Titles, error.BytesNeeded);
                active.DiscardStage(stage);
            }
            catch (Exception)
            {
                // The swap didn't start or didn't finish; startup puts back
                // whatever a marker names, so reopen either way.
                pendingRestoreError = LibraryBackupMessages.RestoreFailed(LibraryRestoreIssue.SwapFailed, [], null);
                active.DiscardStage(stage);
            }
            initializationTask = InitializeCoreAsync(verifyRestore: pendingRestore is not null);
            await initializationTask;
        }
        finally
        {
            pendingRestore = null;
            pendingRestoreError = null;
            if (!closeRequested)
            {
                SetLibraryBusy(false);
            }
        }
    }

    // Hides the staged details without touching a stage being swapped in.
    private void DiscardStagedDetails()
    {
        RestoreMadeCard.Visibility = Visibility.Collapsed;
        RestoreHoldsCard.Visibility = Visibility.Collapsed;
        RestoreCurrentCard.Visibility = Visibility.Collapsed;
        RestoreActionsCard.Visibility = Visibility.Collapsed;
        RestoreStatus.IsOpen = false;
    }
}
