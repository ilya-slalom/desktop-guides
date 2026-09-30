using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ImportGuideDialog : ContentDialog
{
    private const string StoppedMessage = "Checking stopped. Choose another file to try again.";
    private const string FailedMessage = "This file couldn't be checked. Choose another file.";
    internal const string PickerFailedMessage = "The file picker couldn't open. Try again.";
    private const string ImportFailedMessage = "The guide couldn't be imported. Try again.";
    private readonly IGuideImportValidator validator;
    private readonly Func<Task<string?>> pickFile;
    private readonly Func<ImportManifest, string, IProgress<ImportProgress>, CancellationToken, Task<Guid>> import;
    private ImportManifest? manifest;
    private string? firstPath;
    private CancellationTokenSource? check;
    private Task running = Task.CompletedTask;
    private ImportNeedsTxtEncoding? needsEncoding;
    private int generation;
    private bool closing;
    private bool picking;
    private bool settingEncoding;
    private bool importing;

    internal ImportGuideDialog(
        string gameTitle, string path, IGuideImportValidator validator, Func<Task<string?>> pickFile,
        Func<ImportManifest, string, IProgress<ImportProgress>, CancellationToken, Task<Guid>> import)
    {
        InitializeComponent();
        Title = $"Import guide for {gameTitle}";
        this.validator = validator;
        this.pickFile = pickFile;
        this.import = import;
        firstPath = path;
        Opened += DialogOpened;
        Closing += DialogClosing;
        PrimaryButtonClick += ImportClicked;
        SecondaryButtonClick += ChooseAnotherClicked;
    }

    /// <summary>The checked file, once the preview is complete.</summary>
    internal ImportManifest? Manifest
    {
        get => manifest;
        private set
        {
            manifest = value;
            UpdateImportButton();
        }
    }

    /// <summary>The published guide. Hide() makes ShowAsync return None, so the shell checks this.</summary>
    internal Guid? ImportedGuideId { get; private set; }

    private void DialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        if (firstPath is { } path)
        {
            firstPath = null;
            Check(path);
        }
    }

    private async void DialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        closing = true;
        if (running.IsCompleted)
        {
            return;
        }
        var deferral = args.GetDeferral();
        check?.Cancel();
        try
        {
            // RunAsync handles every exception, so this only waits.
            await running;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void CancelClicked(object sender, RoutedEventArgs args) => check?.Cancel();

    private void ChooseAnotherClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        _ = ChooseAnotherAsync();
    }

    private void ImportClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (importing || closing || Manifest is not { } ready ||
            !GuideTitle.TryCreate(GuideTitleInput.Text, out string title))
        {
            return;
        }
        Track(ImportAsync(ready, title));
    }

    private async Task ImportAsync(ImportManifest ready, string title)
    {
        using CancellationTokenSource cancel = new();
        check = cancel;
        ShowImporting(ready.Source.FileName);
        try
        {
            ImportedGuideId = await import(ready, title, new Progress<ImportProgress>(ShowImportProgress), cancel.Token);
            if (!closing)
            {
                Hide();
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // Back to the preview, with the title and encoding unchanged.
        }
        catch (GuideImportException error)
        {
            if (!closing)
            {
                ShowMessage(InfoBarSeverity.Error, error.Message);
            }
        }
        catch (Exception)
        {
            if (!closing)
            {
                ShowMessage(InfoBarSeverity.Error, ImportFailedMessage);
            }
        }
        finally
        {
            if (ReferenceEquals(check, cancel))
            {
                check = null;
            }
            if (!closing && ImportedGuideId is null)
            {
                HideImporting();
            }
        }
    }

    private void ShowImporting(string name)
    {
        importing = true;
        ImportStatus.IsOpen = false;
        GuideTitleInput.IsEnabled = false;
        EncodingOptions.IsEnabled = false;
        IsSecondaryButtonEnabled = false;
        ImportCancel.IsEnabled = true;
        ImportBusyText.Text = $"Importing {name}…";
        ImportProgress.Visibility = Visibility.Collapsed;
        ImportCopyProgress.Value = 0;
        ImportCopyProgress.Visibility = Visibility.Visible;
        ImportBusy.Visibility = Visibility.Visible;
        UpdateImportButton();
    }

    private void HideImporting()
    {
        importing = false;
        ImportBusy.Visibility = Visibility.Collapsed;
        ImportCopyProgress.Visibility = Visibility.Collapsed;
        ImportProgress.Visibility = Visibility.Visible;
        ImportCancel.IsEnabled = true;
        GuideTitleInput.IsEnabled = true;
        EncodingOptions.IsEnabled = true;
        IsSecondaryButtonEnabled = !picking;
        UpdateImportButton();
    }

    private void ShowImportProgress(ImportProgress value)
    {
        // Progress posts to the UI thread, so a late report can arrive after the import ends.
        if (!importing)
        {
            return;
        }
        ImportCopyProgress.Value = value.Fraction;
        if (value.Publishing)
        {
            ImportCancel.IsEnabled = false;
            ImportBusyText.Text = "Saving to your library…";
        }
    }

    private void UpdateImportButton() =>
        IsPrimaryButtonEnabled = !importing && manifest is not null &&
            GuideTitle.TryCreate(GuideTitleInput.Text, out _);

    private async Task ChooseAnotherAsync()
    {
        if (picking || closing || importing)
        {
            return;
        }
        picking = true;
        IsSecondaryButtonEnabled = false;
        try
        {
            string? path;
            try
            {
                path = await pickFile();
            }
            catch (Exception)
            {
                if (!closing)
                {
                    ShowStatus(InfoBarSeverity.Error, PickerFailedMessage);
                }
                return;
            }
            if (path is not null && !closing)
            {
                Check(path);
            }
        }
        finally
        {
            picking = false;
            if (!closing && !importing)
            {
                IsSecondaryButtonEnabled = true;
            }
        }
    }

    private void Check(string path)
    {
        string name = Path.GetFileName(path);
        Manifest = null;
        needsEncoding = null;
        ImportPreview.Visibility = Visibility.Collapsed;
        Track(RunAsync($"Checking {name}…", async (current, token) =>
        {
            ImportInspection inspection = await validator.InspectAsync(path, token);
            if (current != generation || closing)
            {
                return;
            }
            switch (inspection)
            {
                case ImportReady ready:
                    ShowPreview(ready.Manifest.Source, ready.Manifest.SuggestedTitle, ready.Manifest.Format);
                    ShowManifest(ready.Manifest);
                    break;
                case ImportNeedsTxtEncoding needs:
                    ShowPreview(needs.Source, needs.SuggestedTitle, GuideFormat.Txt);
                    ShowEncodingChoice(needs);
                    break;
            }
            GuideTitleInput.Focus(FocusState.Programmatic);
        }));
    }

    private void EncodingChanged(object sender, SelectionChangedEventArgs args)
    {
        if (settingEncoding || needsEncoding is not { } needs || EncodingOptions.SelectedIndex < 0)
        {
            return;
        }
        int codePage = EncodingOptions.SelectedIndex == 0 ? 437 : 1252;
        Manifest = null;
        ImportEncodingRow.Visibility = Visibility.Collapsed;
        Track(RunAsync($"Checking {needs.Source.FileName}…", async (current, token) =>
        {
            TxtImportManifest manifest = await validator.ResolveTxtEncodingAsync(needs, codePage, token);
            if (current != generation || closing)
            {
                return;
            }
            ShowManifest(manifest);
        }, keepPreview: true));
    }

    private void Track(Task task) => running = Task.WhenAll(running, task);

    private async Task RunAsync(
        string busyText, Func<int, CancellationToken, Task> work, bool keepPreview = false)
    {
        check?.Cancel();
        int current = ++generation;
        using CancellationTokenSource cancel = new();
        check = cancel;
        ShowBusy(busyText, keepPreview);
        try
        {
            await work(current, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (current == generation && !closing)
            {
                ShowStatus(InfoBarSeverity.Informational, StoppedMessage);
            }
        }
        catch (GuideImportException error)
        {
            if (current == generation && !closing)
            {
                ShowStatus(InfoBarSeverity.Error, error.Message);
            }
        }
        catch (Exception)
        {
            // The picked file is untrusted: any unexpected failure is a
            // per-file error, not an app crash.
            if (current == generation && !closing)
            {
                ShowStatus(InfoBarSeverity.Error, FailedMessage);
            }
        }
        finally
        {
            if (ReferenceEquals(check, cancel))
            {
                check = null;
            }
            if (current == generation && !closing)
            {
                HideBusy();
            }
        }
    }

    private void ShowBusy(string text, bool keepPreview)
    {
        ImportStatus.IsOpen = false;
        if (!keepPreview)
        {
            ImportPreview.Visibility = Visibility.Collapsed;
        }
        EncodingOptions.IsEnabled = false;
        ImportBusyText.Text = text;
        ImportProgress.IsActive = true;
        ImportBusy.Visibility = Visibility.Visible;
    }

    private void HideBusy()
    {
        ImportBusy.Visibility = Visibility.Collapsed;
        ImportProgress.IsActive = false;
        EncodingOptions.IsEnabled = true;
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        Manifest = null;
        needsEncoding = null;
        ImportPreview.Visibility = Visibility.Collapsed;
        ShowMessage(severity, message);
    }

    private void ShowMessage(InfoBarSeverity severity, string message)
    {
        ImportStatus.Severity = severity;
        ImportStatus.Message = message;
        AutomationProperties.SetName(ImportStatus, message);
        ImportStatus.IsOpen = true;
    }

    private void ShowPreview(ImportSource source, string title, GuideFormat format)
    {
        GuideTitleInput.Text = title;
        ValidateTitle();
        ImportFormat.Text = ImportPresentation.FormatLabel(format);
        ImportFileName.Text = source.FileName;
        ImportSize.Text = ImportPresentation.FormatSize(source.ByteCount);
        ImportEncodingRow.Visibility = Visibility.Collapsed;
        ImportAssetsRow.Visibility = Visibility.Collapsed;
        ImportPagesRow.Visibility = Visibility.Collapsed;
        ImportNoTextWarning.IsOpen = false;
        EncodingOptions.Visibility = Visibility.Collapsed;
        settingEncoding = true;
        try
        {
            EncodingOptions.SelectedIndex = -1;
        }
        finally
        {
            settingEncoding = false;
        }
        ShowWarnings([]);
        ImportPreview.Visibility = Visibility.Visible;
    }

    private void ShowEncodingChoice(ImportNeedsTxtEncoding needs)
    {
        needsEncoding = needs;
        ImportEncodingCp437Sample.Text = needs.Samples.First(s => s.CodePage == 437).Text;
        ImportEncodingWindows1252Sample.Text = needs.Samples.First(s => s.CodePage == 1252).Text;
        EncodingOptions.Visibility = Visibility.Visible;
    }

    private void ShowManifest(ImportManifest manifest)
    {
        Manifest = manifest;
        switch (manifest)
        {
            case TxtImportManifest txt:
                ImportEncodingValue.Text = ImportPresentation.EncodingLabel(txt.CodePage);
                ImportEncodingRow.Visibility = Visibility.Visible;
                break;
            case HtmlImportManifest html:
                string files = html.AssetCount == 1 ? "1 linked file" : $"{html.AssetCount} linked files";
                ImportAssets.Text = $"{files}, {ImportPresentation.FormatSize(html.TotalBytes)} in total";
                ImportAssetsRow.Visibility = Visibility.Visible;
                ShowWarnings(html.Warnings);
                break;
            case PdfImportManifest pdf:
                ImportPages.Text = pdf.PageCount == 1 ? "1 page" : $"{pdf.PageCount} pages";
                ImportPagesRow.Visibility = Visibility.Visible;
                ImportNoTextWarning.IsOpen = !pdf.HasText;
                break;
        }
    }

    // Warnings and details are headered groups only when both are shown.
    private void ShowWarnings(IReadOnlyList<ImportWarning> warnings)
    {
        ImportWarningList.Children.Clear();
        (IReadOnlyList<ImportWarning> shown, int hidden) = ImportPresentation.LimitWarnings(warnings);
        for (int index = 0; index < shown.Count; index++)
        {
            TextBlock row = new()
            {
                Text = ImportPresentation.WarningLine(shown[index]),
                Style = (Style)Application.Current.Resources["DesktopGuidesBodyStyle"],
            };
            AutomationProperties.SetAutomationId(row, $"ImportWarning{index}");
            ImportWarningList.Children.Add(row);
        }
        if (hidden > 0)
        {
            TextBlock more = new()
            {
                Text = ImportPresentation.MoreWarnings(hidden),
                Style = (Style)Application.Current.Resources["DesktopGuidesSecondaryBodyStyle"],
            };
            AutomationProperties.SetAutomationId(more, "ImportWarningsMore");
            ImportWarningList.Children.Add(more);
        }
        PlaceDetails(grouped: shown.Count > 0);
    }

    private void PlaceDetails(bool grouped)
    {
        if (ReferenceEquals(ImportDetailsHeadered.Content, ImportDetailsRows))
        {
            ImportDetailsHeadered.Content = null;
        }
        NativeDetails.Children.Remove(ImportDetailsRows);
        if (grouped)
        {
            ImportDetailsHeadered.Content = ImportDetailsRows;
        }
        else
        {
            NativeDetails.Children.Add(ImportDetailsRows);
        }
        NativeDetails.Visibility = grouped ? Visibility.Collapsed : Visibility.Visible;
        ImportDetailsGroup.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
        ImportWarningsGroup.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleChanged(object sender, TextChangedEventArgs args) => ValidateTitle();

    private void ValidateTitle()
    {
        bool valid = GuideTitle.TryCreate(GuideTitleInput.Text, out _);
        GuideTitleFeedback.Text = valid ? string.Empty : $"Enter a title of 1–{GuideTitle.TitleLimit} characters.";
        GuideTitleFeedback.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        UpdateImportButton();
    }
}
