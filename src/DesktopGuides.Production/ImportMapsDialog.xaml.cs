using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Maps;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopGuides.Production;

// T21.2: several maps at once. Each file is checked, then the good ones
// are published one at a time; a failure is recorded and the rest go on.
public sealed partial class ImportMapsDialog : ContentDialog
{
    private const string DuplicateMessage = "Already a map for this game.";
    private readonly IReadOnlyList<string> paths;
    private readonly IGuideImportValidator validator;
    private readonly Func<ImportManifest, CancellationToken, Task<Guide?>> findDuplicate;
    private readonly Func<ImportManifest, string, bool, IProgress<ImportProgress>, CancellationToken, Task<Guid>> import;
    private readonly List<ReadyMap> ready = [];
    private readonly CancellationTokenSource cancel = new();
    private Task running = Task.CompletedTask;
    private bool importing;

    internal ImportMapsDialog(
        string gameTitle, IReadOnlyList<string> paths, IGuideImportValidator validator,
        Func<ImportManifest, CancellationToken, Task<Guide?>> findDuplicate,
        Func<ImportManifest, string, bool, IProgress<ImportProgress>, CancellationToken, Task<Guid>> import)
    {
        InitializeComponent();
        Title = $"Import maps for {gameTitle}";
        this.paths = paths;
        this.validator = validator;
        this.findDuplicate = findDuplicate;
        this.import = import;
        Opened += (_, _) => running = CheckAllAsync();
        Closing += DialogClosing;
        PrimaryButtonClick += ImportClicked;
    }

    /// <summary>The last map published, for the game page's selection.</summary>
    internal Guid? LastImportedId { get; private set; }

    internal int ImportedCount { get; private set; }

    internal List<(string Title, string Reason)> Failed { get; } = [];

    private sealed record ReadyMap(MapImportManifest Map, TextBox Title);

    private async Task CheckAllAsync()
    {
        foreach (string path in paths)
        {
            if (cancel.IsCancellationRequested) return;
            string file = Path.GetFileName(path);
            try
            {
                ImportInspection inspection = await validator.InspectAsync(path, cancel.Token);
                if (inspection is not ImportReady { Manifest: MapImportManifest map })
                {
                    AddRow(file, null, $"{file} isn't a PNG or JPEG image.");
                    continue;
                }
                bool duplicate = await findDuplicate(map, cancel.Token) is not null;
                AddRow(file, duplicate ? null : map, duplicate ? DuplicateMessage : MapBatch.Facts(map));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (GuideImportException error)
            {
                AddRow(file, null, error.Message);
            }
            catch (Exception)
            {
                AddRow(file, null, $"{file} can't be opened.");
            }
        }
        ImportMapsBusy.Visibility = Visibility.Collapsed;
        UpdatePrimary();
    }

    private void AddRow(string file, MapImportManifest? map, string text)
    {
        Grid row = new() { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel details = new() { Spacing = 4 };
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        if (map is not null)
        {
            Image thumbnail = new() { Width = 48, Height = 48 };
            AutomationProperties.SetAutomationId(thumbnail, "MapThumbnail");
            AutomationProperties.SetName(thumbnail, $"Thumbnail of {file}");
            row.Children.Add(thumbnail);
            _ = LoadThumbnailAsync(thumbnail, map.Source.FullPath);
            TextBox title = new() { Text = map.SuggestedTitle, MaxLength = GuideTitle.TitleLimit };
            AutomationProperties.SetAutomationId(title, "MapTitleInput");
            AutomationProperties.SetName(title, $"Title for {file}");
            title.TextChanged += (_, _) => UpdatePrimary();
            details.Children.Add(title);
            ready.Add(new ReadyMap(map, title));
        }
        // Panels have no automation peer, so the row is named on its facts line.
        TextBlock facts = new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["DesktopGuidesMetadataStyle"],
        };
        AutomationProperties.SetAutomationId(facts, "MapRow");
        AutomationProperties.SetName(facts, file);
        AutomationProperties.SetHelpText(facts, text);
        details.Children.Add(facts);
        ImportMapsList.Children.Add(row);
    }

    private static async Task LoadThumbnailAsync(Image target, string path)
    {
        try
        {
            BitmapImage bitmap = new() { DecodePixelWidth = 48 };
            await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await bitmap.SetSourceAsync(file.AsRandomAccessStream());
            target.Source = bitmap;
        }
        catch (Exception)
        {
            // The check already decoded it; a thumbnail is only a courtesy.
        }
    }

    private void UpdatePrimary()
    {
        PrimaryButtonText = MapBatch.ImportLabel(ready.Count);
        IsPrimaryButtonEnabled = !importing && ready.Count > 0 &&
            ready.All(map => GuideTitle.TryCreate(map.Title.Text, out _));
    }

    private void ImportClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (importing || !IsPrimaryButtonEnabled) return;
        running = ImportAllAsync();
    }

    private async Task ImportAllAsync()
    {
        importing = true;
        UpdatePrimary();
        ImportMapsBusy.Visibility = Visibility.Visible;
        for (int i = 0; i < ready.Count; i++)
        {
            // Cancel stops before the next file, never mid-file.
            if (cancel.IsCancellationRequested) break;
            ReadyMap map = ready[i];
            string title = GuideTitle.Create(map.Title.Text);
            ImportMapsStatus.Text = $"Importing {i + 1} of {ready.Count}…";
            try
            {
                LastImportedId = await import(map.Map, title, false, new Progress<ImportProgress>(), CancellationToken.None);
                ImportedCount++;
            }
            catch (GuideImportException error)
            {
                Failed.Add((title, error.Message));
            }
            catch (Exception)
            {
                Failed.Add((title, "It couldn't be saved to your library."));
            }
        }
        Hide();
    }

    private async void DialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (running.IsCompleted) return;
        var deferral = args.GetDeferral();
        cancel.Cancel();
        try
        {
            await running;
        }
        catch (Exception)
        {
            // The rows already show each file's problem.
        }
        finally
        {
            deferral.Complete();
        }
    }
}
