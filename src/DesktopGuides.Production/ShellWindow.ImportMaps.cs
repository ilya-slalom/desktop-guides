using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Production.Materials;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;

namespace DesktopGuides.Production;

// T21.2: Import maps takes several PNG or JPEG files at once.
public sealed partial class ShellWindow
{
    private async Task<IReadOnlyList<string>?> PickMapFilesAsync()
    {
        FileOpenPicker picker = new(AppWindow.Id);
        foreach (string type in (string[])[".png", ".jpg", ".jpeg"])
        {
            picker.FileTypeFilter.Add(type);
        }
        IReadOnlyList<PickFileResult> results = await picker.PickMultipleFilesAsync();
        return results.Count == 0 ? null : [.. results.Select(result => result.Path)];
    }

    private async void ImportMapsClicked(object sender, RoutedEventArgs args)
    {
        if (importRequested || closeRequested || navigator.Current is not GameRoute route)
        {
            return;
        }
        importRequested = true;
        ImportGuideButton.IsEnabled = false;
        ImportMapsButton.IsEnabled = false;
        try
        {
            await RunNavigationAsync(async () =>
            {
                if (closeRequested || navigator.Current is not GameRoute current || current.GameId != route.GameId)
                {
                    return;
                }
                Game? game = await RequireRepository().GetGameAsync(route.GameId);
                if (closeRequested)
                {
                    return;
                }
                if (game is null)
                {
                    ShowWarningStatus("This game is no longer in your library.");
                    return;
                }
                IReadOnlyList<string>? files;
                try
                {
                    files = await PickMapFilesAsync();
                }
                catch (Exception)
                {
                    ShowWarningStatus(ImportGuideDialog.PickerFailedMessage);
                    return;
                }
                if (files is null || closeRequested)
                {
                    return;
                }
                if (files.Count > MapBatch.MaxFiles)
                {
                    ShowWarningStatus($"Choose at most {MapBatch.MaxFiles} maps at a time.");
                    return;
                }
                GuideImportPublisher publisher = guidePublisher
                    ?? throw new InvalidOperationException("The library is not ready.");
                SqliteLibraryRepository library = RequireRepository();
                ImportMapsDialog dialog = new(
                    game.Title, files, importValidator,
                    (manifest, token) => library.FindGuideByFingerprintAsync(
                        game.Id, manifest.Format, manifest.Fingerprint, token),
                    (manifest, title, allowDuplicate, progress, token) =>
                        PublishAndQueueMapAsync(publisher, manifest, game.Id, title, allowDuplicate, progress, token))
                {
                    XamlRoot = Navigation.XamlRoot
                };
                DialogSurface.Apply(dialog, EffectiveMaterial, DialogTheme);
                activeMapsDialog = dialog;
                try
                {
                    await dialog.ShowAsync();
                }
                finally
                {
                    activeMapsDialog = null;
                }
                if (closeRequested || navigator.Current is not GameRoute shown || shown.GameId != route.GameId)
                {
                    return;
                }
                if (dialog.ImportedCount > 0 || dialog.Failed.Count > 0)
                {
                    pendingGuideFocus = dialog.LastImportedId;
                    await RenderCurrentAsync();
                    string summary = MapBatch.Summary(dialog.ImportedCount, dialog.Failed);
                    if (dialog.Failed.Count == 0)
                    {
                        ShowTransientStatus(summary);
                    }
                    else
                    {
                        ShowWarningStatus(summary);
                    }
                    if (dialog.ImportedCount > 0)
                    {
                        await RemindToExportAsync();
                    }
                }
            });
        }
        finally
        {
            importRequested = false;
            if (!closeRequested && navigator.Current is GameRoute)
            {
                ImportGuideButton.IsEnabled = true;
                ImportMapsButton.IsEnabled = true;
            }
            if (!closeRequested)
            {
                UpdateRemoveGameAction();
            }
        }
    }
}
