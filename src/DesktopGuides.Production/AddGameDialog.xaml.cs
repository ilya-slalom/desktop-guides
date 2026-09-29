using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopGuides.Production;

public sealed class GameSearchItem
{
    internal GameSearchItem(ProviderSearchResult result)
    {
        Result = result;
        Summary = GameMetadataPresentation.ResultSummary(result);
        Platforms = GameMetadataPresentation.PlatformSummary(result.Platforms) ?? "No platforms listed";
        Thumbnail = result.ThumbnailUrl is { } url
            ? new BitmapImage(new Uri(url)) { DecodePixelWidth = 90 }
            : null;
        AccessibleName = $"{result.Title}, {Summary}, {Platforms}";
    }

    internal ProviderSearchResult Result { get; }
    public string Title => Result.Title;
    public string Summary { get; }
    public string Platforms { get; }
    public ImageSource? Thumbnail { get; }
    public string AccessibleName { get; }
}

internal enum AddGameOutcome { None, OpenGame, AddManually, OpenSettings }

public sealed partial class AddGameDialog : ContentDialog
{
    private readonly IGameMetadataProvider provider;
    private readonly ProviderGameImporter importer;
    private CancellationTokenSource? operation;
    private Task running = Task.CompletedTask;
    private Func<Task>? retry;
    private bool closing;

    internal AddGameDialog(IGameMetadataProvider provider, ProviderGameImporter importer)
    {
        InitializeComponent();
        this.provider = provider;
        this.importer = importer;
        Opened += (_, _) => GameSearchInput.Focus(FocusState.Programmatic);
        Closing += DialogClosing;
    }

    internal AddGameOutcome Outcome { get; private set; }
    internal ProviderAddResult? Added { get; private set; }

    private async void DialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        closing = true;
        if (operation is not { } current)
        {
            return;
        }
        var deferral = args.GetDeferral();
        current.Cancel();
        try
        {
            await running;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = SearchAsync(args.QueryText);

    private void SearchClicked(object sender, RoutedEventArgs args) => _ = SearchAsync(GameSearchInput.Text);

    private void ResultClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is GameSearchItem item)
        {
            _ = AddAsync(item);
        }
    }

    private void CancelClicked(object sender, RoutedEventArgs args) => operation?.Cancel();

    private void RetryClicked(object sender, RoutedEventArgs args) => _ = retry?.Invoke();

    private void OpenSettingsClicked(object sender, RoutedEventArgs args) => Finish(AddGameOutcome.OpenSettings);

    private void AddManuallyClicked(object sender, RoutedEventArgs args) => Finish(AddGameOutcome.AddManually);

    private void Finish(AddGameOutcome outcome)
    {
        Outcome = outcome;
        Hide();
    }

    private async Task SearchAsync(string text)
    {
        string query = text.Trim();
        if (operation is not null || closing || query.Length == 0)
        {
            return;
        }
        if (query.Length > IgdbClient.MaxQueryLength)
        {
            ShowStatus(InfoBarSeverity.Warning,
                $"Enter up to {IgdbClient.MaxQueryLength} characters to search.", ProviderRecovery.None);
            return;
        }
        retry = () => SearchAsync(query);
        GameSearchResults.ItemsSource = null;
        await StartAsync("Searching IGDB…", async token =>
        {
            IReadOnlyList<ProviderSearchResult> results = await provider.SearchAsync(query, token);
            GameSearchResults.ItemsSource = results.Select(result => new GameSearchItem(result)).ToList();
            if (results.Count == 0)
            {
                ShowStatus(InfoBarSeverity.Informational, ProviderMessages.NoResults(query), ProviderRecovery.AddManually);
            }
        });
        if (!closing && GameSearchResults.Items.Count > 0)
        {
            GameSearchResults.Focus(FocusState.Programmatic);
        }
    }

    private async Task AddAsync(GameSearchItem item)
    {
        if (operation is not null || closing)
        {
            return;
        }
        retry = () => AddAsync(item);
        await StartAsync($"Adding {item.Title}…", async token =>
        {
            Added = await importer.AddAsync(item.Result.ExternalId, token);
            Outcome = AddGameOutcome.OpenGame;
        });
        if (Added is not null && !closing)
        {
            Hide();
        }
    }

    private Task StartAsync(string busyText, Func<CancellationToken, Task> work)
    {
        running = RunAsync(busyText, work);
        return running;
    }

    private async Task RunAsync(string busyText, Func<CancellationToken, Task> work)
    {
        using CancellationTokenSource cancel = new();
        operation = cancel;
        GameSearchStatus.IsOpen = false;
        SetBusy(busyText);
        try
        {
            await work(cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // Cancelled: back to the previous state, with no status.
        }
        catch (ProviderException error)
        {
            ShowStatus(SeverityFor(error.Kind), ProviderMessages.ForIgdb(error.Kind),
                ProviderMessages.RecoveryFor(error.Kind));
        }
        catch (Exception error)
        {
            ShowStatus(InfoBarSeverity.Error, $"Could not add this game: {error.Message}", ProviderRecovery.Retry);
        }
        finally
        {
            operation = null;
            SetBusy(null);
        }
    }

    private static InfoBarSeverity SeverityFor(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.NotConfigured => InfoBarSeverity.Informational,
        ProviderErrorKind.InvalidCredentials or ProviderErrorKind.MalformedData => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Warning,
    };

    private void SetBusy(string? text)
    {
        bool busy = text is not null;
        GameSearchInput.IsEnabled = !busy;
        GameSearchButton.IsEnabled = !busy;
        GameSearchResults.IsEnabled = !busy;
        GameSearchBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        GameSearchProgress.IsActive = busy;
        GameSearchBusyText.Text = text ?? string.Empty;
        if (busy)
        {
            GameSearchCancel.Focus(FocusState.Programmatic);
        }
        else if (!closing && GameSearchResults.Items.Count == 0)
        {
            GameSearchInput.Focus(FocusState.Programmatic);
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string message, ProviderRecovery recovery)
    {
        bool canRetry = recovery.HasFlag(ProviderRecovery.Retry) && retry is not null;
        bool canOpenSettings = recovery.HasFlag(ProviderRecovery.OpenSettings);
        GameSearchRetry.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        GameSearchOpenSettings.Visibility = canOpenSettings ? Visibility.Visible : Visibility.Collapsed;
        GameSearchActions.Visibility = canRetry || canOpenSettings ? Visibility.Visible : Visibility.Collapsed;
        GameSearchStatus.Severity = severity;
        GameSearchStatus.Message = message;
        AutomationProperties.SetName(GameSearchStatus, message);
        GameSearchStatus.IsOpen = true;
    }
}
